using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public sealed record DuplicateSourceRow(
    int? TransactionId,
    int? ImportBatchId,
    DateOnly Date,
    decimal Amount,
    string Description,
    decimal? Balance,
    string? Reference,
    string? SourceRowSha256);

public sealed record DuplicateCandidateAssessment(
    bool IsCandidate,
    int? MatchedTransactionId,
    string? MatchedSourceRowSha256,
    DuplicateCandidateKind? Kind,
    decimal Confidence,
    IReadOnlyList<string> Reasons)
{
    public static DuplicateCandidateAssessment None { get; } = new(false, null, null, null, 0m, []);
}

/// <summary>
/// Produces review candidates only. It never decides that a row should be discarded: genuine
/// identical same-day transactions and re-imports are retained for an explicit reviewer decision.
/// </summary>
public static class DuplicateCandidateDetector
{
    public static DuplicateCandidateAssessment Assess(
        DuplicateSourceRow incoming,
        IEnumerable<DuplicateSourceRow> possibleMatches)
    {
        var candidates = possibleMatches
            .Select(existing => Score(incoming, existing))
            .Where(candidate => candidate is not null)
            .Cast<ScoredCandidate>()
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Match.TransactionId ?? int.MaxValue)
            .ToArray();
        if (candidates.Length == 0)
            return DuplicateCandidateAssessment.None;

