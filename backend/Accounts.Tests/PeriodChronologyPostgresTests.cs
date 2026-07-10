using Accounts.Api.Data;
using Accounts.Api.Entities;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class PeriodChronologyPostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [PostgresFact]
    public async Task RawAndConcurrentPostgresWrites_EnforceCompletePeriodChronology()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)!;
        var schemaName = $"period_chronology_{Guid.NewGuid():N}";
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
                .Options;
            await using var db = new AccountsDbContext(options);
            await db.Database.MigrateAsync();

            var tenant = new Tenant
            {
                Name = "Period Chronology PostgreSQL Firm",
                Slug = $"period-chronology-{Guid.NewGuid():N}"
            };
            var company = new Company
            {
                Tenant = tenant,
                LegalName = "Period Chronology PostgreSQL Limited",
                CompanyType = CompanyType.Private,
                IncorporationDate = new DateOnly(2025, 1, 1),
                AnnualReturnDate = new DateOnly(2024, 9, 15)
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();

            await AssertPostgresFailureAsync(
                () => InsertRawAsync(db, company.Id, "2024-01-01", "2024-12-31", true),
                PostgresErrorCodes.CheckViolation,
                "CK_accounting_periods_after_incorporation");

            await InsertRawAsync(db, company.Id, "2025-01-01", "2025-12-31", true);

            await AssertPostgresFailureAsync(
                () => InsertRawAsync(db, company.Id, "2025-06-01", "2026-05-31", false),
                PostgresErrorCodes.CheckViolation,
                "CK_accounting_periods_no_overlap");
            await AssertPostgresFailureAsync(
                () => InsertRawAsync(db, company.Id, "2026-01-01", "2026-12-31", true),
                PostgresErrorCodes.UniqueViolation,
                "UX_accounting_periods_one_first_year");
            await AssertPostgresFailureAsync(
                () => InsertRawAsync(db, company.Id, "2026-01-02", "2026-12-31", false),
                PostgresErrorCodes.CheckViolation,
                "CK_accounting_periods_contiguous");

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyCount = 0;
            async Task<Exception?> ConcurrentInsertAsync()
            {
                await using var connection = new NpgsqlConnection(scopedConnection);
                await connection.OpenAsync();
                if (Interlocked.Increment(ref readyCount) == 2)
                    ready.SetResult();
                await start.Task;
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounting_periods
                        ("CompanyId", "PeriodStart", "PeriodEnd", "Status", "IsFirstYear", "CreatedAt")
                    VALUES
                        ({company.Id}, DATE '2026-01-01', DATE '2026-12-31', 'Draft', FALSE, NOW())
                    """;
                try
                {
                    await command.ExecuteNonQueryAsync();
                    return null;
                }
                catch (Exception error)
                {
                    return error;
                }
            }

            var first = ConcurrentInsertAsync();
            var second = ConcurrentInsertAsync();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            start.SetResult();
            var outcomes = await Task.WhenAll(first, second);

            Assert.Single(outcomes, outcome => outcome is null);
            var rejected = Assert.Single(outcomes, outcome => outcome is not null)!;
            var postgres = FindPostgresException(rejected);
            Assert.NotNull(postgres);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Equal("CK_accounting_periods_no_overlap", postgres.ConstraintName);

            db.ChangeTracker.Clear();
            Assert.Equal(2, await db.AccountingPeriods.CountAsync(period => period.CompanyId == company.Id));

            await AssertPostgresFailureAsync(
                () => db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM accounting_periods WHERE \"CompanyId\" = {company.Id} AND \"IsFirstYear\""),
                PostgresErrorCodes.CheckViolation,
                "CK_accounting_periods_delete_history");
            await AssertPostgresFailureAsync(
                () => db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE companies SET \"IncorporationDate\" = {new DateOnly(2024, 1, 1)} WHERE \"Id\" = {company.Id}"),
                PostgresErrorCodes.CheckViolation,
                "CK_companies_incorporation_period_chronology");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static Task<int> InsertRawAsync(
        AccountsDbContext db,
        int companyId,
        string periodStart,
        string periodEnd,
        bool isFirstYear)
    {
        var start = DateOnly.ParseExact(periodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(periodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO accounting_periods
                ("CompanyId", "PeriodStart", "PeriodEnd", "Status", "IsFirstYear", "CreatedAt")
            VALUES
                ({companyId}, {start}, {end}, {PeriodStatus.Draft.ToString()}, {isFirstYear}, {DateTime.UtcNow})
            """);
    }

    private static async Task AssertPostgresFailureAsync(
        Func<Task<int>> action,
        string expectedSqlState,
        string expectedConstraint)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        var postgres = FindPostgresException(error);
        Assert.NotNull(postgres);
        Assert.Equal(expectedSqlState, postgres.SqlState);
        Assert.Equal(expectedConstraint, postgres.ConstraintName);
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
