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
    public async Task BalanceSheet_GroupsFixedAssetDepreciationPerAssetNotPerCategoryTotal()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var fixedAssetCategory = AddCategory(db, period.CompanyId, "0050", "Computer Equipment", AccountCategoryType.Asset);
        var depreciationCategory = AddCategory(db, period.CompanyId, "7000", "Depreciation", AccountCategoryType.Expense);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var laptop = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 100m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 3
        };
        var server = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Server",
            Category = "Computer Equipment",
            Cost = 200m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 4
        };
        db.FixedAssets.AddRange(laptop, server);
        await db.SaveChangesAsync();

        db.DepreciationEntries.AddRange(
            new DepreciationEntry
            {
                AssetId = laptop.Id,
                PeriodId = period.Id,
                OpeningNbv = 100m,
                Charge = 30m,
                ClosingNbv = 70m
            },
            new DepreciationEntry
            {
                AssetId = server.Id,
                PeriodId = period.Id,
                OpeningNbv = 200m,
                Charge = 50m,
                ClosingNbv = 150m
            });
        db.OpeningBalances.AddRange(
            new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = fixedAssetCategory.Id,
                Debit = 300m,
                SourceNote = "Fixed-asset register take-on",
                EnteredBy = "Reviewer",
                Reviewed = true
            },
            new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = retainedCategory.Id,
                Credit = 300m,
                SourceNote = "Balancing retained earnings take-on",
                EnteredBy = "Reviewer",
                Reviewed = true
            });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "Posted depreciation charge",
            DebitCategoryId = depreciationCategory.Id,
            CreditCategoryId = fixedAssetCategory.Id,
            Amount = 80m,
            ImpactOnProfit = -80m,
            ImpactOnAssets = -80m,
            Source = AdjustmentSource.Manual
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var computerEquipment = balanceSheet.FixedAssets.Categories.Single(c => c.Category == "Computer Equipment");

        Assert.Equal(300m, computerEquipment.Cost);
        Assert.Equal(80m, computerEquipment.Depreciation);
        Assert.Equal(220m, computerEquipment.Nbv);
    }

    [Fact]
    public async Task BalanceSheet_IgnoresFixedAssetsAndBankOpeningsAfterPeriodEnd()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var fixedAssetCategory = AddCategory(db, period.CompanyId, "0050", "Computer Equipment", AccountCategoryType.Asset);
        var depreciationCategory = AddCategory(db, period.CompanyId, "7000", "Depreciation", AccountCategoryType.Expense);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var currentAsset = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 100m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 3
        };
        db.FixedAssets.AddRange(
            currentAsset,
            new FixedAsset
            {
                CompanyId = period.CompanyId,
                Name = "Future server",
                Category = "Computer Equipment",
                Cost = 900m,
                AcquisitionDate = period.PeriodEnd.AddDays(1),
                UsefulLifeYears = 4
            });
        db.BankAccounts.AddRange(
            new BankAccount
            {
                CompanyId = period.CompanyId,
                Name = "Current account",
                OpeningBalance = 100m,
                OpeningBalanceDate = period.PeriodStart
            },
            new BankAccount
            {
                CompanyId = period.CompanyId,
                Name = "Future account",
                OpeningBalance = 900m,
                OpeningBalanceDate = period.PeriodEnd.AddDays(1)
            });
        await db.SaveChangesAsync();
        db.DepreciationEntries.Add(new DepreciationEntry
        {
            AssetId = currentAsset.Id,
            PeriodId = period.Id,
            OpeningNbv = 100m,
            Charge = 20m,
            ClosingNbv = 80m
        });
        db.OpeningBalances.AddRange(
            new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = fixedAssetCategory.Id,
                Debit = 100m,
                EnteredBy = "Reviewer",
                Reviewed = true
            },
            new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = retainedCategory.Id,
                Credit = 200m,
                EnteredBy = "Reviewer",
                Reviewed = true
            });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "Posted depreciation charge",
            DebitCategoryId = depreciationCategory.Id,
            CreditCategoryId = fixedAssetCategory.Id,
            Amount = 20m,
            ImpactOnProfit = -20m,
            ImpactOnAssets = -20m,
            Source = AdjustmentSource.Manual
        });
        await db.SaveChangesAsync();

        _ = bankCategory;

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);

        Assert.Equal(80m, balanceSheet.FixedAssets.Total);
        Assert.Equal(100m, balanceSheet.CurrentAssets.Cash);
    }

    [Fact]
    public async Task BalanceSheet_IgnoresFutureLoansAndShareIssues()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var currentLoanCategory = AddCategory(db, period.CompanyId, "2600", "Bank Loan (< 1 year)", AccountCategoryType.Liability);
        var longLoanCategory = AddCategory(db, period.CompanyId, "2700", "Bank Loan (> 1 year)", AccountCategoryType.Liability);
        var shareCategory = AddCategory(db, period.CompanyId, "3000", "Share Capital", AccountCategoryType.Equity);
        var currentLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Current Bank",
            OriginalAmount = 50m,
            Balance = 50m,
            DueWithinYear = 10m,
            DueAfterYear = 40m
        };
        var futureLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Future Bank",
            OriginalAmount = 900m,
            Balance = 900m,
            DueWithinYear = 90m,
            DueAfterYear = 810m
        };
        SetRequiredDate(currentLoan, "DrawdownDate", period.PeriodStart);
        SetRequiredDate(currentLoan, "BalanceAsOfDate", period.PeriodEnd);
        SetRequiredDate(futureLoan, "DrawdownDate", period.PeriodEnd.AddDays(1));
        SetRequiredDate(futureLoan, "BalanceAsOfDate", period.PeriodEnd.AddDays(1));
        db.Loans.AddRange(currentLoan, futureLoan);

        var issuedShare = new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m
        };
        var futureShare = new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Future Ordinary",
            NumberIssued = 900,
            NominalValue = 1m,
            TotalValue = 900m
        };
        SetRequiredDate(issuedShare, "IssueDate", period.PeriodStart);
        SetRequiredDate(futureShare, "IssueDate", period.PeriodEnd.AddDays(1));
        db.ShareCapitals.AddRange(issuedShare, futureShare);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 1m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCategory.Id,
            Credit = 1m,
            EnteredBy = "Reviewer",
            Reviewed = true
        });
        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart,
                Description = "Current loan tranche",
                Amount = 10m,
                CategoryId = currentLoanCategory.Id
            },
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart,
                Description = "Long-term loan tranche",
                Amount = 40m,
                CategoryId = longLoanCategory.Id
            });
        await db.SaveChangesAsync();

        _ = bankCategory;

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await service.GetCashFlowStatementAsync(period.CompanyId, period.Id);
        var equity = await service.GetEquityChangesAsync(period.CompanyId, period.Id);

        Assert.Equal(10m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(40m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(1m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(50m, cashFlow.LoanDrawdowns);
        Assert.Equal(1m, equity.ClosingShareCapital);
    }

    [Fact]
    public async Task CapitalAllowances_ClaimPersistedWhenAdjustmentsGenerated()
    {
        // BL-06: generating a period's adjustments records the actual wear-and-tear claim per asset,
        // so later periods can read the real cumulative claim instead of re-estimating it.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
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

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var claim = await db.CapitalAllowanceClaims.SingleAsync(c => c.PeriodId == period.Id);
        Assert.Equal(1_000m, claim.Claim);   // 8000 * 12.5%, full year
        Assert.Equal(8_000m, claim.Cost);
    }

    [Fact]
    public async Task EquityChanges_FirstYearShowsIncorporationCapitalAsOpeningNotIssuedInYear()
    {
        // BL-22: capital subscribed at incorporation (on the period start date) is the opening balance
        // of the statement of changes in equity, not mis-stated as issued during the first year.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var shareCategory = AddCategory(db, period.CompanyId, "3000", "Share Capital", AccountCategoryType.Equity);
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCategory.Id,
            Credit = 100m,
            EnteredBy = "Reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        _ = bankCategory;

        var equity = await new FinancialStatementsService(db).GetEquityChangesAsync(period.CompanyId, period.Id);

        Assert.Equal(100m, equity.OpeningShareCapital);
        Assert.Equal(0m, equity.SharesIssued);
        Assert.Equal(100m, equity.ClosingShareCapital);
    }

    [Fact]
    public async Task Adjustments_PrepaymentIncreasesProfitAndShowsAsCurrentAsset()
    {
        // BL-32: figure-level proof that a prepayment increases profit (defers an expense) and is
        // carried as a current asset.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.Debtors.Add(new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 600m, Type = DebtorType.Prepayment });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var prepaymentAdj = await db.Adjustments.SingleAsync(a => a.PeriodId == period.Id && a.Description.Contains("Prepayment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(600m, prepaymentAdj.ImpactOnProfit);
        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(600m, bs.CurrentAssets.Prepayments);
    }

    [Fact]
    public async Task OpeningRetainedEarnings_UsesPostedOpeningEquityInsteadOfHistoricalRegisterSnapshot()
    {
        // The posted ledger is the statement source of truth. Historical period metadata is supporting
        // evidence only; the reviewed opening-equity posting controls the current-period statement.
        await using var db = CreateDbContext();
        var period2025 = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        var companyId = period2025.CompanyId;
        var period2024 = new AccountingPeriod { CompanyId = companyId, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true, ClosingRetainedEarnings = 4_242m };
        db.AccountingPeriods.Add(period2024);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        // 2024 transactions would recompute to a profit of 1,000 — but the snapshot (4,242) must win.
        period2024.ClosingRetainedEarnings = 9_999m;
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period2024.Id, Date = new DateOnly(2024, 6, 1), Description = "2024 sales", Amount = 1_000m, CategoryId = Cat("4000") });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period2025.Id,
            AccountCategoryId = Cat("3100"),
            Credit = 4_242m,
            SourceNote = "Reviewed retained earnings take-on",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bs2025 = await statements.GetBalanceSheetAsync(companyId, period2025.Id);
        Assert.Equal(4_242m, bs2025.CapitalAndReserves.OpeningRetainedEarnings);
    }

    [Fact]
    public async Task Dividends_ProposedDoesNotReduceReserves_PaidDoes()
    {
        // accounting-share-capital-and-dividends-reserves: a proposed (DatePaid == null) dividend must
        // NOT reduce reserves; once paid, it reduces reserves consistently with the financing cash-flow.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 100m, OpeningBalanceDate = period.PeriodStart };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = period.Id, AccountCategoryId = Cat("3000"), Credit = 100m, EnteredBy = "r", Reviewed = true });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 6, 1),
            Description = "Sales invoice INV001",
            Amount = 1_000m,
            CategoryId = Cat("4000")
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var baseline = await statements.GetBalanceSheetAsync(companyId, period.Id);
        Assert.Equal(1_000m, baseline.CapitalAndReserves.RetainedEarnings);

        // Proposed (unpaid) dividend: reserves and the dividends-paid figure are unchanged.
        var dividend = new Dividend { PeriodId = period.Id, Amount = 400m, DateDeclared = new DateOnly(2025, 12, 1) };
        db.Dividends.Add(dividend);
        await db.SaveChangesAsync();
        var proposed = await statements.GetBalanceSheetAsync(companyId, period.Id);
        Assert.Equal(0m, proposed.CapitalAndReserves.DividendsPaid);
        Assert.Equal(1_000m, proposed.CapitalAndReserves.RetainedEarnings);

        // Once paid, it reduces reserves.
        dividend.DatePaid = new DateOnly(2025, 12, 20);
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = dividend.DatePaid.Value,
            Description = "Dividend paid",
            Amount = -400m,
            CategoryId = Cat("3200")
        });
        await db.SaveChangesAsync();
        Assert.Equal(400m, await db.Dividends
            .Where(d => d.PeriodId == period.Id && d.DatePaid != null)
            .SumAsync(d => d.Amount));
        var paid = await statements.GetBalanceSheetAsync(companyId, period.Id);
        Assert.Equal(400m, paid.CapitalAndReserves.DividendsPaid);
        Assert.Equal(600m, paid.CapitalAndReserves.RetainedEarnings);
    }

    [Fact]
    public async Task BalanceSheet_RollsPriorPeriodProfitIntoOpeningRetainedEarnings()
    {
        // BL-32: retained earnings brought forward equal the prior period's profit after tax.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Roll Forward Limited",
            CroNumber = "556677",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2024, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var prior = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true };
        var current = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(prior, current);
        AddCategory(db, company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset);
        AddCategory(db, company.Id, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var sales = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = prior.Id, Date = new DateOnly(2024, 6, 1), Description = "Prior sales", Amount = 5_000m, CategoryId = sales.Id });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, current.Id);
        Assert.Equal(5_000m, bs.CapitalAndReserves.OpeningRetainedEarnings);
    }

    [Fact]
    public async Task BalanceSheet_MultiYearRetainedEarningsRollForwardAccumulatesProfitsLessDividends()
    {
        // G2 (money correct over multiple years): reserves brought forward across a three-year chain
        // accumulate prior profits and subtract prior dividends. Proves the roll-forward figure
        // (BL-20/BL-32) is correct year on year, including the dividend reduction.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Roll Forward Chain Limited",
            CroNumber = "778899",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2023, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var y2023 = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2023, 1, 1), PeriodEnd = new DateOnly(2023, 12, 31), IsFirstYear = true };
        var y2024 = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = false };
        var y2025 = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(y2023, y2024, y2025);
        AddCategory(db, company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset);
        AddCategory(db, company.Id, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var sales = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var dividendsCategory = AddCategory(db, company.Id, "3200", "Dividends Paid", AccountCategoryType.Equity);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Profits: 2023 +5,000, 2024 +3,000, 2025 +2,000. A €1,000 dividend is paid in 2024.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2023.Id, Date = new DateOnly(2023, 6, 1), Description = "2023 sales", Amount = 5_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2024.Id, Date = new DateOnly(2024, 6, 1), Description = "2024 sales", Amount = 3_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2024.Id, Date = new DateOnly(2024, 12, 15), Description = "2024 dividend", Amount = -1_000m, CategoryId = dividendsCategory.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2025.Id, Date = new DateOnly(2025, 6, 1), Description = "2025 sales", Amount = 2_000m, CategoryId = sales.Id });
        db.Dividends.Add(new Dividend { PeriodId = y2024.Id, Amount = 1_000m, DateDeclared = new DateOnly(2024, 12, 1), DatePaid = new DateOnly(2024, 12, 15) });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bs2024 = await statements.GetBalanceSheetAsync(company.Id, y2024.Id);
        var bs2025 = await statements.GetBalanceSheetAsync(company.Id, y2025.Id);

        // Opening reserves carried into 2024 = 2023 profit.
        Assert.Equal(5_000m, bs2024.CapitalAndReserves.OpeningRetainedEarnings);
        // Opening reserves carried into 2025 = 2023 profit + 2024 profit - 2024 dividend.
        Assert.Equal(7_000m, bs2025.CapitalAndReserves.OpeningRetainedEarnings);
        // Closing reserves at end of 2025 = brought forward 7,000 + 2025 profit 2,000.
        Assert.Equal(9_000m, bs2025.CapitalAndReserves.RetainedEarnings);
    }

    [Fact]
    public async Task FixedAssetRegister_DoesNotChangeStatementsWithoutPostedJournals()
    {
        // ACC-003: the fixed-asset register is supporting evidence, not an alternative statement
        // source. Acquisition/disposal dates alone must not bypass the posted double-entry ledger.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true); // ends 2025-12-31
        db.FixedAssets.AddRange(
            new FixedAsset { CompanyId = period.CompanyId, Name = "Acquired on year-end", Category = "Equipment", Cost = 1_000m, AcquisitionDate = new DateOnly(2025, 12, 31), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = period.CompanyId, Name = "Acquired after year-end", Category = "Equipment", Cost = 2_000m, AcquisitionDate = new DateOnly(2026, 1, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = period.CompanyId, Name = "Disposed on year-end", Category = "Equipment", Cost = 4_000m, AcquisitionDate = new DateOnly(2025, 1, 1), DisposalDate = new DateOnly(2025, 12, 31), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = period.CompanyId, Name = "Disposed after year-end", Category = "Equipment", Cost = 8_000m, AcquisitionDate = new DateOnly(2025, 1, 1), DisposalDate = new DateOnly(2026, 1, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Empty(bs.FixedAssets.Categories);
        Assert.Equal(0m, bs.FixedAssets.Total);
    }

    [Fact]
    public async Task ShareCapitalRegisterRequiresPostedEquityToAffectStatements()
    {
        // The register records legal membership dates, but cannot bypass the posted double-entry
        // ledger. A reviewed share-capital take-on posting is required for statement recognition.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);
        var shareCapitalCategory = categories.Single(category => category.Code == "3000");
        db.ShareCapitals.AddRange(
            new ShareCapital { CompanyId = period.CompanyId, ShareClass = "On year-end", NumberIssued = 100, NominalValue = 1m, TotalValue = 100m, IssueDate = new DateOnly(2025, 12, 31) },
            new ShareCapital { CompanyId = period.CompanyId, ShareClass = "After year-end", NumberIssued = 200, NominalValue = 1m, TotalValue = 200m, IssueDate = new DateOnly(2026, 1, 1) },
            new ShareCapital { CompanyId = period.CompanyId, ShareClass = "Cancelled on year-end", NumberIssued = 50, NominalValue = 1m, TotalValue = 50m, IssueDate = new DateOnly(2025, 1, 1), CancelledDate = new DateOnly(2025, 12, 31) });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var registerOnly = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(0m, registerOnly.CapitalAndReserves.ShareCapital);

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCapitalCategory.Id,
            Credit = 100m,
            SourceNote = "Reviewed share-capital take-on",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        var posted = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(100m, posted.CapitalAndReserves.ShareCapital);
    }

    [Fact]
    public async Task Adjustments_AccrueLoanInterestAndKeepBalanceSheetBalanced()
    {
        // BL-07: the engine accrues interest on outstanding loans. It posts an interest expense and a
        // matching accrual liability, so the balance sheet stays balanced.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        db.ShareCapitals.Add(new ShareCapital { CompanyId = companyId, ShareClass = "Ordinary", NumberIssued = 100, NominalValue = 1m, TotalValue = 100m, IssueDate = new DateOnly(2025, 1, 1) });
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 100m, OpeningBalanceDate = new DateOnly(2025, 1, 1) };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Reviewer",
            Reviewed = true
        });

        // €10,000 loan drawn (cash in, coded to the loan liability so it is outside the P&L), 5% rate.
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 1, 2), Description = "Loan drawdown", Amount = 10_000m, CategoryId = Cat("2700") });
        db.Loans.Add(new Loan
        {
            CompanyId = companyId,
            Lender = "Bank of Ireland",
            OriginalAmount = 10_000m,
            Balance = 10_000m,
            InterestRate = 5m,
            DueWithinYear = 0m,
            DueAfterYear = 10_000m,
            DrawdownDate = new DateOnly(2025, 1, 2),
            BalanceAsOfDate = period.PeriodEnd
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, period.Id);

        // The interest accrual exists as a creditor and as an interest expense adjustment of €500.
        var interestAccrual = await db.Creditors.SingleAsync(c => c.PeriodId == period.Id && c.Name.Contains("interest", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(500m, interestAccrual.Amount); // 10,000 * 5% * full year
        Assert.Equal(CreditorType.Accrual, interestAccrual.Type);
        Assert.Contains(await db.Adjustments.Where(a => a.PeriodId == period.Id).ToListAsync(),
            a => a.Description.Contains("interest", StringComparison.OrdinalIgnoreCase) && a.ImpactOnProfit == -500m);

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(companyId, period.Id);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.True(balanceSheet.Balances);
        Assert.Equal(500m, balanceSheet.CreditorsWithinYear.Accruals);
    }

    [Fact]
    public async Task Adjustments_ReclassifyOverdrawnDirectorLoanAsReceivable()
    {
        // BL-07: an overdrawn director's loan account (director owes the company) is reclassified to a
        // receivable. The adjustment is P&L-neutral — it only moves a balance between presentation accounts.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var director = await db.CompanyOfficers.FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        db.DirectorLoans.Add(new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = director.Id,
            OpeningBalance = 0m,
            Advances = 3_000m,
            Repayments = 0m,
            ClosingBalance = 3_000m
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var reclass = await db.Adjustments.SingleAsync(a =>
            a.PeriodId == period.Id && a.Description.Contains("Director loan reclassification", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3_000m, reclass.Amount);
        Assert.Equal(0m, reclass.ImpactOnProfit);
        Assert.NotNull(reclass.DebitCategoryId);
        Assert.NotNull(reclass.CreditCategoryId);
        Assert.NotEqual(reclass.DebitCategoryId, reclass.CreditCategoryId);
    }

    [Fact]
    public async Task LoanSnapshots_ForFutureEffectiveLoansDoNotLeakIntoCurrentPeriodReporting()
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
        db.Loans.Add(futureLoan);
        await db.SaveChangesAsync();
        db.LoanBalanceSnapshots.Add(new LoanBalanceSnapshot
        {
            LoanId = futureLoan.Id,
            PeriodId = period.Id,
            OpeningBalance = 0m,
            Drawdowns = 20_000m,
            Repayments = 0m,
            ClosingBalance = 20_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 18_000m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);

        var balanceSheet = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await statements.GetCashFlowStatementAsync(period.CompanyId, period.Id);
        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(0m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(0m, cashFlow.LoanDrawdowns);
        var longTermCreditorNote = Assert.Single(notes, n => n.Code == StatutoryNoteCodes.LongTermCreditors);
        Assert.Equal(NoteChecklistState.NotApplicable, longTermCreditorNote.ChecklistState);
        Assert.False(longTermCreditorNote.IsIncluded);
        Assert.DoesNotContain("Future Bank", string.Join("\n", notes.Select(n => n.Content)));
    }

    [Fact]
    public async Task CashFlow_DoesNotInferRepaymentsForPriorLoansWithoutSnapshot()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        db.Loans.Add(new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Current Bank",
            OriginalAmount = 100m,
            Balance = 60m,
            DueWithinYear = 15m,
            DueAfterYear = 45m,
            DrawdownDate = period.PeriodStart.AddYears(-1),
            BalanceAsOfDate = period.PeriodEnd
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var cashFlow = await service.GetCashFlowStatementAsync(period.CompanyId, period.Id);

        Assert.Equal(0m, cashFlow.LoanRepayments);
    }

    [Fact]
    public async Task TrialBalance_PostsImportedTransactionsWithImplicitBankSide()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddMonths(2),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var trialBalance = await service.GetTrialBalanceAsync(period.CompanyId, period.Id);

        Assert.Equal(100m, trialBalance.Single(l => l.Code == bankCategory.Code).Debit);
        Assert.Equal(100m, trialBalance.Single(l => l.Code == salesCategory.Code).Credit);
        Assert.Equal(trialBalance.Sum(l => l.Debit), trialBalance.Sum(l => l.Credit));
    }

    [Fact]
    public async Task TrialBalance_IncludesReviewedOpeningBalancesAndBankOpeningSide()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 500m,
            OpeningBalanceDate = period.PeriodStart
        });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 500m,
            SourceNote = "Prior-year signed accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var trialBalance = await service.GetTrialBalanceAsync(period.CompanyId, period.Id);

        Assert.Equal(500m, trialBalance.Single(l => l.Code == bankCategory.Code).Debit);
        Assert.Equal(500m, trialBalance.Single(l => l.Code == retainedCategory.Code).Credit);
        Assert.Equal(trialBalance.Sum(l => l.Debit), trialBalance.Sum(l => l.Credit));
    }

    [Fact]
    public async Task FinalOutputs_BlockWhenOpeningTrialBalanceTakeOnDoesNotBalance()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 50m,
            SourceNote = "Unbalanced take-on credit",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.Contains("Opening balances do not agree", error.Message);
    }

    [Fact]
    public async Task StatementSources_ExposeOpeningTransactionsAndAdjustments()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 500m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 500m,
            SourceNote = "Prior accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 4, 1),
            Description = "Customer receipt",
            Amount = 250m,
            CategoryId = salesCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var sources = await service.GetStatementSourcesAsync(period.CompanyId, period.Id);
        var bank = sources.Single(s => s.Code == "1400");
        var sales = sources.Single(s => s.Code == "4000");
        var retained = sources.Single(s => s.Code == "3100");

        Assert.Equal(500m, bank.OpeningDebit);
        Assert.Equal(250m, bank.TransactionDebit);
        Assert.Equal(250m, sales.TransactionCredit);
        Assert.Equal(500m, retained.OpeningCredit);
        Assert.Contains(bank.SourceNotes, n => n.Contains("Bank opening balance"));
    }

    [Fact]
    public async Task AutoAdjustments_PostToDebitAndCreditCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 1_200m,
            AcquisitionDate = new DateOnly(2025, 1, 1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Accountancy fee",
            Amount = 500m,
            Type = CreditorType.Accrual,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();

        var service = new AdjustmentService(db);

        await service.GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var postedAdjustments = await db.Adjustments
            .Where(a => a.PeriodId == period.Id && a.Amount > 0)
            .ToListAsync();
        Assert.NotEmpty(postedAdjustments);
        Assert.All(postedAdjustments, adjustment =>
        {
            Assert.True(adjustment.DebitCategoryId.HasValue, $"{adjustment.Description} missing debit category");
            Assert.True(adjustment.CreditCategoryId.HasValue, $"{adjustment.Description} missing credit category");
        });
    }

    [Fact]
    public async Task AutoAdjustmentService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other company laptop",
            Category = "Computer Equipment",
            Cost = 1_200m,
            AcquisitionDate = otherPeriod.PeriodStart,
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = otherPeriod.Id,
            Name = "Other company accrual",
            Amount = 500m,
            Type = CreditorType.Accrual,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();
        var service = new AdjustmentService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateAutoAdjustmentsAsync(period.CompanyId, otherPeriod.Id));

        Assert.Empty(await db.Adjustments.Where(a => a.PeriodId == otherPeriod.Id).ToListAsync());
        Assert.Empty(await db.DepreciationEntries.Where(d => d.PeriodId == otherPeriod.Id).ToListAsync());
    }

    [Fact]
    public void AdjustmentService_RequiresCompanyIdForAutoGeneration()
    {
        var methods = typeof(AdjustmentService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(AdjustmentService.GenerateAutoAdjustmentsAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length == 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length == 1
            && parameters[0] == typeof(int));
    }

    [Fact]
    public async Task ImportCsv_RejectsBankAccountFromAnotherCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2026,Receipt,100\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var error = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.ImportCsvAsync(period.CompanyId, bankAccount.Id, otherPeriod.Id, csv, "bank.csv"));

        Assert.Contains($"Period {otherPeriod.Id} not found", error.Message);
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Theory]
    [InlineData("Posted Account, Posted Transactions Date, Description, Debit Amount, Credit Amount, Balance", "AIB")]
    [InlineData("Date, Transaction Details, Amount, Balance - Bank of Ireland", "BOI")]
    [InlineData("Type, Started Date, Completed Date, Description, Amount, Balance", "Revolut")]
    [InlineData("id, created, amount, currency, description, balance_transaction", "Stripe")]
    [InlineData("Date, Description, Amount", "Generic")]
    public async Task ImportService_DetectsBankFormatFromHeader(string header, string expected)
    {
        // BL-14: the AIB/BOI/Revolut/Stripe auto-detection was completely untested.
        await using var db = CreateDbContext();
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));
        Assert.Equal(expected, service.DetectFormat(header).Name);
    }

    [Fact]
    public async Task ImportService_AutoDetectsRevolutAndParsesColumnsPerMapping()
    {
        // BL-14: end-to-end proof that auto-detection picks the format and reads each column per the
        // detected mapping (Revolut: date col 0 yyyy-MM-dd, description col 1, amount col 2).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Revolut", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        var csv = "Started Date,Description,Amount,Balance\n2025-03-01,Coffee shop,-4.50,995.50\n2025-03-02,Client payment,1200.00,2195.50\n";
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(
            period.CompanyId, bank.Id, period.Id,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "revolut.csv");

        Assert.Equal(2, result.ImportedRows);
        var txns = await db.ImportedTransactions.Where(t => t.BankAccountId == bank.Id).OrderBy(t => t.Date).ToListAsync();
        Assert.Equal(new DateOnly(2025, 3, 1), txns[0].Date);
        Assert.Equal("Coffee shop", txns[0].Description);
        Assert.Equal(-4.50m, txns[0].Amount);
        Assert.Equal(1_200.00m, txns[1].Amount);

        // Re-importing the identical file retains every row as an explicit review candidate.
        var second = await service.ImportCsvAsync(
            period.CompanyId, bank.Id, period.Id,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "revolut.csv");
        Assert.Equal(2, second.DuplicateCandidates);
        Assert.Equal(2, second.ImportedRows);
    }

    [Theory]
    [InlineData(999, false)]   // strictly below 10% is within section 240
    [InlineData(1000, true)]   // exactly 10% is not "less than" 10%
    [InlineData(1001, true)]   // above 10% requires another substantiated legal basis
    public async Task DirectorLoanCompliance_Section240RelevantAssetsThresholdIsStrict(decimal closingBalance, bool blocked)
    {
        // Section 240 uses relevant assets under section 238(2), not current-period net assets. The
        // exception is strictly below 10%, so the equality boundary fails closed.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var sales = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        var director = await db.CompanyOfficers.FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        director.AppointedDate = period.PeriodStart;
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Sales", Amount = 10_000m, CategoryId = sales.Id });
        db.DirectorLoans.Add(new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = director.Id,
            ArrangementDate = period.PeriodStart,
            OpeningBalance = 0m,
            Advances = closingBalance,
            ClosingBalance = closingBalance,
            MaxBalanceDuringYear = closingBalance,
            TermsStatus = DirectorLoanTermsStatus.WrittenComplete,
            IsDocumented = true,
            LoanTerms = "Written loan terms with repayment date and an explicit interest provision.",
            ComplianceBasis = DirectorLoanComplianceBasis.Section240BelowTenPercent,
            RelevantAssetsBasis = DirectorLoanRelevantAssetsBasis.LastLaidEntityFinancialStatements,
            RelevantAssetsAmount = 10_000m,
            RelevantAssetsAsOfDate = period.PeriodStart.AddDays(-1),
            RelevantAssetsReference = "last-laid-entity-financial-statements.pdf#net-assets",
            RelevantAssetsFallReview = DirectorLoanRelevantAssetsFallReview.NoRelevantFall,
            ReviewDecision = DirectorLoanReviewDecision.Accepted,
            ReviewNote = "Reviewed against the retained section 240 evidence and dated ledger.",
            ReviewedBy = "Qualified reviewer",
            ReviewerRole = "Accountant",
            ReviewedAtUtc = DateTime.UtcNow,
            BalanceMovements =
            [
                new DirectorLoanMovement
                {
                    MovementDate = period.PeriodStart,
                    MovementType = DirectorLoanMovementType.Advance,
                    Amount = closingBalance,
                    EvidenceReference = "bank-ledger#advance-1"
                }
            ]
        });
        await db.SaveChangesAsync();

        var result = await new DirectorLoanComplianceService(db, new FinancialStatementsService(db))
            .GetComplianceStatusAsync(period.CompanyId, period.Id);

        var detail = Assert.Single(result.Loans);
        Assert.Equal(10_000m, detail.RelevantAssets);
        Assert.Equal(1_000m, detail.Section240Threshold);
        Assert.Equal(!blocked, detail.Section240StrictlyBelowThreshold);
        Assert.Equal(blocked, result.HasUnresolvedComplianceBlockers);
        Assert.Equal(blocked, result.RequiresAlternativeLegalBasis);
        if (blocked)
            Assert.Contains(result.BlockingIssues, issue => issue.Contains("not strictly below", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportCsv_RejectsCallerCompanyMismatchBeforeImporting()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherBankAccount = new BankAccount
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(otherBankAccount);
        await db.SaveChangesAsync();
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2025,Receipt,100\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.ImportCsvAsync(period.CompanyId, otherBankAccount.Id, otherPeriod.Id, csv, "bank.csv"));

        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task ImportCsv_EnforcesConfiguredRowLimit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2026,Receipt,100\n02/01/2026,Receipt,200\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig { MaxRows = 1 }));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv"));

        Assert.Contains("too many rows", error.Message);
    }

    [Fact]
    public async Task ImportCsv_SkipsRowsOutsideAccountingPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n31/12/2024,Prior year receipt,100\n01/01/2025,Current receipt,200\n01/01/2026,Future receipt,300\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv");

        Assert.Equal(3, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("outside accounting period")));
        Assert.Equal(3, await db.ImportBatches.Select(b => b.RowCount).SingleAsync());
        var saved = await db.ImportedTransactions.SingleAsync();
        Assert.Equal(new DateOnly(2025, 1, 1), saved.Date);
    }

    [Fact]
    public async Task ImportCsv_ParsesQuotedMultilineBankDescriptionsWithEscapedQuotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        var csvText = "Date,Description,Amount\n01/01/2025,\"Customer said \"\"thanks\"\"\nInvoice 42\",123.45\n";
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv");

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Empty(result.Warnings);
        var saved = await db.ImportedTransactions.SingleAsync();
        Assert.Equal("Customer said \"thanks\"\nInvoice 42", saved.Description);
        Assert.Equal(123.45m, saved.Amount);
    }

    [Fact]
    public async Task ImportCsv_NeutralisesSpreadsheetFormulaInjectionInStoredText()
    {
        // import-csv-formula-injection: a bank memo/reference that begins with = + - @ (or a leading
        // tab/CR/LF) is a CSV-injection vector — it executes as a formula when the imported
        // transactions are later exported to Excel/Sheets. Stored text must be neutralised, while
        // numeric fields (incl. a legitimate negative amount) must still parse.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        // Columns: Date(0), Description(1), Reference(2), Amount(3).
        var csvText =
            "Date,Description,Reference,Amount\n" +
            "01/01/2025,=1+2,@evil,100\n" +
            "02/01/2025,Normal payment,REF123,-12.50\n" +
            "03/01/2025,-Refund issued,+447700,5\n";
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var mapping = new ImportService.ColumnMapping(
            DateColumn: 0, DescriptionColumn: 1, AmountColumn: 3, BalanceColumn: null, ReferenceColumn: 2);
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv", mapping);

        Assert.Equal(3, result.ImportedRows);
        var txns = await db.ImportedTransactions.OrderBy(t => t.Date).ToListAsync();
        Assert.Equal(3, txns.Count);

        // Formula triggers in stored text are neutralised with a leading apostrophe.
        Assert.Equal("'=1+2", txns[0].Description);
        Assert.Equal("'@evil", txns[0].Reference);
        Assert.Equal(100m, txns[0].Amount);

        // Ordinary text is stored unchanged; the negative AMOUNT is parsed, not neutralised.
        Assert.Equal("Normal payment", txns[1].Description);
        Assert.Equal("REF123", txns[1].Reference);
        Assert.Equal(-12.50m, txns[1].Amount);

        // Leading '-' and '+' in stored text are neutralised even though they are valid number signs.
        Assert.Equal("'-Refund issued", txns[2].Description);
        Assert.Equal("'+447700", txns[2].Reference);
        Assert.Equal(5m, txns[2].Amount);
    }

    [Fact]
    public async Task ImportCsv_WarningsDoNotEchoRawCsvFieldValues()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        var csvText = "Date,Description,Amount\nnot-a-real-date-SECRET,Private card payment,100\n01/01/2025,Private card payment,amount-SECRET\n";
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv");
        var warningText = string.Join("\n", result.Warnings);

        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("Row 1", warningText);
        Assert.Contains("Row 2", warningText);
        Assert.Contains("could not parse date", warningText);
        Assert.Contains("could not parse amount", warningText);
        Assert.DoesNotContain("not-a-real-date-SECRET", warningText);
        Assert.DoesNotContain("amount-SECRET", warningText);
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task ImportCsv_ReadFailuresReturnClientSafeBusinessRule()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ImportCsvAsync(
                period.CompanyId,
                bankAccount.Id,
                period.Id,
                new ThrowingReadStream("raw-upload-SECRET"),
                "bank.csv"));

        Assert.Equal("CSV file could not be read. Upload a valid CSV bank statement.", error.Message);
        Assert.DoesNotContain("raw-upload-SECRET", error.Message);
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public void ImportService_RequiresCompanyIdForCsvImport()
    {
        var methods = typeof(ImportService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(ImportService.ImportCsvAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length >= 4
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int)
            && parameters[2] == typeof(int)
            && parameters[3] == typeof(Stream));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length >= 3
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int)
            && parameters[2] == typeof(Stream));
    }

    [Fact]
    public async Task BankingImportEndpoint_ReturnsBadRequestForMalformedMultipartUploads()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/bank-accounts/{bank.Id}/import");
        context.Request.ContentType = "multipart/form-data";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("raw-multipart-SECRET"));
        var importService = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            period.Id,
            context.Request,
            importService,
            new AuditService(db),
            Options.Create(new ImportLimitConfig()),
            db,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var message = valueResult.Value?.GetType().GetProperty("error")?.GetValue(valueResult.Value)?.ToString();
        Assert.Equal("Upload a valid multipart CSV bank statement.", message);
        Assert.DoesNotContain("raw-multipart-SECRET", message);
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "BankCsvImported" || a.EntityType == "ImportBatch").ToListAsync());
    }

    [Fact]
    public async Task BankingImportEndpoint_ReturnsBadRequestForImportLimitFailures()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/bank-accounts/{bank.Id}/import");
        var csvText = "Date,Description,Amount\n01/01/2025,Receipt,100\n02/01/2025,Receipt,200\n";
        var formFile = new FormFile(
            new MemoryStream(Encoding.UTF8.GetBytes(csvText)),
            0,
            csvText.Length,
            "file",
            "bank.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = new FormCollection([], new FormFileCollection { formFile });
        var limits = Options.Create(new ImportLimitConfig { MaxRows = 1 });
        var importService = new ImportService(db, limits);

        var result = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            period.Id,
            context.Request,
            importService,
            new AuditService(db),
            limits,
            db,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var message = valueResult.Value?.GetType().GetProperty("error")?.GetValue(valueResult.Value)?.ToString();
        Assert.Contains("too many rows", message);
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "BankCsvImported" || a.EntityType == "ImportBatch").ToListAsync());
    }

    [Theory]
    [InlineData("route-company-bank-wrong-period")]
    [InlineData("route-company-wrong-bank-wrong-period")]
    public async Task BankingImportEndpoint_RejectsMismatchedRouteCompanyBeforeWrites(string scenario)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = scenario == "route-company-bank-wrong-period" ? period.CompanyId : otherPeriod.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Reviewer");
        var formFile = new FormFile(
            new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2025,Receipt,100\n")),
            0,
            "Date,Description,Amount\n01/01/2025,Receipt,100\n".Length,
            "file",
            "bank.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = new FormCollection([], new FormFileCollection { formFile });
        var importService = new ImportService(db, Options.Create(new ImportLimitConfig()));
        var audit = new AuditService(db);

        var result = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            otherPeriod.Id,
            context.Request,
            importService,
            audit,
            Options.Create(new ImportLimitConfig()),
            db,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "BankCsvImported" || a.EntityType == "ImportBatch").ToListAsync());
    }

    [Fact]
    public async Task ListTransactions_ClampsPageSizeToCapAgainstMemoryDos()
    {
        // data-list-transactions-pagesize-cap: an unbounded pageSize would pull every row into memory.
        // The page size is clamped to a 200 cap.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        for (var i = 0; i < 250; i++)
            db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = period.PeriodStart.AddDays(i % 300), Description = $"txn {i}", Amount = 1m });
        await db.SaveChangesAsync();

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId, period.Id, db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{period.Id}/transactions"),
            1, 100_000, null, null, null, null);

        var payload = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        var items = Assert.IsAssignableFrom<List<ImportedTransaction>>(payload.GetType().GetProperty("items")!.GetValue(payload));
        Assert.Equal(200, items.Count); // clamped to the 200 cap, not 250
        Assert.Equal(200, (int)payload.GetType().GetProperty("pageSize")!.GetValue(payload)!);
    }

    [Fact]
    public async Task ListTransactions_PaginatesAndSortsAllRowsWithPeriodAggregates()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var category = AddCategory(db, period.CompanyId, "4000", "Sales", AccountCategoryType.Income);

        for (var i = 1; i <= 125; i++)
        {
            db.ImportedTransactions.Add(new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(i % 30),
                Description = $"Transaction {i:D3}",
                Amount = i,
                CategoryId = i <= 60 ? category.Id : null
            });
        }
        await db.SaveChangesAsync();

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{period.Id}/transactions"),
            3,
            50,
            null,
            null,
            null,
            null,
            "amount",
            "asc");

        var payload = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        var payloadType = payload.GetType();
        var items = Assert.IsAssignableFrom<List<ImportedTransaction>>(payloadType.GetProperty("items")!.GetValue(payload));
        var aggregates = payloadType.GetProperty("aggregates")!.GetValue(payload)!;
        var aggregatesType = aggregates.GetType();

        Assert.Equal(125, (int)payloadType.GetProperty("total")!.GetValue(payload)!);
        Assert.Equal(3, (int)payloadType.GetProperty("page")!.GetValue(payload)!);
        Assert.Equal(3, (int)payloadType.GetProperty("totalPages")!.GetValue(payload)!);
        Assert.True((bool)payloadType.GetProperty("hasPreviousPage")!.GetValue(payload)!);
        Assert.False((bool)payloadType.GetProperty("hasNextPage")!.GetValue(payload)!);
        Assert.Equal("amount", (string)payloadType.GetProperty("sortBy")!.GetValue(payload)!);
        Assert.Equal("asc", (string)payloadType.GetProperty("sortDirection")!.GetValue(payload)!);
        Assert.Equal(25, items.Count);
        Assert.Equal(101m, items.First().Amount);
        Assert.Equal(125m, items.Last().Amount);
        Assert.Equal(125, (int)aggregatesType.GetProperty("total")!.GetValue(aggregates)!);
        Assert.Equal(60, (int)aggregatesType.GetProperty("categorised")!.GetValue(aggregates)!);
        Assert.Equal(65, (int)aggregatesType.GetProperty("uncategorised")!.GetValue(aggregates)!);
    }

    [Fact]
    public async Task FixedAssetCreate_IgnoresNestedDepreciationEntries()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/fixed-assets");
        var input = new FixedAsset
        {
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 2_000m,
            AcquisitionDate = period.PeriodStart.AddDays(1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine,
            DepreciationEntries =
            [
                new DepreciationEntry
                {
                    PeriodId = otherPeriod.Id,
                    OpeningNbv = 2_000m,
                    Charge = 1_999m,
                    ClosingNbv = 1m
                }
            ]
        };

        var result = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            input,
            db,
            writeGuard,
            audit,
            context);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(result));
        var asset = await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId);
        Assert.Equal("Laptop", asset.Name);
        Assert.Empty(await db.DepreciationEntries.ToListAsync());
    }

    [Fact]
    public async Task FixedAssetCreateAndUpdate_IgnoreNullNestedDepreciationEntries()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/fixed-assets");

        var createResult = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset
            {
                Name = "Laptop",
                Category = "Computer Equipment",
                Cost = 2_000m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                UsefulLifeYears = 3,
                DepreciationMethod = DepreciationMethod.StraightLine,
                DepreciationEntries = null!
            },
            db,
            writeGuard,
            audit,
            context);
        var asset = await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId);

        var updateResult = await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            period.CompanyId,
            asset.Id,
            new FixedAsset
            {
                Name = "Laptop revised",
                Category = "Computer Equipment",
                Cost = 2_200m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                UsefulLifeYears = 4,
                DepreciationMethod = DepreciationMethod.ReducingBalance,
                DepreciationEntries = null!
            },
            db,
            writeGuard,
            audit,
            context);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(createResult));
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(updateResult));
        Assert.Empty(await db.DepreciationEntries.ToListAsync());
        Assert.Equal("Laptop revised", (await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId)).Name);
    }

    [Fact]
    public async Task UpsertOpeningBalance_RejectsIncomeAndExpenseAccountsButAllowsBalanceSheetAccounts()
    {
        // accounting-opening-balance-pl-accounts: an opening balance on a 4xxx/5xxx/6xxx income or
        // expense code folds a brought-forward figure into current-year turnover/expenses. The upsert
        // must reject it with a clean 400 and store nothing; a balance-sheet account is still accepted.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var income = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var expense = AddCategory(db, period.CompanyId, "5000", "Cost of sales", AccountCategoryType.Expense);
        var retainedEarnings = AddCategory(db, period.CompanyId, "3100", "Retained earnings", AccountCategoryType.Equity);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);

        IStatusCodeHttpResult StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);

        var incomeResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId, period.Id, income.Id,
            new OpeningBalanceInput(0m, 10_000m, "Opening sales (wrong account)", null, true),
            db, audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put,
                $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{income.Id}"));
        var expenseResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId, period.Id, expense.Id,
            new OpeningBalanceInput(10_000m, 0m, "Opening expense (wrong account)", null, true),
            db, audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put,
                $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{expense.Id}"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(incomeResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(expenseResult).StatusCode);
        // Nothing persisted for the income/expense codes, so turnover/expenses are untouched.
        Assert.Empty(await db.OpeningBalances.ToListAsync());
        Assert.DoesNotContain("OpeningBalance", (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType));

        // A balance-sheet account (retained earnings) is still accepted.
        var equityResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId, period.Id, retainedEarnings.Id,
            new OpeningBalanceInput(0m, 10_000m, "Opening retained earnings per prior accounts", null, true),
            db, audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put,
                $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{retainedEarnings.Id}"));
        Assert.Equal(StatusCodes.Status200OK, StatusOf(equityResult).StatusCode);
        var stored = Assert.Single(await db.OpeningBalances.ToListAsync());
        Assert.Equal(retainedEarnings.Id, stored.AccountCategoryId);
        Assert.Equal(10_000m, stored.Credit);
    }

    [Fact]
    public async Task YearEndFigureInputs_RejectBadFiguresWithCleanBadRequestAndNoCorruption()
    {
        // G3 (customer inputs are safe): a fat-fingered negative amount, blank name or zero useful life
        // must fail with a clear 400 — never a 500, never a silently corrupted year-end figure.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var writeGuard = new AccountingWriteGuard(db);
        DefaultHttpContext Ctx() => AuthenticatedRequest("Accountant", HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/year-end");

        var badDebtor = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId, period.Id,
            new Debtor { Name = "Customer", Amount = -100m, Type = DebtorType.Trade },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badDebtor));
        Assert.Empty(await db.Debtors.Where(d => d.PeriodId == period.Id).ToListAsync());

        var badCreditor = await YearEndEndpoints.CreateCreditorEndpointAsync(
            period.CompanyId, period.Id,
            new Creditor { Name = "   ", Amount = 50m, Type = CreditorType.Trade },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badCreditor));
        Assert.Empty(await db.Creditors.Where(c => c.PeriodId == period.Id).ToListAsync());

        var badInventory = await YearEndEndpoints.CreateInventoryEndpointAsync(
            period.CompanyId, period.Id,
            new Inventory { Description = "Stock", Value = -5m, ValuationMethod = ValuationMethod.Cost },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badInventory));
        Assert.Empty(await db.Inventories.Where(i => i.PeriodId == period.Id).ToListAsync());

        var badDividend = await YearEndEndpoints.CreateDividendEndpointAsync(
            period.CompanyId, period.Id,
            new Dividend { Amount = -1m },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badDividend));
        Assert.Empty(await db.Dividends.Where(d => d.PeriodId == period.Id).ToListAsync());

        // Zero useful life would otherwise be silently skipped by the depreciation engine.
        var badLifeAsset = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset { Name = "Van", Category = "Motor Vehicles", Cost = 10_000m, AcquisitionDate = period.PeriodStart, UsefulLifeYears = 0 },
            db, writeGuard, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badLifeAsset));

        var badCostAsset = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset { Name = "Van", Category = "Motor Vehicles", Cost = -10_000m, AcquisitionDate = period.PeriodStart, UsefulLifeYears = 4 },
            db, writeGuard, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badCostAsset));

        var badDisposalAsset = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset { Name = "Van", Category = "Motor Vehicles", Cost = 10_000m, AcquisitionDate = period.PeriodEnd, DisposalDate = period.PeriodStart, UsefulLifeYears = 4 },
            db, writeGuard, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badDisposalAsset));

        Assert.Empty(await db.FixedAssets.Where(a => a.CompanyId == period.CompanyId).ToListAsync());

        // Nothing was persisted, so no audit rows were written for any rejected input.
        Assert.Empty(await db.AuditLogs.Where(a =>
            a.EntityType == "Debtor" || a.EntityType == "Creditor" || a.EntityType == "Inventory"
            || a.EntityType == "Dividend" || a.EntityType == "FixedAsset").ToListAsync());
    }

    [Fact]
    public void YearEndPeriodReadEndpoints_UseExplicitPeriodOwnershipGuards()
    {
        var source = YearEndEndpointsSource().Replace("\r\n", "\n");

        var guardedListReads = new (string Marker, string[] Fragments)[]
        {
            ("debtors.MapGet", ["db.Debtors.Where(d => d.PeriodId == periodId)"]),
            ("creditors.MapGet", ["db.Creditors.Where(c => c.PeriodId == periodId)"]),
            ("inventory.MapGet", ["db.Inventories.Where(i => i.PeriodId == periodId)"]),
            ("loanSnapshots.MapGet", ["db.LoanBalanceSnapshots", ".Where(s => s.PeriodId == periodId && s.Loan.CompanyId == companyId)"]),
            ("dirLoans.MapGet", ["db.DirectorLoans", ".Include(d => d.Director)", ".Include(d => d.BalanceMovements", ".Where(d => d.PeriodId == periodId", "d.Period.CompanyId == companyId"]),
            ("taxes.MapGet", ["db.TaxBalances.Where(t => t.PeriodId == periodId)"]),
            ("dividends.MapGet", ["db.Dividends.Where(d => d.PeriodId == periodId)"]),
            ("reviews.MapGet", ["db.YearEndReviewConfirmations", ".Where(r => r.PeriodId == periodId)"]),
            ("openingBalances.MapGet", ["db.OpeningBalances", ".Where(o => o.PeriodId == periodId)"]),
            ("pbse.MapGet", ["db.PostBalanceSheetEvents.Where(x => x.PeriodId == periodId)"]),
            ("rpt.MapGet", ["db.RelatedPartyTransactions.Where(x => x.PeriodId == periodId)"]),
            ("cl.MapGet", ["db.ContingentLiabilities.Where(x => x.PeriodId == periodId)"])
        };

        foreach (var (marker, fragments) in guardedListReads)
        {
            var snippet = EndpointSnippet(source, marker);
            Assert.Contains("ListPeriodOwnedRowsAsync", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("context", snippet);
            Assert.DoesNotContain(".ToListAsync()", snippet);
            foreach (var fragment in fragments)
                Assert.Contains(fragment, snippet);
        }

        var payrollSnippet = EndpointSnippet(source, "payroll.MapGet");
        Assert.Contains("GetPeriodOwnedValueAsync", payrollSnippet);
        Assert.Contains("HttpContext context", payrollSnippet);
        Assert.Contains("db.PayrollSummaries.Where(p => p.PeriodId == periodId)", payrollSnippet);
        Assert.DoesNotContain("FirstOrDefaultAsync", payrollSnippet);

        var summarySnippet = BlockSnippet(source, "app.MapGet($\"{basePath}/year-end-summary\"", "}).WithTags(\"Year-End Summary\")");
        Assert.Contains("HttpContext context", summarySnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", summarySnippet);
        Assert.Contains("d.Period.CompanyId == companyId", summarySnippet);
        AssertOccursBefore(summarySnippet, "if (period == null) return Results.NotFound();", "db.Debtors.Where(d => d.PeriodId == periodId)");
        AssertOccursBefore(summarySnippet, "if (period == null) return Results.NotFound();", "db.Creditors.Where(c => c.PeriodId == periodId)");

        var listHelper = MethodSnippet(source, "private static async Task<IResult> ListPeriodOwnedRowsAsync");
        AssertHelperChecksDirectPeriodAccessBeforeMaterializing(listHelper, "query.ToListAsync()");

        var valueHelper = MethodSnippet(source, "private static async Task<IResult> GetPeriodOwnedValueAsync");
        AssertHelperChecksDirectPeriodAccessBeforeMaterializing(valueHelper, "query.FirstOrDefaultAsync()");

        var periodWriteHelper = MethodSnippet(source, "private static async Task<IResult?> RequirePeriodWriteAccessAsync");
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", periodWriteHelper);
        Assert.Contains("AuthorizeCurrentWriteRequest(context)", periodWriteHelper);
        Assert.Contains("PeriodStatus.Finalised or PeriodStatus.Filed", periodWriteHelper);
        AssertOccursBefore(periodWriteHelper, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "AuthorizeCurrentWriteRequest(context)");
        AssertOccursBefore(periodWriteHelper, "AuthorizeCurrentWriteRequest(context)", "PeriodStatus.Finalised or PeriodStatus.Filed");

        var companyWriteHelper = MethodSnippet(source, "private static async Task<IResult?> RequireCompanyWriteAccessAsync");
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", companyWriteHelper);
        Assert.Contains("AuthorizeCurrentWriteRequest(context)", companyWriteHelper);

        var authorizationHelper = MethodSnippet(source, "private static IResult? AuthorizeCurrentWriteRequest");
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", authorizationHelper);
        Assert.Contains("RoleAuthorizationService.Authorize(user, context.Request.Path, context.Request.Method)", authorizationHelper);

        static string EndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find endpoint marker {marker}.");
            var end = source.IndexOf("\n\n", start, StringComparison.Ordinal);
            return end > start ? source[start..end] : source[start..];
        }

        static string BlockSnippet(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find block start marker {startMarker}.");
            var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.True(end > start, $"Expected to find block end marker {endMarker}.");
            return source[start..end];
        }

        static string MethodSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find method marker {marker}.");
            var end = source.IndexOf("\n    private static ", start + marker.Length, StringComparison.Ordinal);
            return end > start ? source[start..end] : source[start..];
        }

        static void AssertHelperChecksDirectPeriodAccessBeforeMaterializing(string snippet, string materializer)
        {
            var ownershipCheck = snippet.IndexOf("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", StringComparison.Ordinal);
            var materialization = snippet.IndexOf(materializer, StringComparison.Ordinal);
            Assert.True(ownershipCheck >= 0, "Expected helper to check direct period access.");
            Assert.True(materialization >= 0, $"Expected helper to materialize via {materializer}.");
            Assert.True(ownershipCheck < materialization, "Expected direct period access before query materialization.");
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected to find {first}.");
            Assert.True(secondIndex >= 0, $"Expected to find {second}.");
            Assert.True(firstIndex < secondIndex, $"Expected {first} before {second}.");
        }
    }

    [Fact]
    public async Task DirectorLoanInputs_RejectsDirectorFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherDirector = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == otherPeriod.CompanyId && o.Role == OfficerRole.Director);
        var input = new DirectorLoanInput(
            otherDirector.Id,
            OpeningBalance: 0,
            Advances: 1_000m,
            Repayments: 0,
            ClosingBalance: 1_000m,
            InterestRate: 5m,
            InterestCharged: 0,
            IsDocumented: true,
            LoanTerms: "Repayable on demand",
            MaxBalanceDuringYear: 1_000m);

        var validation = await DirectorLoanInputs.ValidateAsync(db, period.CompanyId, period.Id, input);

        Assert.NotNull(validation);
    }

    [Fact]
    public async Task DirectorLoanReporting_IgnoresDirtyRowsForDirectorsFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var otherDirector = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == otherPeriod.CompanyId && o.Role == OfficerRole.Director);
        otherDirector.Name = "Other Company Director";
        var cleanLoan = new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = director.Id,
            OpeningBalance = 100m,
            Advances = 600m,
            Repayments = 100m,
            ClosingBalance = 600m,
            IsDocumented = true,
            LoanTerms = "Documented board-approved advance",
            MaxBalanceDuringYear = 700m
        };
        var dirtyLoan = new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = otherDirector.Id,
            OpeningBalance = 0m,
            Advances = 9_999m,
            Repayments = 0m,
            ClosingBalance = 9_999m,
            IsDocumented = false,
            LoanTerms = "Dirty legacy cross-company row",
            MaxBalanceDuringYear = 9_999m
        };
        db.DirectorLoans.AddRange(cleanLoan, dirtyLoan);
        await db.SaveChangesAsync();
        var compliance = new DirectorLoanComplianceService(db, new FinancialStatementsService(db));

        var status = await compliance.GetComplianceStatusAsync(period.CompanyId, period.Id);
        var note = await compliance.GenerateSection307NoteAsync(period.CompanyId, period.Id);
        var generatedNotes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        var reportedLoan = Assert.Single(status.Loans);
        Assert.Equal(cleanLoan.Id, reportedLoan.Id);
        Assert.Equal(600m, status.TotalDirectorLoans);
        Assert.Contains(director.Name, note);
        Assert.DoesNotContain("Other Company Director", note);
        Assert.Contains(generatedNotes, n => n.Content?.Contains(director.Name, StringComparison.Ordinal) == true);
        Assert.DoesNotContain(generatedNotes, n => n.Content?.Contains("Other Company Director", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task DirectorLoanInputs_AllowsDirectorWhoServedDuringPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        director.AppointedDate = period.PeriodStart.AddDays(10);
        director.ResignedDate = period.PeriodEnd.AddDays(-10);
        await db.SaveChangesAsync();
        var input = new DirectorLoanInput(
            director.Id,
            OpeningBalance: 0,
            Advances: 1_000m,
            Repayments: 0,
            ClosingBalance: 1_000m,
            InterestRate: 5m,
            InterestCharged: 0,
            IsDocumented: true,
            LoanTerms: "Repaid before resignation",
            MaxBalanceDuringYear: 1_000m);

        var validation = await DirectorLoanInputs.ValidateAsync(db, period.CompanyId, period.Id, input);

        Assert.Null(validation);
    }

    [Fact]
    public async Task LoanBalanceSnapshotInputs_RejectsNegativeDueSplits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "AIB",
            OriginalAmount = 10_000m,
            Balance = 0m,
            DrawdownDate = period.PeriodStart,
            BalanceAsOfDate = period.PeriodEnd
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var negativeCurrent = await LoanBalanceSnapshotInputs.ValidateAsync(
            db,
            period.CompanyId,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 0m,
                Drawdowns = 0m,
                Repayments = 0m,
                ClosingBalance = 0m,
                DueWithinYear = -1m,
                DueAfterYear = 1m
            });
        var negativeLongTerm = await LoanBalanceSnapshotInputs.ValidateAsync(
            db,
            period.CompanyId,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 0m,
                Drawdowns = 0m,
                Repayments = 0m,
                ClosingBalance = 0m,
                DueWithinYear = 1m,
                DueAfterYear = -1m
            });

        Assert.NotNull(negativeCurrent);
        Assert.NotNull(negativeLongTerm);
    }

    [Fact]
    public async Task TransactionRuleInputs_RejectsCategoryFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "7001", "Other income", AccountCategoryType.Income);
        var input = new TransactionRuleInput("Stripe", otherCategory.Id, 1);

        var validation = await TransactionRuleInputs.ValidateAsync(db, period.CompanyId, input);

        Assert.NotNull(validation);
    }

    [Fact]
    public async Task AdjustmentInputs_RejectsCategoryFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "7000", "Other expenses", AccountCategoryType.Expense);
        var input = new AdjustmentInput(
            Description: "Cross-company category attempt",
            DebitCategoryId: otherCategory.Id,
            CreditCategoryId: null,
            Amount: 100m,
            Reason: "Testing ownership guard",
            LegalBasis: "Internal control",
            ImpactOnProfit: -100m,
            ImpactOnAssets: 0m);

        var validation = await AdjustmentInputs.ValidateAsync(db, period.CompanyId, input);

        Assert.NotNull(validation);
    }

    [Fact]
    public void EndpointInputs_RequiresReasonWhenReopeningLockedPeriod()
    {
        var period = new AccountingPeriod
        {
            CompanyId = 1,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Finalised,
            LockedAt = DateTime.UtcNow,
            LockedBy = "Reviewer"
        };
        var invalid = new PeriodStatusUpdate(PeriodStatus.Review, null, "too short");
        var valid = new PeriodStatusUpdate(PeriodStatus.Review, null, "Material correction required");
        var owner = AuthenticatedRole("Owner");

        Assert.NotNull(EndpointInputs.ValidatePeriodStatusUpdate(period, invalid, owner));
        Assert.Null(EndpointInputs.ValidatePeriodStatusUpdate(period, valid, owner));
    }

    [Fact]
    public void EndpointInputs_RequiresOwnerWhenReopeningLockedPeriod()
    {
        var period = new AccountingPeriod
        {
            CompanyId = 1,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Finalised,
            LockedAt = DateTime.UtcNow,
            LockedBy = "Reviewer"
        };
        var update = new PeriodStatusUpdate(PeriodStatus.Review, null, "Material correction required");

        Assert.NotNull(EndpointInputs.ValidatePeriodStatusUpdate(period, update, AuthenticatedRole("Reviewer")));
        Assert.Null(EndpointInputs.ValidatePeriodStatusUpdate(period, update, AuthenticatedRole("Owner")));
    }

}
