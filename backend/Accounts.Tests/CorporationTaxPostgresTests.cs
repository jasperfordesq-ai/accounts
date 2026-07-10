using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "corporation_tax_" + Guid.NewGuid().ToString("N");
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
    public async Task ScopeLossAssetAndDirectorFeeEvidence_PersistAndRecomputeOnFreshSchema()
    {
        await using var db = new AccountsDbContext(
            options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required."));
        var tenant = new Tenant
        {
            Name = "PostgreSQL Corporation Tax Firm",
            Slug = $"corporation-tax-{Guid.NewGuid():N}"
        };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Postgres Corporation Tax Limited",
            CroNumber = "CT765432",
            TaxReference = "1234567A",
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
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        var bankControl = new AccountCategory
        {
            CompanyId = company.Id,
            Code = "1400",
            Name = "Bank control",
            Type = AccountCategoryType.Asset
        };
        var sales = new AccountCategory
        {
            CompanyId = company.Id,
            Code = "4000",
            Name = "Sales",
            Type = AccountCategoryType.Income,
            TaxTreatment = TaxTreatment.Deductible
        };
        db.AddRange(period, bank, bankControl, sales);
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2026, 3, 1),
            Description = "Trading receipt",
            Amount = 10_000m,
            CategoryId = sales.Id
        });
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Non-qualifying fixture asset",
            Category = "Land",
            Cost = 5_000m,
            AcquisitionDate = new DateOnly(2026, 2, 1),
            UsefulLifeYears = 20,
            DepreciationMethod = DepreciationMethod.StraightLine,
            CapitalAllowanceTreatment = CapitalAllowanceTreatment.NonQualifying,
            CapitalAllowanceEvidence = "Retained fixture evidence records why this asset is non-qualifying.",
            CapitalAllowanceReviewedBy = "Postgres fixture reviewer",
            CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
        });
        db.PayrollSummaries.Add(new PayrollSummary
        {
            PeriodId = period.Id,
            GrossWages = 3_000m,
            DirectorsFees = 2_500m,
            EmployerPrsi = 300m,
            StaffCount = 1
        });
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        var computation = await service.SaveScopeReviewAsync(
            company.Id,
            period.Id,
            new TaxComputationService.CorporationTaxScopeReviewInput(
                IsCloseCompany: false,
                IsServiceCompany: null,
                HasGroupOrConsortiumRelief: false,
                HasChargeableGains: false,
                HasForeignIncomeOrTaxCredits: false,
                HasExceptedTrade: false,
                HasOtherReliefsOrSpecialRegimes: false,
                DeclaredPassiveIncomePresent: false,
                PassiveIncomeClassificationReviewed: false,
                LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                BroughtForwardTradingLoss: 0m,
                BroughtForwardLossEvidence: null,
                EvidenceNote: "PostgreSQL retained simple-scope evidence for the tax fixture."),
            "Postgres fixture reviewer");
        Assert.True(computation.FinalTaxChargeSupported);
        Assert.Equal(1_250m, computation.TotalCorporationTax);

        db.ChangeTracker.Clear();
        var retainedScope = await db.CorporationTaxScopeReviews.SingleAsync(item => item.PeriodId == period.Id);
        var retainedLoss = await db.CorporationTaxLossRecords.SingleAsync(item => item.PeriodId == period.Id);
        var retainedAsset = await db.FixedAssets.SingleAsync(item => item.CompanyId == company.Id);
        var retainedPayroll = await db.PayrollSummaries.SingleAsync(item => item.PeriodId == period.Id);
        var support = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(company.Id, period.Id);

        Assert.False(retainedScope.IsCloseCompany);
        Assert.Equal(CorporationTaxLossTreatment.NotApplicable, retainedLoss.Treatment);
        Assert.Equal(CapitalAllowanceTreatment.NonQualifying, retainedAsset.CapitalAllowanceTreatment);
        Assert.Equal(2_500m, retainedPayroll.DirectorsFees);
        Assert.Equal(2_500m, support.TotalDirectorsFees);
        Assert.False(support.IsCompleteCt1Return);
        Assert.Equal(TaxComputationService.OutputKind, support.OutputKind);

        var invalidCompany = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Postgres Invalid Tax Ledger Limited",
            CroNumber = "CT765433",
            TaxReference = "1234567B",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2026, 1, 1),
            IsTrading = true
        };
        db.Companies.Add(invalidCompany);
        await db.SaveChangesAsync();
        var invalidPeriod = new AccountingPeriod
        {
            CompanyId = invalidCompany.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true
        };
        var invalidBank = new BankAccount { CompanyId = invalidCompany.Id, Name = "Current", OpeningBalance = 0m };
        var invalidSales = new AccountCategory
        {
            CompanyId = invalidCompany.Id,
            Code = "4000",
            Name = "Sales",
            Type = AccountCategoryType.Income,
            TaxTreatment = TaxTreatment.Deductible
        };
        db.AddRange(invalidPeriod, invalidBank, invalidSales);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = invalidBank.Id,
            PeriodId = invalidPeriod.Id,
            Date = new DateOnly(2026, 4, 1),
            Description = "Unpostable trading receipt",
            Amount = 1_000m,
            CategoryId = invalidSales.Id
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new TaxComputationService(db, new FinancialStatementsService(db)).SaveScopeReviewAsync(
                invalidCompany.Id,
                invalidPeriod.Id,
                new TaxComputationService.CorporationTaxScopeReviewInput(
                    IsCloseCompany: false,
                    IsServiceCompany: null,
                    HasGroupOrConsortiumRelief: false,
                    HasChargeableGains: false,
                    HasForeignIncomeOrTaxCredits: false,
                    HasExceptedTrade: false,
                    HasOtherReliefsOrSpecialRegimes: false,
                    DeclaredPassiveIncomePresent: false,
                    PassiveIncomeClassificationReviewed: false,
                    LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                    BroughtForwardTradingLoss: 0m,
                    BroughtForwardLossEvidence: null,
                    EvidenceNote: "This scope write must roll back when its ledger cannot be posted."),
                "Postgres fixture reviewer"));
        db.ChangeTracker.Clear();
        Assert.False(await db.CorporationTaxScopeReviews.AnyAsync(item => item.PeriodId == invalidPeriod.Id));
        Assert.False(await db.CorporationTaxLossRecords.AnyAsync(item => item.PeriodId == invalidPeriod.Id));
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
