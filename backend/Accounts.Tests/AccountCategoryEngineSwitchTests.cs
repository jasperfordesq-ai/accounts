using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Data.Migrations;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

// Chart task C2: the statements engine, ledger, automatic adjustments, CT support and working papers
// read each account's explicit statement line / cash-flow class / system role, never its code. Every
// fixture here deliberately uses codes whose generic-chart meaning differs from their attributes
// (1200 = bank, 3000 = members' funds, 3200 = restricted funds, 8000 = depreciation, 8100 =
// interest, 2300 = creditor after one year), so a code-reading engine would misstate every figure.
public sealed class AccountCategoryEngineSwitchTests
{
    private const string PostgresConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private static readonly DateTime ReviewedAt = new(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Statements_PresentEveryAccountByItsAttributes_NotItsCode()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.CharityClg);
        var statements = new FinancialStatementsService(db);

        var trialBalance = await statements.GetTrialBalanceAsync(fixture.Company.Id, fixture.Period.Id);
        var profitAndLoss = await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.Period.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, fixture.Period.Id);
        var cashFlow = await statements.GetCashFlowStatementAsync(fixture.Company.Id, fixture.Period.Id);
        var equity = await statements.GetEquityChangesAsync(fixture.Company.Id, fixture.Period.Id);

        // No 1400 account exists: the 1200 BankControl account is the bank side of every transaction.
        Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "1400"));
        Assert.Equal(6_350m, Assert.Single(trialBalance, line => line.Code == "1200").Debit);

        // P&L: 8000 is depreciation (an overhead, not tax), 8100 is interest payable.
        Assert.Equal(1_000m, profitAndLoss.Turnover);
        Assert.Equal(0m, profitAndLoss.OtherIncome);
        Assert.Contains(profitAndLoss.Overheads, line => line.Code == "8000" && line.Amount == 200m);
        Assert.Contains(profitAndLoss.Overheads, line => line.Code == "6000" && line.Amount == 250m);
        Assert.Equal(450m, profitAndLoss.TotalOverheads);
        Assert.Equal(100m, profitAndLoss.InterestPayable);
        Assert.Equal(0m, profitAndLoss.TaxCharge);
        Assert.Equal(450m, profitAndLoss.ProfitBeforeTax);
        Assert.Equal(450m, profitAndLoss.ProfitAfterTax);

        // Balance sheet: 1200 is cash (not prepayments), 1120 is prepayments, 2300 and 2310 are
        // creditors after one year (not tax creditors), 3000 is reserves (not share capital).
        Assert.Equal(1_800m, balanceSheet.FixedAssets.Total);
        Assert.Equal(6_350m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(50m, balanceSheet.CurrentAssets.Prepayments);
        Assert.Equal(0m, balanceSheet.CurrentAssets.Debtors);
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.TaxCreditors);
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.Total);
        Assert.Equal(2_000m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(500m, balanceSheet.CreditorsAfterYear.Other);
        Assert.Equal(5_700m, balanceSheet.NetAssets);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.DividendsPaid);
        Assert.Equal(5_700m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.True(balanceSheet.Balances);

        // Cash flow by class: 8000 is added back as depreciation (not "tax paid"), 8100 is interest
        // paid, 2300/2310 are loan drawdowns, 3200 is other financing (not a dividend).
        Assert.Equal(200m, Assert.Single(cashFlow.OperatingAdjustments, item => item.Description == "Depreciation").Amount);
        Assert.Equal(-100m, Assert.Single(cashFlow.OperatingAdjustments, item => item.Description == "Interest paid").Amount);
        Assert.Equal(0m, cashFlow.TaxPaid);
        Assert.Equal(2_000m, cashFlow.CapitalExpenditurePurchases);
        Assert.Equal(2_500m, cashFlow.LoanDrawdowns);
        Assert.Equal(0m, cashFlow.DividendsPaid);
        Assert.Equal(0m, cashFlow.ShareIssues);
        Assert.Equal(250m, cashFlow.OtherFinancing);
        Assert.Equal(5_000m, cashFlow.OpeningCash);
        Assert.Equal(6_350m, cashFlow.ClosingCash);
        Assert.Equal(balanceSheet.CurrentAssets.Cash, cashFlow.ClosingCash);

        // SOCIE: 3000 is a reserve, 3200 restricted funds move reserves, not dividends.
        Assert.Equal(0m, equity.OpeningShareCapital);
        Assert.Equal(5_000m, equity.OpeningRetainedEarnings);
        Assert.Equal(0m, equity.DividendsPaid);
        Assert.Equal(250m, equity.OtherReserveMovements);
        Assert.Equal(0m, equity.SharesIssued);
        Assert.Equal(5_700m, equity.ClosingRetainedEarnings);
    }

    [Fact]
    public async Task Ledger_ClosesThePriorYearIntoTheRetainedEarningsRole_AndCarriesBalancesByType()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.CharityClg);
        var next = new AccountingPeriod
        {
            CompanyId = fixture.Company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(next);
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);

        var sources = await statements.GetStatementSourcesAsync(fixture.Company.Id, next.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, next.Id);
        var equity = await statements.GetEquityChangesAsync(fixture.Company.Id, next.Id);

        // No 3100 exists: the prior year's result closes into 3000, the RetainedEarnings role, which
        // (being a reserve, not share capital) is itself closed and reopened. Restricted funds (3200)
        // keep their own balance across the year end (C5 ruling).
        Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "3100"));
        var members = Assert.Single(sources, source => source.Code == "3000");
        Assert.Equal(5_450m, members.OpeningCredit - members.OpeningDebit);
        Assert.Contains(members.SourceNotes, note => note.Contains("closed to retained earnings", StringComparison.Ordinal));
        var restricted = Assert.Single(sources, source => source.Code == "3200");
        Assert.Equal(250m, restricted.OpeningCredit - restricted.OpeningDebit);
        Assert.Equal(6_350m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(5_700m, balanceSheet.NetAssets);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(5_700m, equity.OpeningRetainedEarnings);
        Assert.True(balanceSheet.Balances);
    }

    [Fact]
    public async Task Readiness_FindsShareCapitalAndVatControlsByLineAndRole_NotByCode()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        // 3000 here is a reserve: an opening balance on it is not share capital.
        var readiness = await new FinancialStatementsService(db).GetReadinessScoreAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Contains("Share capital not recorded", readiness.MissingItems);

        // The VAT controls are the role holders (2260 / 1310), not codes 2200 / 1300.
        var vatPayable = Explicit(fixture.Company.Id, "2260", "VAT", AccountCategoryType.Liability, AccountStatementLine.TaxCreditors, AccountCashFlowClass.Operating, AccountSystemRole.VatPayable);
        var notVat = Explicit(fixture.Company.Id, "2200", "Bank overdraft", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Cash);
        db.AccountCategories.AddRange(vatPayable, notVat);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            Transaction(fixture.Bank, fixture.Period, new DateOnly(2025, 5, 1), "VAT collected", 230m, vatPayable.Id),
            Transaction(fixture.Bank, fixture.Period, new DateOnly(2025, 5, 2), "Overdraft drawn", 999m, notVat.Id));
        db.TaxBalances.Add(new TaxBalance { PeriodId = fixture.Period.Id, TaxType = TaxType.Vat, Liability = 230m, Balance = 230m });
        await db.SaveChangesAsync();

        readiness = await new FinancialStatementsService(db).GetReadinessScoreAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.DoesNotContain(readiness.Warnings, warning => warning.Contains("does not reconcile to the VAT control accounts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ledger_RefusesABankControlRoleHeldByANonCashAccount_InsteadOfMisPosting()
    {
        await using var db = CreateDb();
        var (company, period, bank) = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading);
        var wrongBank = Explicit(company.Id, "2250", "Wrongly flagged bank", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Operating, AccountSystemRole.BankControl);
        var sales = Explicit(company.Id, "4000", "Sales", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating);
        db.AccountCategories.AddRange(wrongBank, sales);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 3, 1), "Receipt", 100m, sales.Id));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, period.Id));
        Assert.Contains("BankControl", error.Message, StringComparison.Ordinal);
        Assert.Contains("2250", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adjustments_FindAccountsByRole_AndCreateOnlyWhatAPostingNeeds_ForANonGenericChart()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.CharityClg);
        var before = await db.AccountCategories.Select(category => category.Id).ToListAsync();
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = fixture.Company.Id,
            Name = "Laptop",
            Category = "Plant & Machinery",
            Cost = 1_000m,
            AcquisitionDate = fixture.Period.PeriodStart,
            UsefulLifeYears = 5,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Debtors.Add(new Debtor { PeriodId = fixture.Period.Id, Name = "Sundry prepaid", Amount = 120m, Type = DebtorType.Prepayment });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);

        var auto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == fixture.Period.Id && adjustment.IsAuto).ToListAsync();
        var depreciation = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Depreciation —", StringComparison.Ordinal));
        Assert.Equal(fixture.Ids["8000"], depreciation.DebitCategoryId);
        Assert.Equal(fixture.Ids["0020"], depreciation.CreditCategoryId);
        var prepayment = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Prepayment —", StringComparison.Ordinal));
        Assert.Equal(fixture.Ids["1120"], prepayment.DebitCategoryId);

        // Only the expense head the prepayment needed was created (the charity chart's 7700 sundry
        // expenses); no generic role account (1400, 7000, 3200, 2400, 8000 CT ...) was added to the chart.
        var created = await db.AccountCategories.Where(category => !before.Contains(category.Id)).ToListAsync();
        var added = Assert.Single(created);
        Assert.Equal("7700", added.Code);
        Assert.Equal(prepayment.CreditCategoryId, added.Id);
    }

    [Fact]
    public async Task Adjustments_PostCorporationTaxToTheCharityChartsOwn8900_NeverToThe8000DepreciationAccount()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.CharityClg);
        // A CT provision needs the CorporationTaxCharge role; the generic chart's code for it, 8000, is
        // this chart's depreciation account, which must not be adopted: the charity chart's own 8900
        // is created instead.
        db.TaxBalances.Add(new TaxBalance { PeriodId = fixture.Period.Id, TaxType = TaxType.CorporationTax, Liability = 100m, Balance = 100m });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);

        var provision = await db.Adjustments.SingleAsync(adjustment => adjustment.Description == "Corporation tax provision");
        Assert.NotEqual(fixture.Ids["8000"], provision.DebitCategoryId);
        var charge = await db.AccountCategories.SingleAsync(category => category.Id == provision.DebitCategoryId);
        Assert.Equal(("8900", AccountSystemRole.CorporationTaxCharge), (charge.Code, charge.SystemRole));
        Assert.Equal(1, await db.AccountCategories.CountAsync(category => category.Code == "8000"));
        Assert.Equal(AccountSystemRole.Depreciation, (await db.AccountCategories.SingleAsync(category => category.Code == "8000")).SystemRole);
    }

    [Fact]
    public async Task Adjustments_NeverAdoptAnAccountThatOnlySharesTheDefaultCode()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.CharityClg);
        // 8900 is the charity chart's default code for the CorporationTaxCharge role, but here it is a
        // user's own expense account without that role: it must not be adopted, and nothing is posted.
        db.AccountCategories.Add(Explicit(fixture.Company.Id, "8900", "Other finance charges", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating));
        db.TaxBalances.Add(new TaxBalance { PeriodId = fixture.Period.Id, TaxType = TaxType.CorporationTax, Liability = 100m, Balance = 100m });
        await db.SaveChangesAsync();
        var adjustmentsBefore = await db.Adjustments.CountAsync();
        var categoriesBefore = await db.AccountCategories.CountAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id));
        Assert.Contains("CorporationTaxCharge", error.Message, StringComparison.Ordinal);
        Assert.Contains("8900", error.Message, StringComparison.Ordinal);
        Assert.Equal(adjustmentsBefore, await db.Adjustments.CountAsync());
        Assert.Equal(categoriesBefore, await db.AccountCategories.CountAsync());
        Assert.Null((await db.AccountCategories.SingleAsync(category => category.Code == "8900")).SystemRole);
    }

    [Fact]
    public async Task Adjustments_RefuseARoleHolderOfTheWrongType_OnlyWhenAPostingUsesIt()
    {
        await using var db = CreateDb();
        var (company, period, _) = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading);
        // An unclassified 1200 resolves to the Prepayments role by code, but it is a Liability.
        db.AccountCategories.Add(new AccountCategory { CompanyId = company.Id, Code = "1200", Name = "Odd liability", Type = AccountCategoryType.Liability });
        await db.SaveChangesAsync();

        // Resolving the role up front (generic chart) is not a posting: no prepayment fact, no throw.
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        db.Debtors.Add(new Debtor { PeriodId = period.Id, Name = "Sundry prepaid", Amount = 120m, Type = DebtorType.Prepayment });
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id));
        Assert.Contains("Prepayments", error.Message, StringComparison.Ordinal);
        Assert.Contains("Liability", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2800", AccountCategoryType.Equity)]
    [InlineData("7050", AccountCategoryType.Asset)]
    public async Task Adjustments_GenericCompanyWithAWrongTypeDefaultCodedRow_RegeneratesExactlyAsBefore(string code, AccountCategoryType oddType)
    {
        // Two identical generic companies, one with a user-created row of an unexpected Type on a
        // role's default code. With no fact that posts to that role, both regenerate the same journals.
        await using var db = CreateDb();
        var plain = await SeedGenericRegenerationCompanyAsync(db);
        var odd = await SeedGenericRegenerationCompanyAsync(db);
        db.AccountCategories.Add(new AccountCategory { CompanyId = odd.Company.Id, Code = code, Name = "User-created account", Type = oddType });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(plain.Company.Id, plain.Period.Id);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(odd.Company.Id, odd.Period.Id);

        async Task<List<string>> JournalsAsync(int periodId) =>
            (await db.Adjustments.Include(a => a.DebitCategory).Include(a => a.CreditCategory)
                .Where(a => a.PeriodId == periodId).OrderBy(a => a.Id).ToListAsync())
            .Select(a => $"{a.Description}|{a.DebitCategory!.Code}|{a.CreditCategory!.Code}|{a.Amount}|{a.ImpactOnProfit}|{a.ImpactOnAssets}")
            .ToList();
        var plainJournals = await JournalsAsync(plain.Period.Id);
        Assert.NotEmpty(plainJournals);
        Assert.Equal(plainJournals, await JournalsAsync(odd.Period.Id));
        // The odd row was adopted by code (as before), not duplicated.
        Assert.Equal(1, await db.AccountCategories.CountAsync(c => c.CompanyId == odd.Company.Id && c.Code == code));
    }

    [Fact]
    public async Task Adjustments_WrongTypeDividendsPayableHolder_ThrowsWhenADeclaredDividendPostsToIt()
    {
        await using var db = CreateDb();
        var fixture = await SeedGenericRegenerationCompanyAsync(db);
        db.AccountCategories.Add(new AccountCategory { CompanyId = fixture.Company.Id, Code = "2800", Name = "User-created reserve", Type = AccountCategoryType.Equity });
        db.Dividends.Add(new Dividend { PeriodId = fixture.Period.Id, Amount = 300m, DateDeclared = new DateOnly(2025, 12, 1) });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id));
        Assert.Contains("DividendsPayable", error.Message, StringComparison.Ordinal);
        Assert.Contains("Equity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adjustments_WrongTypeDisposalLossHolder_ThrowsWhenADisposalLossPostsToIt()
    {
        await using var db = CreateDb();
        var fixture = await SeedGenericRegenerationCompanyAsync(db);
        db.AccountCategories.Add(new AccountCategory { CompanyId = fixture.Company.Id, Code = "7050", Name = "User-created asset", Type = AccountCategoryType.Asset });
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = fixture.Company.Id,
            Name = "Scrapped van",
            Category = "Motor Vehicles",
            Cost = 1_000m,
            AcquisitionDate = new DateOnly(2024, 1, 1),
            DisposalDate = new DateOnly(2025, 6, 30),
            DisposalProceeds = 0m,
            UsefulLifeYears = 5,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id));
        Assert.Contains("DisposalLoss", error.Message, StringComparison.Ordinal);
        Assert.Contains("Asset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adjustments_PaidDividendOnACharityChart_IsRefused_AndTheUncategorisedBankRowIsUntouched()
    {
        await using var db = CreateDb();
        var (company, period, bank) = await SeedCompanyAsync(db, ChartOfAccountsVariant.CharityClg);
        db.AccountCategories.Add(Explicit(company.Id, "1200", "Bank current account", AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash, AccountSystemRole.BankControl));
        db.Dividends.Add(new Dividend { PeriodId = period.Id, Amount = 300m, DateDeclared = new DateOnly(2025, 9, 1), DatePaid = new DateOnly(2025, 10, 1) });
        // Uncategorised (CategoryId null) bank row with the dividend's amount and date.
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 10, 1),
            Description = "Unexplained payment",
            Amount = -300m
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id));
        Assert.Equal("A company limited by guarantee cannot pay dividends; remove the dividend entry.", error.Message);
        Assert.False(await db.AccountCategories.AnyAsync(c => c.SystemRole == AccountSystemRole.DividendsPaid));
        Assert.Empty(await db.Adjustments.Where(a => a.PeriodId == period.Id).ToListAsync());
        Assert.Null((await db.ImportedTransactions.SingleAsync(t => t.Description == "Unexplained payment")).CategoryId);
    }

    [Fact]
    public async Task DividendBankMatch_WithNoDividendsAccount_NeverMatchesAnUncategorisedBankRow()
    {
        await using var db = CreateDb();
        var (company, period, bank) = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        var dividendsPaid = Explicit(company.Id, "3200", "Dividends paid", AccountCategoryType.Equity, AccountStatementLine.DividendsPaid, AccountCashFlowClass.Dividend, AccountSystemRole.DividendsPaid);
        db.AccountCategories.Add(dividendsPaid);
        await db.SaveChangesAsync();
        var paidDate = new DateOnly(2025, 10, 1);
        // Uncategorised (CategoryId null) bank row with the dividend's amount and date.
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = paidDate, Description = "Unexplained payment", Amount = -300m });
        await db.SaveChangesAsync();

        Assert.False(await AdjustmentService.IsDividendPaymentBankedAsync(db, period.Id, null, paidDate, 300m));
        Assert.False(await AdjustmentService.IsDividendPaymentBankedAsync(db, period.Id, dividendsPaid.Id, paidDate, 300m));

        db.ImportedTransactions.Add(Transaction(bank, period, paidDate, "Dividend", -300m, dividendsPaid.Id));
        await db.SaveChangesAsync();
        Assert.True(await AdjustmentService.IsDividendPaymentBankedAsync(db, period.Id, dividendsPaid.Id, paidDate, 300m));
        Assert.False(await AdjustmentService.IsDividendPaymentBankedAsync(db, period.Id, null, paidDate, 300m));
    }

    [Fact]
    public async Task Ledger_LegacyShaped1400_UnclassifiedOrBackfilled_IsAUsableBankControl()
    {
        foreach (var backfilled in new[] { false, true })
        {
            await using var db = CreateDb();
            var (company, period, bank) = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading);
            var bankControl = backfilled
                ? Explicit(company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash, AccountSystemRole.BankControl)
                : new AccountCategory { CompanyId = company.Id, Code = "1400", Name = "Bank Current Account", Type = AccountCategoryType.Asset };
            var reserves = new AccountCategory { CompanyId = company.Id, Code = "3100", Name = "Retained earnings", Type = AccountCategoryType.Equity };
            var sales = new AccountCategory { CompanyId = company.Id, Code = "4000", Name = "Sales", Type = AccountCategoryType.Income };
            db.AccountCategories.AddRange(bankControl, reserves, sales);
            await db.SaveChangesAsync();
            db.OpeningBalances.Add(new OpeningBalance { PeriodId = period.Id, AccountCategoryId = reserves.Id, Credit = 5_000m, EnteredBy = "Reviewer", Reviewed = true, ReviewedBy = "Reviewer", ReviewedAt = ReviewedAt });
            db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 3, 1), "Receipt", 100m, sales.Id));
            await db.SaveChangesAsync();

            var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, period.Id);
            Assert.Equal(5_100m, balanceSheet.CurrentAssets.Cash);
            Assert.True(balanceSheet.Balances);
        }
    }

    // A generic company with an unclassified legacy chart and facts that post to most roles except
    // dividends and disposals: a prepayment, an accrual, a trade debtor, a trade creditor, closing stock.
    private static async Task<(Company Company, AccountingPeriod Period)> SeedGenericRegenerationCompanyAsync(AccountsDbContext db)
    {
        var (company, period, _) = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        db.AccountCategories.AddRange(
            new AccountCategory { CompanyId = company.Id, Code = "1400", Name = "Bank", Type = AccountCategoryType.Asset },
            new AccountCategory { CompanyId = company.Id, Code = "3100", Name = "Retained earnings", Type = AccountCategoryType.Equity });
        db.Debtors.AddRange(
            new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 120m, Type = DebtorType.Prepayment },
            new Debtor { PeriodId = period.Id, Name = "Customer A", Amount = 400m, Type = DebtorType.Trade });
        db.Creditors.AddRange(
            new Creditor { PeriodId = period.Id, Name = "Accountancy fee", Amount = 90m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = period.Id, Name = "Supplier rent", Amount = 60m, Type = CreditorType.Trade, DueWithinYear = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Closing stock", Value = 75m });
        await db.SaveChangesAsync();
        return (company, period);
    }

    [Fact]
    public async Task CorporationTaxIncomeAmbiguity_FollowsTheIncomeLine_NotTheCode()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        var tradingNine = Explicit(fixture.Company.Id, "9500", "Trading receipts", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating);
        var otherFour = Explicit(fixture.Company.Id, "4500", "Sundry income", AccountCategoryType.Income, AccountStatementLine.OtherOperatingIncome, AccountCashFlowClass.Operating);
        db.AccountCategories.AddRange(tradingNine, otherFour);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            Transaction(fixture.Bank, fixture.Period, new DateOnly(2025, 6, 1), "Trading", 300m, tradingNine.Id),
            Transaction(fixture.Bank, fixture.Period, new DateOnly(2025, 6, 2), "Sundry", 40m, otherFour.Id));
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var profitAndLoss = await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Equal(1_300m, profitAndLoss.Turnover);
        Assert.Equal(40m, profitAndLoss.OtherIncome);

        var computation = await new TaxComputationService(db, statements).ComputeAsync(fixture.Company.Id, fixture.Period.Id);
        var ambiguity = Assert.Single(computation.BlockingReasons, reason => reason.StartsWith("Income categories require explicit", StringComparison.Ordinal));
        Assert.Contains("4500 Sundry income", ambiguity, StringComparison.Ordinal);
        Assert.DoesNotContain("9500", ambiguity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkingPapers_AttributeBankAndFactSourcesToRoleHolders_NotFixedCodes()
    {
        await using var db = CreateDb();
        var fixture = await SeedAttributeChartAsync(db, ChartOfAccountsVariant.CharityClg);
        db.Debtors.Add(new Debtor { PeriodId = fixture.Period.Id, Name = "Insurance paid ahead", Amount = 50m, Type = DebtorType.Prepayment });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var service = new AccountantWorkingPaperService(
            db,
            statements,
            new TaxComputationService(db, statements),
            new FilingReleaseIdentityProvider(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FilingRelease:Candidate"] = "c2-test-candidate" }).Build(),
                new TestEnvironment()),
            new AuditService(db),
            new FixedTimeProvider(new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)));
        var actor = new AuthenticatedUser(
            901,
            fixture.Company.TenantId!.Value,
            "Chart Firm",
            "chart.accountant@example.invalid",
            "Chart Accountant",
            "Accountant",
            new HashSet<int> { fixture.Company.Id });

        var pack = await service.GenerateAndRetainAsync(fixture.Company.Id, fixture.Period.Id, actor, "c2-working-papers");

        Assert.DoesNotContain(pack.LeadSchedules.Rows, row => row.Code == "1400");
        var bank = Assert.Single(pack.LeadSchedules.Rows, row => row.Code == "1200");
        Assert.Contains(bank.Sources, source => source.SourceType == "imported-transaction" && source.EntityId == fixture.DonationTransactionId);
        Assert.Contains(bank.Sources, source => source.SourceType == "bank-opening");
        var prepayments = Assert.Single(pack.LeadSchedules.Rows, row => row.Code == "1120");
        Assert.Contains(prepayments.Sources, source => source.SourceType == "debtor");
        Assert.DoesNotContain(bank.Sources, source => source.SourceType == "debtor");
        var loans = Assert.Single(pack.LeadSchedules.Rows, row => row.Code == "2310");
        Assert.DoesNotContain(loans.Sources, source => source.SourceType == "debtor");
    }

    [Fact]
    public void ClassifyNew_DerivesNoRoleWhenTheGivenLineDiffersFromTheCodesLine()
    {
        var bank = new AccountCategory { CompanyId = 7, Code = "1200", Name = "Bank current account", Type = AccountCategoryType.Asset, StatementLine = AccountStatementLine.CashAtBank };
        var overdraft = new AccountCategory { CompanyId = 7, Code = "2200", Name = "Bank overdraft", Type = AccountCategoryType.Liability, StatementLine = AccountStatementLine.OtherCreditorsWithinYear };
        var prepayments = new AccountCategory { CompanyId = 7, Code = "1200", Name = "Prepayments", Type = AccountCategoryType.Asset, StatementLine = AccountStatementLine.Prepayments };
        var explicitRole = new AccountCategory { CompanyId = 7, Code = "1200", Name = "Bank", Type = AccountCategoryType.Asset, StatementLine = AccountStatementLine.CashAtBank, SystemRole = AccountSystemRole.BankControl };

        CategoryClassification.ClassifyNew(bank, []);
        CategoryClassification.ClassifyNew(overdraft, []);
        CategoryClassification.ClassifyNew(prepayments, []);
        CategoryClassification.ClassifyNew(explicitRole, []);

        Assert.Null(bank.SystemRole);
        Assert.Equal(AccountCashFlowClass.Cash, bank.CashFlowClass);
        Assert.Null(overdraft.SystemRole);
        Assert.Equal(AccountSystemRole.Prepayments, prepayments.SystemRole);
        Assert.Equal(AccountSystemRole.BankControl, explicitRole.SystemRole);
        Assert.True(CategoryClassification.IsCash(explicitRole));
    }

    [Fact]
    public void CashFlowClassCash_IsOnlyForCashAtBankOrABankOverdraft()
    {
        Assert.True(CategoryClassification.IsCashFlowClassAllowed(AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash));
        Assert.True(CategoryClassification.IsCashFlowClassAllowed(AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Cash));
        Assert.False(CategoryClassification.IsCashFlowClassAllowed(AccountCategoryType.Asset, AccountStatementLine.Debtors, AccountCashFlowClass.Cash));
        Assert.False(CategoryClassification.IsCashFlowClassAllowed(AccountCategoryType.Liability, AccountStatementLine.TaxCreditors, AccountCashFlowClass.Cash));
        Assert.False(CategoryClassification.IsCashFlowClassAllowed(AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Operating));
        Assert.True(CategoryClassification.IsCashFlowClassAllowed(AccountCategoryType.Asset, AccountStatementLine.Debtors, AccountCashFlowClass.Operating));
    }

    [Fact]
    public async Task CreateCategoryEndpoint_ValidatesCashFlowClassCash_AndDerivesNoRoleForARedefinedCode()
    {
        await using var host = await CategoryHost.StartAsync();

        var bank = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "1200", name = "Bank current account", type = "Asset", statementLine = "CashAtBank" });
        Assert.Equal(HttpStatusCode.Created, bank.StatusCode);
        var overdraft = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "2200", name = "Bank overdraft", type = "Liability", statementLine = "OtherCreditorsWithinYear", cashFlowClass = "Cash" });
        Assert.Equal(HttpStatusCode.Created, overdraft.StatusCode);

        var cashDebtor = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "1310", name = "Debtor marked cash", type = "Asset", statementLine = "Debtors", cashFlowClass = "Cash" });
        Assert.Equal(HttpStatusCode.BadRequest, cashDebtor.StatusCode);
        Assert.Contains("cashFlowClass", await cashDebtor.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var nonCashBank = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "1410", name = "Bank marked operating", type = "Asset", cashFlowClass = "Operating" });
        Assert.Equal(HttpStatusCode.BadRequest, nonCashBank.StatusCode);
        Assert.Contains("cashFlowClass", await nonCashBank.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await host.InScopeAsync(async db =>
        {
            var storedBank = await db.AccountCategories.SingleAsync(category => category.Code == "1200");
            Assert.Equal(AccountStatementLine.CashAtBank, storedBank.StatementLine);
            Assert.Equal(AccountCashFlowClass.Cash, storedBank.CashFlowClass);
            Assert.Null(storedBank.SystemRole);
            var storedOverdraft = await db.AccountCategories.SingleAsync(category => category.Code == "2200");
            Assert.Null(storedOverdraft.SystemRole);
            Assert.Equal(AccountCashFlowClass.Cash, storedOverdraft.CashFlowClass);
            Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "1310" || category.Code == "1410"));
        });
    }

    [PostgresFact]
    public async Task MigrationGuard_FailsClosedWhenTheBackfillLeavesAnAccountUnclassified()
    {
        var baseConnection = Environment.GetEnvironmentVariable(PostgresConnectionEnvVar)!;
        var schema = $"chart_c2_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(baseConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema }.ConnectionString;
        async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await ExecuteAsync("""
                CREATE TABLE account_categories (
                    "Id" integer PRIMARY KEY,
                    "CompanyId" integer NULL,
                    "Code" character varying(20) NOT NULL,
                    "Type" text NOT NULL,
                    "IsNonTradingIncome" boolean NOT NULL DEFAULT FALSE,
                    "StatementLine" text NULL,
                    "CashFlowClass" text NULL,
                    "SystemRole" text NULL,
                    "IsArchived" boolean NOT NULL DEFAULT FALSE);
                INSERT INTO account_categories ("Id", "CompanyId", "Code", "Type") VALUES
                    (1, 41, '1400', 'Asset'), (2, 41, '4000', 'Income');
                """);
            await ExecuteAsync(AddExplicitAccountCategoryClassification.BackfillSql);
            await ExecuteAsync(AddExplicitAccountCategoryClassification.UnclassifiedGuardSql);

            await ExecuteAsync("""
                INSERT INTO account_categories ("Id", "CompanyId", "Code", "Type") VALUES (3, 41, '5100', 'Mystery');
                """);
            await ExecuteAsync(AddExplicitAccountCategoryClassification.BackfillSql);
            var error = await Assert.ThrowsAsync<PostgresException>(
                () => ExecuteAsync(AddExplicitAccountCategoryClassification.UnclassifiedGuardSql));
            Assert.Contains("missing a statement line or cash-flow class", error.MessageText, StringComparison.Ordinal);
            Assert.Contains("company 41 code 5100", error.MessageText, StringComparison.Ordinal);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(baseConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed record AttributeChartFixture(
        Company Company,
        AccountingPeriod Period,
        BankAccount Bank,
        IReadOnlyDictionary<string, int> Ids,
        int DonationTransactionId);

    // A chart whose codes all mean something other than the generic chart's code for them. Bank
    // opening 5,000 (members' funds 5,000); donations 1,000; activities 300 (50 prepaid); interest
    // 100; loan 2,000 and lease 500 drawn; equipment 2,000 bought and depreciated 200; restricted
    // grant 250.
    private static async Task<AttributeChartFixture> SeedAttributeChartAsync(
        AccountsDbContext db,
        ChartOfAccountsVariant variant,
        CompanyType companyType = CompanyType.CompanyLimitedByGuarantee)
    {
        var (company, period, bank) = await SeedCompanyAsync(db, variant, companyType);
        var categories = new[]
        {
            Explicit(company.Id, "1200", "Bank current account", AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash, AccountSystemRole.BankControl),
            Explicit(company.Id, "1120", "Prepayments", AccountCategoryType.Asset, AccountStatementLine.Prepayments, AccountCashFlowClass.Operating, AccountSystemRole.Prepayments),
            Explicit(company.Id, "0020", "Office and IT equipment", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),
            Explicit(company.Id, "2300", "Finance lease due after one year", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsAfterYear, AccountCashFlowClass.Loan),
            Explicit(company.Id, "2310", "Long-term loans", AccountCategoryType.Liability, AccountStatementLine.LoansAfterYear, AccountCashFlowClass.Loan),
            Explicit(company.Id, "3000", "Members' funds brought forward", AccountCategoryType.Equity, AccountStatementLine.Reserves, AccountCashFlowClass.OtherFinancing, AccountSystemRole.RetainedEarnings),
            Explicit(company.Id, "3200", "Restricted funds", AccountCategoryType.Equity, AccountStatementLine.RestrictedFunds, AccountCashFlowClass.OtherFinancing),
            Explicit(company.Id, "4000", "Donations", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating),
            Explicit(company.Id, "6000", "Charitable activities", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating),
            Explicit(company.Id, "8000", "Depreciation", AccountCategoryType.Expense, AccountStatementLine.Depreciation, AccountCashFlowClass.Operating, AccountSystemRole.Depreciation),
            Explicit(company.Id, "8100", "Overdraft interest", AccountCategoryType.Expense, AccountStatementLine.InterestPayable, AccountCashFlowClass.Operating)
        };
        db.AccountCategories.AddRange(categories);
        await db.SaveChangesAsync();
        var ids = categories.ToDictionary(category => category.Code, category => category.Id);

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = ids["3000"],
            Credit = 5_000m,
            SourceNote = "Members' funds take-on",
            EnteredBy = "Reviewer",
            Reviewed = true,
            ReviewedBy = "Reviewer",
            ReviewedAt = ReviewedAt
        });
        var donation = Transaction(bank, period, new DateOnly(2025, 2, 1), "Donations received", 1_000m, ids["4000"]);
        db.ImportedTransactions.AddRange(
            donation,
            Transaction(bank, period, new DateOnly(2025, 3, 1), "Activity costs", -300m, ids["6000"]),
            Transaction(bank, period, new DateOnly(2025, 4, 1), "Overdraft interest", -100m, ids["8100"]),
            Transaction(bank, period, new DateOnly(2025, 5, 1), "Community loan drawn", 2_000m, ids["2310"]),
            Transaction(bank, period, new DateOnly(2025, 5, 2), "Lease financing", 500m, ids["2300"]),
            Transaction(bank, period, new DateOnly(2025, 5, 3), "Equipment purchase", -2_000m, ids["0020"]),
            Transaction(bank, period, new DateOnly(2025, 6, 1), "Restricted grant", 250m, ids["3200"]));
        db.Adjustments.AddRange(
            Journal(period, "Depreciation of equipment", ids["8000"], ids["0020"], 200m),
            Journal(period, "Activity costs paid in advance", ids["1120"], ids["6000"], 50m));
        await db.SaveChangesAsync();
        return new AttributeChartFixture(company, period, bank, ids, donation.Id);
    }

    private static async Task<(Company Company, AccountingPeriod Period, BankAccount Bank)> SeedCompanyAsync(
        AccountsDbContext db,
        ChartOfAccountsVariant variant,
        CompanyType companyType = CompanyType.CompanyLimitedByGuarantee)
    {
        var tenant = new Tenant { Name = "Chart Firm", Slug = $"chart-c2-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Attribute Chart Example CLG",
            CroNumber = "C20000",
            CompanyType = companyType,
            IncorporationDate = new DateOnly(2020, 1, 1),
            ChartOfAccountsVariant = variant
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        var bank = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current account",
            OpeningBalance = 5_000m,
            OpeningBalanceDate = new DateOnly(2025, 1, 1)
        };
        db.AddRange(period, bank);
        await db.SaveChangesAsync();
        return (company, period, bank);
    }

    private static AccountCategory Explicit(
        int companyId,
        string code,
        string name,
        AccountCategoryType type,
        AccountStatementLine line,
        AccountCashFlowClass cashFlowClass,
        AccountSystemRole? role = null) => new()
    {
        CompanyId = companyId,
        Code = code,
        Name = name,
        Type = type,
        TaxTreatment = type == AccountCategoryType.Expense ? TaxTreatment.Deductible : TaxTreatment.Other,
        IsSystem = true,
        StatementLine = line,
        CashFlowClass = cashFlowClass,
        SystemRole = role
    };

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
        CategoryId = categoryId,
        ManualOverride = true
    };

    private static Adjustment Journal(AccountingPeriod period, string description, int debitId, int creditId, decimal amount) => new()
    {
        PeriodId = period.Id,
        Description = description,
        DebitCategoryId = debitId,
        CreditCategoryId = creditId,
        Amount = amount,
        Source = AdjustmentSource.Manual,
        Reason = "Reviewed year-end journal",
        LegalBasis = "FRS 102",
        CreatedBy = "Reviewer",
        ApprovedBy = "Reviewer",
        ApprovedAt = ReviewedAt,
        CreatedAt = ReviewedAt
    };

    private static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"chart-c2-{Guid.NewGuid():N}").Options);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CategoryHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private CategoryHost(WebApplication app, HttpClient client, int companyId)
        {
            _app = app;
            Client = client;
            CompanyId = companyId;
        }

        public HttpClient Client { get; }
        public int CompanyId { get; }

        public static async Task<CategoryHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            var databaseName = $"chart-c2-host-{Guid.NewGuid():N}";
            builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
            builder.Services.Configure<ImportLimitConfig>(_ => { });
            builder.Services.AddSingleton<ApiAccessService>();
            builder.Services.AddScoped<AccountingWriteGuard>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddScoped<CategoryService>();
            builder.Services.AddScoped<ImportService>();

            var app = builder.Build();
            int tenantId;
            int companyId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var tenant = new Tenant { Name = "Chart Tenant", Slug = $"chart-{Guid.NewGuid():N}" };
                var company = new Company { Tenant = tenant, LegalName = "Chart Route CLG", CompanyType = CompanyType.CompanyLimitedByGuarantee, IncorporationDate = new DateOnly(2025, 1, 1) };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
                tenantId = tenant.Id;
                companyId = company.Id;
            }

            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                    UserId: 1,
                    TenantId: tenantId,
                    TenantName: "Chart Tenant",
                    Email: "chart.owner@example.ie",
                    DisplayName: "Chart Owner",
                    Role: "Owner",
                    AllowedCompanyIds: new HashSet<int> { companyId });
                await next();
            });
            app.MapBankingEndpoints();
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            return new CategoryHost(app, client, companyId);
        }

        public async Task InScopeAsync(Func<AccountsDbContext, Task> action)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionEnvVar)))
                Skip = $"{PostgresConnectionEnvVar} is not set.";
        }
    }
}
