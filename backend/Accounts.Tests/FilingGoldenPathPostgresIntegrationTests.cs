using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using QuestPDF.Infrastructure;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// tests-ci-filing-path-on-postgres: drive the whole golden filing path — onboard -> import a real
/// CSV -> categorise -> year-end facts -> classify -> regime -> adjustments -> notes -> statements ->
/// accounts PDF -> iXBRL — and assert the balance sheet has <c>UnexplainedDifference == 0</c>, the PDF
/// is %PDF-prefixed and the iXBRL is well-formed. The body runs on a real PostgreSQL service in CI
/// (the [PostgresFact]); the identical body also runs on the EF InMemory provider (the [Fact]) so the
/// logic is proven locally without a database. Every statement/PDF/iXBRL was previously exercised only
/// on InMemory, so a Postgres-specific mapping regression could ship green — this closes that gap.
/// </summary>
public sealed class FilingGoldenPathPostgresIntegrationTests : IAsyncLifetime
{
    static FilingGoldenPathPostgresIntegrationTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "filing_it_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? services;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        var connection = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }
            .ConnectionString;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var createSchema = admin.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContext<AccountsDbContext>(options => options.UseNpgsql(connection));
        services = serviceCollection.BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (services is not null)
            await services.DisposeAsync();

        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var dropSchema = admin.CreateCommand();
        dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await dropSchema.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task GoldenFilingPath_OnRealPostgres_BalancesAndEmitsPdfAndIxbrl()
    {
        var provider = services ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await RunGoldenFilingPathAsync(db);
    }

    [Fact]
    public async Task GoldenFilingPath_OnInMemory_BalancesAndEmitsPdfAndIxbrl()
    {
        // Same body as the [PostgresFact] above, proven locally on the InMemory provider so the path
        // is exercised even when no PostgreSQL test database is configured.
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AccountsDbContext(options);
        await RunGoldenFilingPathAsync(db);
    }

    private static async Task RunGoldenFilingPathAsync(AccountsDbContext db)
    {
        // --- Onboard: company + first accounting period -----------------------------------------
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Postgres Filing Path Limited",
            CroNumber = Guid.NewGuid().ToString("N")[..12],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 9,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var companyId = company.Id;

        db.CompanyOfficers.AddRange(
            new CompanyOfficer { CompanyId = companyId, Name = "A Director", Role = OfficerRole.Director },
            new CompanyOfficer { CompanyId = companyId, Name = "B Secretary", Role = OfficerRole.Secretary });

        var period = new AccountingPeriod
        {
            CompanyId = companyId,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        var periodId = period.Id;

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        // --- Onboarding facts: €100 share capital funded by the opening bank balance ------------
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = periodId,
            Turnover = 500m,
            BalanceSheetTotal = 600m,
            AvgEmployees = 1
        });
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = periodId,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.TransactionRules.Add(new TransactionRule
        {
            CompanyId = companyId,
            Pattern = "Sales",
            CategoryId = Cat("4000"),
            Priority = 1
        });
        await db.SaveChangesAsync();

        // --- Import a real bank-statement CSV through the real ImportService ---------------------
        const string csv = "Date,Description,Amount\n01/03/2025,Sales invoice INV001,500.00\n";
        var import = await new ImportService(db).ImportCsvAsync(
            companyId, bank.Id, periodId, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "statement.csv");
        Assert.Equal(1, import.ImportedRows);
        Assert.Equal(1, import.AutoCategorised);

        // --- Year-end questionnaire: nil positions confirmed ------------------------------------
        db.YearEndReviewConfirmations.AddRange(
            NilReview(periodId, "debtors"), NilReview(periodId, "creditors"),
            NilReview(periodId, "payroll"), NilReview(periodId, "tax"),
            NilReview(periodId, "dividends"), NilReview(periodId, "post-balance-sheet-events"),
            NilReview(periodId, "related-parties"), NilReview(periodId, "contingent-liabilities"),
            NilReview(periodId, "going-concern"));
        await db.SaveChangesAsync();

        // --- Classify -> regime -> adjustments -> notes -----------------------------------------
        await new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()))
            .ClassifyAsync(companyId, periodId);
        var regime = await new FilingRegimeService(db).DetermineAsync(companyId, periodId, ElectedRegime.Micro);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, periodId);
        if (!await db.Adjustments.AnyAsync(a => a.PeriodId == periodId))
        {
            db.Adjustments.Add(new Adjustment
            {
                PeriodId = periodId,
                Description = "No year-end adjustment required",
                Amount = 0m,
                ImpactOnProfit = 0m,
                Source = AdjustmentSource.Manual,
                CreatedBy = "Accounts reviewer"
            });
            await db.SaveChangesAsync();
        }
        foreach (var adj in await db.Adjustments.Where(a => a.PeriodId == periodId && a.ApprovedAt == null).ToListAsync())
        {
            adj.ApprovedBy = "Accounts reviewer";
            adj.ApprovedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(companyId, periodId);

        // --- Statements balance, then final outputs emit ----------------------------------------
        var statements = new FinancialStatementsService(db);
        var readiness = await statements.GetReadinessScoreAsync(companyId, periodId);
        var balanceSheet = await statements.GetBalanceSheetAsync(companyId, periodId);

        Assert.Equal(ElectedRegime.Micro, regime.Regime);
        Assert.True(regime.AuditExempt);
        Assert.Empty(readiness.MissingItems);
        Assert.Empty(readiness.Warnings);
        Assert.True(readiness.BalanceSheetBalances);
        Assert.True(balanceSheet.Balances);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(600m, balanceSheet.NetAssets);

        // The accounts PDF only generates past the readiness gate, and is a real PDF.
        var pdf = await new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(companyId, periodId);
        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

        // The iXBRL is well-formed XML carrying the entity name.
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(companyId, periodId));
        var ixbrlDocument = System.Xml.Linq.XDocument.Parse(ixbrl);
        Assert.NotNull(ixbrlDocument.Root);
        Assert.Contains("Postgres Filing Path Limited", ixbrl);
    }

    private static YearEndReviewConfirmation NilReview(int periodId, string sectionKey) => new()
    {
        PeriodId = periodId,
        SectionKey = sectionKey,
        Confirmed = true,
        ConfirmedBy = "Accounts reviewer",
        Note = "Nil position reviewed."
    };

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
