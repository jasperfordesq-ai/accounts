using System.Data.Common;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class CompanyOnboardingPostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [PostgresFact]
    public async Task ConcurrentDuplicateAndRetryCreateExactlyOneCompleteCompanyAggregate()
    {
        await using var schema = await PostgresSchema.CreateAsync();
        var setupOptions = Options(schema.ConnectionString);
        int tenantId;
        await using (var setup = new AccountsDbContext(setupOptions))
        {
            await setup.Database.MigrateAsync();
            tenantId = await SeedTenantAsync(setup);
        }

        const string key = "postgres-onboarding-concurrency-0001";
        async Task<CompanyOnboardingResult> SubmitAsync()
        {
            await using var db = new AccountsDbContext(Options(schema.ConnectionString));
            return await Service(db).CreateAsync(ValidInput(), key, Actor(tenantId));
        }

        var results = await Task.WhenAll(Task.Run(SubmitAsync), Task.Run(SubmitAsync));
        Assert.Single(results, result => !result.WasReplay);
        Assert.Single(results, result => result.WasReplay);
        Assert.Equal(results[0].Outcome.CompanyId, results[1].Outcome.CompanyId);
        Assert.Equal(results[0].Outcome.FirstPeriodId, results[1].Outcome.FirstPeriodId);
        Assert.Equal(results[0].Outcome.OpeningBankAccountId, results[1].Outcome.OpeningBankAccountId);

        await using (var retryDb = new AccountsDbContext(Options(schema.ConnectionString)))
        {
            var retry = await Service(retryDb).CreateAsync(ValidInput(), key, Actor(tenantId));
            Assert.True(retry.WasReplay);
            Assert.Equal(results[0].Outcome.CompanyId, retry.Outcome.CompanyId);
        }

        await using var verify = new AccountsDbContext(Options(schema.ConnectionString));
        Assert.Equal(1, await verify.Companies.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await verify.AccountingPeriods.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await verify.BankAccounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await verify.CompanyOfficers.IgnoreQueryFilters().CountAsync());
        Assert.Equal(results[0].Outcome.CategoryCount, await verify.AccountCategories.IgnoreQueryFilters().CountAsync());
        var retained = await verify.IdempotencyRecords.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Completed", retained.Status);
        Assert.Equal(results[0].Outcome.CompanyId.ToString(), retained.ResultResourceId);
        Assert.Single(await verify.AuditLogs.Where(log => log.Action == AuditEventCodes.CompanyOnboarded).ToListAsync());
    }

    [PostgresFact]
    public async Task OfficerPersistenceFailureRollsBackReservationCompanyPeriodBankAndCategories()
    {
        await using var schema = await PostgresSchema.CreateAsync();
        var setupOptions = Options(schema.ConnectionString);
        int tenantId;
        await using (var setup = new AccountsDbContext(setupOptions))
        {
            await setup.Database.MigrateAsync();
            tenantId = await SeedTenantAsync(setup);
        }

        var fault = new OfficerInsertFailureInterceptor();
        await using (var failing = new AccountsDbContext(Options(schema.ConnectionString, fault)))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => Service(failing).CreateAsync(
                ValidInput(),
                "postgres-onboarding-rollback-0001",
                Actor(tenantId)));
            Assert.Contains("deterministic officer persistence failure", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.True(fault.Triggered);

        await using var verify = new AccountsDbContext(Options(schema.ConnectionString));
        Assert.Empty(await verify.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.AccountingPeriods.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.BankAccounts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.AccountCategories.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.CompanyOfficers.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.AuditLogs.IgnoreQueryFilters().ToListAsync());
    }

    private static CompanyOnboardingService Service(AccountsDbContext db) => new(
        db,
        new PeriodChronologyService(db),
        new CategoryService(db),
        new AnnualReturnDateService(db, new AuditService(db)),
        new AuditService(db));

    private static CompanyOnboardingInput ValidInput()
    {
        var incorporationDate = new DateOnly(2025, 1, 1);
        return new CompanyOnboardingInput
        {
            Company = new CompanyInput
            {
                LegalName = "PostgreSQL Atomic Onboarding Limited",
                CompanyType = CompanyType.Private,
                IncorporationDate = incorporationDate,
                FinancialYearStartMonth = 1,
                AnnualReturnDate = new DateOnly(2025, 7, 1),
                AnnualReturnDateEffectiveFrom = new DateOnly(2025, 7, 1),
                AnnualReturnDateSource = AnnualReturnDateSource.CroRecord,
                AnnualReturnDateEvidenceReference = "CRO-CORE-POSTGRES-ARD",
                IsTrading = true
            },
            Officers =
            [
                new CompanyOfficerInput { Name = "Aisling Director", Role = OfficerRole.Director },
                new CompanyOfficerInput { Name = "Seamus Secretary", Role = OfficerRole.Secretary }
            ],
            FirstPeriod = new AccountingPeriodInput
            {
                PeriodStart = incorporationDate,
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                GoingConcernConfirmed = true
            },
            OpeningBankAccount = new BankAccountInput
            {
                Name = "Main Current Account",
                Currency = "EUR",
                OpeningBalance = 100m,
                OpeningBalanceDate = incorporationDate
            }
        };
    }

    private static AuthenticatedUser Actor(int tenantId) => new(
        100,
        tenantId,
        "PostgreSQL Onboarding Firm",
        "owner@example.ie",
        "Owner User",
        "Owner");

    private static async Task<int> SeedTenantAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "PostgreSQL Onboarding Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static DbContextOptions<AccountsDbContext> Options(
        string connectionString,
        DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new AccountingConcurrencyInterceptor());
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    private sealed class TwoArrivalInsertGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public int Arrivals => Volatile.Read(ref arrivals);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO company_onboarding_requests", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref arrivals) == 2)
                    allArrived.TrySetResult();
                await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class OfficerInsertFailureInterceptor : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO company_officers", StringComparison.OrdinalIgnoreCase))
            {
                Triggered = true;
                throw new InvalidOperationException("Deterministic officer persistence failure after opening aggregate writes.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PostgresSchema : IAsyncDisposable
    {
        private readonly NpgsqlConnection admin;
        private readonly string schemaName;

        private PostgresSchema(NpgsqlConnection admin, string schemaName, string connectionString)
        {
            this.admin = admin;
            this.schemaName = schemaName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<PostgresSchema> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)!;
            var admin = new NpgsqlConnection(baseConnectionString);
            await admin.OpenAsync();
            var schemaName = $"company_onboarding_{Guid.NewGuid():N}";
            await using (var command = admin.CreateCommand())
            {
                command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
                await command.ExecuteNonQueryAsync();
            }
            var scoped = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = schemaName
            }.ConnectionString;
            return new PostgresSchema(admin, schemaName, scoped);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var command = admin.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await command.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
