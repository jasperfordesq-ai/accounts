using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accounts.Api.Services;

public static class AuditIntegrityCheckpointIssueCodes
{
    public const string MissingCheckpoint = "MissingCheckpoint";
    public const string MissingSigningKey = "MissingSigningKey";
    public const string SignatureMismatch = "SignatureMismatch";
    public const string AnchoredAuditLogMissing = "AnchoredAuditLogMissing";
    public const string AnchoredHashMismatch = "AnchoredHashMismatch";
    public const string CurrentChainInvalid = "CurrentChainInvalid";
}

public record AuditIntegrityCheckpointIssue(
    int? CheckpointId,
    int? AuditLogId,
    string Code,
    string Message,
    string? Expected = null,
    string? Actual = null);

public record AuditIntegrityCheckpointVerification(
    int CompanyId,
    bool HasCheckpoint,
    bool IsValid,
    int? CheckpointId,
    int? LastAuditLogId,
    string? LastIntegrityHash,
    string? KeyId,
    DateTime CheckedAtUtc,
    int IssueCount,
    IReadOnlyList<AuditIntegrityCheckpointIssue> Issues);

public class AuditIntegrityCheckpointService(
    AccountsDbContext db,
    IOptions<AuditIntegrityConfig> options)
{
    public const string DevelopmentSigningKeyBase64 =
        "gIGCg4SFhoeIiYqLjI2Oj5CRkpOUlZaXmJmam5ydnp+goaKjpKWmp6ipqqusra6vsLGys7S1tre4ubq7vL2+vw==";

    private const int MaxUserIdLength = 320;
    private const int MaxDisplayNameLength = 200;
    private const int MaxRequestIdLength = 128;
    private const int MaxKeyIdLength = 120;
    private const long TicksPerMicrosecond = 10;

    private static readonly JsonSerializerOptions SignatureJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AuditIntegrityConfig config = options.Value;

    public async Task<AuditIntegrityCheckpoint> CreateCompanyCheckpointAsync(
        int companyId,
        string? createdByUserId,
        string? createdByDisplayName,
        string? requestId,
        int? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var activeKey = GetActiveSigningKey();
        var verifier = new AuditIntegrityService(db);
        var report = await verifier.VerifyCompanyAsync(companyId, cancellationToken);
        if (!report.IsValid || report.LastAuditLogId is null || string.IsNullOrWhiteSpace(report.LastHash))
        {
            throw new InvalidOperationException(
                "Cannot create an audit integrity checkpoint without a valid audit integrity chain.");
        }

        var checkpoint = new AuditIntegrityCheckpoint
        {
            CompanyId = companyId,
            TenantId = tenantId,
            LastAuditLogId = report.LastAuditLogId.Value,
            LastIntegrityHash = report.LastHash,
            CheckedEntries = report.CheckedEntries,
            CreatedAtUtc = TimestampUtc(),
            CreatedByUserId = Normalize(createdByUserId, MaxUserIdLength),
            CreatedByDisplayName = Normalize(createdByDisplayName, MaxDisplayNameLength),
            RequestId = Normalize(requestId, MaxRequestIdLength),
            KeyId = Normalize(activeKey.KeyId, MaxKeyIdLength) ?? activeKey.KeyId,
            Signature = ""
        };
        checkpoint.Signature = ComputeSignature(checkpoint, activeKey.SigningKey);

        db.AuditIntegrityCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    public async Task<AuditIntegrityCheckpointVerification> VerifyLatestCompanyCheckpointAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await db.AuditIntegrityCheckpoints
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var issues = new List<AuditIntegrityCheckpointIssue>();

        if (checkpoint is null)
        {
            issues.Add(new AuditIntegrityCheckpointIssue(
                null,
                null,
                AuditIntegrityCheckpointIssueCodes.MissingCheckpoint,
                "No audit integrity checkpoint exists for this company."));
            return Verification(companyId, null, issues);
        }

        var signingKey = FindSigningKey(checkpoint.KeyId);
        if (signingKey is null)
        {
            issues.Add(new AuditIntegrityCheckpointIssue(
                checkpoint.Id,
                checkpoint.LastAuditLogId,
                AuditIntegrityCheckpointIssueCodes.MissingSigningKey,
                "The signing key for this audit integrity checkpoint is not configured.",
                checkpoint.KeyId,
                null));
        }
        else
        {
            var expectedSignature = ComputeSignature(checkpoint, signingKey.SigningKey);
            if (!FixedTimeEquals(expectedSignature, checkpoint.Signature))
            {
                issues.Add(new AuditIntegrityCheckpointIssue(
                    checkpoint.Id,
                    checkpoint.LastAuditLogId,
                    AuditIntegrityCheckpointIssueCodes.SignatureMismatch,
                    "Audit integrity checkpoint signature does not match its anchor metadata."));
            }
        }

        var anchoredEntry = await db.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == checkpoint.LastAuditLogId && a.CompanyId == companyId,
                cancellationToken);
        if (anchoredEntry is null)
        {
            issues.Add(new AuditIntegrityCheckpointIssue(
                checkpoint.Id,
                checkpoint.LastAuditLogId,
                AuditIntegrityCheckpointIssueCodes.AnchoredAuditLogMissing,
                "The audit log entry anchored by this checkpoint no longer exists.",
                checkpoint.LastIntegrityHash,
                null));
        }
        else if (!string.Equals(anchoredEntry.IntegrityHash, checkpoint.LastIntegrityHash, StringComparison.Ordinal))
        {
            issues.Add(new AuditIntegrityCheckpointIssue(
                checkpoint.Id,
                checkpoint.LastAuditLogId,
                AuditIntegrityCheckpointIssueCodes.AnchoredHashMismatch,
                "The audit log entry anchored by this checkpoint no longer has the recorded integrity hash.",
                checkpoint.LastIntegrityHash,
                anchoredEntry.IntegrityHash));
        }

        var chainReport = await new AuditIntegrityService(db).VerifyCompanyAsync(companyId, cancellationToken);
        if (!chainReport.IsValid)
        {
            issues.Add(new AuditIntegrityCheckpointIssue(
                checkpoint.Id,
                checkpoint.LastAuditLogId,
                AuditIntegrityCheckpointIssueCodes.CurrentChainInvalid,
                "The current audit integrity chain is not valid.",
                "0",
                chainReport.IssueCount.ToString()));
        }

        return Verification(companyId, checkpoint, issues);
    }

    public static IReadOnlyList<string> ValidateConfiguration(AuditIntegrityConfig config)
    {
        var failures = new List<string>();
        var activeKeyId = config.ActiveKeyId.Trim();
        if (string.IsNullOrWhiteSpace(activeKeyId))
            failures.Add("AuditIntegrity:ActiveKeyId must identify the active audit checkpoint signing key outside development.");

        if (config.SigningKeys.Count == 0)
            failures.Add("AuditIntegrity:SigningKeys must contain at least one audit checkpoint signing key outside development.");

        var duplicateKeyIds = config.SigningKeys
            .Select(k => k.KeyId.Trim())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateKeyIds.Length > 0)
            failures.Add("AuditIntegrity:SigningKeys key ids must be unique.");

        var activeKey = config.SigningKeys.FirstOrDefault(k =>
            k.KeyId.Trim().Equals(activeKeyId, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(activeKeyId) && activeKey is null)
            failures.Add("AuditIntegrity:SigningKeys must include the configured AuditIntegrity:ActiveKeyId.");

        foreach (var key in config.SigningKeys)
        {
            if (string.IsNullOrWhiteSpace(key.KeyId))
                failures.Add("AuditIntegrity:SigningKeys entries must include a KeyId.");

            if (!AuthSessionKey.HasStrongKey(key.SigningKey))
                failures.Add($"AuditIntegrity:SigningKeys[{key.KeyId}]:SigningKey must be a generated Base64 or Base64Url-encoded secret of at least 32 bytes outside development.");

            if (IsKnownDevelopmentSigningKey(key.SigningKey))
                failures.Add($"AuditIntegrity:SigningKeys[{key.KeyId}]:SigningKey uses the committed development audit checkpoint key outside development. Generate a fresh deployment secret.");
        }

        return failures;
    }

    private static bool IsKnownDevelopmentSigningKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            return false;

        return AuthSessionKey.TryDecode(signingKey.Trim(), out var decoded)
            && AuthSessionKey.TryDecode(DevelopmentSigningKeyBase64, out var developmentKey)
            && decoded.Length == developmentKey.Length
            && CryptographicOperations.FixedTimeEquals(decoded, developmentKey);
    }

    private AuditIntegritySigningKeyConfig GetActiveSigningKey()
    {
        var activeKeyId = config.ActiveKeyId.Trim();
        var activeKey = FindSigningKey(activeKeyId);
        if (activeKey is null)
            throw new InvalidOperationException("AuditIntegrity:SigningKeys must include the active audit checkpoint signing key.");

        AuthSessionKey.DecodeRequired(activeKey.SigningKey);
        return activeKey;
    }

    private AuditIntegritySigningKeyConfig? FindSigningKey(string keyId) =>
        config.SigningKeys.FirstOrDefault(k => k.KeyId.Trim().Equals(keyId.Trim(), StringComparison.Ordinal));

    private static string ComputeSignature(AuditIntegrityCheckpoint checkpoint, string signingKey)
    {
        var payload = new AuditIntegrityCheckpointPayload(
            checkpoint.CompanyId,
            checkpoint.TenantId,
            checkpoint.LastAuditLogId,
            checkpoint.LastIntegrityHash,
            checkpoint.CheckedEntries,
            checkpoint.CreatedAtUtc.ToUniversalTime().ToString("O"),
            checkpoint.CreatedByUserId,
            checkpoint.CreatedByDisplayName,
            checkpoint.RequestId,
            checkpoint.KeyId);
        var json = JsonSerializer.Serialize(payload, SignatureJsonOptions);
        var keyBytes = AuthSessionKey.DecodeRequired(signingKey);
        var signatureBytes = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(signatureBytes).ToLowerInvariant();
    }

    private static AuditIntegrityCheckpointVerification Verification(
        int companyId,
        AuditIntegrityCheckpoint? checkpoint,
        IReadOnlyList<AuditIntegrityCheckpointIssue> issues) =>
        new(
            companyId,
            checkpoint is not null,
            checkpoint is not null && issues.Count == 0,
            checkpoint?.Id,
            checkpoint?.LastAuditLogId,
            checkpoint?.LastIntegrityHash,
            checkpoint?.KeyId,
            DateTime.UtcNow,
            issues.Count,
            issues);

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));

    private static string? Normalize(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static DateTime TimestampUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TicksPerMicrosecond), DateTimeKind.Utc);
    }

    private sealed record AuditIntegrityCheckpointPayload(
        int CompanyId,
        int? TenantId,
        int LastAuditLogId,
        string LastIntegrityHash,
        int CheckedEntries,
        string CreatedAtUtc,
        string? CreatedByUserId,
        string? CreatedByDisplayName,
        string? RequestId,
        string KeyId);
}
