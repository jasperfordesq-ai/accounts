using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class DirectorLoanCompliancePostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "director_loan_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<AccountsDbContext>? options;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var createSchema = admin.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        var scoped = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName };
        options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(scoped.ConnectionString)
            .Options;
        await using var db = new AccountsDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var dropSchema = admin.CreateCommand();
        dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await dropSchema.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task DatedEvidencePersistsAndTheComplianceQueryRunsAgainstFreshPostgresSchema()
    {
        await using var db = new AccountsDbContext(
            options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required."));
        var tenant = new Tenant
        {
            Name = "PostgreSQL Director Loan Test Firm",
            Slug = $"director-loan-{Guid.NewGuid():N}"
        };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Postgres Director Loan Limited",
            CroNumber = "DL765432",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2026, 1, 1),
            IsTrading = true
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true
        };
        var director = new CompanyOfficer
        {
            CompanyId = company.Id,
            Name = "Postgres Director",
            Role = OfficerRole.Director,
            AppointedDate = new DateOnly(2020, 1, 1)
        };
        db.AccountingPeriods.Add(period);
        db.CompanyOfficers.Add(director);
        await db.SaveChangesAsync();

        db.DirectorLoans.Add(new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = director.Id,
            ArrangementDate = period.PeriodStart,
            OpeningBalance = 0m,
            Advances = 9_999.99m,
            ClosingBalance = 9_999.99m,
            MaxBalanceDuringYear = 9_999.99m,
            TermsStatus = DirectorLoanTermsStatus.WrittenComplete,
            IsDocumented = true,
            LoanTerms = "Written repayment date and explicit interest terms retained.",
            ComplianceBasis = DirectorLoanComplianceBasis.Section240BelowTenPercent,
            RelevantAssetsBasis = DirectorLoanRelevantAssetsBasis.LastLaidEntityFinancialStatements,
            RelevantAssetsAmount = 100_000m,
            RelevantAssetsAsOfDate = period.PeriodStart.AddDays(-1),
            RelevantAssetsReference = "postgres:last-laid-accounts#net-assets",
            RelevantAssetsFallReview = DirectorLoanRelevantAssetsFallReview.NoRelevantFall,
            ReviewDecision = DirectorLoanReviewDecision.Accepted,
            ReviewNote = "PostgreSQL integration review of retained evidence and dated movement.",
            ReviewedBy = "Postgres Reviewer",
            ReviewerRole = "Accountant",
            ReviewedAtUtc = DateTime.UtcNow,
            BalanceMovements =
            [
                new DirectorLoanMovement
                {
                    MovementDate = new DateOnly(2026, 3, 1),
                    MovementType = DirectorLoanMovementType.Advance,
                    Amount = 9_999.99m,
                    EvidenceReference = "postgres:bank-ledger#advance"
                }
            ]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new DirectorLoanComplianceService(db, new FinancialStatementsService(db))
            .GetComplianceStatusAsync(company.Id, period.Id);
        var retained = await db.DirectorLoans
            .Include(loan => loan.BalanceMovements)
            .SingleAsync(loan => loan.PeriodId == period.Id);

        Assert.False(result.HasUnresolvedComplianceBlockers);
        Assert.True(Assert.Single(result.Loans).Section240StrictlyBelowThreshold);
        Assert.Equal(9_999.99m, result.AggregateMaximumExposure);
        Assert.Single(retained.BalanceMovements);
        Assert.Equal("postgres:bank-ledger#advance", retained.BalanceMovements[0].EvidenceReference);
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
