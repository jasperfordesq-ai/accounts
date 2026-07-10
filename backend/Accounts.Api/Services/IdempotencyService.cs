using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed class IdempotencyConfig
{
    public int RetentionDays { get; set; } = 30;
    public int CleanupIntervalMinutes { get; set; } = 360;
    public int CleanupBatchSize { get; set; } = 1000;
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;

    public static bool IsValid(IdempotencyConfig config) =>
        config.RetentionDays is >= 1 and <= 90
        && config.CleanupIntervalMinutes is >= 5 and <= 1440
        && config.CleanupBatchSize is >= 1 and <= 10_000
        && config.MaxResponseBytes is >= 64 * 1024 and <= 64 * 1024 * 1024;
}

public static class IdempotencyOperations
{
    public const string CompanyCreate = "company.create.v1";
    public const string CompanyOnboard = "company.onboard.v1";
    public const string PeriodCreate = "period.create.v1";
    public const string BankImport = "bank-import.create.v1";
    public const string CroAccountsGenerate = "document.cro-accounts.generate.v1";
    public const string CroSignatureGenerate = "document.cro-signature.generate.v1";
    public const string CroStatus = "filing.cro-status.v1";
    public const string CroPayment = "filing.cro-payment.v1";
    public const string CharityReportGenerated = "filing.charity-report-generated.v1";
    public const string CharityStatus = "filing.charity-status.v1";
    public const string RevenueExternalValidation = "filing.revenue-external-validation.v1";
    public const string RevenueStatus = "filing.revenue-status.v1";
    public const string RevenueIxbrlValidation = "filing.revenue-ixbrl-validation.v1";
    public const string DeadlineMarkFiled = "filing.deadline-mark-filed.v1";
    public const string PeriodStatus = "period.status.v1";
    public const string ExternalFilingAuthorityRecord = "external-filing.authority-record.v1";
    public const string ExternalFilingAuthorityRevoke = "external-filing.authority-revoke.v1";
    public const string ExternalFilingCroSnapshot = "external-filing.cro-snapshot.v1";
    public const string ExternalFilingRevenueSnapshot = "external-filing.revenue-snapshot.v1";
    public const string ExternalFilingOutcome = "external-filing.outcome.v1";
}

public sealed class IdempotencyConflictException(string message) : BusinessRuleException(message);

public sealed record IdempotencyOperationOutcome<T>(
    T Result,
    string ResourceType,
    string ResourceId,
    int HttpStatusCode = StatusCodes.Status200OK);

public sealed record IdempotencyExecution<T>(
    T Result,
    bool WasReplay,
    long RecordId,
    string Operation,
    string ResourceType,
    string ResourceId,
    int HttpStatusCode,
    DateTime ExpiresAtUtc);

public sealed record IdempotencyRequest(string Key);

public sealed record IdempotencyEndpointExecution<T>(
    IdempotencyExecution<T>? Execution,
    IResult? Error)
{
    public bool Succeeded => Execution is not null && Error is null;
}

public sealed record IdempotencyConflictResponse(string Error, string Code, bool RetryWithNewKey);

public static class IdempotencyHttpContract
{
    public const string RequestHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotency-Replayed";
    public const string RecordIdHeader = "Idempotency-Record-Id";
    public const string OperationHeader = "Idempotency-Operation";
    public const string ExpiresAtHeader = "Idempotency-Expires-At";

    public static bool TryRead(HttpContext context, out IdempotencyRequest request, out IResult? error)
    {
        var values = context.Request.Headers[RequestHeader];
        var key = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        if (!IdempotencyService.IsValidKey(key))
        {
            request = new IdempotencyRequest(string.Empty);
            error = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] =
                ["Idempotency-Key must contain 8 to 128 letters, digits, dots, colons, underscores, or hyphens."]
            });
            return false;
        }

        request = new IdempotencyRequest(key);
        error = null;
        return true;
    }

    public static async Task<IdempotencyEndpointExecution<T>> ExecuteAsync<T>(
        HttpContext context,
        IdempotencyService service,
        AuthenticatedUser actor,
        string operation,
        object requestPayload,
        Func<CancellationToken, Task<IdempotencyOperationOutcome<T>>> action)
    {
        if (!TryRead(context, out var request, out var error))
            return new IdempotencyEndpointExecution<T>(null, error);

        try
        {
            var execution = await service.ExecuteAsync(
                actor.TenantId,
                request.Key,
                operation,
                requestPayload,
                actor,
                action,
                context.RequestAborted);
            ApplyResponseHeaders(context, execution);
            return new IdempotencyEndpointExecution<T>(execution, null);
        }
        catch (IdempotencyConflictException conflict)
        {
            return new IdempotencyEndpointExecution<T>(
                null,
                Results.Conflict(new IdempotencyConflictResponse(
                    conflict.Message,
                    "idempotency_key_conflict",
                    RetryWithNewKey: true)));
        }
    }

    public static void ApplyResponseHeaders<T>(HttpContext context, IdempotencyExecution<T> execution)
    {
        context.Response.Headers[ReplayedHeader] = execution.WasReplay ? "true" : "false";
        context.Response.Headers[RecordIdHeader] = execution.RecordId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers[OperationHeader] = execution.Operation;
        context.Response.Headers[ExpiresAtHeader] = execution.ExpiresAtUtc.ToUniversalTime().ToString("O");
    }

    public static IResult JsonResult<T>(IdempotencyExecution<T> execution, string? createdLocation = null)
    {
        if (execution.HttpStatusCode == StatusCodes.Status201Created
            && !string.IsNullOrWhiteSpace(createdLocation))
        {
            return Results.Created(createdLocation, execution.Result);
        }

        return Results.Json(execution.Result, statusCode: execution.HttpStatusCode);
    }
}

