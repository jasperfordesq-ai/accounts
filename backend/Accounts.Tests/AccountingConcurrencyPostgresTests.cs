using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Accounts.Tests;

public sealed class AccountingConcurrencyPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private static int nextIsolatedCompanyId = 2_000_000;
    private static int nextIsolatedPeriodId = 1_000_000;
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = $"accounting_concurrency_{Guid.NewGuid():N}";
    private string? scopedConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await create.ExecuteNonQueryAsync();
        }

        scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        }.ConnectionString;

        await using var db = CreateDb(protectWrites: false);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task WritePausedAfterRead_FinalisationWinsAndFinancialRowCannotChange()
    {
        var seeded = await SeedAsync();
        var writerHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerMayAttemptSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerCallingSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerApplicationName = $"accounting-race-writer-{Guid.NewGuid():N}";

        var writer = Task.Run(async () =>
        {
            await using var writeDb = CreateDb(applicationName: writerApplicationName);
            var debtor = await writeDb.Debtors.SingleAsync(item => item.Id == seeded.DebtorId);
            writerHasRead.SetResult();
            await writerMayAttemptSave.Task.WaitAsync(TimeSpan.FromSeconds(15));

            debtor.Amount = 999_999m;
            writerCallingSave.SetResult();
            try
            {
                await writeDb.SaveChangesAsync();
                return (Succeeded: true, Error: (Exception?)null);
            }
            catch (Exception error)
            {
                return (Succeeded: false, Error: error);
            }
        });

        await writerHasRead.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var finalisationSucceeded = false;
        try
        {
            await using var finaliseDb = CreateDb();
            var coordinator = new AccountingConcurrencyCoordinator(finaliseDb);
            await using var lease = await coordinator.AcquirePeriodAsync(
                seeded.CompanyId,
                seeded.PeriodId);
            var period = await finaliseDb.AccountingPeriods.SingleAsync(item => item.Id == seeded.PeriodId);
            period.Status = PeriodStatus.Finalised;
            period.LockedAt = DateTime.UtcNow;
            period.LockedBy = "qualified.accountant@example.ie";
            await finaliseDb.SaveChangesAsync();

            // The writer now resumes from its stale initial read while finalisation still owns the
            // advisory lock and its status update remains uncommitted. PostgreSQL must report that
            // second physical connection waiting on the advisory lock before finalisation commits.
            writerMayAttemptSave.SetResult();
            await writerCallingSave.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(await WaitForAdvisoryLockWaitAsync(writerApplicationName));
            Assert.False(writer.IsCompleted);

            await lease.CommitIfOwnedAsync();
            finalisationSucceeded = true;
        }
        finally
        {
            writerMayAttemptSave.TrySetResult();
        }

        var writeOutcome = await writer.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(finalisationSucceeded);
        Assert.False(writeOutcome.Succeeded);
        Assert.IsType<AccountingConcurrencyException>(writeOutcome.Error);

        await using var verifyDb = CreateDb(protectWrites: false);
        var storedPeriod = await verifyDb.AccountingPeriods.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.PeriodId);
        var storedDebtor = await verifyDb.Debtors.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DebtorId);
        var storedPackage = await verifyDb.CroFilingPackages.AsNoTracking()
            .SingleAsync(item => item.PeriodId == seeded.PeriodId);

        Assert.Equal(PeriodStatus.Finalised, storedPeriod.Status);
        Assert.Equal(seeded.DebtorAmount, storedDebtor.Amount);
        Assert.Equal(seeded.ApprovedManifest, storedPackage.ApprovedArtifactManifestSha256);
    }

    [PostgresFact]
    public async Task PeriodStatusEndpoint_WaitsForThePeriodLeaseBeforeExecutingTheCommand()
    {
        var seeded = await SeedAsync();
        await using var blockerDb = CreateDb(protectWrites: false);
        await using var blockerLease = await new AccountingConcurrencyCoordinator(blockerDb)
            .AcquirePeriodAsync(seeded.CompanyId, seeded.PeriodId);
        var applicationName = $"period-status-endpoint-{Guid.NewGuid():N}";

        var endpointTask = Task.Run(async () =>
        {
            await using var endpointDb = CreateDb(applicationName: applicationName);
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = $"/api/companies/{seeded.CompanyId}/periods/{seeded.PeriodId}/status";
            context.Request.Headers[IdempotencyHttpContract.RequestHeader] = $"period-status-{Guid.NewGuid():N}";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                7,
                seeded.TenantId,
                "Concurrency Test Firm",
                "owner@example.invalid",
                "Owner User",
                "Owner");

            return await PeriodStatusEndpoint.UpdateAsync(
                seeded.CompanyId,
                seeded.PeriodId,
                new PeriodStatusUpdate(PeriodStatus.Draft, null, null),
                endpointDb,
                new AuditService(endpointDb),
                new FinancialStatementsService(endpointDb),
                context,
                new ApiAccessService(
                    Options.Create(new ApiAccessConfig { Enabled = false }),
                    new TestEnvironment()));
        });

        var completedWhileLeaseHeld = await Task.WhenAny(endpointTask, Task.Delay(500)) == endpointTask;
        await blockerLease.CommitIfOwnedAsync();
        var result = await endpointTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(completedWhileLeaseHeld);
        Assert.Equal(
            StatusCodes.Status200OK,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
                ?? StatusCodes.Status200OK);
    }

    [PostgresFact]
    public async Task ConcurrentDeadlineCalculations_SerializeToOneConsistentRowPerType()
    {
        var seeded = await SeedAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<List<FilingDeadline>> CalculateAsync()
        {
            await start.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var calculationDb = CreateDb();
            return await new DeadlineService(calculationDb)
                .CalculateDeadlinesAsync(seeded.CompanyId, seeded.PeriodId, "user:7");
        }

        var first = CalculateAsync();
        var second = CalculateAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));

        await using var verifyDb = CreateDb(protectWrites: false);
        var stored = await verifyDb.FilingDeadlines.AsNoTracking()
            .Where(item => item.CompanyId == seeded.CompanyId && item.PeriodId == seeded.PeriodId)
            .OrderBy(item => item.DeadlineType)
            .ToListAsync();

        Assert.Equal(2, stored.Count);
        Assert.Equal([DeadlineType.CRO, DeadlineType.Revenue], stored.Select(item => item.DeadlineType));
        Assert.All(stored, item => Assert.Equal(64, item.CalculationFingerprintSha256?.Length));
        Assert.All(results, result => Assert.Equal(2, result.Count));
        foreach (var result in results)
        {
            var byType = result.ToDictionary(item => item.DeadlineType);
            Assert.All(stored, item =>
            {
                Assert.Equal(item.Id, byType[item.DeadlineType].Id);
                Assert.Equal(item.DueDate, byType[item.DeadlineType].DueDate);
                Assert.Equal(item.CalculationFingerprintSha256, byType[item.DeadlineType].CalculationFingerprintSha256);
            });
        }
    }

    [PostgresFact]
    public async Task StaleMutableRow_IsRejectedAndPeriodWideETagAdvancesForWinningWrite()
    {
        var seeded = await SeedAsync();
        await using var firstDb = CreateDb();
        await using var staleDb = CreateDb();
        var first = await firstDb.Debtors.SingleAsync(item => item.Id == seeded.DebtorId);
        var stale = await staleDb.Debtors.SingleAsync(item => item.Id == seeded.DebtorId);

        var before = await new PeriodConcurrencyTokenService(firstDb)
            .GetAsync(seeded.CompanyId, seeded.PeriodId);
        first.Amount = 43_210m;
        await firstDb.SaveChangesAsync();

        await using var tokenDb = CreateDb(protectWrites: false);
        var after = await new PeriodConcurrencyTokenService(tokenDb)
            .GetAsync(seeded.CompanyId, seeded.PeriodId);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before, after);

        stale.Name = "Stale overwrite attempt";
        var conflict = await Assert.ThrowsAsync<AccountingConcurrencyException>(
            () => staleDb.SaveChangesAsync());
        Assert.Equal("accounting_concurrency_conflict", conflict.Code);
        Assert.Equal(after, conflict.CurrentETag);

        tokenDb.ChangeTracker.Clear();
        var stored = await tokenDb.Debtors.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DebtorId);
        Assert.Equal(43_210m, stored.Amount);
        Assert.Equal("Trade debtor", stored.Name);
    }

    [PostgresFact]
    public async Task StaleIfMatch_ReturnsSafe409WithReloadAndReconcileContract()
    {
        var seeded = await SeedAsync();
        await using var db = CreateDb(protectWrites: false);
        var current = await new PeriodConcurrencyTokenService(db)
            .GetAsync(seeded.CompanyId, seeded.PeriodId);
        Assert.NotNull(current);

        var nextInvoked = false;
        var middleware = new PeriodConcurrencyMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "accounting-conflict-test",
            Response = { Body = new MemoryStream() }
        };
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{seeded.CompanyId}/periods/{seeded.PeriodId}/debtors/{seeded.DebtorId}";
        context.Request.Headers.IfMatch = "\"stale-period-version\"";

        await middleware.InvokeAsync(context, new PeriodConcurrencyTokenService(db), db);

        Assert.False(nextInvoked);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(current, context.Response.Headers.ETag.ToString());
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;
        Assert.Equal(AccountingConflict.SafeMessage, root.GetProperty("error").GetString());
        Assert.Equal("accounting_concurrency_conflict", root.GetProperty("code").GetString());
        Assert.True(root.GetProperty("reloadRequired").GetBoolean());
        Assert.True(root.GetProperty("reconcileRequired").GetBoolean());
        Assert.Equal("accounting-conflict-test", root.GetProperty("correlationId").GetString());

        var responseText = root.GetRawText();
        Assert.DoesNotContain("xmin", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debtor", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task QuarantinedCompanyWithNoPeriods_StillRejectsCompanyOwnedAccountingWrite()
    {
        var companyId = Interlocked.Increment(ref nextIsolatedCompanyId);
        int bankAccountId;
        await using (var seedDb = CreateDb(protectWrites: false))
        {
            var company = new Company
            {
                Id = companyId,
                Tenant = new Tenant
                {
                    Name = "Zero-period quarantine firm",
                    Slug = $"zero-period-quarantine-{Guid.NewGuid():N}"
                },
                LegalName = "Zero Period Quarantine Limited",
                CompanyType = CompanyType.Private,
                IncorporationDate = new DateOnly(2026, 1, 1),
                AnnualReturnDate = new DateOnly(2024, 1, 15)
            };
            var bank = new BankAccount
            {
                Company = company,
                Name = "Current account"
            };
            seedDb.BankAccounts.Add(bank);
            await seedDb.SaveChangesAsync();
            bankAccountId = bank.Id;
        }

        await using var writerDb = CreateDb();
        var staleBank = await writerDb.BankAccounts.SingleAsync(bank => bank.Id == bankAccountId);
        await using (var quarantineDb = CreateDb(protectWrites: false))
        {
            var occurredAt = DateTime.UtcNow;
            var evidenceHash = new string('b', 64);
            await quarantineDb.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE companies
                SET "IsQuarantined" = TRUE,
                    "QuarantinedAtUtc" = {occurredAt},
                    "QuarantinedByUserId" = {"owner@example.ie"},
                    "QuarantinedByDisplayName" = {"Owner"},
                    "QuarantineReason" = {"Deterministic no-period write-boundary test."},
                    "QuarantineEvidenceSha256" = {evidenceHash}
                WHERE "Id" = {companyId}
                """);
        }

        staleBank.Name = "Forbidden renamed account";
        var conflict = await Assert.ThrowsAsync<AccountingConcurrencyException>(
            () => writerDb.SaveChangesAsync());
        Assert.Equal(AccountingConflict.QuarantinedSafeMessage, conflict.Message);

        await using var verifyDb = CreateDb(protectWrites: false);
        Assert.False(await verifyDb.AccountingPeriods.IgnoreQueryFilters()
            .AnyAsync(period => period.CompanyId == companyId));
        Assert.Equal(
            "Current account",
            await verifyDb.BankAccounts.IgnoreQueryFilters()
                .Where(bank => bank.Id == bankAccountId)
                .Select(bank => bank.Name)
                .SingleAsync());
    }

    [PostgresFact]
    public async Task FutureDatedCompanyAccountingData_RemainsWritableAfterHistoricPeriodFinalisation()
    {
        var seeded = await SeedAsync();
        await using (var lockDb = CreateDb(protectWrites: false))
        {
            var period = await lockDb.AccountingPeriods.SingleAsync(item => item.Id == seeded.PeriodId);
            period.Status = PeriodStatus.Finalised;
            period.LockedAt = DateTime.UtcNow;
            period.LockedBy = "qualified.accountant@example.ie";
            await lockDb.SaveChangesAsync();
        }

        await using (var futureDb = CreateDb())
        {
            futureDb.FixedAssets.Add(new FixedAsset
            {
                CompanyId = seeded.CompanyId,
                Name = "Next-period computer",
                Category = "Computer equipment",
                Cost = 2_000m,
                ResidualValue = 0m,
                AcquisitionDate = new DateOnly(2026, 1, 1),
                UsefulLifeYears = 3
            });
            futureDb.AccountCategories.Add(new AccountCategory
            {
                CompanyId = seeded.CompanyId,
                Code = "7999",
                Name = "Next-period expense",
                Type = AccountCategoryType.Expense
            });
            await futureDb.SaveChangesAsync();
        }

        await using (var historicDb = CreateDb())
        {
            historicDb.FixedAssets.Add(new FixedAsset
            {
                CompanyId = seeded.CompanyId,
                Name = "Historic computer",
                Category = "Computer equipment",
                Cost = 2_000m,
                ResidualValue = 0m,
                AcquisitionDate = new DateOnly(2025, 6, 1),
                UsefulLifeYears = 3
            });
            await Assert.ThrowsAsync<AccountingConcurrencyException>(() => historicDb.SaveChangesAsync());
        }

        await using var verifyDb = CreateDb(protectWrites: false);
        Assert.True(await verifyDb.FixedAssets.AnyAsync(asset =>
            asset.CompanyId == seeded.CompanyId && asset.Name == "Next-period computer"));
        Assert.True(await verifyDb.AccountCategories.AnyAsync(category =>
            category.CompanyId == seeded.CompanyId && category.Code == "7999"));
        Assert.False(await verifyDb.FixedAssets.AnyAsync(asset =>
            asset.CompanyId == seeded.CompanyId && asset.Name == "Historic computer"));
    }

    [Fact]
    public async Task SaveInterceptorConflict_IsMappedToTheSameSafe409Contract()
    {
        const string currentETag = "\"current-safe-etag\"";
        var middleware = new ExceptionMiddleware(
            _ => throw new AccountingConcurrencyException("database detail must not escape", currentETag),
            NullLogger<ExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "interceptor-conflict-test",
            Response = { Body = new MemoryStream() }
        };

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(currentETag, context.Response.Headers.ETag.ToString());
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;
        Assert.Equal(AccountingConflict.SafeMessage, root.GetProperty("error").GetString());
        Assert.True(root.GetProperty("reloadRequired").GetBoolean());
        Assert.True(root.GetProperty("reconcileRequired").GetBoolean());
        Assert.DoesNotContain("database detail", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task ImmediateSecondSave_UsesPostgresTimestampPrecisionWithoutFalseConflict()
    {
        await using var db = CreateDb();
        var tenant = new Tenant
        {
            Name = "Timestamp precision test firm",
            Slug = $"timestamp-precision-{Guid.NewGuid():N}"
        };
        var company = new Company
        {
            Id = Interlocked.Increment(ref nextIsolatedCompanyId),
            Tenant = tenant,
            LegalName = "Timestamp Precision Test Limited",
            CroNumber = Guid.NewGuid().ToString("N")[..20],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2025, 9, 15),
            IsTrading = true
        };
        var period = new AccountingPeriod
        {
            Id = Interlocked.Increment(ref nextIsolatedPeriodId),
            Company = company,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.Add(period);
        await db.SaveChangesAsync();

        period.Status = PeriodStatus.Filed;
        period.LockedAt = DateTime.UtcNow;
        period.LockedBy = "seed.workflow@accounts.local";
        await db.SaveChangesAsync();

        Assert.Equal(PeriodStatus.Filed, period.Status);
    }

    private AccountsDbContext CreateDb(bool protectWrites = true, string? applicationName = null)
    {
        var connectionString = ScopedConnectionString;
        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            connectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = applicationName
            }.ConnectionString;
        }
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(connectionString);
        if (protectWrites)
            options.AddInterceptors(new AccountingConcurrencyInterceptor());
        return new AccountsDbContext(options.Options);
    }

    private async Task<bool> WaitForAdvisoryLockWaitAsync(string applicationName)
    {
        // This third connection is observation-only; the race itself still has exactly two
        // operation connections (writer and finaliser).
        await using var observer = new NpgsqlConnection(baseConnectionString!);
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = observer.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_locks AS lock
                    INNER JOIN pg_stat_activity AS activity ON activity.pid = lock.pid
                    WHERE activity.application_name = @applicationName
                      AND lock.locktype = 'advisory'
                      AND NOT lock.granted
                )
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "applicationName";
            parameter.Value = applicationName;
            command.Parameters.Add(parameter);
            if (await command.ExecuteScalarAsync() is true) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private async Task<SeededAccountingData> SeedAsync()
    {
        await using var db = CreateDb(protectWrites: false);
        var tenant = new Tenant
        {
            Name = "Accounting concurrency test firm",
            Slug = $"accounting-concurrency-{Guid.NewGuid():N}"
        };
        var company = new Company
        {
            Id = Interlocked.Increment(ref nextIsolatedCompanyId),
            Tenant = tenant,
            LegalName = "Accounting Concurrency Test Limited",
            CroNumber = Guid.NewGuid().ToString("N")[..20],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true
        };
        var period = new AccountingPeriod
        {
            // PostgreSQL advisory locks are database-wide, while these tests isolate tables by
            // schema. Use a process-unique period key so parallel schema tests cannot share a lock.
            Id = Interlocked.Increment(ref nextIsolatedPeriodId),
            Company = company,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        const decimal debtorAmount = 12_345m;
        var debtor = new Debtor
        {
            Period = period,
            Name = "Trade debtor",
            Amount = debtorAmount,
            Type = DebtorType.Trade
        };
        var approvedManifest = new string('a', 64);
        var package = new CroFilingPackage
        {
            Period = period,
            Status = FilingPackageStatus.Generated,
            ApprovedBy = "qualified.accountant@example.ie",
            ApprovedAt = DateTime.UtcNow,
            ApprovedArtifactManifestSha256 = approvedManifest,
            ApprovedReleaseCandidate = "race-test-candidate"
        };
        db.AddRange(debtor, package);
        await db.SaveChangesAsync();
        return new SeededAccountingData(
            tenant.Id,
            company.Id,
            period.Id,
            debtor.Id,
            debtorAmount,
            approvedManifest);
    }

    private string ScopedConnectionString => scopedConnectionString
        ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required for PostgreSQL integration tests.");

    private sealed record SeededAccountingData(
        int TenantId,
        int CompanyId,
        int PeriodId,
        int DebtorId,
        decimal DebtorAmount,
        string ApprovedManifest);

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class AccountingConcurrencyCoverageTests
{
    [Fact]
    public void EveryMutableDomainEntity_IsCoveredByThePersistenceConcurrencyBoundary()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"accounting-concurrency-coverage-{Guid.NewGuid():N}")
            .Options;
        using var db = new AccountsDbContext(options);
        var independentlyControlledTypes = new HashSet<Type>
        {
            typeof(Tenant),
            typeof(UserAccount),
            typeof(UserCompanyAccess),
            typeof(AuditLog),
            typeof(AuditIntegrityCheckpoint),
            typeof(CompanyQuarantineEvent),
            // These records are append-only or have their own database-enforced state machine,
            // compare-and-swap lease, idempotency, or identity lifecycle boundary. They must not
            // be coupled to an accounting-period advisory lock.
            typeof(AnnualReturnDateRecord),
            typeof(CompanyOnboardingRequest),
            typeof(DeadlineReminderOutbox),
            typeof(ExternalFilingHandoffSnapshot),
            typeof(ExternalFilingOutcomeEvent),
            typeof(FilingAuthorityEngagement),
            typeof(IdempotencyRecord),
            typeof(LoginSecurityEvent),
            typeof(PlatformJobRun),
            typeof(PrivacyIncidentExercise),
            typeof(PrivacySubjectRequest),
            typeof(UserActionToken),
            typeof(UserLifecycleEvent),
            typeof(UserMfaChallenge),
            typeof(UserMfaCredential),
            typeof(UserMfaRecoveryCode)
        };
        var mutableDomainTypes = db.Model.GetEntityTypes()
            .Select(entity => entity.ClrType)
            .Where(type => !independentlyControlledTypes.Contains(type))
            .ToHashSet();
        var protectedTypes = Assert.IsAssignableFrom<IEnumerable<Type>>(
            typeof(AccountingConcurrencyInterceptor)
                .GetField("ProtectedTypes", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null))
            .ToHashSet();

        Assert.True(
            mutableDomainTypes.IsSubsetOf(protectedTypes),
            $"Mutable domain entities missing concurrency protection: {string.Join(", ", mutableDomainTypes.Except(protectedTypes).Select(type => type.Name).Order())}");
    }

    [Fact]
    public void CompositionPolicy_RegistersInterceptorAndOrdersRequestPreflights()
    {
        // This is deliberately a composition-policy invariant. Observable locking, endpoint waiting,
        // concurrent deadline upserts and conflict responses are covered by the PostgreSQL tests above.
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        Assert.Contains(".AddInterceptors(", program);
        Assert.Contains("services.GetRequiredService<AccountingConcurrencyInterceptor>()", program);
        var ownership = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.PeriodOwnershipMiddleware>", StringComparison.Ordinal);
        var concurrency = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.PeriodConcurrencyMiddleware>", StringComparison.Ordinal);
        var lockPreflight = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.PeriodLockMiddleware>", StringComparison.Ordinal);
        Assert.True(ownership >= 0 && ownership < concurrency);
        Assert.True(concurrency < lockPreflight);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(sourceFilePath)!,
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory()
                 }.Distinct())
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "backend", "Accounts.Api")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