        var best = candidates[0];
        return new DuplicateCandidateAssessment(
            true,
            best.Match.TransactionId,
            best.Match.SourceRowSha256,
            best.Kind,
            best.Confidence,
            best.Reasons);
    }

    public static string ComputeSourceRowSha256(
        string sourceFileSha256,
        int sourceRowNumber,
        DateOnly date,
        decimal amount,
        string description,
        decimal? balance,
        string? reference)
    {
        var canonical = string.Join('|',
            sourceFileSha256.Trim().ToLowerInvariant(),
            sourceRowNumber.ToString(CultureInfo.InvariantCulture),
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount.ToString("0.############################", CultureInfo.InvariantCulture),
            Normalize(description),
            balance?.ToString("0.############################", CultureInfo.InvariantCulture) ?? string.Empty,
            Normalize(reference));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string ComputeSourceFileSha256(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public static string ComputeSourceFileSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static ScoredCandidate? Score(DuplicateSourceRow incoming, DuplicateSourceRow existing)
    {
        if (!string.IsNullOrWhiteSpace(incoming.SourceRowSha256)
            && string.Equals(incoming.SourceRowSha256, existing.SourceRowSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ScoredCandidate(
                existing,
                DuplicateCandidateKind.ExactSourceReimport,
                1m,
                ["The retained source-file hash and source row number match a previously imported row."]);
        }

        if (incoming.Date != existing.Date || incoming.Amount != existing.Amount)
        {
            return null;
        }

        var descriptionMatches = string.Equals(
            Normalize(incoming.Description),
            Normalize(existing.Description),
            StringComparison.Ordinal);
        var referenceMatches = HasValue(incoming.Reference)
            && HasValue(existing.Reference)
            && string.Equals(Normalize(incoming.Reference), Normalize(existing.Reference), StringComparison.Ordinal);
        var balanceMatches = incoming.Balance is not null
            && existing.Balance is not null
            && incoming.Balance == existing.Balance;
        if (!descriptionMatches && !referenceMatches && !balanceMatches) return null;

        var reasons = new List<string> { "Date and amount match a retained transaction." };
        reasons.Add(descriptionMatches
            ? "Normalised description also matches."
            : "Description differs, so the stable statement metadata requires review.");
        if (referenceMatches) reasons.Add("Bank reference also matches.");
        if (balanceMatches) reasons.Add("Statement running balance also matches.");

        return (referenceMatches, balanceMatches) switch
        {
            (true, true) => new(existing, DuplicateCandidateKind.ReferenceAndBalanceMatch, descriptionMatches ? 0.95m : 0.90m, reasons),
            (true, false) => new(existing, DuplicateCandidateKind.ReferenceMatch, descriptionMatches ? 0.80m : 0.75m, reasons),
            (false, true) => new(existing, DuplicateCandidateKind.BalanceMatch, descriptionMatches ? 0.75m : 0.65m, reasons),
            _ => new(existing, DuplicateCandidateKind.SameDateAmountDescription, 0.55m, reasons)
        };
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static string NormalizeForIndex(string? value) => Normalize(value);

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record ScoredCandidate(
        DuplicateSourceRow Match,
        DuplicateCandidateKind Kind,
        decimal Confidence,
        IReadOnlyList<string> Reasons);
}

/// <summary>
/// Bounded duplicate lookup for an import. It retains only the best candidate per evidence key, so
/// even a 25,000-row statement containing repeated identical facts performs constant-size scoring
/// per incoming row instead of rescanning every prior period row.
/// </summary>
public sealed class DuplicateCandidateIndex
{
    private readonly Dictionary<string, DuplicateSourceRow> _bySourceHash =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FactKey, DuplicateSourceRow> _byFacts = [];
    private readonly Dictionary<ReferenceKey, DuplicateSourceRow> _byReference = [];
    private readonly Dictionary<BalanceKey, DuplicateSourceRow> _byBalance = [];
    private readonly Dictionary<ReferenceBalanceKey, DuplicateSourceRow> _byReferenceAndBalance = [];

    public DuplicateCandidateIndex(IEnumerable<DuplicateSourceRow> initialRows)
    {
        foreach (var row in initialRows) Add(row);
    }

    public DuplicateCandidateAssessment AssessAndAdd(DuplicateSourceRow incoming)
    {
        var candidates = new HashSet<DuplicateSourceRow>();
        if (incoming.SourceRowSha256 is { Length: 64 } incomingHash
            && _bySourceHash.TryGetValue(incomingHash, out var exact))
            candidates.Add(exact);

        var factKey = Facts(incoming);
        if (_byFacts.TryGetValue(factKey, out var facts)) candidates.Add(facts);
        var normalizedReference = DuplicateCandidateDetector.NormalizeForIndex(incoming.Reference);
        if (normalizedReference.Length > 0)
        {
            var amountDateKey = AmountDate(incoming);
            if (_byReference.TryGetValue(new(amountDateKey, normalizedReference), out var reference))
                candidates.Add(reference);
            if (incoming.Balance is { } referenceBalance
                && _byReferenceAndBalance.TryGetValue(new(amountDateKey, normalizedReference, referenceBalance), out var both))
                candidates.Add(both);
        }
        if (incoming.Balance is { } balance
            && _byBalance.TryGetValue(new(AmountDate(incoming), balance), out var balanceMatch))
            candidates.Add(balanceMatch);

        var assessment = DuplicateCandidateDetector.Assess(incoming, candidates);
        Add(incoming);
        return assessment;
    }

    private void Add(DuplicateSourceRow row)
    {
        if (row.SourceRowSha256 is { Length: 64 } rowHash)
            AddBest(_bySourceHash, rowHash, row);
        var factKey = Facts(row);
        AddBest(_byFacts, factKey, row);
        var normalizedReference = DuplicateCandidateDetector.NormalizeForIndex(row.Reference);
        if (normalizedReference.Length > 0)
        {
            var amountDateKey = AmountDate(row);
            AddBest(_byReference, new(amountDateKey, normalizedReference), row);
            if (row.Balance is { } referenceBalance)
                AddBest(_byReferenceAndBalance, new(amountDateKey, normalizedReference, referenceBalance), row);
        }
        if (row.Balance is { } balance)
            AddBest(_byBalance, new(AmountDate(row), balance), row);
    }

    private static void AddBest<TKey>(Dictionary<TKey, DuplicateSourceRow> index, TKey key, DuplicateSourceRow row)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var current) || Rank(row) < Rank(current))
            index[key] = row;
    }

    private static int Rank(DuplicateSourceRow row) => row.TransactionId ?? int.MaxValue;
    private static FactKey Facts(DuplicateSourceRow row) =>
        new(row.Date, row.Amount, DuplicateCandidateDetector.NormalizeForIndex(row.Description));
    private static AmountDateKey AmountDate(DuplicateSourceRow row) => new(row.Date, row.Amount);

    private readonly record struct AmountDateKey(DateOnly Date, decimal Amount);
    private readonly record struct FactKey(DateOnly Date, decimal Amount, string Description);
    private readonly record struct ReferenceKey(AmountDateKey AmountDate, string Reference);
    private readonly record struct BalanceKey(AmountDateKey AmountDate, decimal Balance);
    private readonly record struct ReferenceBalanceKey(AmountDateKey AmountDate, string Reference, decimal Balance);
}
