using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class IdempotencyPostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [PostgresFact]
    public async Task ConcurrentRetrySerializesToOneDomainRowOneAuditAndOneCompletedRecord()
    {
        await using var schema = await PostgresSchema.CreateAsync();
        int tenantId;
        await using (var setup = CreateDb(schema.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            tenantId = await SeedTenantAsync(setup);
        }

        const string key = "postgres-idempotency-concurrent-0001";
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IdempotencyExecution<CommandResult>> SubmitAsync()
        {
            await using var db = CreateDb(schema.ConnectionString);
            await start.Task.WaitAsync(TimeSpan.FromSeconds(20));
            return await new IdempotencyService(db).ExecuteAsync(
                tenantId,
                key,
                IdempotencyOperations.CompanyCreate,
                new { legalName = "Concurrent Idempotency Limited" },
                Actor(tenantId),
                async cancellationToken =>
                {
                    var company = Company(tenantId, "Concurrent Idempotency Limited");
                    db.Companies.Add(company);
                    await db.SaveChangesAsync(cancellationToken);
                    await new AuditService(db).LogAsync(
                        company.Id,
                        null,
                        nameof(Company),
                        company.Id,
                        "IdempotencyConcurrentCreated",
                        newValue: new { company.Id },
                        userId: "owner@example.ie",
                        tenantId: tenantId,
                        cancellationToken: cancellationToken);
                    return new IdempotencyOperationOutcome<CommandResult>(
                        new CommandResult(company.Id, company.LegalName),
                        nameof(Company),
                        company.Id.ToString());
                });
        }

        var firstTask = Task.Run(SubmitAsync);
        var secondTask = Task.Run(SubmitAsync);
        start.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => !result.WasReplay);
        Assert.Single(results, result => result.WasReplay);
        Assert.Equal(results[0].Result, results[1].Result);
        Assert.Equal(results[0].RecordId, results[1].RecordId);
        await using var verify = CreateDb(schema.ConnectionString);
        Assert.Single(await verify.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await verify.AuditLogs.IgnoreQueryFilters()
            .Where(log => log.Action == "IdempotencyConcurrentCreated")
            .ToListAsync());
        var retained = await verify.IdempotencyRecords.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Completed", retained.Status);
        Assert.Equal(IdempotencyOperations.CompanyCreate, retained.Operation);
    }

    [PostgresFact]
    public async Task DomainFailureRollsBackReservationAndRowsThenSameKeyCanSucceed()
    {
        await using var schema = await PostgresSchema.CreateAsync();
        int tenantId;
        await using (var setup = CreateDb(schema.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            tenantId = await SeedTenantAsync(setup);
        }

        const string key = "postgres-idempotency-rollback-0001";
        await using (var failing = CreateDb(schema.ConnectionString))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new IdempotencyService(failing).ExecuteAsync<CommandResult>(
                tenantId,
                key,
                IdempotencyOperations.CompanyCreate,
                new { legalName = "Rollback Limited" },
                Actor(tenantId),
                async cancellationToken =>
                {
                    failing.Companies.Add(Company(tenantId, "Rollback Limited"));
                    await failing.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException("failure after the domain insert");
                }));
        }

        await using (var afterFailure = CreateDb(schema.ConnectionString))
        {
            Assert.Empty(await afterFailure.Companies.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await afterFailure.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
        }

        await using (var retry = CreateDb(schema.ConnectionString))
        {
            var execution = await new IdempotencyService(retry).ExecuteAsync(
                tenantId,
                key,
                IdempotencyOperations.CompanyCreate,
                new { legalName = "Rollback Limited" },
                Actor(tenantId),
                async cancellationToken =>
                {
                    var company = Company(tenantId, "Rollback Limited");
                    retry.Companies.Add(company);
                    await retry.SaveChangesAsync(cancellationToken);
                    return new IdempotencyOperationOutcome<CommandResult>(
                        new CommandResult(company.Id, company.LegalName),
                        nameof(Company),
                        company.Id.ToString());
                });
            Assert.False(execution.WasReplay);
        }

        await using var verify = CreateDb(schema.ConnectionString);
        Assert.Single(await verify.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await verify.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    [PostgresFact]
    public async Task DatabaseRejectsMutationAndPrematureDeletionButAllowsExpiredCleanup()
    {
        await using var schema = await PostgresSchema.CreateAsync();
        int tenantId;
        await using (var setup = CreateDb(schema.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            tenantId = await SeedTenantAsync(setup);
            await new IdempotencyService(setup).ExecuteAsync(
                tenantId,
                "postgres-idempotency-immutable-0001",
                IdempotencyOperations.CompanyCreate,
                new { id = 1 },
                Actor(tenantId),
                _ => Task.FromResult(new IdempotencyOperationOutcome<CommandResult>(
                    new CommandResult(1, "immutable"), nameof(Company), "1")));
        }

        await using var db = CreateDb(schema.ConnectionString);
        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE idempotency_records SET \"ResultResourceId\" = 'changed' WHERE \"TenantId\" = {0}",
            tenantId));
        Assert.Equal("23514", update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM idempotency_records WHERE \"TenantId\" = {0}",
            tenantId));
        Assert.Equal("23514", delete.SqlState);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO idempotency_records (
                "TenantId", "IdempotencyKey", "Operation", "RequestFingerprintSha256", "Status",
                "CreatedByUserId", "CreatedByDisplayName", "StartedAtUtc", "ExpiresAtUtc")
            VALUES ({0}, 'postgres-expired-cleanup-0001', 'cleanup.test.v1', repeat('a', 64),
                'InProgress', 'owner@example.ie', 'Owner User', CURRENT_TIMESTAMP - INTERVAL '2 days',
                CURRENT_TIMESTAMP - INTERVAL '1 day')
            """, tenantId);
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM idempotency_records WHERE \"IdempotencyKey\" = 'postgres-expired-cleanup-0001'"));
    }

    [PostgresFact]
    public async Task UpgradeBackfillsLegacyOnboardingKeyAndReplaysItsExactOutcome()
    {
        await using var schema = await PostgresSchema.CreateAsync();
        var input = OnboardingInput();
        const string key = "legacy-onboarding-upgrade-0001";
        int tenantId;
        CompanyOnboardingOutcome? outcome = null;
        var legacyJsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var requestJson = JsonSerializer.Serialize(input, legacyJsonOptions);

        await using (var previous = CreateDb(schema.ConnectionString))
        {
            await previous.Database.MigrateAsync("20260711000000_AddCorporationTaxFilingSupport");
            tenantId = await SeedTenantAsync(previous);
            var company = Company(tenantId, "Legacy Upgrade Limited");
            previous.Companies.Add(company);
            await previous.SaveChangesAsync();
            var period = new AccountingPeriod
            {
                CompanyId = company.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var bank = new BankAccount { CompanyId = company.Id, Name = "Legacy Bank", Currency = "EUR" };
            var officer = new CompanyOfficer { CompanyId = company.Id, Name = "Legacy Director", Role = OfficerRole.Director };
            previous.AccountingPeriods.Add(period);
            previous.BankAccounts.Add(bank);
            previous.CompanyOfficers.Add(officer);
            await previous.SaveChangesAsync();
            outcome = new CompanyOnboardingOutcome(
                company.Id,
                company.LegalName,
                period.Id,
                period.PeriodStart,
                period.PeriodEnd,
                bank.Id,
                bank.Name,
                1,
                [new OnboardedOfficer(officer.Id, officer.Name, officer.Role)]);
            var responseJson = JsonSerializer.Serialize(outcome, legacyJsonOptions);
            var legacy = new CompanyOnboardingRequest
            {
                TenantId = tenantId,
                IdempotencyKey = key,
                RequestSha256 = Hash(requestJson),
                Status = "InProgress",
                CreatedByUserId = "owner@example.ie",
                CreatedByDisplayName = "Owner User",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-1)
            };
            previous.CompanyOnboardingRequests.Add(legacy);
            await previous.SaveChangesAsync();
            legacy.Status = "Completed";
            legacy.CompanyId = outcome.CompanyId;
            legacy.PeriodId = outcome.FirstPeriodId;
            legacy.BankAccountId = outcome.OpeningBankAccountId;
            legacy.CategoryCount = outcome.CategoryCount;
            legacy.CompletedAtUtc = DateTime.UtcNow;
            legacy.ResponseJson = responseJson;
            legacy.ResponseSha256 = Hash(responseJson);
            await previous.SaveChangesAsync();
            await previous.Database.MigrateAsync();
        }

        await using var upgraded = CreateDb(schema.ConnectionString);
        var replay = await new IdempotencyService(upgraded).ExecuteAsync<CompanyOnboardingOutcome>(
            tenantId,
            key,
            IdempotencyOperations.CompanyOnboard,
            input,
            Actor(tenantId),
            _ => throw new InvalidOperationException("backfilled command must not execute"));

        Assert.True(replay.WasReplay);
        Assert.NotNull(outcome);
        Assert.Equal(outcome.CompanyId, replay.Result.CompanyId);
        Assert.Equal(outcome.FirstPeriodId, replay.Result.FirstPeriodId);
        Assert.Equal(outcome.OpeningBankAccountId, replay.Result.OpeningBankAccountId);
        Assert.Equal(outcome.Officers.Select(officer => officer.Id), replay.Result.Officers.Select(officer => officer.Id));
        Assert.Equal(StatusCodes.Status201Created, replay.HttpStatusCode);
        Assert.Single(await upgraded.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    private static AccountsDbContext CreateDb(string connectionString) => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new AccountingConcurrencyInterceptor())
            .Options);

    private static Company Company(int tenantId, string name) => new()
    {
        TenantId = tenantId,
        LegalName = name,
        IncorporationDate = new DateOnly(2025, 1, 1),
        FinancialYearStartMonth = 1
    };

    private static async Task<int> SeedTenantAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Idempotency PostgreSQL", Slug = $"idem-pg-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static AuthenticatedUser Actor(int tenantId) => new(
        100,
        tenantId,
        "Idempotency PostgreSQL",
        "owner@example.ie",
        "Owner User",
        "Owner");

    private static CompanyOnboardingInput OnboardingInput()
    {
        var start = new DateOnly(2025, 1, 1);
        return new CompanyOnboardingInput
        {
            Company = new CompanyInput
            {
                LegalName = "Legacy Upgrade Limited",
                IncorporationDate = start,
                FinancialYearStartMonth = 1,
                IsTrading = true
            },
            Officers = [new CompanyOfficerInput { Name = "Legacy Director", Role = OfficerRole.Director }],
            FirstPeriod = new AccountingPeriodInput
            {
                PeriodStart = start,
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            },
            OpeningBankAccount = new BankAccountInput { Name = "Legacy Bank", Currency = "EUR" }
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public sealed record CommandResult(int Id, string Name);

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
            var schemaName = $"idempotency_{Guid.NewGuid():N}";
            await using (var command = admin.CreateCommand())
            {
                command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
                await command.ExecuteNonQueryAsync();
            }
            var scoped = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
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
