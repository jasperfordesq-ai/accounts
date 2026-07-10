using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record AnnualReturnDateChangeInput(
    DateOnly? AnnualReturnDate,
    DateOnly? EffectiveFrom,
    AnnualReturnDateSource? Source,
    string? EvidenceReference,
    string? EvidenceSha256,
    string? ChangeReason);

public sealed class AnnualReturnDateValidationException(
    IReadOnlyDictionary<string, string[]> errors) : BusinessRuleException("Annual Return Date evidence is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class AnnualReturnDateService(
    AccountsDbContext db,
    AuditService audit,
    TimeProvider? timeProvider = null)
{
    public AnnualReturnDateRecord PrepareInitial(
        Company company,
        AnnualReturnDateChangeInput input,
        AuthenticatedUser actor)
    {
        if (company.AnnualReturnDate is not null)
            throw new BusinessRuleException("The company already has an Annual Return Date.");

        return Prepare(company, input, actor, initial: true);
    }

    public async Task<AnnualReturnDateRecord> RecordChangeAsync(
        int companyId,
        AnnualReturnDateChangeInput input,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var company = await db.Companies
            .FirstOrDefaultAsync(candidate => candidate.Id == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Company {companyId} not found");
        var oldValue = new { company.AnnualReturnDate };
        var record = Prepare(company, input, actor, initial: company.AnnualReturnDate is null);

        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync(
            companyId,
            null,
            nameof(AnnualReturnDateRecord),
            record.Id,
            AuditEventCodes.AnnualReturnDateRecorded,
            oldValue,
            EvidenceSnapshot(record),
            AuthenticatedIdentity.AuditUserId(actor),
            actor.TenantId,
            actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(actor),
            cancellationToken: cancellationToken);
        return record;
    }

    public async Task<List<AnnualReturnDateRecord>> GetHistoryAsync(
        int companyId,
        CancellationToken cancellationToken = default) =>
        await db.AnnualReturnDateRecords
            .Where(record => record.CompanyId == companyId)
            .OrderByDescending(record => record.RecordedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    private AnnualReturnDateRecord Prepare(
        Company company,
        AnnualReturnDateChangeInput input,
        AuthenticatedUser actor,
        bool initial)
    {
        var errors = Validate(company.AnnualReturnDate, input, initial);
        if (errors.Count > 0)
            throw new AnnualReturnDateValidationException(errors);

        var annualReturnDate = input.AnnualReturnDate!.Value;
        var effectiveFrom = input.EffectiveFrom!.Value;
        var source = input.Source!.Value;
        var evidenceReference = input.EvidenceReference!.Trim();
        var evidenceSha256 = NormalizeSha256(input.EvidenceSha256);
        var reason = Normalize(input.ChangeReason);
        var recordedAtUtc = UtcNowMicrosecond();
        var actorUserId = AuthenticatedIdentity.AuditUserId(actor);
        var actorDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor);
        var record = new AnnualReturnDateRecord
        {
            Company = company,
            CompanyId = company.Id,
            PreviousAnnualReturnDate = company.AnnualReturnDate,
            AnnualReturnDate = annualReturnDate,
            EffectiveFrom = effectiveFrom,
            Source = source,
            EvidenceReference = evidenceReference,
            EvidenceSha256 = evidenceSha256,
            ChangeReason = reason,
            RecordedByUserId = actorUserId,
            RecordedByDisplayName = actorDisplayName,
            RecordedAtUtc = recordedAtUtc,
            RecordSha256 = AnnualReturnDateEvidenceIntegrity.ComputeHash(
                company.AnnualReturnDate,
                annualReturnDate,
                effectiveFrom,
                source,
                evidenceReference,
                evidenceSha256,
                reason,
                actorUserId,
                actorDisplayName,
                recordedAtUtc)
        };
        company.AnnualReturnDate = annualReturnDate;
        company.UpdatedAt = recordedAtUtc;
        db.AnnualReturnDateRecords.Add(record);
        return record;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        DateOnly? previousAnnualReturnDate,
        AnnualReturnDateChangeInput input,
        bool initial)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.AnnualReturnDate is null || input.AnnualReturnDate == default)
            errors["annualReturnDate"] = ["Exact Annual Return Date is required."];
        if (input.EffectiveFrom is null || input.EffectiveFrom == default)
            errors["annualReturnDateEffectiveFrom"] = ["ARD effective date is required."];
        if (input.Source is null || !Enum.IsDefined(input.Source.Value))
            errors["annualReturnDateSource"] = ["A supported ARD evidence source is required."];
        if (string.IsNullOrWhiteSpace(input.EvidenceReference))
            errors["annualReturnDateEvidenceReference"] = ["A CRO or retained evidence reference is required."];
        else if (input.EvidenceReference.Trim().Length > 300)
            errors["annualReturnDateEvidenceReference"] = ["ARD evidence reference must be 300 characters or fewer."];
        if (!string.IsNullOrWhiteSpace(input.EvidenceSha256)
            && !IsSha256(input.EvidenceSha256.Trim()))
        {
            errors["annualReturnDateEvidenceSha256"] = ["ARD evidence SHA-256 must contain exactly 64 hexadecimal characters."];
        }
        if (input.ChangeReason?.Trim().Length > 1000)
            errors["annualReturnDateChangeReason"] = ["ARD change reason must be 1000 characters or fewer."];

        if (input.AnnualReturnDate is { } annualReturnDate
            && input.EffectiveFrom is { } effectiveFrom
            && effectiveFrom > annualReturnDate)
        {
            errors["annualReturnDateEffectiveFrom"] = ["ARD effective date cannot be after the exact ARD."];
        }

        if (!initial && previousAnnualReturnDate is { } previous)
        {
            if (input.AnnualReturnDate == previous)
                errors["annualReturnDate"] = ["The replacement ARD must differ from the current exact ARD."];
            if (string.IsNullOrWhiteSpace(input.ChangeReason) || input.ChangeReason.Trim().Length < 20)
                errors["annualReturnDateChangeReason"] = ["A specific ARD change reason of at least 20 characters is required."];

            if (input.Source == AnnualReturnDateSource.BroughtForward
                && input.AnnualReturnDate >= previous)
            {
                errors["annualReturnDate"] = ["A brought-forward ARD must be earlier than the current ARD."];
            }
            if (input.Source == AnnualReturnDateSource.ExtendedB73
                && (input.AnnualReturnDate <= previous || input.AnnualReturnDate > previous.AddMonths(6)))
            {
                errors["annualReturnDate"] = ["A B73 extension must be later than the current ARD and no more than six months after it."];
            }
        }

        if (input.Source == AnnualReturnDateSource.ManualOverride)
        {
            if (string.IsNullOrWhiteSpace(input.ChangeReason) || input.ChangeReason.Trim().Length < 20)
                errors["annualReturnDateChangeReason"] = ["A manual ARD override requires a specific reason of at least 20 characters."];
            if (string.IsNullOrWhiteSpace(input.EvidenceSha256) || !IsSha256(input.EvidenceSha256.Trim()))
                errors["annualReturnDateEvidenceSha256"] = ["A manual ARD override requires the SHA-256 of retained evidence."];
        }

        return errors;
    }

    private DateTime UtcNowMicrosecond()
    {
        var utc = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeSha256(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static object EvidenceSnapshot(AnnualReturnDateRecord record) => new
    {
        record.Id,
        record.PreviousAnnualReturnDate,
        record.AnnualReturnDate,
        record.EffectiveFrom,
        record.Source,
        record.EvidenceReference,
        record.EvidenceSha256,
        record.ChangeReason,
        record.RecordedByUserId,
        record.RecordedByDisplayName,
        record.RecordedAtUtc,
        record.RecordSha256
    };
}

public static class AnnualReturnDateEvidenceIntegrity
{
    public static bool IsValid(AnnualReturnDateRecord record) =>
        string.Equals(
            ComputeHash(
                record.PreviousAnnualReturnDate,
                record.AnnualReturnDate,
                record.EffectiveFrom,
                record.Source,
                record.EvidenceReference,
                record.EvidenceSha256,
                record.ChangeReason,
                record.RecordedByUserId,
                record.RecordedByDisplayName,
                record.RecordedAtUtc),
            record.RecordSha256,
            StringComparison.OrdinalIgnoreCase);

    public static string ComputeHash(
        DateOnly? previousAnnualReturnDate,
        DateOnly annualReturnDate,
        DateOnly effectiveFrom,
        AnnualReturnDateSource source,
        string evidenceReference,
        string? evidenceSha256,
        string? changeReason,
        string recordedByUserId,
        string recordedByDisplayName,
        DateTime recordedAtUtc)
    {
        var canonical = string.Join('\n',
            previousAnnualReturnDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            annualReturnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            source.ToString(),
            evidenceReference,
            evidenceSha256 ?? string.Empty,
            changeReason ?? string.Empty,
            recordedByUserId,
            recordedByDisplayName,
            recordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
