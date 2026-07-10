using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using Xunit;

namespace Accounts.Tests;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task SameTenantKeyAndPayloadReplaysExactResultWithoutRunningCommandTwice()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db, "first");
        var actor = Actor(tenantId);
        var service = new IdempotencyService(db);
        var calls = 0;

        async Task<IdempotencyOperationOutcome<ScalarResult>> Command(CancellationToken cancellationToken)
        {
            calls++;
            var company = new Company
            {
                TenantId = tenantId,
                LegalName = "Idempotent Limited",
                IncorporationDate = new DateOnly(2025, 1, 1)
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync(cancellationToken);
            return new IdempotencyOperationOutcome<ScalarResult>(
                new ScalarResult(company.Id, company.LegalName),
                nameof(Company),
                company.Id.ToString(),
                StatusCodes.Status201Created);
        }

        var first = await service.ExecuteAsync(
            tenantId,
            "company-create-unit-0001",
            IdempotencyOperations.CompanyCreate,
            new { legalName = "Idempotent Limited" },
            actor,
            Command);
        var replay = await service.ExecuteAsync(
            tenantId,
            "company-create-unit-0001",
            IdempotencyOperations.CompanyCreate,
            new { legalName = "Idempotent Limited" },
            actor,
            Command);

        Assert.False(first.WasReplay);
        Assert.True(replay.WasReplay);
        Assert.Equal(first.RecordId, replay.RecordId);
        Assert.Equal(first.Result, replay.Result);
        Assert.Equal(StatusCodes.Status201Created, replay.HttpStatusCode);
        Assert.Equal(1, calls);
        Assert.Single(await db.Companies.IgnoreQueryFilters().ToListAsync());
        var retained = await db.IdempotencyRecords.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(IdempotencyOperations.CompanyCreate, retained.Operation);
        Assert.Equal("Completed", retained.Status);
        Assert.Equal(64, retained.RequestFingerprintSha256.Length);
        Assert.Equal(64, retained.ResponseSha256?.Length);
    }

    [Fact]
    public async Task SameTenantKeyWithDifferentPayloadOrOperationConflicts()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db, "conflict");
        var actor = Actor(tenantId);
        var service = new IdempotencyService(db);
        await service.ExecuteAsync(
            tenantId,
            "shared-command-key-0001",
            IdempotencyOperations.CompanyCreate,
            new { legalName = "Original Limited" },
            actor,
            _ => Task.FromResult(new IdempotencyOperationOutcome<ScalarResult>(
                new ScalarResult(10, "Original Limited"), nameof(Company), "10")));

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.ExecuteAsync(
            tenantId,
            "shared-command-key-0001",
            IdempotencyOperations.CompanyCreate,
            new { legalName = "Changed Limited" },
            actor,
            _ => Task.FromResult(new IdempotencyOperationOutcome<ScalarResult>(
                new ScalarResult(11, "Changed Limited"), nameof(Company), "11"))));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.ExecuteAsync(
            tenantId,
            "shared-command-key-0001",
            IdempotencyOperations.PeriodCreate,
            new { legalName = "Original Limited" },
            actor,
            _ => Task.FromResult(new IdempotencyOperationOutcome<ScalarResult>(
                new ScalarResult(12, "period"), "AccountingPeriod", "12"))));
    }

    [Fact]
    public async Task FailedAttemptDoesNotPoisonAValidRetry()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db, "retry");
        var actor = Actor(tenantId);
        var service = new IdempotencyService(db);
        const string key = "retry-after-failure-0001";

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync<ScalarResult>(
            tenantId,
            key,
            IdempotencyOperations.CompanyCreate,
            new { legalName = "Retry Limited" },
            actor,
            _ => throw new InvalidOperationException("deterministic transient failure")));
        Assert.Empty(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());

        var retry = await service.ExecuteAsync(
            tenantId,
            key,
            IdempotencyOperations.CompanyCreate,
            new { legalName = "Retry Limited" },
            actor,
            _ => Task.FromResult(new IdempotencyOperationOutcome<ScalarResult>(
                new ScalarResult(20, "Retry Limited"), nameof(Company), "20")));

        Assert.False(retry.WasReplay);
        Assert.Equal(20, retry.Result.Id);
        Assert.Single(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task TenantScopeAllowsSameKeyButRejectsActorTenantMismatch()
    {
        await using var db = CreateDb();
        var tenantOne = await SeedTenantAsync(db, "tenant-one");
        var tenantTwo = await SeedTenantAsync(db, "tenant-two");
        var service = new IdempotencyService(db);
        const string key = "tenant-shared-key-0001";

        var first = await ExecuteScalarAsync(service, tenantOne, key, Actor(tenantOne), 1);
        var second = await ExecuteScalarAsync(service, tenantTwo, key, Actor(tenantTwo), 2);

        Assert.False(first.WasReplay);
        Assert.False(second.WasReplay);
        Assert.Equal(2, await db.IdempotencyRecords.IgnoreQueryFilters().CountAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteScalarAsync(
            service,
            tenantOne,
            "tenant-mismatch-key-0001",
            Actor(tenantTwo),
            3));
    }

    [Fact]
    public async Task RetentionDeletesOnlyExpiredEvidenceAndHeaderContractIsStrict()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db, "retention");
        var past = new FixedTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new IdempotencyConfig
        {
            RetentionDays = 1,
            CleanupIntervalMinutes = 60,
            CleanupBatchSize = 10,
            MaxResponseBytes = 1024 * 1024
        });
        var service = new IdempotencyService(db, options, past);
        await ExecuteScalarAsync(service, tenantId, "expired-retention-key-0001", Actor(tenantId), 1);

        var retention = new IdempotencyRetentionService(
            db,
            options,
            new FixedTimeProvider(new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(1, await retention.PurgeExpiredAsync());
        Assert.Empty(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());

        var context = new DefaultHttpContext();
        Assert.False(IdempotencyHttpContract.TryRead(context, out _, out var missing));
        Assert.NotNull(missing);
        context.Request.Headers[IdempotencyHttpContract.RequestHeader] =
            new Microsoft.Extensions.Primitives.StringValues(["valid-header-key-0001", "second-key-0001"]);
        Assert.False(IdempotencyHttpContract.TryRead(context, out _, out var duplicate));
        Assert.NotNull(duplicate);
        context.Request.Headers[IdempotencyHttpContract.RequestHeader] = "valid-header-key-0001";
        Assert.True(IdempotencyHttpContract.TryRead(context, out var parsed, out var error));
        Assert.Equal("valid-header-key-0001", parsed.Key);
        Assert.Null(error);
    }

    [Fact]
    public async Task CompletedKeyBypassesStaleEtagAndNewlyLockedPeriodPreflights()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db, "preflight");
        var actor = Actor(tenantId);
        var company = Company(tenantId, "Preflight Limited");
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Finalised,
            LockedAt = DateTime.UtcNow,
            LockedBy = "Owner User"
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        const string key = "completed-preflight-key-0001";
        await ExecuteScalarAsync(new IdempotencyService(db), tenantId, key, actor, 42);

        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = actor;
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{company.Id}/periods/{period.Id}/filing/revenue-status";
        context.Request.Headers.IfMatch = "\"stale-token\"";
        context.Request.Headers[IdempotencyHttpContract.RequestHeader] = key;
        var etagNext = 0;
        var etag = new PeriodConcurrencyMiddleware(_ =>
        {
            etagNext++;
            return Task.CompletedTask;
        });
        await etag.InvokeAsync(context, new PeriodConcurrencyTokenService(db), db);
        Assert.Equal(1, etagNext);

        context.Items.Remove(IdempotencyReplayPreflight.ReplayCandidateItemKey);
        var lockNext = 0;
        var periodLock = new PeriodLockMiddleware(_ =>
        {
            lockNext++;
            return Task.CompletedTask;
        });
        await periodLock.InvokeAsync(context, db);
        Assert.Equal(1, lockNext);
    }

    [Fact]
    public async Task AuditTrailDoesNotCreateAnotherTransportAuditForReplay()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db, "audit-replay");
        var actor = Actor(tenantId);
        var company = Company(tenantId, "Replay Audit Limited");
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = actor;
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{company.Id}";
        context.Response.Body = new MemoryStream();
        var middleware = new AuditTrailMiddleware(nextContext =>
        {
            nextContext.Response.StatusCode = StatusCodes.Status200OK;
            nextContext.Response.Headers[IdempotencyHttpContract.ReplayedHeader] = "true";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db, new AuditService(db));

        Assert.Empty(await db.AuditLogs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void ProductionWiringCoversEveryRequiredCommandAndKeepsLegacyOnboardingHistoricalOnly()
    {
        var root = RepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Services", "IdempotencyService.cs"));
        var company = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyEndpoints.cs"));
        var onboarding = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Services", "CompanyOnboardingService.cs"));
        var banking = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "BankingEndpoints.cs"));
        var documents = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "DocumentEndpoints.cs"));
        var filing = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "FilingWorkflowEndpoints.cs"));
        var periodStatus = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"));
        var deadline = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "DeadlineEndpoints.cs"));
        var etag = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Middleware", "PeriodConcurrencyMiddleware.cs"));
        var periodLock = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Middleware", "PeriodLockMiddleware.cs"));
        var audit = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Middleware", "AuditTrailMiddleware.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Data", "Migrations", "20260711010000_AddTenantScopedIdempotencyLedger.cs"));

        Assert.Contains("pg_advisory_xact_lock", service);
        Assert.DoesNotContain("PostgresException", service);
        Assert.Contains("IdempotencyOperations.CompanyCreate", company);
        Assert.Contains("IdempotencyOperations.PeriodCreate", company);
        Assert.Contains("IdempotencyOperations.CompanyOnboard", onboarding);
        Assert.DoesNotContain("CompanyOnboardingRequests", onboarding);
        Assert.Contains("IdempotencyOperations.BankImport", banking);
        Assert.Contains("IdempotencyOperations.CroAccountsGenerate", documents);
        Assert.Contains("IdempotencyOperations.CroSignatureGenerate", documents);
        foreach (var marker in new[]
        {
            "IdempotencyOperations.CroStatus",
            "IdempotencyOperations.CroPayment",
            "IdempotencyOperations.CharityReportGenerated",
            "IdempotencyOperations.CharityStatus",
            "IdempotencyOperations.RevenueExternalValidation",
            "IdempotencyOperations.RevenueStatus",
            "IdempotencyOperations.RevenueIxbrlValidation"
        })
        {
            Assert.Contains(marker, filing);
        }
        Assert.Contains("IdempotencyOperations.PeriodStatus", periodStatus);
        Assert.Contains("IdempotencyOperations.DeadlineMarkFiled", deadline);
        Assert.Contains("IdempotencyReplayPreflight.IsCompletedCandidateAsync", etag);
        Assert.Contains("IdempotencyReplayPreflight.IsCompletedCandidateAsync", periodLock);
        Assert.Contains("IdempotencyHttpContract.ReplayedHeader", audit);
        Assert.Contains("FROM company_onboarding_requests AS legacy", migration);
        Assert.Contains("accounts_protect_idempotency_record", migration);
        Assert.Contains("OLD.\"ExpiresAtUtc\" <= CURRENT_TIMESTAMP", migration);
    }

    private static Task<IdempotencyExecution<ScalarResult>> ExecuteScalarAsync(
        IdempotencyService service,
        int tenantId,
        string key,
        AuthenticatedUser actor,
        int id) =>
        service.ExecuteAsync(
            tenantId,
            key,
            IdempotencyOperations.CompanyCreate,
            new { id },
            actor,
            _ => Task.FromResult(new IdempotencyOperationOutcome<ScalarResult>(
                new ScalarResult(id, $"Result {id}"), nameof(Company), id.ToString())));

    private static AccountsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFilePath) })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var directory = new DirectoryInfo(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yml")))
                directory = directory.Parent;
            if (directory is not null) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static Company Company(int tenantId, string name) => new()
    {
        TenantId = tenantId,
        LegalName = name,
        IncorporationDate = new DateOnly(2025, 1, 1),
        FinancialYearStartMonth = 1
    };

    private static async Task<int> SeedTenantAsync(AccountsDbContext db, string suffix)
    {
        var tenant = new Tenant { Name = $"Idempotency {suffix}", Slug = $"idem-{suffix}-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static AuthenticatedUser Actor(int tenantId) => new(
        100,
        tenantId,
        $"Tenant {tenantId}",
        $"owner-{tenantId}@example.ie",
        $"Owner {tenantId}",
        "Owner");

    public sealed record ScalarResult(int Id, string Name);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