public sealed class IdempotencyService(
    AccountsDbContext db,
    IOptions<IdempotencyConfig>? configuredOptions = null,
    TimeProvider? configuredTimeProvider = null)
{
    internal const int AdvisoryLockFamily = 51_003;
    internal const string CompletedStatus = "Completed";
    internal const string InProgressStatus = "InProgress";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions LegacyCompanyOnboardingJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IdempotencyConfig options = configuredOptions?.Value ?? new IdempotencyConfig();
    private readonly TimeProvider timeProvider = configuredTimeProvider ?? TimeProvider.System;

    public async Task<IdempotencyExecution<T>> ExecuteAsync<T>(
        int tenantId,
        string idempotencyKey,
        string operation,
        object requestPayload,
        AuthenticatedUser actor,
        Func<CancellationToken, Task<IdempotencyOperationOutcome<T>>> action,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || actor.TenantId != tenantId)
            throw new UnauthorizedAccessException("Idempotency tenant scope does not match the authenticated user.");
        if (!IsValidKey(idempotencyKey))
            throw new ArgumentException("Idempotency key is invalid.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 160)
            throw new ArgumentException("Idempotency operation is invalid.", nameof(operation));
        if (!IdempotencyConfig.IsValid(options))
            throw new InvalidOperationException("Idempotency retention configuration is invalid.");

        var normalizedKey = idempotencyKey.Trim();
        var normalizedOperation = operation.Trim();
        var requestFingerprint = RequestFingerprint(normalizedOperation, requestPayload);
        SemaphoreSlim? localLock = null;
        if (!db.Database.IsRelational())
        {
            localLock = LocalLocks.GetOrAdd($"{tenantId}:{normalizedKey}", _ => new SemaphoreSlim(1, 1));
            await localLock.WaitAsync(cancellationToken);
        }

        try
        {
            return await ExecuteCoreAsync(
                tenantId,
                normalizedKey,
                normalizedOperation,
                requestFingerprint,
                actor,
                action,
                cancellationToken);
        }
        finally
        {
            localLock?.Release();
        }
    }

    private async Task<IdempotencyExecution<T>> ExecuteCoreAsync<T>(
        int tenantId,
        string idempotencyKey,
        string operation,
        string requestFingerprint,
        AuthenticatedUser actor,
        Func<CancellationToken, Task<IdempotencyOperationOutcome<T>>> action,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        if (await ResolveReplayAsync<T>(tenantId, idempotencyKey, operation, requestFingerprint, now, cancellationToken) is { } fastReplay)
            return fastReplay;

        var ownsTransaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        IdempotencyRecord? reservation = null;
        try
        {
            if (ownsTransaction)
                transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            if (db.Database.IsRelational())
                await AcquireKeyLockAsync(tenantId, idempotencyKey, cancellationToken);

            now = UtcNow();
            if (await ResolveReplayAsync<T>(tenantId, idempotencyKey, operation, requestFingerprint, now, cancellationToken) is { } lockedReplay)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return lockedReplay;
            }

            await DeleteExpiredKeyAsync(tenantId, idempotencyKey, now, cancellationToken);
            reservation = new IdempotencyRecord
            {
                TenantId = tenantId,
                IdempotencyKey = idempotencyKey,
                Operation = operation,
                RequestFingerprintSha256 = requestFingerprint,
                Status = InProgressStatus,
                CreatedByUserId = AuthenticatedIdentity.AuditUserId(actor),
                CreatedByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor),
                StartedAtUtc = now,
                ExpiresAtUtc = now.AddDays(options.RetentionDays)
            };
            db.IdempotencyRecords.Add(reservation);
            await db.SaveChangesAsync(cancellationToken);

            var outcome = await action(cancellationToken);
            if (outcome.Result is null
                || string.IsNullOrWhiteSpace(outcome.ResourceType)
                || string.IsNullOrWhiteSpace(outcome.ResourceId)
                || outcome.ResourceType.Length > 160
                || outcome.ResourceId.Length > 200
                || outcome.HttpStatusCode is < 100 or > 599)
            {
                throw new InvalidOperationException("Idempotent command returned an invalid scalar result identity.");
            }

            var responseJson = JsonSerializer.Serialize(outcome.Result, JsonOptions);
            if (Encoding.UTF8.GetByteCount(responseJson) > options.MaxResponseBytes)
                throw new BusinessRuleException("Idempotent command response exceeds the retained replay limit.");

            var completedAt = UtcNow();
            reservation.Status = CompletedStatus;
            reservation.CompletedAtUtc = completedAt;
            reservation.ExpiresAtUtc = completedAt.AddDays(options.RetentionDays);
            reservation.ResultResourceType = outcome.ResourceType.Trim();
            reservation.ResultResourceId = outcome.ResourceId.Trim();
            reservation.ResultHttpStatusCode = outcome.HttpStatusCode;
            reservation.ResponseJson = responseJson;
            reservation.ResponseSha256 = Hash(responseJson);
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new IdempotencyExecution<T>(
                outcome.Result,
                WasReplay: false,
                reservation.Id,
                operation,
                reservation.ResultResourceType,
                reservation.ResultResourceId,
                outcome.HttpStatusCode,
                reservation.ExpiresAtUtc);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            if (!db.Database.IsRelational() && reservation is { Id: > 0 })
            {
                // EF's in-memory provider has no transaction support. Remove the transaction-only
                // reservation so unit-test retries model the production rollback semantics.
                try
                {
                    reservation.ExpiresAtUtc = DateTime.UtcNow.AddTicks(-10);
                    db.Entry(reservation).State = EntityState.Deleted;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the command exception; PostgreSQL remains the authoritative atomicity boundary.
                }
            }
            if (reservation is not null)
                db.Entry(reservation).State = EntityState.Detached;
            if (ownsTransaction)
                db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<IdempotencyExecution<T>?> ResolveReplayAsync<T>(
        int tenantId,
        string idempotencyKey,
        string operation,
        string requestFingerprint,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var retained = await db.IdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.TenantId == tenantId && record.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (retained is null || retained.ExpiresAtUtc <= now)
            return null;
        if (!string.Equals(retained.Operation, operation, StringComparison.Ordinal)
            || !string.Equals(retained.RequestFingerprintSha256, requestFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new IdempotencyConflictException(
                "This Idempotency-Key was already used for a different operation or request payload.");
        }
        if (!string.Equals(retained.Status, CompletedStatus, StringComparison.Ordinal)
            || retained.CompletedAtUtc is null
            || retained.ResultResourceType is null
            || retained.ResultResourceId is null
            || retained.ResultHttpStatusCode is null
            || retained.ResponseJson is null
            || retained.ResponseSha256 is null
            || !string.Equals(Hash(retained.ResponseJson), retained.ResponseSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IdempotencyConflictException(
                "The retained idempotency result is incomplete or failed its integrity check.");
        }

        T? result;
        try
        {
            result = JsonSerializer.Deserialize<T>(retained.ResponseJson, JsonOptions);
        }
        catch (JsonException)
        {
            throw new IdempotencyConflictException("The retained idempotency response could not be restored.");
        }
        if (result is null)
            throw new IdempotencyConflictException("The retained idempotency response could not be restored.");

        return new IdempotencyExecution<T>(
            result,
            WasReplay: true,
            retained.Id,
            retained.Operation,
            retained.ResultResourceType,
            retained.ResultResourceId,
            retained.ResultHttpStatusCode.Value,
            retained.ExpiresAtUtc);
    }

    private async Task DeleteExpiredKeyAsync(
        int tenantId,
        string idempotencyKey,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_records WHERE \"TenantId\" = {tenantId} AND \"IdempotencyKey\" = {idempotencyKey} AND \"ExpiresAtUtc\" <= CURRENT_TIMESTAMP",
                cancellationToken);
            return;
        }

        var expired = await db.IdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record => record.TenantId == tenantId
                && record.IdempotencyKey == idempotencyKey
                && record.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        db.IdempotencyRecords.RemoveRange(expired);
        if (expired.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AcquireKeyLockAsync(int tenantId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var lockId = AdvisoryLockId(tenantId, idempotencyKey);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockFamily}, {lockId})",
            cancellationToken);
    }

    internal static int AdvisoryLockId(int tenantId, string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{idempotencyKey}"));
        return BitConverter.ToInt32(bytes, 0);
    }

    internal static bool IsValidKey(string? key)
    {
        var normalized = key?.Trim() ?? string.Empty;
        return normalized.Length is >= 8 and <= 128
            && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
    }

    internal static string RequestFingerprint(string operation, object requestPayload)
    {
        // The original atomic onboarding endpoint retained this exact request hash before the
        // generic ledger existed. Keeping its established canonicalisation makes upgraded,
        // unexpired onboarding keys replayable instead of silently creating a second company.
        if (string.Equals(operation, IdempotencyOperations.CompanyOnboard, StringComparison.Ordinal)
            && requestPayload is CompanyOnboardingInput onboarding)
        {
            return Hash(JsonSerializer.Serialize(onboarding, LegacyCompanyOnboardingJsonOptions));
        }

        var payload = JsonSerializer.Serialize(requestPayload, requestPayload.GetType(), JsonOptions);
        return Hash(operation + "\n" + payload);
    }

    internal static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private DateTime UtcNow()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}

