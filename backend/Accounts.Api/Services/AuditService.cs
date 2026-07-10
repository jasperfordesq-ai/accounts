using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accounts.Api.Services;

public class AuditService(AccountsDbContext db, IHttpContextAccessor? httpContextAccessor = null)
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int CompanyAuditChainLockFamily = 200_014_001;
    private const int TenantAuditChainLockFamily = 200_014_002;
    private const int GlobalAuditChainLockFamily = 200_014_003;
    private const int MaxUserIdLength = 320;
    private const int MaxRequestIdLength = 128;
    private const int MaxActorDisplayNameLength = 200;
    private const long TicksPerMicrosecond = 10;
    private const string RedactedAuditValue = "[REDACTED]";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ChainLocks = new();

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task LogAsync(
        int? companyId,
        int? periodId,
        string entityType,
        int entityId,
        string action,
        object? oldValue = null,
        object? newValue = null,
        string? userId = null,
        int? tenantId = null,
        string? requestId = null,
        string? actorDisplayName = null,
        bool isolatePendingChanges = false,
        bool durableAudit = false,
        CancellationToken cancellationToken = default)
    {
        var auditCancellationToken = durableAudit ? CancellationToken.None : cancellationToken;
        var ambientUser = httpContextAccessor?.HttpContext is { } httpContext
            ? AuthContext.GetUser(httpContext)
            : null;
        if (ambientUser is not null && tenantId is not null && ambientUser.TenantId != tenantId)
            throw new PersistenceOwnershipException(nameof(AuditLog));
        tenantId ??= ambientUser?.TenantId;
        userId ??= ambientUser is null ? null : AuthenticatedIdentity.AuditUserId(ambientUser);
        actorDisplayName ??= ambientUser is null ? null : AuthenticatedIdentity.ReviewerDisplayName(ambientUser);
        requestId ??= httpContextAccessor?.HttpContext is { } requestContext
            ? GetRequestId(requestContext)
            : null;
        var resolvedTenantId = await ResolveTenantIdAsync(companyId, tenantId, auditCancellationToken);
        var chainLock = ChainLocks.GetOrAdd(ChainLockKey(companyId, resolvedTenantId), _ => new SemaphoreSlim(1, 1));
        await chainLock.WaitAsync(auditCancellationToken);
        try
        {
            if (isolatePendingChanges)
                db.ChangeTracker.Clear();

            await using var distributedTransaction = await BeginDistributedChainTransactionAsync(auditCancellationToken);
            await AcquireDistributedChainLockAsync(companyId, resolvedTenantId, auditCancellationToken);

            var entry = new AuditLog
            {
                CompanyId = companyId,
                PeriodId = periodId,
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                OldValueJson = oldValue != null ? SerializeAuditPayload(oldValue) : null,
                NewValueJson = newValue != null ? SerializeAuditPayload(newValue) : null,
                UserId = Normalize(userId, MaxUserIdLength),
                TenantId = resolvedTenantId,
                RequestId = Normalize(requestId, MaxRequestIdLength),
                ActorDisplayName = Normalize(actorDisplayName, MaxActorDisplayNameLength),
                Timestamp = AuditTimestampUtc()
            };

            var previousHash = await GetPreviousIntegrityHashAsync(companyId, resolvedTenantId, auditCancellationToken);
            entry.PreviousIntegrityHash = previousHash;
            entry.IntegrityHash = AuditLogIntegrity.ComputeHash(entry);

            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(auditCancellationToken);
            if (distributedTransaction is not null)
                await distributedTransaction.CommitAsync(auditCancellationToken);
        }
        finally
        {
            chainLock.Release();
        }
    }

    private async Task<int?> ResolveTenantIdAsync(
        int? companyId,
        int? suppliedTenantId,
        CancellationToken cancellationToken)
    {
        if (companyId is null)
            return suppliedTenantId;

        var company = await db.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(candidate => candidate.Id == companyId)
            .Select(candidate => new { candidate.TenantId })
            .FirstOrDefaultAsync(cancellationToken);
        if (company is null)
            return suppliedTenantId;

        if (suppliedTenantId is not null && suppliedTenantId != company.TenantId)
            throw new PersistenceOwnershipException(nameof(Company));

        return company.TenantId;
    }

    private async Task<IDbContextTransaction?> BeginDistributedChainTransactionAsync(CancellationToken cancellationToken)
    {
        if (!UsesPostgresRelationalProvider() || db.Database.CurrentTransaction is not null)
            return null;

        return await db.Database.BeginTransactionAsync(cancellationToken);
    }

    private async Task AcquireDistributedChainLockAsync(int? companyId, int? tenantId, CancellationToken cancellationToken)
    {
        if (!UsesPostgresRelationalProvider())
            return;

        var (lockFamily, lockId) = DistributedChainLockKey(companyId, tenantId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockFamily}, {lockId})",
            cancellationToken);
    }

    private bool UsesPostgresRelationalProvider() =>
        db.Database.IsRelational()
        && string.Equals(db.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal);

    private static (int LockFamily, int LockId) DistributedChainLockKey(int? companyId, int? tenantId)
    {
        if (companyId is not null)
            return (CompanyAuditChainLockFamily, companyId.Value);

        if (tenantId is not null)
            return (TenantAuditChainLockFamily, tenantId.Value);

        return (GlobalAuditChainLockFamily, 0);
    }

    private async Task<string?> GetPreviousIntegrityHashAsync(int? companyId, int? tenantId, CancellationToken cancellationToken)
    {
        var query = db.AuditLogs
            .AsNoTracking()
            .Where(a => a.IntegrityHash != null);

        query = companyId is not null
            ? query.Where(a => a.CompanyId == companyId)
            : tenantId is not null
                ? query.Where(a => a.CompanyId == null && a.TenantId == tenantId)
                : query.Where(a => a.CompanyId == null && a.TenantId == null);

        return await query
            .OrderByDescending(a => a.Id)
            .Select(a => a.IntegrityHash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static DateTime AuditTimestampUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TicksPerMicrosecond), DateTimeKind.Utc);
    }

    private static string ChainLockKey(int? companyId, int? tenantId)
    {
        if (companyId is not null)
            return $"company:{companyId.Value}";

        if (tenantId is not null)
            return $"tenant:{tenantId.Value}";

        return "global";
    }

    private static string? Normalize(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? GetRequestId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId)) return correlationId;
        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(requestId) ? context.TraceIdentifier : requestId;
    }

    private static string SerializeAuditPayload(object value)
    {
        var json = JsonSerializer.Serialize(value, AuditJsonOptions);
        using var document = JsonDocument.Parse(json);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            WriteRedactedElement(writer, document.RootElement, redact: false);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void WriteRedactedElement(Utf8JsonWriter writer, JsonElement element, bool redact)
    {
        if (redact)
        {
            writer.WriteStringValue(RedactedAuditValue);
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteRedactedElement(writer, property.Value, IsSensitiveAuditField(property.Name));
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteRedactedElement(writer, item, redact: false);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveAuditField(string propertyName)
    {
        var normalized = NormalizeFieldName(propertyName);
        if (normalized.Length == 0)
            return false;

        if (normalized is "key" or "apikey" or "accesskey" or "privatekey" or "signingkey"
            or "secretkey" or "sessionkey" or "authkey" or "auditkey" or "encryptionkey"
            or "decryptionkey" or "developmentkey" or "keyhash")
        {
            return true;
        }

        if (normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("csrf", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("cookie", StringComparison.Ordinal)
            || normalized.Contains("hash", StringComparison.Ordinal)
            || normalized.Contains("salt", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.EndsWith("key", StringComparison.Ordinal)
            && (normalized.StartsWith("api", StringComparison.Ordinal)
                || normalized.StartsWith("access", StringComparison.Ordinal)
                || normalized.StartsWith("private", StringComparison.Ordinal)
                || normalized.StartsWith("signing", StringComparison.Ordinal)
                || normalized.StartsWith("secret", StringComparison.Ordinal)
                || normalized.StartsWith("session", StringComparison.Ordinal)
                || normalized.StartsWith("auth", StringComparison.Ordinal)
                || normalized.StartsWith("audit", StringComparison.Ordinal)
                || normalized.StartsWith("encryption", StringComparison.Ordinal)
                || normalized.StartsWith("decryption", StringComparison.Ordinal)
                || normalized.StartsWith("development", StringComparison.Ordinal));
    }

    private static string NormalizeFieldName(string propertyName)
    {
        Span<char> buffer = propertyName.Length <= 256
            ? stackalloc char[propertyName.Length]
            : new char[propertyName.Length];
        var index = 0;
        foreach (var c in propertyName)
        {
            if (c is '_' or '-' or '.' or ' ')
                continue;

            buffer[index++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..index]);
    }
}
