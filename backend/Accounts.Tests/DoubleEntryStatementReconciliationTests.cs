using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class DoubleEntryStatementReconciliationTests
{
    [Fact]
    public async Task MultiPeriodLedger_ReconcilesInventoryInterestDisposalTaxCashAndEquityToCent()
    {
        await using var db = CreateDb();
        var fixture = await SeedTwoPeriodFixtureAsync(db);
        var adjustments = new AdjustmentService(db);
        var statements = new FinancialStatementsService(db);

        await adjustments.GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.First.Id);
        var firstTrialBalance = await statements.GetTrialBalanceAsync(fixture.Company.Id, fixture.First.Id);
        var firstProfitAndLoss = await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.First.Id);
        var firstBalanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, fixture.First.Id);
        var firstCashFlow = await statements.GetCashFlowStatementAsync(fixture.Company.Id, fixture.First.Id);
        var firstEquity = await statements.GetEquityChangesAsync(fixture.Company.Id, fixture.First.Id);

        AssertTrialBalance(firstTrialBalance);
        Assert.Equal(3_200m, firstProfitAndLoss.CostOfSales);
        Assert.Equal(100m, firstProfitAndLoss.InterestPayable);
        Assert.Equal(500m, firstProfitAndLoss.TaxCharge);
        Assert.Equal(6_000m, firstProfitAndLoss.ProfitAfterTax);
        Assert.Equal(1_000m, firstBalanceSheet.FixedAssets.Total);
        Assert.Equal(800m, firstBalanceSheet.CurrentAssets.Stock);
        Assert.Equal(5_000m, firstBalanceSheet.CurrentAssets.Cash);
        Assert.Equal(100m, firstBalanceSheet.CreditorsWithinYear.TaxCreditors);
        Assert.Equal(6_700m, firstBalanceSheet.NetAssets);
        Assert.Equal(0m, firstBalanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(firstBalanceSheet.CurrentAssets.Cash, firstCashFlow.ClosingCash);
        Assert.Equal(100m, -Assert.Single(firstCashFlow.OperatingAdjustments, a => a.Description == "Interest paid").Amount);
        Assert.Equal(300m, firstEquity.DividendsPaid);
        Assert.Equal(firstBalanceSheet.CapitalAndReserves.RetainedEarnings, firstEquity.ClosingRetainedEarnings);

        await adjustments.GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Second.Id);
        var secondTrialBalance = await statements.GetTrialBalanceAsync(fixture.Company.Id, fixture.Second.Id);
        var secondProfitAndLoss = await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.Second.Id);
        var secondBalanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, fixture.Second.Id);
        var secondCashFlow = await statements.GetCashFlowStatementAsync(fixture.Company.Id, fixture.Second.Id);
        var secondEquity = await statements.GetEquityChangesAsync(fixture.Company.Id, fixture.Second.Id);
        var secondSources = await statements.GetStatementSourcesAsync(fixture.Company.Id, fixture.Second.Id);

        AssertTrialBalance(secondTrialBalance);
        Assert.Equal(5_200m, secondProfitAndLoss.CostOfSales);
        Assert.Equal(120m, secondProfitAndLoss.InterestPayable);
        Assert.Equal(600m, secondProfitAndLoss.TaxCharge);
        Assert.Equal(5_780m, secondProfitAndLoss.ProfitAfterTax);
        Assert.Contains(secondProfitAndLoss.Overheads, line =>
            line.Code == "7050" && line.Amount == 200.82m);
        Assert.Equal(0m, secondBalanceSheet.FixedAssets.Total);
        Assert.Equal(600m, secondBalanceSheet.CurrentAssets.Stock);
        Assert.Equal(11_680m, secondBalanceSheet.CurrentAssets.Cash);
        Assert.Equal(200m, secondBalanceSheet.CreditorsWithinYear.TaxCreditors);
        Assert.Equal(12_080m, secondBalanceSheet.NetAssets);
        Assert.Equal(0m, secondBalanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.True(secondBalanceSheet.Balances);

        Assert.Equal(5_000m, secondCashFlow.OpeningCash);
        Assert.Equal(6_680m, secondCashFlow.NetIncreaseInCash);
        Assert.Equal(11_680m, secondCashFlow.ClosingCash);
        Assert.Equal(secondBalanceSheet.CurrentAssets.Cash, secondCashFlow.ClosingCash);
        Assert.Equal(700m, secondCashFlow.CapitalExpenditureDisposals);
        Assert.Equal(400m, secondCashFlow.DividendsPaid);
        Assert.Equal(500m, secondCashFlow.TaxPaid);
        Assert.Equal(-120m, Assert.Single(secondCashFlow.OperatingAdjustments, a => a.Description == "Interest paid").Amount);

        Assert.Equal(6_700m, secondEquity.OpeningRetainedEarnings);
        Assert.Equal(400m, secondEquity.DividendsPaid);
        Assert.Equal(12_080m, secondEquity.ClosingRetainedEarnings);
        Assert.Equal(secondBalanceSheet.CapitalAndReserves.RetainedEarnings, secondEquity.ClosingRetainedEarnings);

        var stockSources = Assert.Single(secondSources, source => source.Code == "1000");
        Assert.Contains(stockSources.SourceNotes, note => note.Contains("Carried from exact prior period", StringComparison.Ordinal));
        Assert.Contains(stockSources.SourceNotes, note => note.Contains("Opening reversal", StringComparison.Ordinal));
        Assert.Contains(stockSources.SourceNotes, note => note.Contains("Closing stock", StringComparison.Ordinal));
        var assetSources = Assert.Single(secondSources, source => source.Code == "0050");
        Assert.Contains(assetSources.SourceNotes, note => note.Contains("Imported transaction #", StringComparison.Ordinal));
        Assert.Contains(assetSources.SourceNotes, note => note.Contains("Fixed asset disposal", StringComparison.Ordinal));
        Assert.All(secondSources.SelectMany(source => source.SourceNotes), note => Assert.False(string.IsNullOrWhiteSpace(note)));
    }

    [Fact]
    public async Task JournalRules_RejectInvalidPostingsAndIgnoreCallerSuppliedImpact()
    {
        await using var db = CreateDb();
        var fixture = await SeedBaseAsync(db);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(fixture.Company.Id);
        var expense = categories.Single(category => category.Code == "6810");
        var accrual = categories.Single(category => category.Code == "2100");

        foreach (var invalid in new[]
                 {
                     Input(expense.Id, accrual.Id, 0m),
                     Input(expense.Id, accrual.Id, -1m),
                     Input(null, accrual.Id, 1m),
                     Input(expense.Id, null, 1m),
                     Input(expense.Id, expense.Id, 1m)
                 })
        {
            Assert.NotNull(await AdjustmentInputs.ValidateAsync(db, fixture.Company.Id, invalid));
        }

        var input = new AdjustmentInput(
            "Accrued accountancy fee",
            expense.Id,
            accrual.Id,
            125.55m,
            "Invoice received after year end",
            "FRS 102 accruals concept",
            ImpactOnProfit: 999_999m,
            ImpactOnAssets: 999_999m);
        Assert.Null(await AdjustmentInputs.ValidateAsync(db, fixture.Company.Id, input));
        var adjustment = AdjustmentInputs.ToManualAdjustment(
            input,
            fixture.First.Id,
            new AuthenticatedUser(
                UserId: 1,
                TenantId: fixture.Tenant.Id,
                TenantName: fixture.Tenant.Name,
                Email: "accountant@example.ie",
                DisplayName: "Accountant",
                Role: "Accountant"),
            DateTime.UtcNow);
        await AdjustmentPostingRules.ApplyDerivedImpactAsync(db, adjustment);

        Assert.Equal(-125.55m, adjustment.ImpactOnProfit);
        Assert.Equal(0m, adjustment.ImpactOnAssets);
        AdjustmentPostingRules.EnsureValidPosting(adjustment);
    }

    [Fact]
    public async Task GeneratedJournals_PropertyInvariantEveryPostingBalancesAndImpactIsDerived()
    {
        await using var db = CreateDb();
        var fixture = await SeedTwoPeriodFixtureAsync(db);
        var service = new AdjustmentService(db);
        await service.GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.First.Id);
        await service.GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Second.Id);

        var categories = await db.AccountCategories.ToDictionaryAsync(category => category.Id);
        var journals = await db.Adjustments.OrderBy(journal => journal.Id).ToListAsync();
        Assert.NotEmpty(journals);
        foreach (var journal in journals)
        {
            AdjustmentPostingRules.EnsureValidPosting(journal);
            var postedDebitTotal = new[] { journal.Amount }.Sum();
            var postedCreditTotal = new[] { journal.Amount }.Sum();
            Assert.Equal(postedDebitTotal, postedCreditTotal);
            var expected = AdjustmentPostingRules.DeriveImpact(
                categories[journal.DebitCategoryId!.Value],
                categories[journal.CreditCategoryId!.Value],
                journal.Amount);
            Assert.Equal(expected.Profit, journal.ImpactOnProfit);
            Assert.Equal(expected.Assets, journal.ImpactOnAssets);
        }
    }

    [Fact]
    public async Task Depreciation_RespectsResidualValueAndActualActualAcquisitionApportionment()
    {
        await using var db = CreateDb();
        var fixture = await SeedBaseAsync(db);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(fixture.Company.Id);
        var bank = new BankAccount
        {
            CompanyId = fixture.Company.Id,
            Name = "Current account",
            OpeningBalance = 1_000m,
            OpeningBalanceDate = fixture.First.PeriodStart
        };
        db.BankAccounts.Add(bank);
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = fixture.First.Id,
            AccountCategoryId = categories.Single(category => category.Code == "3100").Id,
            Credit = 1_000m,
            Reviewed = true,
            EnteredBy = "Reviewer"
        });
        var asset = new FixedAsset
        {
            CompanyId = fixture.Company.Id,
            Name = "Machine",
            Category = "Plant & Machinery",
            Cost = 1_000m,
            ResidualValue = 200m,
            AcquisitionDate = new DateOnly(2024, 7, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = fixture.First.Id,
            Date = asset.AcquisitionDate,
            Description = "Machine purchase",
            Amount = -1_000m,
            CategoryId = categories.Single(category => category.Code == "0020").Id
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.First.Id);

        var entry = await db.DepreciationEntries.SingleAsync(depreciation => depreciation.AssetId == asset.Id);
        var expectedFraction = 184m / 366m;
        Assert.Equal(Math.Round(200m * expectedFraction, 2), entry.Charge);
        Assert.True(entry.ClosingNbv >= asset.ResidualValue);
    }

    private static AdjustmentInput Input(int? debit, int? credit, decimal amount) => new(
        "Journal validation",
        debit,
        credit,
        amount,
        null,
        null,
        0m,
        0m);

    private static void AssertTrialBalance(IEnumerable<FinancialStatementsService.TrialBalanceLine> lines)
    {
        var materialised = lines.ToList();
        Assert.Equal(materialised.Sum(line => line.Debit), materialised.Sum(line => line.Credit));
    }

    private static async Task<TwoPeriodFixture> SeedTwoPeriodFixtureAsync(AccountsDbContext db)
    {
        var fixture = await SeedBaseAsync(db);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(fixture.Company.Id);
        int Category(string code) => categories.Single(category => category.Code == code).Id;
        var bank = new BankAccount
        {
            CompanyId = fixture.Company.Id,
            Name = "Current account",
            OpeningBalance = 1_000m,
            OpeningBalanceDate = fixture.First.PeriodStart
        };
        db.BankAccounts.Add(bank);
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = fixture.First.Id,
            AccountCategoryId = Category("3100"),
            Credit = 1_000m,
            SourceNote = "Incorporation capital and opening reserves",
            EnteredBy = "Reviewer",
            Reviewed = true,
            ReviewedBy = "Reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        var asset = new FixedAsset
        {
            CompanyId = fixture.Company.Id,
            Name = "Production computer",
            Category = "Computer Equipment",
            Cost = 1_200m,
            ResidualValue = 200m,
            AcquisitionDate = fixture.First.PeriodStart,
            DisposalDate = new DateOnly(2025, 6, 30),
            DisposalProceeds = 700m,
            UsefulLifeYears = 5,
            DepreciationMethod = DepreciationMethod.StraightLine
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            Transaction(bank, fixture.First, new DateOnly(2024, 2, 1), "Sales receipts", 10_000m, Category("4000")),
            Transaction(bank, fixture.First, new DateOnly(2024, 3, 1), "Inventory purchases", -4_000m, Category("5000")),
            Transaction(bank, fixture.First, new DateOnly(2024, 4, 1), "Interest paid", -100m, Category("6900")),
            Transaction(bank, fixture.First, fixture.First.PeriodStart, "Computer acquisition", -1_200m, Category("0050")),
            Transaction(bank, fixture.First, new DateOnly(2024, 10, 1), "Dividend paid", -300m, Category("3200")),
            Transaction(bank, fixture.First, new DateOnly(2024, 11, 1), "Corporation tax paid", -400m, Category("2400")),
            Transaction(bank, fixture.Second, new DateOnly(2025, 2, 1), "Sales receipts", 12_000m, Category("4000")),
            Transaction(bank, fixture.Second, new DateOnly(2025, 3, 1), "Inventory purchases", -5_000m, Category("5000")),
            Transaction(bank, fixture.Second, new DateOnly(2025, 4, 1), "Interest paid", -120m, Category("6900")),
            Transaction(bank, fixture.Second, new DateOnly(2025, 6, 30), "Computer disposal proceeds", 700m, Category("0050")),
            Transaction(bank, fixture.Second, new DateOnly(2025, 10, 1), "Dividend paid", -400m, Category("3200")),
            Transaction(bank, fixture.Second, new DateOnly(2025, 11, 1), "Corporation tax paid", -500m, Category("2400")));
        db.Inventories.AddRange(
            new Inventory { PeriodId = fixture.First.Id, Description = "Closing inventory", Value = 800m },
            new Inventory { PeriodId = fixture.Second.Id, Description = "Closing inventory", Value = 600m });
        db.TaxBalances.AddRange(
            new TaxBalance { PeriodId = fixture.First.Id, TaxType = TaxType.CorporationTax, Liability = 500m, Paid = 400m, Balance = 100m },
            new TaxBalance { PeriodId = fixture.Second.Id, TaxType = TaxType.CorporationTax, Liability = 600m, Paid = 500m, Balance = 100m });
        db.Dividends.AddRange(
            new Dividend { PeriodId = fixture.First.Id, Amount = 300m, DateDeclared = new DateOnly(2024, 9, 1), DatePaid = new DateOnly(2024, 10, 1) },
            new Dividend { PeriodId = fixture.Second.Id, Amount = 400m, DateDeclared = new DateOnly(2025, 9, 1), DatePaid = new DateOnly(2025, 10, 1) });
        await db.SaveChangesAsync();
        return new TwoPeriodFixture(fixture.Tenant, fixture.Company, fixture.First, fixture.Second, bank, asset);
    }

    private static ImportedTransaction Transaction(
        BankAccount bank,
        AccountingPeriod period,
        DateOnly date,
        string description,
        decimal amount,
        int categoryId) => new()
    {
        BankAccountId = bank.Id,
        PeriodId = period.Id,
        Date = date,
        Description = description,
        Amount = amount,
        CategoryId = categoryId
    };

    private static async Task<TwoPeriodFixture> SeedBaseAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Ledger test tenant", Slug = $"ledger-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Balanced Ledger Limited",
            CroNumber = "778899",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2024, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            HasStock = true,
            OwnsAssets = true,
            RegisteredOfficeAddress1 = "1 Ledger Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var first = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = true
        };
        var second = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.AddRange(first, second);
        await db.SaveChangesAsync();
        return new TwoPeriodFixture(tenant, company, first, second, null!, null!);
    }

    private static AccountsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"double-entry-{Guid.NewGuid():N}")
            .Options);

    private sealed record TwoPeriodFixture(
        Tenant Tenant,
        Company Company,
        AccountingPeriod First,
        AccountingPeriod Second,
        BankAccount Bank,
        FixedAsset Asset);
}
