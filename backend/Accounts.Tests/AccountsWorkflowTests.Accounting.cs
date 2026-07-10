using Accounts.Api.Endpoints;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Accounts.Tests;

public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task SizeClassificationService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = otherPeriod.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 40_000m,
            AvgEmployees = 3
        });
        await db.SaveChangesAsync();
        var before = await db.SizeClassifications
            .AsNoTracking()
            .SingleAsync(sc => sc.PeriodId == otherPeriod.Id);
        var service = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.ClassifyAsync(period.CompanyId, otherPeriod.Id));

        var unchanged = await db.SizeClassifications.SingleAsync(sc => sc.PeriodId == otherPeriod.Id);
        Assert.Equal(before.CalculatedClass, unchanged.CalculatedClass);
        Assert.Equal(before.CalculatedAt, unchanged.CalculatedAt);
        Assert.Null(unchanged.QualificationNotes);
    }

    [Fact]
    public void ClassificationServices_RequireCompanyIdForPeriodMutations()
    {
        var classifyMethods = typeof(SizeClassificationService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(SizeClassificationService.ClassifyAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();
        var regimeMethods = typeof(FilingRegimeService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(FilingRegimeService.DetermineAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(classifyMethods, parameters =>
            parameters.Length >= 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(classifyMethods, parameters =>
            parameters.Length >= 1
            && parameters[0] == typeof(int)
            && (parameters.Length == 1 || parameters[1] != typeof(int)));

        Assert.Contains(regimeMethods, parameters =>
            parameters.Length >= 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(regimeMethods, parameters =>
            parameters.Length >= 1
            && parameters[0] == typeof(int)
            && (parameters.Length == 1 || parameters[1] != typeof(int)));
    }

    [Fact]
    public async Task BalanceSheet_ExposesUnexplainedDifferenceInsteadOfPluggingReserves()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var shareCategory = AddCategory(db, period.CompanyId, "3000", "Share Capital", AccountCategoryType.Equity);
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart,
            Currency = "EUR"
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m,
            IssueDate = period.PeriodStart
        });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCategory.Id,
            Credit = 1m,
            SourceNote = "Only €1 of the €100 bank opening is supported by issued capital",
            EnteredBy = "Reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        _ = bankCategory;

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);

        Assert.False(balanceSheet.Balances);
        Assert.Equal(99m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(1m, balanceSheet.CapitalAndReserves.Total);
    }

    [Fact]
    public async Task BalanceSheet_AccrualDueAfterYearIsNotDoubleCounted()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var expense = AddCategory(db, period.CompanyId, "7900", "Sundry expenses", AccountCategoryType.Expense);
        var longTermCreditor = AddCategory(db, period.CompanyId, "2900", "Other creditors (> 1 year)", AccountCategoryType.Liability);
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Long-term accrual",
            Amount = 1_000m,
            Type = CreditorType.Accrual,
            DueWithinYear = false
        });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "Posted long-term accrual",
            DebitCategoryId = expense.Id,
            CreditCategoryId = longTermCreditor.Id,
            Amount = 1_000m,
            ImpactOnProfit = -1_000m,
            Source = AdjustmentSource.Manual
        });
        await db.SaveChangesAsync();

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);

        // A non-current accrual belongs only in creditors due after more than one year,
        // not also in accruals due within the year.
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.Accruals);
        Assert.Equal(1_000m, balanceSheet.CreditorsAfterYear.Other);
        // Counted once across the two creditor sections, not twice.
        Assert.Equal(1_000m, balanceSheet.CreditorsWithinYear.Total + balanceSheet.CreditorsAfterYear.Total);
    }

    [Fact]
    public async Task CompanyLevelAccountingFacts_AreIgnoredUntilEffectiveForCurrentPeriodOutputs()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Small,
            CanUseMicro = false,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Future server",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = period.PeriodEnd.AddDays(1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });

        var futureLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Future Bank",
            OriginalAmount = 20_000m,
            Balance = 20_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 18_000m
        };
        SetRequiredDate(futureLoan, "DrawdownDate", period.PeriodEnd.AddDays(1));
        SetRequiredDate(futureLoan, "BalanceAsOfDate", period.PeriodEnd.AddDays(1));

        var futureShare = new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Future Ordinary",
            NumberIssued = 20_000,
            NominalValue = 1m,
            TotalValue = 20_000m
        };
        SetRequiredDate(futureShare, "IssueDate", period.PeriodEnd.AddDays(1));
        db.Loans.Add(futureLoan);
        db.ShareCapitals.Add(futureShare);
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);
        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db)).GetCt1SupportDataAsync(period.CompanyId, period.Id);
        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        Assert.Empty(await db.DepreciationEntries.Where(d => d.PeriodId == period.Id).ToListAsync());
        Assert.DoesNotContain(await db.Adjustments.Where(a => a.PeriodId == period.Id).ToListAsync(), a =>
            a.Description.Contains("Future server", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tax.Adjustments, a =>
            a.Description.Contains("Capital allowances", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, ct1.CapitalAllowances);
        var fixedAssetNote = Assert.Single(notes, n => n.Code == StatutoryNoteCodes.FixedAssets);
        Assert.Equal(NoteChecklistState.NotApplicable, fixedAssetNote.ChecklistState);
        Assert.False(fixedAssetNote.IsIncluded);
        var longTermCreditorNote = Assert.Single(notes, n => n.Code == StatutoryNoteCodes.LongTermCreditors);
        Assert.Equal(NoteChecklistState.NotApplicable, longTermCreditorNote.ChecklistState);
        Assert.False(longTermCreditorNote.IsIncluded);
        var shareNote = Assert.Single(notes, n => n.Title == "Share Capital");
        Assert.DoesNotContain("Future Ordinary", shareNote.Content);
        Assert.DoesNotContain("Future Bank", string.Join("\n", notes.Select(n => n.Content)));
    }

    [Fact]
    public async Task CapitalAllowances_AreProRatedForShortAccountingPeriod()
    {
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Short Period Limited",
            CroNumber = "654321",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 6, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        // First accounting period of 181 days (1 Jan – 30 Jun 2025), shorter than 12 months.
        var shortPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 6, 30),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(shortPeriod);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Laptop fleet",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 15),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine,
            CapitalAllowanceTreatment = CapitalAllowanceTreatment.PlantAndMachinery12Point5,
            CapitalAllowanceEvidence = "Invoice and exclusive trade-use evidence retained for test.",
            CapitalAllowanceReviewedBy = "Automated test actor (not human acceptance)",
            CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(company.Id, shortPeriod.Id);

        // s.284 TCA: 8000 * 12.5% * (181/365) = 495.89, not the full-year 1000.
        Assert.Equal(495.89m, ct1.CapitalAllowances);
        Assert.True(ct1.CapitalAllowances < 1_000m);
    }

    [Fact]
    public async Task CapitalAllowances_FullTwelveMonthPeriodGivesFullWearAndTear()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true); // 1 Jan – 31 Dec 2025
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop fleet",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 15),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine,
            CapitalAllowanceTreatment = CapitalAllowanceTreatment.PlantAndMachinery12Point5,
            CapitalAllowanceEvidence = "Invoice and exclusive trade-use evidence retained for test.",
            CapitalAllowanceReviewedBy = "Automated test actor (not human acceptance)",
            CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(period.CompanyId, period.Id);

        // A full 12-month period attracts the full 12.5% wear and tear: 8000 * 12.5% = 1000.
        Assert.Equal(1_000m, ct1.CapitalAllowances);
    }

    [Fact]
    public async Task CapitalAllowances_CapCumulativeClaimUsingPersistedPriorClaims()
    {
        // BL-06: prior claims come from persisted records, not re-estimated from period length or
        // depreciation entries. An asset already 7,500/8,000 claimed leaves only 500 this period,
        // even with no depreciation entries from which the old code could re-derive the prior claim.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Capital Allowance Limited",
            CroNumber = "778899",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2018, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var priorPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = false
        };
        var currentPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.AddRange(priorPeriod, currentPeriod);
        var asset = new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Machine",
            Category = "Plant & Machinery",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2018, 6, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine,
            CapitalAllowanceTreatment = CapitalAllowanceTreatment.PlantAndMachinery12Point5,
            CapitalAllowanceEvidence = "Invoice and exclusive trade-use evidence retained for test.",
            CapitalAllowanceReviewedBy = "Automated test actor (not human acceptance)",
            CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();

        // 7,500 already claimed in a prior period — deliberately with no depreciation entries.
        db.CapitalAllowanceClaims.Add(new CapitalAllowanceClaim
        {
            AssetId = asset.Id,
            PeriodId = priorPeriod.Id,
            Cost = 8_000m,
            Claim = 7_500m
        });
        await db.SaveChangesAsync();

        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(company.Id, currentPeriod.Id);

        // Only 500 of cost remains, so the claim is capped at 500, not the full-year 1,000.
        Assert.Equal(500m, ct1.CapitalAllowances);
    }

    [Fact]
    public async Task Depreciation_ReducingBalanceFullyWritesDownByEndOfUsefulLife()
    {
        // BL-21: a reducing-balance asset must be fully written down by the end of its useful life,
        // not leave an indefinite reducing-balance residual.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Reducing Balance Limited",
            CroNumber = "112233",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var periods = new List<AccountingPeriod>();
        for (var year = 2025; year <= 2027; year++)
            periods.Add(new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(year, 1, 1), PeriodEnd = new DateOnly(year, 12, 31), IsFirstYear = year == 2025 });
        db.AccountingPeriods.AddRange(periods);
        var asset = new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Van",
            Category = "Motor Vehicles",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.ReducingBalance
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();

        var service = new AdjustmentService(db);
        foreach (var p in periods)
            await service.GenerateAutoAdjustmentsAsync(company.Id, p.Id);

        var finalEntry = await db.DepreciationEntries.SingleAsync(d => d.AssetId == asset.Id && d.PeriodId == periods[2].Id);
        Assert.Equal(0m, finalEntry.ClosingNbv);
        var totalCharge = await db.DepreciationEntries.Where(d => d.AssetId == asset.Id).SumAsync(d => d.Charge);
        Assert.Equal(8_000m, totalCharge);
    }

    [Fact]
    public async Task BalanceSheet_MultiYearCashOnMovementBasis_CarriesPriorYearsAndBalances()
    {
        // accounting-multiyear-cash-movement-basis: year-2+ cash must carry forward prior years' net
        // movement, not just the current period's transactions. 3-year chain, bank opening 0, no manual
        // opening rows -> each year's cash == cumulative net movement AND the balance sheet balances.
        await using var db = CreateDbContext();
        var period2025 = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        var companyId = period2025.CompanyId;
        var period2023 = new AccountingPeriod { CompanyId = companyId, PeriodStart = new DateOnly(2023, 1, 1), PeriodEnd = new DateOnly(2023, 12, 31), IsFirstYear = true };
        var period2024 = new AccountingPeriod { CompanyId = companyId, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(period2023, period2024);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        void AddTxn(int periodId, DateOnly date, string desc, decimal amount, string code) =>
            db.ImportedTransactions.Add(new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = periodId,
                Date = date,
                Description = desc,
                Amount = amount,
                CategoryId = Cat(code)
            });

        AddTxn(period2023.Id, new DateOnly(2023, 6, 1), "Sales invoice", 1_000m, "4000");
        AddTxn(period2023.Id, new DateOnly(2023, 7, 1), "Office supplies", -200m, "6500");
        AddTxn(period2024.Id, new DateOnly(2024, 6, 1), "Sales invoice", 1_200m, "4000");
        AddTxn(period2024.Id, new DateOnly(2024, 7, 1), "Office supplies", -300m, "6500");
        AddTxn(period2025.Id, new DateOnly(2025, 6, 1), "Sales invoice", 1_500m, "4000");
        AddTxn(period2025.Id, new DateOnly(2025, 7, 1), "Office supplies", -500m, "6500");
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bs2023 = await statements.GetBalanceSheetAsync(companyId, period2023.Id);
        var bs2024 = await statements.GetBalanceSheetAsync(companyId, period2024.Id);
        var bs2025 = await statements.GetBalanceSheetAsync(companyId, period2025.Id);

        // Cash accumulates the net movement of every period to date.
        Assert.Equal(800m, bs2023.CurrentAssets.Cash);
        Assert.Equal(1_700m, bs2024.CurrentAssets.Cash);   // 800 + 900
        Assert.Equal(2_700m, bs2025.CurrentAssets.Cash);   // 800 + 900 + 1000

        // The year-2 and year-3 balance sheets balance with no manual opening rows.
        Assert.Equal(0m, bs2024.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(0m, bs2025.CapitalAndReserves.UnexplainedDifference);
        Assert.True(bs2024.Balances);
        Assert.True(bs2025.Balances);
    }

    [Fact]
    public async Task BalanceSheet_MixedCashAccrualScenario_BalancesWithZeroUnexplainedDifference()
    {
        // BL-01: a realistic mixed cash/accrual set must reconcile. Net assets are built from the
        // entity tables (debtors/creditors/stock/fixed assets/loans) + bank cash, while reserves come
        // from the P&L. The auto-adjustment engine posts the accrual contras (trade debtors -> turnover,
        // trade creditors/accruals -> expense, prepayments, stock, depreciation) that keep the two sides
        // in step, so UnexplainedDifference must be exactly zero.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        // Share capital of €100 funded by an opening bank balance of €100 (cash the members paid in).
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = new DateOnly(2025, 1, 1)
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = new DateOnly(2025, 1, 1)
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        // Cash movements: +10,000 trading sales, -3,000 rent, -4,000 capex (asset — outside the P&L),
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Reviewer",
            Reviewed = true
        });

        // Cash movements: +10,000 trading sales, -3,000 rent, -4,000 capex (asset — outside the P&L),
        // +5,000 loan drawdown. Capex and loan are coded to balance-sheet categories, so they move cash
        // without touching profit.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 2, 1), Description = "Sales", Amount = 10_000m, CategoryId = Cat("4000") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Rent", Amount = -3_000m, CategoryId = Cat("6100") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 4, 1), Description = "Plant purchase", Amount = -4_000m, CategoryId = Cat("0020") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 5, 1), Description = "Bank loan drawdown", Amount = 5_000m, CategoryId = Cat("2600") });

        // Accrual-basis facts entered as year-end entity rows.
        db.Debtors.AddRange(
            new Debtor { PeriodId = period.Id, Name = "Customer X", Amount = 2_000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 300m, Type = DebtorType.Prepayment });
        db.Creditors.AddRange(
            new Creditor { PeriodId = period.Id, Name = "Supplier Y", Amount = 1_500m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = period.Id, Name = "Accountancy fees", Amount = 500m, Type = CreditorType.Accrual, DueWithinYear = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Closing stock", Value = 800m, ValuationMethod = ValuationMethod.Cost });

        // Fixed asset matching the €4,000 capex, depreciated straight-line over 4 years (€1,000/yr).
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = companyId,
            Name = "Plant",
            Category = "Plant & Machinery",
            Cost = 4_000m,
            AcquisitionDate = new DateOnly(2025, 4, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });

        // Loan: €5,000 drawn in-period, €1,000 due within a year and €4,000 after.
        db.Loans.Add(new Loan
        {
            CompanyId = companyId,
            Lender = "Bank",
            OriginalAmount = 5_000m,
            Balance = 5_000m,
            DueWithinYear = 1_000m,
            DueAfterYear = 4_000m,
            DrawdownDate = new DateOnly(2025, 5, 1),
            BalanceAsOfDate = period.PeriodEnd
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, period.Id);

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(companyId, period.Id);

        // The whole point of BL-01: a correct mixed cash/accrual set balances exactly.
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.True(balanceSheet.Balances);

        // Headline figures match the hand-computed scenario.
        Assert.Equal(7_446.58m, balanceSheet.NetAssets);
        Assert.Equal(7_446.58m, balanceSheet.CapitalAndReserves.Total);
        Assert.Equal(100m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(7_346.58m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(2_000m, balanceSheet.CurrentAssets.Debtors);
        Assert.Equal(1_500m, balanceSheet.CreditorsWithinYear.TradeCreditors);
        Assert.Equal(3_246.58m, balanceSheet.FixedAssets.Total);
    }

    // ----------------------------------------------------------------------------------------------
    // Golden-path end-to-end tests (Trust guarantee #1). Each drives the WHOLE pipeline with the real
    // services — onboard -> import a real CSV -> categorise -> year-end facts -> generate adjustments ->
    // statements that BALANCE -> accounts PDF -> iXBRL — for a shipped regime, and proves the period
    // clears the readiness gate so the final outputs actually generate.
    // ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ElectedRegime.Medium)]
    [InlineData(ElectedRegime.Full)]
    public void AccountsPackage_MediumAndFullIncludeEveryRequiredPrimaryStatement(ElectedRegime regime)
    {
        // BL-02 / BL-03: Medium and Full packages must render a Cash Flow Statement, a Statement of
        // Changes in Equity and an Auditor's Report — the sections the Small PDF omitted. The rendered
        // section set must cover every primary statement the filing regime requires.
        var rendered = DocumentGeneratorService.GetIncludedPrimaryStatements(regime, DocumentPackagePurpose.StatutoryApproval, auditExempt: false);
        Assert.Contains("Cash Flow Statement", rendered);
        Assert.Contains("Statement of Changes in Equity", rendered);
        Assert.Contains("Independent Auditor's Report", rendered);
        Assert.Contains("Profit and Loss Account", rendered);
        Assert.Contains("Balance Sheet", rendered);

        // Cross-check against the regime contract: every required primary statement is covered.
        var required = FilingRegimeService.GetRequiredStatements(regime, CompanySizeClass.Medium);
        foreach (var heading in new[] { "Cash Flow Statement", "Statement of Changes in Equity", "Auditor's Report" })
        {
            Assert.Contains(required, r => r.Contains(heading, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rendered, r => r.Contains(heading, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData(nameof(FinancialStatementsService.GetTrialBalanceAsync), typeof(List<FinancialStatementsService.TrialBalanceLine>))]
    [InlineData(nameof(FinancialStatementsService.GetProfitAndLossAsync), typeof(FinancialStatementsService.ProfitAndLoss))]
    [InlineData(nameof(FinancialStatementsService.GetBalanceSheetAsync), typeof(FinancialStatementsService.BalanceSheet))]
    [InlineData(nameof(FinancialStatementsService.GetReadinessScoreAsync), typeof(FinancialStatementsService.ReadinessScore))]
    [InlineData(nameof(FinancialStatementsService.GetStatementSourcesAsync), typeof(List<FinancialStatementsService.StatementSourceSummary>))]
    [InlineData(nameof(FinancialStatementsService.GetCashFlowStatementAsync), typeof(FinancialStatementsService.CashFlowStatement))]
    [InlineData(nameof(FinancialStatementsService.GetEquityChangesAsync), typeof(FinancialStatementsService.EquityChanges))]
    public async Task StatementServices_RejectMismatchedCompanyPeriod(string methodName, Type resultType)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new FinancialStatementsService(db);
        var method = typeof(FinancialStatementsService).GetMethod(methodName, [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [period.CompanyId, otherPeriod.Id]));
        Assert.True(typeof(Task<>).MakeGenericType(resultType).IsInstanceOfType(task));
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void FinancialStatementsService_RequiresCompanyIdForPublicStatementOutputs()
    {
        var methodNames = new HashSet<string>
        {
            nameof(FinancialStatementsService.GetTrialBalanceAsync),
            nameof(FinancialStatementsService.GetProfitAndLossAsync),
            nameof(FinancialStatementsService.GetBalanceSheetAsync),
            nameof(FinancialStatementsService.GetReadinessScoreAsync),
            nameof(FinancialStatementsService.GetStatementSourcesAsync),
            nameof(FinancialStatementsService.GetCashFlowStatementAsync),
            nameof(FinancialStatementsService.GetEquityChangesAsync)
        };
        var methods = typeof(FinancialStatementsService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => methodNames.Contains(m.Name))
            .Select(m => new { m.Name, Parameters = m.GetParameters().Select(p => p.ParameterType).ToArray() })
            .ToList();

        foreach (var methodName in methodNames)
        {
            Assert.Contains(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 2
                && m.Parameters[0] == typeof(int)
                && m.Parameters[1] == typeof(int));
            Assert.DoesNotContain(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 1
                && m.Parameters[0] == typeof(int));
        }
    }

    [Fact]
    public async Task CategoryService_SeedsDefaultIrishChartOfAccounts()
    {
        // BL-17: the default chart of accounts was untested.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var cats = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);

        Assert.True(cats.Count >= 50, $"expected a full chart of accounts, got {cats.Count}");
        Assert.All(cats, c => Assert.Equal(period.CompanyId, c.CompanyId));
        Assert.Contains(cats, c => c.Code == "4000" && c.Type == AccountCategoryType.Income);
        Assert.Contains(cats, c => c.Code == "1400" && c.Type == AccountCategoryType.Asset);
        Assert.Contains(cats, c => c.Code == "2000" && c.Type == AccountCategoryType.Liability);
        Assert.Contains(cats, c => c.Code == "3000" && c.Type == AccountCategoryType.Equity);
        Assert.Contains(cats, c => c.Code == "7000" && c.TaxTreatment == TaxTreatment.NonDeductible);

        // Re-seeding is idempotent — it returns the existing set without duplicating.
        var again = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);
        Assert.Equal(cats.Count, again.Count);
        Assert.Equal(cats.Count, await db.AccountCategories.CountAsync(c => c.CompanyId == period.CompanyId));
    }

    [Fact]
    public async Task CategoryService_AutoCategorisesByRuleThenFuzzyNameWithConfidence()
    {
        // BL-17: confidence-scored auto-categorisation was untested.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new CategoryService(db);
        var cats = await service.SeedDefaultCategoriesAsync(period.CompanyId);
        var rent = cats.Single(c => c.Code == "6100");

        // A matching transaction rule wins with high (0.85) confidence.
        db.TransactionRules.Add(new TransactionRule { CompanyId = period.CompanyId, Pattern = "ACME LANDLORD", CategoryId = rent.Id, Priority = 1 });
        await db.SaveChangesAsync();
        var ruled = await service.AutoCategoriseAsync(period.CompanyId, "Payment to ACME LANDLORD Ltd");
        Assert.Equal(rent.Id, ruled.categoryId);
        Assert.Equal(0.85m, ruled.confidence);

        // No rule: fall back to a fuzzy category-name match at lower (0.5) confidence.
        var fuzzy = await service.AutoCategoriseAsync(period.CompanyId, "Monthly insurance premium");
        Assert.NotNull(fuzzy.categoryId);
        Assert.Equal(0.5m, fuzzy.confidence);

        // Nothing matches: no category, zero confidence.
        var none = await service.AutoCategoriseAsync(period.CompanyId, "zzzz qqqq");
        Assert.Null(none.categoryId);
        Assert.Equal(0m, none.confidence);
    }

    [Fact]
    public void CorsOriginConfig_UsesConfiguredOriginsAndDevelopmentFallbackOnlyInDevelopment()
    {
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AllowedOrigins:1"] = "https://app.accounts.example.ie"
            })
            .Build();
        var empty = new ConfigurationBuilder().Build();

        Assert.Equal(
            ["https://accounts.example.ie", "https://app.accounts.example.ie"],
            CorsOriginConfig.Resolve(configured, new TestEnvironment("Production")));
        Assert.Equal(
            ["http://localhost:3000", "http://localhost:5173", "http://localhost:5174"],
            CorsOriginConfig.Resolve(empty, new TestEnvironment("Development")));
        Assert.Empty(CorsOriginConfig.Resolve(empty, new TestEnvironment("Staging")));
    }

    [Fact]
    public void DatabaseConnectionConfig_UsesDevelopmentFallbackOnlyInDevelopment()
    {
        var empty = new ConfigurationBuilder().Build();
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = " Host=db;Database=accounts "
            })
            .Build();

        Assert.Contains("accounts_dev", DatabaseConnectionConfig.Resolve(empty, new TestEnvironment("Development")));
        Assert.Equal(
            "Host=db;Database=accounts",
            DatabaseConnectionConfig.Resolve(configured, new TestEnvironment("Production")));
        var error = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionConfig.Resolve(empty, new TestEnvironment("Staging")));
        Assert.Contains("ConnectionStrings:DefaultConnection", error.Message);
        Assert.Contains("outside Development", error.Message);
    }

    [Fact]
    public void ProductionSafety_BlocksDemoDatabaseStartupInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AutoMigrateOnStartup"));
        Assert.Contains(failures, f => f.Contains("SeedDemoData"));
        Assert.Contains(failures, f => f.Contains("development database password"));
        Assert.Contains(failures, f => f.Contains("localhost"));
        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
        Assert.Contains(failures, f => f.Contains("AuditIntegrity:SigningKeys"));
        Assert.Contains(failures, f => f.Contains("ApiAccess:Enabled"));
    }

    [Fact]
    public void ProductionSafety_BlocksDevelopmentDefaultsInStaging()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000",
                ["AuthSession:SigningKey"] = DevelopmentSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Staging"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Staging")),
            Options.Create(new AuditIntegrityConfig
            {
                ActiveKeyId = "development-audit-checkpoint",
                SigningKeys =
                [
                    new AuditIntegritySigningKeyConfig
                    {
                        KeyId = "development-audit-checkpoint",
                        SigningKey = AuditIntegrityCheckpointService.DevelopmentSigningKeyBase64
                    }
                ]
            }));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AutoMigrateOnStartup"));
        Assert.Contains(failures, f => f.Contains("SeedDemoData"));
        Assert.Contains(failures, f => f.Contains("development database password"));
        Assert.Contains(failures, f => f.Contains("localhost"));
        Assert.Contains(failures, f => f.Contains("AllowedHosts"));
        Assert.Contains(failures, f => f.Contains("development session signing key"));
        Assert.Contains(failures, f => f.Contains("development audit checkpoint key"));
        Assert.Contains(failures, f => f.Contains("ApiAccess:Enabled"));
    }

    [Fact]
    public void ProductionSafety_AllowsDevelopmentDefaultsInDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000",
                ["AuthSession:SigningKey"] = DevelopmentSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Development"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Development")),
            Options.Create(new AuditIntegrityConfig()));

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksDemoSeedingOutsideDevelopmentEvenWhenExplicitlyAllowed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Staging"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = true,
                AllowDemoSeedInProduction = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Staging")),
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("SeedDemoData must be disabled"));
    }

    [Fact]
    public void ProductionSafety_BlocksMissingConnectionStringInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("DefaultConnection must be explicitly configured"));
    }

    [Fact]
    public void ProductionSafety_RequiresCertificateVerifiedDatabaseTlsOutsideDevelopment()
    {
        IConfiguration Config(string connectionString) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();

        ProductionSafetyService Build(string connectionString, bool allowInsecure)
        {
            var config = Config(connectionString);
            return new ProductionSafetyService(
                new TestEnvironment("Staging"),
                config,
                Options.Create(new DatabaseStartupConfig { AutoMigrateOnStartup = false, SeedDemoData = false, AllowInsecureDatabaseConnection = allowInsecure }),
                AuthSessionOptions(config),
                new ApiAccessService(
                    Options.Create(new ApiAccessConfig
                    {
                        Enabled = true,
                        RequireInProduction = true,
                        Keys = [new ApiAccessKeyConfig { Name = "Production firm", KeyHash = ApiAccessService.HashKey("real-secret") }]
                    }),
                    new TestEnvironment("Staging")),
                Options.Create(AuditIntegrityCheckpointOptions()),
                Options.Create(new BootstrapOwnerConfig
                {
                    Enabled = true,
                    TenantName = "Production Firm",
                    TenantSlug = "production-firm",
                    OwnerEmail = "owner@example.ie",
                    OwnerDisplayName = "Owner User",
                    OwnerInitialPassword = "Correct Horse Battery Staple 1!"
                }));
        }

        const string tlsFailure = "certificate-verified TLS";
        Assert.Contains(Build("Host=db;Password=secure-prod-password", allowInsecure: false).Validate(), f => f.Contains(tlsFailure));
        Assert.Contains(Build("Host=db;Password=secure-prod-password;SSL Mode=Require", allowInsecure: false).Validate(), f => f.Contains(tlsFailure));
        Assert.Contains(Build("Host=db;Password=secure-prod-password;SSL Mode=VerifyCA;Root Certificate=/run/secrets/postgres_ca_certificate", allowInsecure: false).Validate(), f => f.Contains(tlsFailure));
        Assert.Contains(Build("Host=db;Password=secure-prod-password;SSL Mode=VerifyFull", allowInsecure: false).Validate(), f => f.Contains(tlsFailure));
        Assert.Contains(Build("Host=db;Password=secure-prod-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=true", allowInsecure: false).Validate(), f => f.Contains(tlsFailure));
        Assert.DoesNotContain(Build("Host=db;Password=secure-prod-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false", allowInsecure: false).Validate(), f => f.Contains(tlsFailure));

        var insecureOverrideFailures = Build("Host=db;Password=secure-prod-password", allowInsecure: true).Validate();
        Assert.Contains(insecureOverrideFailures, f => f.Contains("AllowInsecureDatabaseConnection must be false"));
        Assert.Contains(insecureOverrideFailures, f => f.Contains(tlsFailure));
    }

    [Fact]
    public void ProductionSafety_AllowsDeliberateProductionConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["RateLimits:TrustForwardedFor"] = "true",
                ["TRUST_PROXY_HEADERS"] = "true"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions(),
            deadlineDelivery: SecureDeadlineDeliveryOptions(),
            platformMetrics: SecurePlatformMetricsOptions(),
            databaseTenantIsolation: SecureDatabaseTenantIsolationOptions(),
            identitySecurity: SecureIdentitySecurityOptions());

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksTrustedForwardedForWithoutIngressAcknowledgement()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["RateLimits:TrustForwardedFor"] = "true"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions(),
            deadlineDelivery: SecureDeadlineDeliveryOptions(),
            platformMetrics: SecurePlatformMetricsOptions(),
            databaseTenantIsolation: SecureDatabaseTenantIsolationOptions(),
            identitySecurity: SecureIdentitySecurityOptions());

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("TRUST_PROXY_HEADERS"));
    }

    [Fact]
    public void ProductionSafety_BlocksBlankAllowedHostsInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = " ",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions(),
            deadlineDelivery: SecureDeadlineDeliveryOptions(),
            platformMetrics: SecurePlatformMetricsOptions(),
            databaseTenantIsolation: SecureDatabaseTenantIsolationOptions(),
            identitySecurity: SecureIdentitySecurityOptions());

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AllowedHosts must be explicitly configured"));
    }

    [Fact]
    public void ProductionSafety_BlocksWildcardAllowedHostsInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions());

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AllowedHosts"));
    }

    [Fact]
    public void ProductionSafety_BlocksHttpAllowedOriginsInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "http://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions());

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AllowedOrigins"));
    }

    [Fact]
    public async Task SizeClassificationSaveEndpoint_DeniesClientBeforeMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Client",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/size-classification");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { period.CompanyId }
        };

        var result = await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            period.CompanyId,
            period.Id,
            new SizeClassificationInput(120_000m, 40_000m, 3, null),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(result));
        Assert.Empty(await db.SizeClassifications.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.SizeClassificationDataSaved).ToListAsync());
    }

    [Fact]
    public async Task DeleteCompany_QuarantinesRecoverablyWithTypedConfirmationAndReason()
    {
        // data-company-soft-delete: no company is cascade-wiped. Owner confirmation and a reason are
        // required even for an empty company, and populated dependants remain retained but filtered.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var company = await db.Companies.FirstAsync(c => c.Id == companyId);
        var bank = new BankAccount { CompanyId = companyId, Name = "Current account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = period.PeriodStart, Description = "Receipt", Amount = 100m });
        await db.SaveChangesAsync();
        var writeGuard = new AccountingWriteGuard(db);

        // No confirmation -> blocked, company preserved.
        var blocked = await CompanyDeletionEndpoint.DeleteAsync(companyId, null,
            AuthenticatedRequest("Owner", HttpMethods.Delete, $"/api/companies/{companyId}"), DisabledApiAccess(), db, writeGuard, new AuditService(db));
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(blocked));
        Assert.NotNull(await db.Companies.FindAsync(companyId));

        var quarantined = await CompanyDeletionEndpoint.DeleteAsync(
            companyId,
            new CompanyQuarantineRequest(company.LegalName, "Owner-requested quarantine after retained engagement closure."),
            AuthenticatedRequest("Owner", HttpMethods.Delete, $"/api/companies/{companyId}"),
            DisabledApiAccess(),
            db,
            writeGuard,
            new AuditService(db));
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(quarantined));
        db.ChangeTracker.Clear();
        Assert.Null(await db.Companies.SingleOrDefaultAsync(candidate => candidate.Id == companyId));
        Assert.True((await db.Companies.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == companyId)).IsQuarantined);
        Assert.Equal(1, await db.ImportedTransactions.IgnoreQueryFilters().CountAsync());

        // Empty companies are protected by the same explicit confirmation gate.
        var emptyPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var emptyDelete = await CompanyDeletionEndpoint.DeleteAsync(emptyPeriod.CompanyId, null,
            AuthenticatedRequest("Owner", HttpMethods.Delete, $"/api/companies/{emptyPeriod.CompanyId}"), DisabledApiAccess(), db, new AccountingWriteGuard(db), new AuditService(db));
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(emptyDelete));
    }

    [Fact]
    public void KestrelIntegrationTests_DoNotReserveLoopbackPortsBeforeBinding()
    {
        var source = AccountsWorkflowTestSource();

        Assert.DoesNotContain("Get" + "FreeLoopbackPort", source);
        Assert.DoesNotContain("Tcp" + "Listener", source);
        Assert.DoesNotContain("UseUrls($\"http://127.0.0.1:{" + "port}\")", source);
    }

    [Fact]
    public async Task PeriodOwnershipMiddleware_BlocksPeriodFromDifferentCompany()
    {
        await using var db = CreateDbContext();
        var allowedPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{allowedPeriod.CompanyId}/periods/{otherPeriod.Id}/debtors";
        var middleware = new PeriodOwnershipMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task PeriodOwnershipMiddleware_AllowsPeriodFromRouteCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodOwnershipMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CompanyList_FiltersHumanClientCompanyAssignments()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });

        var visibleCompanyIds = await CompanyListQuery
            .ForContext(context, db.Companies)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal([assignedCompany.Id], visibleCompanyIds);
        Assert.DoesNotContain(otherCompany.Id, visibleCompanyIds);
    }

    [Fact]
    public async Task CompanyDashboardRows_ProjectLatestPeriodAndAssignedReviewer()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var otherTenant = await SeedTenantAsync(db, name: "Tenant B", slug: "tenant-b");
        var company = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        company.IsGroupMember = true;
        var unassignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Unassigned Client Limited");
        var hiddenCompany = await SeedTenantCompanyAsync(db, otherTenant.Id, "Hidden Client Limited");
        var priorPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            Status = PeriodStatus.Filed
        };
        var latestPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Review,
            IsFirstYear = false,
            MemberAuditNoticeReceived = true,
            MemberAuditNoticeDate = new DateOnly(2026, 4, 1),
            GoingConcernConfirmed = true
        };
        db.AccountingPeriods.AddRange(priorPeriod, latestPeriod);
        await db.SaveChangesAsync();

        var reviewer = await SeedUserAsync(db, tenant, "reviewer@example.ie", "Correct Horse Battery Staple 1!", role: "Reviewer");
        reviewer.DisplayName = "Niamh Reviewer";
        var accountant = await SeedUserAsync(db, tenant, "accountant@example.ie", "Correct Horse Battery Staple 1!", role: "Accountant");
        accountant.DisplayName = "Aine Accountant";
        var inactiveReviewer = await SeedUserAsync(db, tenant, "inactive@example.ie", "Correct Horse Battery Staple 1!", isActive: false, role: "Reviewer");
        inactiveReviewer.DisplayName = "Inactive Reviewer";
        var crossTenantReviewer = await SeedUserAsync(db, otherTenant, "cross@example.ie", "Correct Horse Battery Staple 1!", role: "Reviewer");
        crossTenantReviewer.DisplayName = "A Cross Tenant Reviewer";
        db.UserCompanyAccesses.AddRange(
            new UserCompanyAccess { UserId = reviewer.Id, CompanyId = company.Id },
            new UserCompanyAccess { UserId = accountant.Id, CompanyId = company.Id },
            new UserCompanyAccess { UserId = inactiveReviewer.Id, CompanyId = company.Id });
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");

        var rows = await CompanyDashboardRows
            .ForContext(context, db)
            .ToListAsync();

        Assert.DoesNotContain(rows, row => row.Id == hiddenCompany.Id);
        var assignedRow = Assert.Single(rows, row => row.Id == company.Id);
        Assert.True(assignedRow.IsGroupMember);
        Assert.Equal(2, assignedRow.PeriodCount);
        Assert.Equal("Niamh Reviewer", assignedRow.AssignedReviewerName);
        Assert.Equal("reviewer@example.ie", assignedRow.AssignedReviewerEmail);
        Assert.NotNull(assignedRow.LatestPeriod);
        Assert.Equal(latestPeriod.Id, assignedRow.LatestPeriod!.Id);
        Assert.Equal(PeriodStatus.Review, assignedRow.LatestPeriod.Status);
        Assert.Equal(new DateOnly(2026, 12, 31), assignedRow.LatestPeriod.PeriodEnd);

        var unassignedRow = Assert.Single(rows, row => row.Id == unassignedCompany.Id);
        Assert.Null(unassignedRow.AssignedReviewerName);
        Assert.Null(unassignedRow.AssignedReviewerEmail);
        Assert.Null(unassignedRow.LatestPeriod);
    }

    [Fact]
    public async Task AccountingWriteGuard_AllowsFutureDatedCompanyAccountingWritesAfterLockedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var guard = new AccountingWriteGuard(db);

        var decision = await guard.CheckCompanyAccountingWriteAsync(period.CompanyId, period.PeriodEnd.AddDays(1));

        Assert.True(decision.CanWrite);
    }

    [Fact]
    public void EndpointInputs_RejectsInvalidCompanyAndPeriodInputs()
    {
        var badCompany = new CompanyInput
        {
            LegalName = "",
            IncorporationDate = default,
            FinancialYearStartMonth = 13,
            AnnualReturnDate = null
        };
        var badPeriod = new AccountingPeriodInput
        {
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2026, 8, 1),
            MemberAuditNoticeReceived = true
        };

        Assert.NotNull(EndpointInputs.ValidateCompany(badCompany));
        Assert.NotNull(EndpointInputs.ValidatePeriod(badPeriod));
    }

}
