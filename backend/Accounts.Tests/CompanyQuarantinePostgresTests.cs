using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class CompanyQuarantinePostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const int PeriodAdvisoryLockFamily = 41001;

    [PostgresFact]
    public async Task PostgreSqlQuarantineWaitsForPeriodWritesRetainsInventoryAndEnforcesImmutableEvidence()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)!;
        var schemaName = $"company_quarantine_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await create.ExecuteNonQueryAsync();
        }
        var scopedConnection = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        }.ConnectionString;

        try
        {
            var options = new DbContextOptionsBuilder<AccountsDbContext>()
                .UseNpgsql(scopedConnection)
                .AddInterceptors(new AccountingConcurrencyInterceptor())
                .Options;
            int tenantId;
            int companyId;
            int periodId;
            const string legalName = "PostgreSQL Quarantine Limited";
            await using (var setup = new AccountsDbContext(options))
            {
                await setup.Database.MigrateAsync();
                var tenant = new Tenant { Name = "PostgreSQL Quarantine Firm", Slug = Guid.NewGuid().ToString("N") };
                var company = new Company
                {
                    Tenant = tenant,
                    LegalName = legalName,
                    CompanyType = CompanyType.Private,
                    IncorporationDate = new DateOnly(2025, 1, 1),
                    AnnualReturnDate = new DateOnly(2024, 9, 15)
                };
                setup.Companies.Add(company);
                await setup.SaveChangesAsync();
                var period = new AccountingPeriod
                {
                    CompanyId = company.Id,
                    PeriodStart = new(2025, 1, 1),
                    PeriodEnd = new(2025, 12, 31),
                    IsFirstYear = true
                };
                setup.AccountingPeriods.Add(period);
                await setup.SaveChangesAsync();
                tenantId = tenant.Id;
                companyId = company.Id;
                periodId = period.Id;
            }

            await using var writer = new AccountsDbContext(options);
            await using var writerTransaction = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({PeriodAdvisoryLockFamily}, {periodId})");
            writer.Debtors.Add(new Debtor { PeriodId = periodId, Name = "Concurrent debtor", Amount = 10m });
            await writer.SaveChangesAsync();

            await using var quarantineDb = new AccountsDbContext(options);
            var quarantineTask = new CompanyQuarantineService(quarantineDb, new AuditService(quarantineDb)).QuarantineAsync(
                companyId,
                new(legalName, "Owner-approved quarantine after the PostgreSQL race review completed."),
                Actor(tenantId),
                "postgres-quarantine-race");
            await Task.Delay(250);
            Assert.False(quarantineTask.IsCompleted, "Quarantine must wait for the same period advisory lock as accounting writes.");

            await writerTransaction.CommitAsync();
            var outcome = await quarantineTask.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(1, outcome.Inventory["debtors"]);

            await using (var blockedWriter = new AccountsDbContext(options))
            {
                blockedWriter.Creditors.Add(new Creditor { PeriodId = periodId, Name = "Rejected creditor", Amount = 1m });
                var conflict = await Assert.ThrowsAsync<AccountingConcurrencyException>(() => blockedWriter.SaveChangesAsync());
                Assert.Contains("quarantined", conflict.Message, StringComparison.OrdinalIgnoreCase);
            }

            await using var verify = new AccountsDbContext(options);
            Assert.Empty(await verify.Companies.ToListAsync());
            Assert.Empty(await verify.AccountingPeriods.ToListAsync());
            Assert.Equal(1, await verify.Debtors.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await verify.Creditors.IgnoreQueryFilters().CountAsync());
            var evidence = await verify.CompanyQuarantineEvents.SingleAsync();
            Assert.True(CompanyQuarantineEvidenceIntegrity.IsValid(evidence));

            await AssertPostgresImmutableFailureAsync(
                () => verify.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE company_quarantine_events SET \"Reason\" = {'x'} WHERE \"Id\" = {evidence.Id}"));
            await AssertPostgresImmutableFailureAsync(
                () => verify.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM company_quarantine_events WHERE \"Id\" = {evidence.Id}"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static AuthenticatedUser Actor(int tenantId) => new(
        10,
        tenantId,
        "PostgreSQL Quarantine Firm",
        "owner@example.ie",
        "Owner User",
        "Owner");

    private static async Task AssertPostgresImmutableFailureAsync(Func<Task<int>> action)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        var postgres = FindPostgresException(error);
        Assert.NotNull(postgres);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        Assert.Equal("CK_company_quarantine_events_immutable", postgres.ConstraintName);
    }

    private static PostgresException? FindPostgresException(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres;
        }
        return null;
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
