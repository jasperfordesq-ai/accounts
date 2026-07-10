using Accounts.Api.Entities;
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class DuplicateCandidateDetectorTests
{
    [Fact]
    public void ExactReimport_UsesFileAndRowFingerprintAsStrongestEvidence()
    {
        var fileHash = DuplicateCandidateDetector.ComputeSourceFileSha256("same retained statement");
        var sourceHash = DuplicateCandidateDetector.ComputeSourceRowSha256(
            fileHash, 12, new DateOnly(2026, 1, 5), -12.34m, "CARD SHOP", 987.66m, "REF-1");
        var incoming = Row(null, sourceHash);
        var retained = Row(41, sourceHash);

        var result = DuplicateCandidateDetector.Assess(incoming, [retained]);

        Assert.True(result.IsCandidate);
        Assert.Equal(41, result.MatchedTransactionId);
        Assert.Equal(DuplicateCandidateKind.ExactSourceReimport, result.Kind);
        Assert.Equal(1m, result.Confidence);
    }

    [Fact]
    public void IdenticalGenuineSameDayRows_RemainReviewCandidatesRatherThanAnAutomaticDiscard()
    {
        var incoming = Row(null, new string('b', 64), balance: null, reference: null);
        var earlierSameDay = Row(42, new string('a', 64), balance: null, reference: null);

        var result = DuplicateCandidateDetector.Assess(incoming, [earlierSameDay]);

        Assert.True(result.IsCandidate);
        Assert.Equal(DuplicateCandidateKind.SameDateAmountDescription, result.Kind);
        Assert.Equal(0.55m, result.Confidence);
        Assert.DoesNotContain(result.Reasons, reason => reason.Contains("discard", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true, true, DuplicateCandidateKind.ReferenceAndBalanceMatch, "0.95")]
    [InlineData(true, false, DuplicateCandidateKind.ReferenceMatch, "0.80")]
    [InlineData(false, true, DuplicateCandidateKind.BalanceMatch, "0.75")]
    [InlineData(false, false, DuplicateCandidateKind.SameDateAmountDescription, "0.55")]
    public void AvailableStatementMetadata_ExplainsCandidateStrength(
        bool sameReference,
        bool sameBalance,
        DuplicateCandidateKind expectedKind,
        string expectedConfidence)
    {
        var incoming = Row(null, new string('b', 64), balance: 100m, reference: "BANK-REF");
        var retained = Row(
            43,
            new string('a', 64),
            balance: sameBalance ? 100m : 90m,
            reference: sameReference ? "BANK-REF" : "OTHER-REF");

        var result = DuplicateCandidateDetector.Assess(incoming, [retained]);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(decimal.Parse(expectedConfidence, System.Globalization.CultureInfo.InvariantCulture), result.Confidence);
    }

    [Fact]
    public void DifferentTransactionFacts_AreNotCandidateDuplicates()
    {
        var incoming = Row(null, new string('b', 64));
        var other = Row(44, new string('a', 64)) with { Amount = -12.35m };

        Assert.False(DuplicateCandidateDetector.Assess(incoming, [other]).IsCandidate);
    }

    [Fact]
    public void StableReferenceAndBalance_DetectRegeneratedExportWhenMemoChanges()
    {
        var incoming = Row(null, new string('b', 64)) with { Description = "Card Shop Dublin terminal 4" };
        var retained = Row(45, new string('a', 64)) with { Description = "CARD SHOP" };

        var result = DuplicateCandidateDetector.Assess(incoming, [retained]);

        Assert.True(result.IsCandidate);
        Assert.Equal(DuplicateCandidateKind.ReferenceAndBalanceMatch, result.Kind);
        Assert.Equal(0.90m, result.Confidence);
        Assert.Contains(result.Reasons, reason => reason.Contains("Description differs", StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateIndex_BoundsLookupWhileRetainingStrongestMetadataMatch()
    {
        var rows = Enumerable.Range(1, 10_000)
            .Select(index => Row(index, index.ToString("x64"), balance: 900m + index, reference: $"REF-{index}"))
            .ToArray();
        var strongest = Row(10_001, new string('c', 64), balance: 987.66m, reference: "REF-1");
        var index = new DuplicateCandidateIndex(rows.Append(strongest));
        var incoming = Row(null, new string('d', 64), balance: 987.66m, reference: "REF-1") with
        {
            Description = "Changed export memo"
        };

        var result = index.AssessAndAdd(incoming);

        Assert.Equal(10_001, result.MatchedTransactionId);
        Assert.Equal(DuplicateCandidateKind.ReferenceAndBalanceMatch, result.Kind);
    }

    private static DuplicateSourceRow Row(
        int? id,
        string sourceSha256,
        decimal? balance = 987.66m,
        string? reference = "REF-1") => new(
        id,
        7,
        new DateOnly(2026, 1, 5),
        -12.34m,
        "Card Shop",
        balance,
        reference,
        sourceSha256);
}