public sealed class IdempotencyRetentionService(
    AccountsDbContext db,
    IOptions<IdempotencyConfig> configuredOptions,
    TimeProvider timeProvider)
{
    private readonly IdempotencyConfig options = configuredOptions.Value;

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
        PurgeExpiredForTenantAsync(null, cancellationToken);

    public async Task<int> PurgeExpiredForTenantAsync(
        int? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId is <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ids = await db.IdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record => (tenantId == null || record.TenantId == tenantId)
                && record.ExpiresAtUtc <= now)
            .OrderBy(record => record.Id)
            .Take(options.CleanupBatchSize)
            .Select(record => record.Id)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0) return 0;

        if (db.Database.IsRelational())
            return await db.IdempotencyRecords.IgnoreQueryFilters().Where(record => ids.Contains(record.Id)).ExecuteDeleteAsync(cancellationToken);

        var records = await db.IdempotencyRecords.IgnoreQueryFilters().Where(record => ids.Contains(record.Id)).ToListAsync(cancellationToken);
        db.IdempotencyRecords.RemoveRange(records);
        return await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class IdempotencyRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IdempotencyConfig> configuredOptions,
    TimeProvider timeProvider,
    ILogger<IdempotencyRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(configuredOptions.Value.CleanupIntervalMinutes);
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                IReadOnlyList<int> tenantIds;
                await using (var discovery = scopeFactory.CreateAsyncScope())
                {
                    tenantIds = await discovery.ServiceProvider
                        .GetRequiredService<DatabaseTenantBootstrapResolver>()
                        .ListTenantIdsForJobsAsync(stoppingToken);
                }
                var purged = 0;
                foreach (var tenantId in tenantIds)
                {
                    await using var tenantScope = scopeFactory.CreateAsyncScope();
                    tenantScope.ServiceProvider.GetRequiredService<DatabaseTenantContext>().SetResolvedTenant(tenantId);
                    purged += await tenantScope.ServiceProvider.GetRequiredService<IdempotencyRetentionService>()
                        .PurgeExpiredForTenantAsync(tenantId, stoppingToken);
                }
                if (purged > 0)
                    logger.LogInformation("Purged {Count} expired idempotency records", purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Idempotency retention cleanup failed");
            }
        }
    }
}

public static class IdempotencyReplayPreflight
{
    public const string ReplayCandidateItemKey = "Accounts.IdempotencyReplayCandidate";

    public static async Task<bool> IsCompletedCandidateAsync(
        HttpContext context,
        AccountsDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (context.Items.TryGetValue(ReplayCandidateItemKey, out var cached) && cached is bool result)
            return result;
        var user = AuthContext.GetUser(context);
        var values = context.Request.Headers[IdempotencyHttpContract.RequestHeader];
        var key = values.Count == 1 ? values[0]?.Trim() : null;
        var candidate = user is not null
            && IdempotencyService.IsValidKey(key)
            && await db.IdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(record => record.TenantId == user.TenantId
                    && record.IdempotencyKey == key
                    && record.Status == IdempotencyService.CompletedStatus
                    && record.ExpiresAtUtc > DateTime.UtcNow,
                    cancellationToken);
        context.Items[ReplayCandidateItemKey] = candidate;
        return candidate;
    }
}
