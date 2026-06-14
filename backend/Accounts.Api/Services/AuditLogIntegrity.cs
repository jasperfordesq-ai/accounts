using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accounts.Api.Services;

public static class AuditIntegrityIssueCodes
{
    public const string MissingHash = "MissingHash";
    public const string HashMismatch = "HashMismatch";
    public const string ChainBreak = "ChainBreak";
}

public record AuditIntegrityIssue(
    int AuditLogId,
    string Code,
    string Message,
    DateTime Timestamp,
    string? Expected = null,
    string? Actual = null);

public record AuditIntegrityReport(
    int CompanyId,
    int CheckedEntries,
    int UncheckedLegacyEntries,
    int IssueCount,
    bool IsValid,
    int? FirstAuditLogId,
    int? LastAuditLogId,
    string? FirstHash,
    string? LastHash,
    DateTime CheckedAtUtc,
    IReadOnlyList<AuditIntegrityIssue> Issues);

public class AuditIntegrityService(AccountsDbContext db)
{
    public async Task<AuditIntegrityReport> VerifyCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var entries = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);
        var issues = new List<AuditIntegrityIssue>();
        string? previousHash = null;
        string? firstHash = null;
        string? lastHash = null;
        int? firstAuditLogId = null;
        int? lastAuditLogId = null;
        var checkedEntries = 0;
        var uncheckedLegacyEntries = 0;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.IntegrityHash))
            {
                uncheckedLegacyEntries++;
                issues.Add(new AuditIntegrityIssue(
                    entry.Id,
                    AuditIntegrityIssueCodes.MissingHash,
                    "Audit log entry does not have an integrity hash.",
                    entry.Timestamp));
                continue;
            }

            checkedEntries++;
            firstHash ??= entry.IntegrityHash;
            lastHash = entry.IntegrityHash;
            firstAuditLogId ??= entry.Id;
            lastAuditLogId = entry.Id;

            var recomputedHash = AuditLogIntegrity.ComputeHash(entry);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(recomputedHash),
                Encoding.UTF8.GetBytes(entry.IntegrityHash)))
            {
                issues.Add(new AuditIntegrityIssue(
                    entry.Id,
                    AuditIntegrityIssueCodes.HashMismatch,
                    "Audit log entry content does not match its integrity hash.",
                    entry.Timestamp,
                    recomputedHash,
                    entry.IntegrityHash));
            }

            if (!string.Equals(entry.PreviousIntegrityHash, previousHash, StringComparison.Ordinal))
            {
                issues.Add(new AuditIntegrityIssue(
                    entry.Id,
                    AuditIntegrityIssueCodes.ChainBreak,
                    "Audit log entry does not link to the previous hashed entry.",
                    entry.Timestamp,
                    previousHash,
                    entry.PreviousIntegrityHash));
            }

            previousHash = entry.IntegrityHash;
        }

        return new AuditIntegrityReport(
            companyId,
            checkedEntries,
            uncheckedLegacyEntries,
            issues.Count,
            issues.Count == 0,
            firstAuditLogId,
            lastAuditLogId,
            firstHash,
            lastHash,
            DateTime.UtcNow,
            issues);
    }
}

public static class AuditLogIntegrity
{
    private static readonly JsonSerializerOptions IntegrityJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ComputeHash(AuditLog entry)
    {
        var payload = new AuditIntegrityPayload(
            entry.PreviousIntegrityHash,
            entry.CompanyId,
            entry.PeriodId,
            entry.TenantId,
            entry.EntityType,
            entry.EntityId,
            entry.Action,
            entry.OldValueJson,
            entry.NewValueJson,
            entry.UserId,
            entry.ActorDisplayName,
            entry.RequestId,
            entry.Timestamp.ToUniversalTime().ToString("O"));

        var json = JsonSerializer.Serialize(payload, IntegrityJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record AuditIntegrityPayload(
        string? PreviousIntegrityHash,
        int? CompanyId,
        int? PeriodId,
        int? TenantId,
        string EntityType,
        int EntityId,
        string Action,
        string? OldValueJson,
        string? NewValueJson,
        string? UserId,
        string? ActorDisplayName,
        string? RequestId,
        string Timestamp);
}
