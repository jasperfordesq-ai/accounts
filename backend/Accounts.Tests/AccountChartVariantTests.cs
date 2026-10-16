using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Accounts.Tests;

// Chart task C3: chart variants (generic trading + charity/CLG) and seeding by the company's variant.
public sealed class AccountChartVariantTests
{
    [Fact]
    public async Task GenericSeed_IsByteIdenticalToTheChartBeforeVariantsExisted()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);

        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);

        // Today's 52 rows, in today's insertion order, with the attributes ClassifyNew derives for them.
        Assert.Equal(52, seeded.Count);
        Assert.Equal(GenericSeedSnapshot.Replace("\r\n", "\n"), string.Join("\n", seeded.Select(Canonical)));
        var stored = await db.AccountCategories.Where(c => c.CompanyId == company.Id).OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(seeded.Select(Canonical), stored.Select(Canonical));
    }

    private const string GenericSeedSnapshot = """
        4000|Sales / Revenue|Income|Deductible|True|False|False||Turnover|Operating|TradeDebtorIncome
        4100|Other Income|Income|Deductible|True|False|False||Turnover|Operating|
        4200|Interest Received|Income|Deductible|True|False|False||Turnover|Operating|
        4300|Grants Received|Income|Deductible|True|False|False||Turnover|Operating|
        5000|Cost of Sales|Expense|Deductible|True|False|False||CostOfSales|Operating|CostOfSalesStockMovement
        5100|Direct Materials|Expense|Deductible|True|False|False||CostOfSales|Operating|
        5200|Direct Labour|Expense|Deductible|True|False|False||CostOfSales|Operating|
        6000|Wages & Salaries|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6010|Employer PRSI|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6020|Pension Contributions|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6100|Rent|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6110|Rates|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6200|Insurance|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6300|Light & Heat|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6400|Telephone & Internet|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6500|Office Supplies & Stationery|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6600|Motor Expenses|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6700|Travel & Subsistence|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6800|Professional Fees|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6810|Accountancy Fees|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6820|Legal Fees|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        6900|Bank Charges & Interest|Expense|Deductible|True|False|False||InterestPayable|Operating|
        7000|Depreciation|Expense|NonDeductible|True|False|False||Depreciation|Operating|Depreciation
        7100|Repairs & Maintenance|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        7200|Advertising & Marketing|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        7300|Software & Subscriptions|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        7400|Training & Development|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        7500|Entertainment|Expense|NonDeductible|True|False|False||AdministrativeExpenses|Operating|
        7900|Sundry Expenses|Expense|Deductible|True|False|False||AdministrativeExpenses|Operating|
        8000|Corporation Tax Charge|Expense|NonDeductible|True|False|False||TaxOnProfit|Tax|CorporationTaxCharge
        0010|Land & Buildings|Asset|CapitalAllowance|True|False|False||TangibleFixedAssets|Investing|
        0020|Plant & Machinery|Asset|CapitalAllowance|True|False|False||TangibleFixedAssets|Investing|
        0030|Motor Vehicles|Asset|CapitalAllowance|True|False|False||TangibleFixedAssets|Investing|
        0040|Office Equipment|Asset|CapitalAllowance|True|False|False||TangibleFixedAssets|Investing|
        0050|Computer Equipment|Asset|CapitalAllowance|True|False|False||TangibleFixedAssets|Investing|
        1000|Stock / Inventory|Asset|Other|True|False|False||Stocks|Operating|Stock
        1100|Trade Debtors|Asset|Other|True|False|False||Debtors|Operating|TradeDebtors
        1200|Prepayments|Asset|Other|True|False|False||Prepayments|Operating|Prepayments
        1300|VAT Receivable|Asset|Other|True|False|False||Debtors|Operating|VatReceivable
        1400|Bank Current Account|Asset|Other|True|False|False||CashAtBank|Cash|BankControl
        1410|Petty Cash|Asset|Other|True|False|False||CashAtBank|Cash|
        2000|Trade Creditors|Liability|Other|True|False|False||TradeCreditors|Operating|TradeCreditors
        2100|Accruals|Liability|Other|True|False|False||Accruals|Operating|Accruals
        2200|VAT Payable|Liability|Other|True|False|False||TaxCreditors|Operating|VatPayable
        2300|PAYE / PRSI Payable|Liability|Other|True|False|False||TaxCreditors|Operating|
        2400|Corporation Tax Payable|Liability|Other|True|False|False||TaxCreditors|Tax|CorporationTaxPayable
        2500|Director Loan Account|Liability|Other|True|False|False||OtherCreditorsWithinYear|OtherFinancing|DirectorLoanLiability
        2600|Bank Loan (< 1 year)|Liability|Other|True|False|False||OtherCreditorsWithinYear|Loan|
        2700|Bank Loan (> 1 year)|Liability|Other|True|False|False||LoansAfterYear|Loan|
        3000|Share Capital|Equity|Other|True|False|False||ShareCapital|ShareIssue|
        3100|Retained Earnings|Equity|Other|True|False|False||Reserves|OtherFinancing|RetainedEarnings
        3200|Dividends Paid|Equity|Other|True|False|False||DividendsPaid|Dividend|DividendsPaid
        """;

    // Spec §4: (code, type, statement line, cash-flow class, role) for every charity chart row.
    private static readonly (string Code, AccountCategoryType Type, AccountStatementLine Line, AccountCashFlowClass CashFlow, AccountSystemRole? Role)[] CharityChart =
    [
        ("0010", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing, null),
        ("0011", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing, null),
        ("0020", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing, null),
        ("0021", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing, null),
        ("0030", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing, null),
        ("0031", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing, null),
        ("1000", AccountCategoryType.Asset, AccountStatementLine.Stocks, AccountCashFlowClass.Operating, AccountSystemRole.Stock),
        ("1100", AccountCategoryType.Asset, AccountStatementLine.Debtors, AccountCashFlowClass.Operating, AccountSystemRole.TradeDebtors),
        ("1110", AccountCategoryType.Asset, AccountStatementLine.Debtors, AccountCashFlowClass.Operating, null),
        ("1120", AccountCategoryType.Asset, AccountStatementLine.Prepayments, AccountCashFlowClass.Operating, AccountSystemRole.Prepayments),
        ("1200", AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash, AccountSystemRole.BankControl),
        ("1230", AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash, null),
        ("2000", AccountCategoryType.Liability, AccountStatementLine.TradeCreditors, AccountCashFlowClass.Operating, AccountSystemRole.TradeCreditors),
        ("2100", AccountCategoryType.Liability, AccountStatementLine.Accruals, AccountCashFlowClass.Operating, AccountSystemRole.Accruals),
        ("2200", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Cash, null),
        ("2210", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan, null),
        ("2215", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan, null),
        ("2220", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan, null),
        ("2230", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.OtherFinancing, AccountSystemRole.DirectorLoanLiability),
        ("2240", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Operating, null),
        ("2250", AccountCategoryType.Liability, AccountStatementLine.TaxCreditors, AccountCashFlowClass.Operating, null),
        ("2260", AccountCategoryType.Liability, AccountStatementLine.TaxCreditors, AccountCashFlowClass.Operating, AccountSystemRole.VatPayable),
        ("2300", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsAfterYear, AccountCashFlowClass.Loan, null),
        ("2310", AccountCategoryType.Liability, AccountStatementLine.LoansAfterYear, AccountCashFlowClass.Loan, null),
        ("3000", AccountCategoryType.Equity, AccountStatementLine.Reserves, AccountCashFlowClass.OtherFinancing, AccountSystemRole.RetainedEarnings),
        ("3100", AccountCategoryType.Equity, AccountStatementLine.Reserves, AccountCashFlowClass.OtherFinancing, null),
        ("3200", AccountCategoryType.Equity, AccountStatementLine.RestrictedFunds, AccountCashFlowClass.OtherFinancing, null),
        ("4000", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, AccountSystemRole.TradeDebtorIncome),
        ("4010", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4020", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4030", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4040", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4100", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4110", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4200", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4300", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4400", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("4800", AccountCategoryType.Income, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, null),
        ("5000", AccountCategoryType.Expense, AccountStatementLine.CostOfSales, AccountCashFlowClass.Operating, null),
        ("5010", AccountCategoryType.Expense, AccountStatementLine.CostOfSales, AccountCashFlowClass.Operating, AccountSystemRole.CostOfSalesStockMovement),
        ("6000", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("6010", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("6020", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("6100", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("6110", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("6200", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("6300", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7000", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7010", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7100", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7200", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7210", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7300", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7400", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7500", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7600", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7700", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("7800", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("8000", AccountCategoryType.Expense, AccountStatementLine.Depreciation, AccountCashFlowClass.Operating, AccountSystemRole.Depreciation),
        ("8010", AccountCategoryType.Expense, AccountStatementLine.Depreciation, AccountCashFlowClass.Operating, null),
        ("8100", AccountCategoryType.Expense, AccountStatementLine.InterestPayable, AccountCashFlowClass.Operating, null),
        ("8110", AccountCategoryType.Expense, AccountStatementLine.InterestPayable, AccountCashFlowClass.Operating, null),
        ("8120", AccountCategoryType.Expense, AccountStatementLine.InterestPayable, AccountCashFlowClass.Operating, null),
        ("8200", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, null),
        ("9998", AccountCategoryType.Liability, AccountStatementLine.Suspense, AccountCashFlowClass.Operating, AccountSystemRole.Suspense),
        ("9999", AccountCategoryType.Equity, AccountStatementLine.OpeningBalanceControl, AccountCashFlowClass.OtherFinancing, AccountSystemRole.OpeningBalanceControl),
    ];

    [Fact]
    public async Task CharitySeed_HasEveryCharityChartCode_WithExplicitAttributesAndGenericBankNames()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.CharityClg, CompanyType.CompanyLimitedByGuarantee);

        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);

        Assert.Equal(CharityChart.Select(row => row.Code), seeded.Select(category => category.Code));
        foreach (var (code, type, line, cashFlow, role) in CharityChart)
        {
            var category = Assert.Single(seeded, category => category.Code == code);
            Assert.Equal(type, category.Type);
            Assert.Equal(line, category.StatementLine);
            Assert.Equal(cashFlow, category.CashFlowClass);
            Assert.Equal(role, category.SystemRole);
            Assert.Equal(company.Id, category.CompanyId);
            Assert.True(category.IsSystem);
            Assert.False(category.IsArchived);
            Assert.True(CategoryClassification.IsLineAllowedForType(category.Type, line), code);
            Assert.True(CategoryClassification.IsCashFlowClassAllowed(category.Type, line, cashFlow), code);
            // No account number, contract number or other long digit run is ever part of a name.
            Assert.DoesNotMatch(@"\d{4,}", category.Name);
        }
        Assert.Equal("Bank current account", seeded.Single(category => category.Code == "1200").Name);
        Assert.Equal("Bank overdraft", seeded.Single(category => category.Code == "2200").Name);
        Assert.Equal(TaxTreatment.NonDeductible, seeded.Single(category => category.Code == "8000").TaxTreatment);
        Assert.Equal(TaxTreatment.CapitalAllowance, seeded.Single(category => category.Code == "0010").TaxTreatment);
        Assert.DoesNotContain(seeded, category => category.Code is "1400" or "2400" or "3300");
        var roles = seeded.Where(category => category.SystemRole is not null).Select(category => category.SystemRole).ToList();
        Assert.Equal(roles.Count, roles.Distinct().Count());

        var again = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        Assert.Equal(seeded.Count, again.Count);
        Assert.Equal(CharityChart.Length, await db.AccountCategories.CountAsync(category => category.CompanyId == company.Id));
    }

    [Fact]
    public async Task CharityStatements_PresentTheCharityChartByItsAttributes()
    {
        await using var db = CreateDb();
        var fixture = await SeedCharityActivityAsync(db);
        var statements = new FinancialStatementsService(db);

        var trialBalance = await statements.GetTrialBalanceAsync(fixture.Company.Id, fixture.Period.Id);
        var profitAndLoss = await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.Period.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, fixture.Period.Id);
        var cashFlow = await statements.GetCashFlowStatementAsync(fixture.Company.Id, fixture.Period.Id);
        var equity = await statements.GetEquityChangesAsync(fixture.Company.Id, fixture.Period.Id);

        // The ledger needs no 1400 or 3100: the 1200 BankControl account is the bank side of every row.
        Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "1400"));
        Assert.Equal(5_685m, Assert.Single(trialBalance, line => line.Code == "1200").Debit);

        // P&L: 4xxx is turnover; 8000/8010 depreciation and 8200 levies are administrative overheads
        // (not tax); 8100/8110/8120 are interest payable.
        Assert.Equal(1_040m, profitAndLoss.Turnover);
        Assert.Equal(0m, profitAndLoss.OtherIncome);
        Assert.Contains(profitAndLoss.Overheads, line => line.Code == "8000" && line.Amount == 200m);
        Assert.Contains(profitAndLoss.Overheads, line => line.Code == "8010" && line.Amount == 100m);
        Assert.Contains(profitAndLoss.Overheads, line => line.Code == "8200" && line.Amount == 5m);
        Assert.DoesNotContain(profitAndLoss.Overheads, line => line.Code.StartsWith("81", StringComparison.Ordinal));
        Assert.Equal(555m, profitAndLoss.TotalOverheads);
        Assert.Equal(100m, profitAndLoss.InterestPayable);
        Assert.Equal(0m, profitAndLoss.TaxCharge);
        Assert.Equal(385m, profitAndLoss.ProfitAfterTax);

        // Balance sheet: 1200 is cash; 2210 is within one year; 2300 / 2310 after one year; members'
        // funds (3000/3100/3200) are reserves, never share capital.
        Assert.Equal(2_700m, balanceSheet.FixedAssets.Total);
        Assert.Equal(5_685m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(50m, balanceSheet.CurrentAssets.Prepayments);
        Assert.Equal(0m, balanceSheet.CurrentAssets.Debtors);
        Assert.Equal(300m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.TaxCreditors);
        Assert.Equal(300m, balanceSheet.CreditorsWithinYear.Total);
        Assert.Equal(2_000m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(500m, balanceSheet.CreditorsAfterYear.Other);
        Assert.Equal(5_635m, balanceSheet.NetAssets);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.DividendsPaid);
        Assert.Equal(5_635m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.True(balanceSheet.Balances);

        // Cash flow: depreciation (8000 + 8010) is added back by line, interest is interest paid, and
        // nothing is "tax paid"; the lease and loan creditors are loan drawdowns.
        Assert.Equal(300m, Assert.Single(cashFlow.OperatingAdjustments, item => item.Description == "Depreciation").Amount);
        Assert.Equal(-100m, Assert.Single(cashFlow.OperatingAdjustments, item => item.Description == "Interest paid").Amount);
        Assert.Equal(0m, cashFlow.TaxPaid);
        Assert.Equal(3_000m, cashFlow.CapitalExpenditurePurchases);
        Assert.Equal(2_800m, cashFlow.LoanDrawdowns);
        Assert.Equal(0m, cashFlow.DividendsPaid);
        Assert.Equal(0m, cashFlow.ShareIssues);
        Assert.Equal(250m, cashFlow.OtherFinancing);
        Assert.Equal(5_000m, cashFlow.OpeningCash);
        Assert.Equal(5_685m, cashFlow.ClosingCash);

        Assert.Equal(0m, equity.OpeningShareCapital);
        Assert.Equal(5_000m, equity.OpeningRetainedEarnings);
        Assert.Equal(0m, equity.DividendsPaid);
        Assert.Equal(250m, equity.OtherReserveMovements);
        Assert.Equal(5_635m, equity.ClosingRetainedEarnings);
    }

    [Fact]
    public async Task CharityCashFlow_ShowsNoTaxPaid_ForInterestDepreciationOrLevies()
    {
        await using var db = CreateDb();
        var fixture = await SeedCharityActivityAsync(db);

        var cashFlow = await new FinancialStatementsService(db).GetCashFlowStatementAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(0m, cashFlow.TaxPaid);
        Assert.Equal(cashFlow.CashFromOperations, cashFlow.NetCashFromOperating);
        Assert.All(
            await db.AccountCategories.Where(category => category.CompanyId == fixture.Company.Id).ToListAsync(),
            category => Assert.NotEqual(AccountCashFlowClass.Tax, CategoryClassification.CashFlow(category)));
    }

    [Fact]
    public async Task CharityLedger_ClosesThePriorYearResultInto3000_TheRetainedEarningsRole()
    {
        await using var db = CreateDb();
        var fixture = await SeedCharityActivityAsync(db);
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

        // C5 ruling: restricted funds (3200) persist across the year end; everything else closes into 3000.
        var members = Assert.Single(sources, source => source.Code == "3000");
        Assert.Equal(5_385m, members.OpeningCredit - members.OpeningDebit);
        var restricted = Assert.Single(sources, source => source.Code == "3200");
        Assert.Equal(250m, restricted.OpeningCredit - restricted.OpeningDebit);
        Assert.DoesNotContain(sources, source => source.Code == "3100" && source.OpeningCredit - source.OpeningDebit != 0m);
        Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "1400"));
        Assert.Equal(1, await db.AccountCategories.CountAsync(category => category.Code == "3100"));
        Assert.Equal(5_685m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(5_635m, balanceSheet.NetAssets);
        Assert.Equal(5_635m, equity.OpeningRetainedEarnings);
        Assert.True(balanceSheet.Balances);
    }

    [Fact]
    public async Task CharityAdjustments_PostToTheCharityRoles_AndCreateNoAccounts()
    {
        await using var db = CreateDb();
        var (company, period, _, ids) = await SeedCharityCompanyAsync(db);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Laptop",
            Category = "Equipment",
            Cost = 1_000m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 5,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Debtors.AddRange(
            new Debtor { PeriodId = period.Id, Name = "Insurance paid ahead", Amount = 120m, Type = DebtorType.Prepayment },
            new Debtor { PeriodId = period.Id, Name = "Service agreement", Amount = 400m, Type = DebtorType.Trade });
        db.Creditors.AddRange(
            new Creditor { PeriodId = period.Id, Name = "Accountancy fee", Amount = 90m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = period.Id, Name = "Software subscription", Amount = 60m, Type = CreditorType.Trade, DueWithinYear = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Goods held for resale", Value = 75m });
        await db.SaveChangesAsync();
        var before = await db.AccountCategories.CountAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var auto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == period.Id && adjustment.IsAuto).ToListAsync();
        Adjustment Journal(string prefix) => Assert.Single(auto, adjustment => adjustment.Description.StartsWith(prefix, StringComparison.Ordinal));
        var depreciation = Journal("Depreciation —");
        // Credited to accumulated depreciation (0021), never to the 0020 cost account.
        Assert.Equal((ids["8000"], ids["0021"]), (depreciation.DebitCategoryId, depreciation.CreditCategoryId));
        var prepayment = Journal("Prepayment —");
        Assert.Equal((ids["1120"], ids["7000"]), (prepayment.DebitCategoryId, prepayment.CreditCategoryId));
        var stock = Journal("Closing stock");
        Assert.Equal((ids["1000"], ids["5010"]), (stock.DebitCategoryId, stock.CreditCategoryId));
        var accrual = Assert.Single(auto, adjustment => adjustment.CreditCategoryId == ids["2100"]);
        Assert.Equal(ids["7210"], accrual.DebitCategoryId);
        var tradeCreditor = Assert.Single(auto, adjustment => adjustment.CreditCategoryId == ids["2000"]);
        Assert.Equal(ids["6000"], tradeCreditor.DebitCategoryId);
        var tradeDebtor = Assert.Single(auto, adjustment => adjustment.DebitCategoryId == ids["1100"]);
        Assert.Equal(ids["4000"], tradeDebtor.CreditCategoryId);

        // Every posting found an account of the charity chart; nothing was added, least of all a
        // 1400 bank or a second 3100.
        Assert.Equal(before, await db.AccountCategories.CountAsync());
        Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "1400"));
        Assert.Equal(1, await db.AccountCategories.CountAsync(category => category.Code == "3100"));
        Assert.All(auto, adjustment =>
        {
            Assert.Contains(adjustment.DebitCategoryId!.Value, ids.Values);
            Assert.Contains(adjustment.CreditCategoryId!.Value, ids.Values);
        });
    }

    [Fact]
    public async Task CharityDepreciation_CreditsEachClassAccumulatedDepreciationAccount()
    {
        // Defect: the generator credited the cost account (0010 / 0020), so cost fell each year and
        // accumulated depreciation stayed nil; the live charity carried manual reclass journals to fix it.
        await using var db = CreateDb();
        var (company, period, _, ids) = await SeedCharityCompanyAsync(db);
        db.FixedAssets.AddRange(
            new FixedAsset
            {
                CompanyId = company.Id,
                Name = "Coffee machine",
                Category = ChartDefinitions.LeasedEquipmentClass,
                Cost = 5_000m,
                AcquisitionDate = period.PeriodStart,
                UsefulLifeYears = 5,
                DepreciationMethod = DepreciationMethod.StraightLine
            },
            new FixedAsset
            {
                CompanyId = company.Id,
                Name = "Shop counter",
                Category = "Furniture",
                Cost = 1_000m,
                AcquisitionDate = period.PeriodStart,
                UsefulLifeYears = 5,
                DepreciationMethod = DepreciationMethod.StraightLine
            });
        // The register does not post cost; the take-on opening balances carry it, as in live use.
        db.OpeningBalances.AddRange(
            new OpeningBalance { PeriodId = period.Id, AccountCategoryId = ids["0010"], Debit = 5_000m, Credit = 0m, SourceNote = "Take-on", Reviewed = true },
            new OpeningBalance { PeriodId = period.Id, AccountCategoryId = ids["0030"], Debit = 1_000m, Credit = 0m, SourceNote = "Take-on", Reviewed = true });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var auto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == period.Id && adjustment.IsAuto).ToListAsync();
        var machine = Assert.Single(auto, adjustment => adjustment.Description.Contains("Coffee machine", StringComparison.Ordinal));
        Assert.Equal((ids["8010"], ids["0011"]), (machine.DebitCategoryId, machine.CreditCategoryId));
        var counter = Assert.Single(auto, adjustment => adjustment.Description.Contains("Shop counter", StringComparison.Ordinal));
        Assert.Equal((ids["8000"], ids["0031"]), (counter.DebitCategoryId, counter.CreditCategoryId));
        Assert.DoesNotContain(auto, adjustment => adjustment.CreditCategoryId == ids["0010"] || adjustment.CreditCategoryId == ids["0030"]);

        // The note shows each class as cost, accumulated depreciation and net book value: the
        // accumulated account is folded into its cost account, never listed as a negative class.
        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, period.Id);
        Assert.Equal(6_000m - 1_200m, balanceSheet.FixedAssets.Total);
        var leased = Assert.Single(balanceSheet.FixedAssets.Categories, line => line.Category == "Equipment held under finance lease");
        Assert.Equal((5_000m, 1_000m, 4_000m), (leased.Cost, leased.Depreciation, leased.Nbv));
        var fittings = Assert.Single(balanceSheet.FixedAssets.Categories, line => line.Category == "Fixtures and fittings");
        Assert.Equal((1_000m, 200m, 800m), (fittings.Cost, fittings.Depreciation, fittings.Nbv));
        Assert.DoesNotContain(balanceSheet.FixedAssets.Categories, line => line.Category.Contains("accumulated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OpeningStock_FromOpeningBalances_IsReleasedToCostOfSales_Once()
    {
        // Defect: the generator posted closing stock only and reversed only a prior FilingBridge closing
        // journal, so stock taken on through opening balances was double-counted. A take-on opening
        // stock is released here; one reversed through the prior period's own journal is not released twice.
        await using var db = CreateDb();
        var (company, period, _, ids) = await SeedCharityCompanyAsync(db);
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = period.Id, AccountCategoryId = ids["1000"], Debit = 5_000m, Credit = 0m, SourceNote = "Filed FY2025 stock", Reviewed = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Goods held for resale", Value = 7_000m });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var auto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == period.Id && adjustment.IsAuto).ToListAsync();
        var release = Assert.Single(auto, adjustment => adjustment.Description == AdjustmentService.OpeningStockReleaseDescription);
        Assert.Equal((ids["5010"], ids["1000"], 5_000m), (release.DebitCategoryId, release.CreditCategoryId, release.Amount));
        var closing = Assert.Single(auto, adjustment => adjustment.Description == AdjustmentService.ClosingStockDescription);
        Assert.Equal((ids["1000"], ids["5010"], 7_000m), (closing.DebitCategoryId, closing.CreditCategoryId, closing.Amount));
        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, period.Id);
        Assert.Equal(7_000m, balanceSheet.CurrentAssets.Stock);

        // A following period whose opening stock is the prior period's own closing journal: the reversal
        // releases it, and no second release is posted.
        var next = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = period.PeriodEnd.AddDays(1),
            PeriodEnd = period.PeriodEnd.AddYears(1),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(next);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = next.Id, AccountCategoryId = ids["1000"], Debit = 7_000m, Credit = 0m, SourceNote = "Carried", Reviewed = true });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, next.Id);

        var nextAuto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == next.Id && adjustment.IsAuto).ToListAsync();
        Assert.DoesNotContain(nextAuto, adjustment => adjustment.Description == AdjustmentService.OpeningStockReleaseDescription);
        var reversal = Assert.Single(nextAuto, adjustment => adjustment.CreditCategoryId == ids["1000"]);
        Assert.Equal((ids["5010"], 7_000m), (reversal.DebitCategoryId, reversal.Amount));
        Assert.StartsWith("Opening reversal —", reversal.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CharityAdjustments_CreateDisposalAndDirectorLoanReceivableRowsLazily_WithNonClashingCodes()
    {
        await using var db = CreateDb();
        var (company, period, _, ids) = await SeedCharityCompanyAsync(db);
        var director = new CompanyOfficer { CompanyId = company.Id, Name = "Example Director", Role = OfficerRole.Director };
        db.CompanyOfficers.Add(director);
        db.FixedAssets.AddRange(
            new FixedAsset
            {
                CompanyId = company.Id,
                Name = "Scrapped printer",
                Category = "Equipment",
                Cost = 1_000m,
                AcquisitionDate = new DateOnly(2024, 1, 1),
                DisposalDate = new DateOnly(2025, 6, 30),
                DisposalProceeds = 0m,
                UsefulLifeYears = 5,
                DepreciationMethod = DepreciationMethod.StraightLine
            },
            new FixedAsset
            {
                CompanyId = company.Id,
                Name = "Sold display unit",
                Category = "Furniture",
                Cost = 1_000m,
                AcquisitionDate = new DateOnly(2025, 1, 1),
                DisposalDate = new DateOnly(2025, 3, 31),
                DisposalProceeds = 2_000m,
                UsefulLifeYears = 5,
                DepreciationMethod = DepreciationMethod.StraightLine
            });
        await db.SaveChangesAsync();
        db.DirectorLoans.Add(new DirectorLoan { PeriodId = period.Id, DirectorId = director.Id, ClosingBalance = 150m, MaxBalanceDuringYear = 150m });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var auto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == period.Id && adjustment.IsAuto).ToListAsync();
        var loss = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Fixed asset disposal loss", StringComparison.Ordinal));
        var gain = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Fixed asset disposal gain", StringComparison.Ordinal));
        var reclass = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Director loan reclassification", StringComparison.Ordinal));
        Assert.Equal(ids["0020"], loss.CreditCategoryId);
        Assert.Equal(ids["0030"], gain.DebitCategoryId);
        Assert.Equal(ids["2230"], reclass.CreditCategoryId);

        var created = await db.AccountCategories
            .Where(category => category.CompanyId == company.Id && !ids.Values.Contains(category.Id))
            .OrderBy(category => category.Code)
            .ToListAsync();
        Assert.Equal(["1150", "4900", "7050"], created.Select(category => category.Code));
        var receivable = created.Single(category => category.Code == "1150");
        Assert.Equal((AccountCategoryType.Asset, AccountStatementLine.Debtors, AccountCashFlowClass.Operating, AccountSystemRole.DirectorLoanReceivable),
            (receivable.Type, receivable.StatementLine, receivable.CashFlowClass, receivable.SystemRole));
        Assert.Equal(receivable.Id, reclass.DebitCategoryId);
        var gainAccount = created.Single(category => category.Code == "4900");
        Assert.Equal((AccountCategoryType.Income, AccountStatementLine.OtherOperatingIncome, AccountCashFlowClass.Operating, AccountSystemRole.DisposalGain),
            (gainAccount.Type, gainAccount.StatementLine, gainAccount.CashFlowClass, gainAccount.SystemRole));
        Assert.Equal(gainAccount.Id, gain.CreditCategoryId);
        var lossAccount = created.Single(category => category.Code == "7050");
        Assert.Equal((AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, AccountSystemRole.DisposalLoss),
            (lossAccount.Type, lossAccount.StatementLine, lossAccount.CashFlowClass, lossAccount.SystemRole));
        Assert.Equal(lossAccount.Id, loss.DebitCategoryId);
    }

    [Fact]
    public async Task NonExemptCharityClg_GetsTheCorporationTaxProvisionOnLazy8900And2270()
    {
        await using var db = CreateDb();
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 2, 1), "Service agreement income", 1_000m, ids["4040"]));
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = 100m, Balance = 100m });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var provision = await db.Adjustments.SingleAsync(adjustment => adjustment.PeriodId == period.Id && adjustment.Description == "Corporation tax provision");
        var charge = await db.AccountCategories.SingleAsync(category => category.Id == provision.DebitCategoryId);
        var payable = await db.AccountCategories.SingleAsync(category => category.Id == provision.CreditCategoryId);
        Assert.Equal(
            ("8900", AccountCategoryType.Expense, AccountStatementLine.TaxOnProfit, AccountCashFlowClass.Tax, AccountSystemRole.CorporationTaxCharge, TaxTreatment.NonDeductible),
            (charge.Code, charge.Type, charge.StatementLine, charge.CashFlowClass, charge.SystemRole, charge.TaxTreatment));
        Assert.Equal(
            ("2270", AccountCategoryType.Liability, AccountStatementLine.TaxCreditors, AccountCashFlowClass.Tax, AccountSystemRole.CorporationTaxPayable),
            (payable.Code, payable.Type, payable.StatementLine, payable.CashFlowClass, payable.SystemRole));
        Assert.False(await db.AccountCategories.AnyAsync(category => category.Code == "2400"));
        Assert.Equal(AccountSystemRole.Depreciation, (await db.AccountCategories.SingleAsync(category => category.Id == ids["8000"])).SystemRole);

        // Part of the tax is paid from the bank during the year.
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 11, 1), "Corporation tax payment", -60m, payable.Id));
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var profitAndLoss = await statements.GetProfitAndLossAsync(company.Id, period.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(company.Id, period.Id);
        var cashFlow = await statements.GetCashFlowStatementAsync(company.Id, period.Id);

        Assert.Equal(100m, profitAndLoss.TaxCharge);
        Assert.DoesNotContain(profitAndLoss.Overheads, line => line.Code == "8900");
        Assert.Equal(900m, profitAndLoss.ProfitAfterTax);
        Assert.Equal(40m, balanceSheet.CreditorsWithinYear.TaxCreditors);
        Assert.True(balanceSheet.Balances);
        Assert.Equal(60m, cashFlow.TaxPaid);
        Assert.Equal(5_940m, cashFlow.ClosingCash);
    }

    [Fact]
    public async Task CharityLeasedEquipment_IsDepreciatedTo8010AgainstAccumulatedDepreciation0011()
    {
        await using var db = CreateDb();
        var (company, period, _, ids) = await SeedCharityCompanyAsync(db);
        db.FixedAssets.AddRange(
            new FixedAsset { CompanyId = company.Id, Name = "Leased machine", Category = ChartDefinitions.LeasedEquipmentClass, Cost = 5_000m, AcquisitionDate = period.PeriodStart, UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = company.Id, Name = "Laptop", Category = "IT", Cost = 1_000m, AcquisitionDate = period.PeriodStart, UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine });
        await db.SaveChangesAsync();
        var before = await db.AccountCategories.CountAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var auto = await db.Adjustments.Where(adjustment => adjustment.PeriodId == period.Id && adjustment.IsAuto).ToListAsync();
        var leased = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Depreciation — Leased machine", StringComparison.Ordinal));
        // Credited to the class's accumulated-depreciation account (0011 / 0021), never to cost.
        Assert.Equal((ids["8010"], ids["0011"], 1_000m), (leased.DebitCategoryId, leased.CreditCategoryId, leased.Amount));
        var owned = Assert.Single(auto, adjustment => adjustment.Description.StartsWith("Depreciation — Laptop", StringComparison.Ordinal));
        Assert.Equal((ids["8000"], ids["0021"]), (owned.DebitCategoryId, owned.CreditCategoryId));
        Assert.Equal(before, await db.AccountCategories.CountAsync());
    }

    [Fact]
    public async Task SeedingAnUnknownCompany_IsNotFound()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new CategoryService(db).SeedDefaultCategoriesAsync(987_654));
        Assert.False(await db.AccountCategories.AnyAsync());
    }

    [Theory]
    [InlineData("0040", "Asset", "TangibleFixedAssets", "Investing")]
    [InlineData("1050", "Asset", "Stocks", "Operating")]
    [InlineData("1115", "Asset", "Debtors", "Operating")]
    [InlineData("1130", "Asset", "Prepayments", "Operating")]
    [InlineData("1240", "Asset", "CashAtBank", "Cash")]
    [InlineData("2050", "Liability", "TradeCreditors", "Operating")]
    [InlineData("2150", "Liability", "Accruals", "Operating")]
    [InlineData("2245", "Liability", "OtherCreditorsWithinYear", "Operating")]
    [InlineData("2255", "Liability", "TaxCreditors", "Operating")]
    [InlineData("2320", "Liability", "OtherCreditorsAfterYear", "Loan")]
    [InlineData("3050", "Equity", "Reserves", "OtherFinancing")]
    [InlineData("3210", "Equity", "RestrictedFunds", "OtherFinancing")]
    [InlineData("4500", "Income", "Turnover", "Operating")]
    [InlineData("5020", "Expense", "CostOfSales", "Operating")]
    [InlineData("6400", "Expense", "AdministrativeExpenses", "Operating")]
    [InlineData("7900", "Expense", "AdministrativeExpenses", "Operating")]
    [InlineData("8020", "Expense", "Depreciation", "Operating")]
    [InlineData("8130", "Expense", "InterestPayable", "Operating")]
    [InlineData("8300", "Expense", "AdministrativeExpenses", "Operating")]
    [InlineData("8950", "Expense", "TaxOnProfit", "Tax")]
    public async Task CustomCategoryOnACharityChart_TakesTheCharityRangeDefaults_AndNoRole(string code, string type, string line, string cashFlow)
    {
        await using var host = await CategoryHost.StartAsync(ChartOfAccountsVariant.CharityClg);

        var response = await host.Client.PostAsJsonAsync($"/api/companies/{host.CompanyId}/categories", new { code, name = $"Custom {code}", type });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await host.InScopeAsync(async db =>
        {
            var stored = await db.AccountCategories.SingleAsync(category => category.Code == code);
            Assert.Equal(Enum.Parse<AccountStatementLine>(line), stored.StatementLine);
            Assert.Equal(Enum.Parse<AccountCashFlowClass>(cashFlow), stored.CashFlowClass);
            Assert.Null(stored.SystemRole);
        });
    }

    [Theory]
    [InlineData("9500", "Liability", "no default statement line")]
    [InlineData("1500", "Asset", "no default statement line")]
    [InlineData("8300", "Income", "range, which cannot be used for a Income account")]
    [InlineData("4500", "Expense", "range, which cannot be used for a Expense account")]
    public async Task CustomCategoryOnACharityChart_WithoutAUsableDefault_MustSupplyALine(string code, string type, string message)
    {
        await using var host = await CategoryHost.StartAsync(ChartOfAccountsVariant.CharityClg);

        var refused = await host.Client.PostAsJsonAsync($"/api/companies/{host.CompanyId}/categories", new { code, name = $"Custom {code}", type });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("statementLine", body, StringComparison.Ordinal);
        Assert.Contains(message, body, StringComparison.Ordinal);
        Assert.Contains("supply a statement line", body, StringComparison.Ordinal);

        // With an explicit line the same code is accepted.
        var explicitLine = type switch
        {
            "Liability" => "OtherCreditorsWithinYear",
            "Asset" => "Debtors",
            "Income" => "OtherOperatingIncome",
            _ => "AdministrativeExpenses"
        };
        var accepted = await host.Client.PostAsJsonAsync($"/api/companies/{host.CompanyId}/categories", new { code, name = $"Custom {code}", type, statementLine = explicitLine });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task CustomCategoryOnAGenericChart_StillUsesTheGenericCodeRules()
    {
        await using var host = await CategoryHost.StartAsync(ChartOfAccountsVariant.GenericTrading);

        var response = await host.Client.PostAsJsonAsync($"/api/companies/{host.CompanyId}/categories", new { code = "8300", name = "Custom 8300", type = "Expense" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await host.InScopeAsync(async db =>
        {
            var stored = await db.AccountCategories.SingleAsync(category => category.Code == "8300");
            Assert.Equal(AccountStatementLine.TaxOnProfit, stored.StatementLine);
            Assert.Equal(AccountCashFlowClass.Tax, stored.CashFlowClass);
        });
    }

    [Fact]
    public async Task CharityChart_RefusesADividendWithAClearReason()
    {
        await using var db = CreateDb();
        var (company, period, _, _) = await SeedCharityCompanyAsync(db);
        db.Dividends.Add(new Dividend { PeriodId = period.Id, Amount = 300m, DateDeclared = new DateOnly(2025, 9, 1) });
        await db.SaveChangesAsync();
        var before = await db.AccountCategories.CountAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id));

        Assert.Equal("A company limited by guarantee cannot pay dividends; remove the dividend entry.", error.Message);
        Assert.Equal(before, await db.AccountCategories.CountAsync());
        Assert.Throws<BusinessRuleException>(() => ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.DividendsPaid));
        Assert.Throws<BusinessRuleException>(() => ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.DividendsPayable));
        Assert.Equal("3200", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.GenericTrading, AccountSystemRole.DividendsPaid).Code);
    }

    [Fact]
    public void ChartAccountDefaults_GenericTablesAreUnchanged_AndCharityTablesUseCharityCodes()
    {
        // Generic: today's codes.
        Assert.Equal("1400", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.GenericTrading, AccountSystemRole.BankControl).Code);
        Assert.Equal("9000", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.GenericTrading, AccountSystemRole.DisposalGain).Code);
        Assert.Null(ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.GenericTrading, AccountSystemRole.DisposalGain).StatementLine);
        Assert.Equal("0010", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.GenericTrading, "Property").Code);
        Assert.Equal("0040", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.GenericTrading, "Equipment").Code);
        Assert.Equal("0050", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.GenericTrading, "IT").Code);
        Assert.Equal("6810", ChartAccountDefaults.ExpenseAccountForDescription(ChartOfAccountsVariant.GenericTrading, "Audit fee").Code);
        Assert.Equal("7900", ChartAccountDefaults.ExpenseAccountForDescription(ChartOfAccountsVariant.GenericTrading, "Sundry").Code);

        // Charity: role defaults are the charity chart's own rows, fully explicit.
        var bank = ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.BankControl);
        Assert.Equal(("1200", AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash), (bank.Code, bank.StatementLine, bank.CashFlowClass));
        Assert.Equal("8000", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.Depreciation).Code);
        Assert.Equal("1120", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.Prepayments).Code);
        Assert.Equal("5010", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.CostOfSalesStockMovement).Code);
        Assert.Equal("2230", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.DirectorLoanLiability).Code);
        Assert.Equal("1150", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.DirectorLoanReceivable).Code);
        Assert.Equal("7050", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.DisposalLoss).Code);
        Assert.Equal("4900", ChartAccountDefaults.RoleAccount(ChartOfAccountsVariant.CharityClg, AccountSystemRole.DisposalGain).Code);

        // Charity fixed-asset classes post to the cost accounts 0010 / 0020 / 0030.
        Assert.Equal("0010", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, "Leased Equipment").Code);
        Assert.Equal("0020", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, "Equipment").Code);
        Assert.Equal("0020", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, "IT").Code);
        Assert.Equal("0020", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, "Plant & Machinery").Code);
        Assert.Equal("0030", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, "Furniture").Code);
        Assert.Equal("0020", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, "Other").Code);

        // Charity expense heads for accruals, prepayments and trade creditors.
        string Head(string description) => ChartAccountDefaults.ExpenseAccountForDescription(ChartOfAccountsVariant.CharityClg, description).Code;
        Assert.Equal("8120", Head("Loan interest — community lender"));
        Assert.Equal("8100", Head("Overdraft interest"));
        Assert.Equal("7210", Head("Accountancy fee"));
        Assert.Equal("7200", Head("Solicitor"));
        Assert.Equal("7000", Head("Insurance renewal"));
        Assert.Equal("6300", Head("Electricity"));
        Assert.Equal("6300", Head("Office rent"));
        Assert.Equal("7500", Head("Phone bill"));
        Assert.Equal("6000", Head("Software subscription"));
        Assert.Equal("7700", Head("Sundry"));
        Assert.Equal("7600", Head("Bank charges"));
        Assert.Equal("7600", Head("Quarterly bank fee"));
        Assert.Equal("7600", Head("Account fees"));
        Assert.Equal("7210", Head("Accountancy fee"));

        // Leased equipment: cost 0010 and its own depreciation account 8010 on the charity chart; the
        // generic chart keeps one depreciation account and treats the new class like other equipment.
        Assert.Equal("0010", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, ChartDefinitions.LeasedEquipmentClass).Code);
        var leasedDepreciation = ChartAccountDefaults.DepreciationAccount(ChartOfAccountsVariant.CharityClg, ChartDefinitions.LeasedEquipmentClass);
        Assert.Equal(("8010", AccountStatementLine.Depreciation), (leasedDepreciation!.Value.Code, leasedDepreciation.Value.StatementLine));
        Assert.Null(ChartAccountDefaults.DepreciationAccount(ChartOfAccountsVariant.CharityClg, "Equipment"));
        Assert.Null(ChartAccountDefaults.DepreciationAccount(ChartOfAccountsVariant.GenericTrading, ChartDefinitions.LeasedEquipmentClass));
        Assert.Equal("0040", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.GenericTrading, ChartDefinitions.LeasedEquipmentClass).Code);
        var interest = ChartAccountDefaults.ExpenseAccountForDescription(ChartOfAccountsVariant.CharityClg, "Loan interest");
        Assert.Equal(AccountStatementLine.InterestPayable, interest.StatementLine);
        Assert.Equal(AccountCashFlowClass.Operating, interest.CashFlowClass);
        var insurance = ChartAccountDefaults.ExpenseAccountForDescription(ChartOfAccountsVariant.CharityClg, "Insurance");
        Assert.Equal(AccountStatementLine.AdministrativeExpenses, insurance.StatementLine);
    }

    [Fact]
    public async Task CharityBalanceSheet_MatchesTheFixedAssetRegisterThroughTheCharityClassTable()
    {
        await using var db = CreateDb();
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Shop fittings",
            Category = "Furniture",
            Cost = 1_200m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 5,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 1, 2), "Shop fittings", -1_200m, ids["0030"]));
        db.Adjustments.Add(Journal(period, "Depreciation of fittings", ids["8000"], ids["0030"], 240m));
        await db.SaveChangesAsync();

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, period.Id);

        var fittings = Assert.Single(balanceSheet.FixedAssets.Categories);
        Assert.Equal(1_200m, fittings.Cost);
        Assert.Equal(240m, fittings.Depreciation);
        Assert.Equal(960m, fittings.Nbv);
    }

    [Theory]
    [InlineData(CompanyType.CompanyLimitedByGuarantee, true, ChartOfAccountsVariant.CharityClg)]
    [InlineData(CompanyType.CompanyLimitedByGuarantee, false, ChartOfAccountsVariant.GenericTrading)]
    [InlineData(CompanyType.Private, true, ChartOfAccountsVariant.GenericTrading)]
    [InlineData(CompanyType.Private, false, ChartOfAccountsVariant.GenericTrading)]
    [InlineData(CompanyType.DesignatedActivityCompany, false, ChartOfAccountsVariant.GenericTrading)]
    public void DefaultVariant_IsCharityOnlyForACharitableClg(CompanyType type, bool charitable, ChartOfAccountsVariant expected)
    {
        Assert.Equal(expected, ChartDefinitions.DefaultVariantFor(type, charitable));
        var company = EndpointInputs.ToCompany(new CompanyInput
        {
            LegalName = "Default Variant Example",
            CompanyType = type,
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsCharitableOrganisation = charitable
        });
        Assert.Equal(expected, company.ChartOfAccountsVariant);
    }

    [Fact]
    public void CompanyInput_ExplicitVariantWinsAtCreation_ButNeverChangesAnExistingCompany()
    {
        var charityAsGeneric = EndpointInputs.ToCompany(new CompanyInput
        {
            LegalName = "Override Example CLG",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsCharitableOrganisation = true,
            ChartOfAccountsVariant = ChartOfAccountsVariant.GenericTrading
        });
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, charityAsGeneric.ChartOfAccountsVariant);

        var existing = new Company { LegalName = "Existing Example", ChartOfAccountsVariant = ChartOfAccountsVariant.GenericTrading };
        EndpointInputs.ApplyCompany(existing, new CompanyInput
        {
            LegalName = "Existing Example CLG",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsCharitableOrganisation = true,
            ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg
        });
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, existing.ChartOfAccountsVariant);
    }

    [Fact]
    public void CompanyInputValidation_RejectsAnUnknownVariant()
    {
        var input = new CompanyInput
        {
            LegalName = "Unknown Variant Example",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2020, 1, 1),
            AnnualReturnDate = new DateOnly(2020, 7, 1),
            ChartOfAccountsVariant = (ChartOfAccountsVariant)99
        };
        var details = Assert.IsType<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>(
            Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(EndpointInputs.ValidateCompany(input)).Value);
        Assert.Contains("chartOfAccountsVariant", details.Errors.Keys);

        var onboarding = CompanyOnboardingValidation.Validate(OnboardingInput(CompanyType.Private, false, (ChartOfAccountsVariant)99), "onboard-variant-0001");
        Assert.Contains("chartOfAccountsVariant", onboarding.Keys);
    }

    [Fact]
    public void OnboardingRequestFingerprint_IsUnchangedWhenNoVariantIsGiven()
    {
        // The legacy onboarding fingerprint serialises the whole input; an omitted variant must not
        // appear in it, so idempotency keys retained before this field existed still replay.
        var json = System.Text.Json.JsonSerializer.Serialize(
            OnboardingInput(CompanyType.Private, false, null),
            new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.DoesNotContain("ChartOfAccountsVariant", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CompanyType.CompanyLimitedByGuarantee, true, null, ChartOfAccountsVariant.CharityClg)]
    [InlineData(CompanyType.Private, false, null, ChartOfAccountsVariant.GenericTrading)]
    [InlineData(CompanyType.CompanyLimitedByGuarantee, true, ChartOfAccountsVariant.GenericTrading, ChartOfAccountsVariant.GenericTrading)]
    [InlineData(CompanyType.CompanyLimitedByGuarantee, false, ChartOfAccountsVariant.CharityClg, ChartOfAccountsVariant.CharityClg)]
    public async Task Onboarding_SeedsTheChartOfTheSelectedOrDefaultVariant(
        CompanyType type,
        bool charitable,
        ChartOfAccountsVariant? requested,
        ChartOfAccountsVariant expected)
    {
        await using var db = CreateDb();
        var tenant = new Tenant { Name = "Onboarding Variant Firm", Slug = $"chart-c3-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var actor = new AuthenticatedUser(100, tenant.Id, tenant.Name, "owner@example.ie", "Owner User", "Owner");
        var service = new CompanyOnboardingService(
            db,
            new PeriodChronologyService(db),
            new CategoryService(db),
            new AnnualReturnDateService(db, new AuditService(db)),
            new AuditService(db));

        var result = await service.CreateAsync(OnboardingInput(type, charitable, requested), $"onboard-variant-{Guid.NewGuid():N}", actor);

        var company = await db.Companies.IgnoreQueryFilters().SingleAsync(c => c.Id == result.Outcome.CompanyId);
        Assert.Equal(expected, company.ChartOfAccountsVariant);
        var categories = await db.AccountCategories.IgnoreQueryFilters().Where(c => c.CompanyId == company.Id).ToListAsync();
        Assert.Equal(categories.Count, result.Outcome.CategoryCount);
        var bankControl = Assert.Single(categories, c => c.SystemRole == AccountSystemRole.BankControl
            || c.SystemRole is null && c.StatementLine is null && c.Code == "1400");
        if (expected == ChartOfAccountsVariant.CharityClg)
        {
            Assert.Equal(CharityChart.Length, categories.Count);
            Assert.Equal("1200", bankControl.Code);
        }
        else
        {
            Assert.Equal(52, categories.Count);
            Assert.Equal("1400", bankControl.Code);
        }
    }

    private const string CharityChartNeedsClg =
        "The charity / CLG chart of accounts is only available to a company limited by guarantee.";

    [Theory]
    [InlineData(CompanyType.Private, false)]
    [InlineData(CompanyType.Private, true)]
    [InlineData(CompanyType.PrivateUnlimited, false)]
    [InlineData(CompanyType.DesignatedActivityCompany, true)]
    [InlineData(CompanyType.PublicLimitedCompany, false)]
    public async Task CharityChart_IsRefusedAtCreationAndOnboarding_ForACompanyNotLimitedByGuarantee(CompanyType type, bool charitable)
    {
        var input = new CompanyInput
        {
            LegalName = "Not A Guarantee Company",
            CompanyType = type,
            IncorporationDate = new DateOnly(2020, 1, 1),
            AnnualReturnDate = new DateOnly(2020, 7, 1),
            IsCharitableOrganisation = charitable,
            ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg
        };
        var details = Assert.IsType<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>(
            Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(EndpointInputs.ValidateCompany(input)).Value);
        Assert.Equal([CharityChartNeedsClg], details.Errors["chartOfAccountsVariant"]);

        await using var db = CreateDb();
        var tenant = new Tenant { Name = "Onboarding Variant Firm", Slug = $"chart-c3-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var actor = new AuthenticatedUser(100, tenant.Id, tenant.Name, "owner@example.ie", "Owner User", "Owner");
        var service = new CompanyOnboardingService(
            db,
            new PeriodChronologyService(db),
            new CategoryService(db),
            new AnnualReturnDateService(db, new AuditService(db)),
            new AuditService(db));

        var refused = await Assert.ThrowsAsync<CompanyOnboardingValidationException>(() =>
            service.CreateAsync(OnboardingInput(type, charitable, ChartOfAccountsVariant.CharityClg), $"onboard-variant-{Guid.NewGuid():N}", actor));

        Assert.Equal([CharityChartNeedsClg], refused.Errors["chartOfAccountsVariant"]);
        Assert.Empty(await db.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.AccountCategories.IgnoreQueryFilters().ToListAsync());

        // The same company on the standard chart, or with no chart named, is accepted.
        Assert.Null(EndpointInputs.ValidateCompany(new CompanyInput
        {
            LegalName = input.LegalName,
            CompanyType = type,
            IncorporationDate = input.IncorporationDate,
            AnnualReturnDate = input.AnnualReturnDate,
            IsCharitableOrganisation = charitable,
            ChartOfAccountsVariant = ChartOfAccountsVariant.GenericTrading
        }));
        Assert.False(CompanyOnboardingValidation.Validate(OnboardingInput(type, charitable, null), "onboard-variant-0002").ContainsKey("chartOfAccountsVariant"));
    }

    [Theory]
    [InlineData(CompanyType.Private)]
    [InlineData(CompanyType.DesignatedActivityCompany)]
    [InlineData(CompanyType.PublicLimitedCompany)]
    public void ACompanyOnTheCharityChart_CannotChangeItsTypeAwayFromClg(CompanyType newType)
    {
        var existing = new Company
        {
            LegalName = "Charity Chart Example CLG",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IsCharitableOrganisation = true,
            ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg
        };
        CompanyInput Input(CompanyType type) => new()
        {
            LegalName = "Charity Chart Example Renamed",
            CompanyType = type,
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsCharitableOrganisation = true
        };

        var refused = Assert.Throws<BusinessRuleException>(() => EndpointInputs.ApplyCompany(existing, Input(newType)));

        Assert.Equal(
            "This company uses the charity / CLG chart of accounts, which is only available to a company limited by guarantee; keep the company type as company limited by guarantee.",
            refused.Message);
        Assert.Equal(CompanyType.CompanyLimitedByGuarantee, existing.CompanyType);
        Assert.Equal("Charity Chart Example CLG", existing.LegalName);

        // Other edits to a charity-chart CLG still apply, and a company on the standard chart may change type.
        EndpointInputs.ApplyCompany(existing, Input(CompanyType.CompanyLimitedByGuarantee));
        Assert.Equal("Charity Chart Example Renamed", existing.LegalName);
        var generic = new Company
        {
            LegalName = "Standard Chart Example",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            ChartOfAccountsVariant = ChartOfAccountsVariant.GenericTrading
        };
        EndpointInputs.ApplyCompany(generic, Input(newType));
        Assert.Equal(newType, generic.CompanyType);
    }

    [Fact]
    public async Task CompanyRoutes_RefuseTheCharityChartForANonClg_AndATypeChangeAwayFromClg_With400()
    {
        await using var host = await CompanyHost.StartAsync();
        var created = await host.Client.PostAsJsonAsync("/api/companies", CompanyPayload("Private", "CharityClg"));
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains(CharityChartNeedsClg, await created.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var changed = await host.Client.PutAsJsonAsync($"/api/companies/{host.CompanyId}", CompanyPayload("Private", null));
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        using (var document = System.Text.Json.JsonDocument.Parse(await changed.Content.ReadAsStringAsync()))
        {
            Assert.Contains("only available to a company limited by guarantee", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        }

        await host.InScopeAsync(async db =>
        {
            var company = Assert.Single(await db.Companies.IgnoreQueryFilters().ToListAsync());
            Assert.Equal(CompanyType.CompanyLimitedByGuarantee, company.CompanyType);
            Assert.Equal(ChartOfAccountsVariant.CharityClg, company.ChartOfAccountsVariant);
            Assert.Equal("Company Route Example CLG", company.LegalName);
        });

        var kept = await host.Client.PutAsJsonAsync($"/api/companies/{host.CompanyId}", CompanyPayload("CompanyLimitedByGuarantee", null));
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
    }

    private static object CompanyPayload(string companyType, string? chartOfAccountsVariant) => new
    {
        legalName = "Company Route Example CLG",
        companyType,
        incorporationDate = "2025-01-01",
        financialYearStartMonth = 1,
        annualReturnDate = "2025-07-01",
        annualReturnDateEffectiveFrom = "2025-07-01",
        annualReturnDateSource = "CroRecord",
        annualReturnDateEvidenceReference = "CRO-CHART-ROUTE-001",
        isCharitableOrganisation = true,
        chartOfAccountsVariant
    };

    [Fact]
    public async Task SeedingAnExistingCompanyNeverChangesItsChart()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.CharityClg, CompanyType.CompanyLimitedByGuarantee);
        db.AccountCategories.Add(new AccountCategory { CompanyId = company.Id, Code = "4000", Name = "Sales", Type = AccountCategoryType.Income });
        await db.SaveChangesAsync();

        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);

        Assert.Equal("Sales", Assert.Single(seeded).Name);
    }

    private static CompanyOnboardingInput OnboardingInput(CompanyType type, bool charitable, ChartOfAccountsVariant? variant)
    {
        var incorporationDate = new DateOnly(2025, 1, 1);
        return new CompanyOnboardingInput
        {
            Company = new CompanyInput
            {
                LegalName = "Variant Onboarding Example",
                CompanyType = type,
                IncorporationDate = incorporationDate,
                FinancialYearStartMonth = 1,
                AnnualReturnDate = new DateOnly(2025, 7, 1),
                AnnualReturnDateEffectiveFrom = new DateOnly(2025, 7, 1),
                AnnualReturnDateSource = AnnualReturnDateSource.CroRecord,
                AnnualReturnDateEvidenceReference = "CRO-CORE-TEST-ARD",
                IsCharitableOrganisation = charitable,
                ChartOfAccountsVariant = variant
            },
            Officers = [new CompanyOfficerInput { Name = "Example Director", Role = OfficerRole.Director }],
            FirstPeriod = new AccountingPeriodInput
            {
                PeriodStart = incorporationDate,
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                GoingConcernConfirmed = true
            },
            OpeningBankAccount = new BankAccountInput { Name = "Main Current Account", Currency = "EUR" }
        };
    }

    private sealed record CharityFixture(Company Company, AccountingPeriod Period, BankAccount Bank, IReadOnlyDictionary<string, int> Ids);

    // A charity-seeded company with bank opening 5,000 and members' funds 5,000 brought forward.
    private static async Task<(Company Company, AccountingPeriod Period, BankAccount Bank, IReadOnlyDictionary<string, int> Ids)> SeedCharityCompanyAsync(AccountsDbContext db)
    {
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.CharityClg, CompanyType.CompanyLimitedByGuarantee);
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
        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        var ids = seeded.ToDictionary(category => category.Code, category => category.Id);
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
        await db.SaveChangesAsync();
        return (company, period, bank, ids);
    }

    // Donations 1,000 and online receipts 40; software 300 (50 prepaid); interest 60 + 30 + 10; levies 5;
    // long-term loan 2,000, lease after one year 500 and within one year 300 drawn; equipment 2,000
    // and leased equipment 1,000 bought, depreciated 200 and 100; restricted grant 250.
    private static async Task<CharityFixture> SeedCharityActivityAsync(AccountsDbContext db)
    {
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        db.ImportedTransactions.AddRange(
            Transaction(bank, period, new DateOnly(2025, 2, 1), "Donations received", 1_000m, ids["4000"]),
            Transaction(bank, period, new DateOnly(2025, 2, 2), "Online receipts", 40m, ids["4300"]),
            Transaction(bank, period, new DateOnly(2025, 3, 1), "Software", -300m, ids["6000"]),
            Transaction(bank, period, new DateOnly(2025, 4, 1), "Overdraft interest", -60m, ids["8100"]),
            Transaction(bank, period, new DateOnly(2025, 4, 2), "Lease interest", -30m, ids["8110"]),
            Transaction(bank, period, new DateOnly(2025, 4, 3), "Loan interest", -10m, ids["8120"]),
            Transaction(bank, period, new DateOnly(2025, 4, 4), "Card stamp duty", -5m, ids["8200"]),
            Transaction(bank, period, new DateOnly(2025, 5, 1), "Community loan drawn", 2_000m, ids["2310"]),
            Transaction(bank, period, new DateOnly(2025, 5, 2), "Lease financing after one year", 500m, ids["2300"]),
            Transaction(bank, period, new DateOnly(2025, 5, 3), "Lease financing within one year", 300m, ids["2210"]),
            Transaction(bank, period, new DateOnly(2025, 5, 4), "Equipment purchase", -2_000m, ids["0020"]),
            Transaction(bank, period, new DateOnly(2025, 5, 5), "Leased equipment", -1_000m, ids["0010"]),
            Transaction(bank, period, new DateOnly(2025, 6, 1), "Restricted grant", 250m, ids["3200"]));
        db.Adjustments.AddRange(
            Journal(period, "Depreciation of owned equipment", ids["8000"], ids["0020"], 200m),
            Journal(period, "Depreciation of leased equipment", ids["8010"], ids["0010"], 100m),
            Journal(period, "Software paid in advance", ids["1120"], ids["6000"], 50m));
        await db.SaveChangesAsync();
        return new CharityFixture(company, period, bank, ids);
    }

    private static readonly DateTime ReviewedAt = new(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    private static ImportedTransaction Transaction(BankAccount bank, AccountingPeriod period, DateOnly date, string description, decimal amount, int categoryId) => new()
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

    private static string Canonical(AccountCategory category) =>
        $"{category.Code}|{category.Name}|{category.Type}|{category.TaxTreatment}|{category.IsSystem}|{category.IsNonTradingIncome}|{category.IsArchived}|{category.ParentId}|{category.StatementLine}|{category.CashFlowClass}|{category.SystemRole}";

    private static async Task<Company> SeedCompanyAsync(AccountsDbContext db, ChartOfAccountsVariant variant, CompanyType companyType)
    {
        var tenant = new Tenant { Name = "Chart Variant Firm", Slug = $"chart-c3-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Chart Variant Example",
            CroNumber = "C30000",
            CompanyType = companyType,
            IncorporationDate = new DateOnly(2020, 1, 1),
            ChartOfAccountsVariant = variant
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"chart-c3-{Guid.NewGuid():N}").Options);

    // The banking endpoints over an in-memory database, for one company on the given chart variant.
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

        public static async Task<CategoryHost> StartAsync(ChartOfAccountsVariant variant)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            var databaseName = $"chart-c3-host-{Guid.NewGuid():N}";
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
                var company = new Company
                {
                    Tenant = tenant,
                    LegalName = "Chart Route Example",
                    CompanyType = CompanyType.CompanyLimitedByGuarantee,
                    IncorporationDate = new DateOnly(2025, 1, 1),
                    ChartOfAccountsVariant = variant
                };
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

    // The company endpoints, behind the exception middleware, with one charity-chart CLG.
    private sealed class CompanyHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private CompanyHost(WebApplication app, HttpClient client, int companyId)
        {
            _app = app;
            Client = client;
            CompanyId = companyId;
        }

        public HttpClient Client { get; }
        public int CompanyId { get; }

        public static async Task<CompanyHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            var databaseName = $"chart-company-host-{Guid.NewGuid():N}";
            builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
            builder.Services.Configure<ImportLimitConfig>(_ => { });
            builder.Services.Configure<IdempotencyConfig>(_ => { });
            builder.Services.AddSingleton<ApiAccessService>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddScoped<AnnualReturnDateService>();
            builder.Services.AddScoped<DeadlineService>();
            builder.Services.AddScoped<IdempotencyService>();
            builder.Services.AddScoped<AccountingWriteGuard>();
            builder.Services.AddScoped<PeriodChronologyService>();
            builder.Services.AddScoped<CategoryService>();
            builder.Services.AddScoped<CompanyOnboardingService>();
            builder.Services.AddScoped<ImportService>();
            builder.Services.AddScoped<AccountingConcurrencyCoordinator>();
            builder.Services.AddScoped<DuplicateReviewService>();
            builder.Services.AddScoped<FinancialStatementsService>();
            builder.Services.AddScoped<FilingReadinessProfileService>();
            builder.Services.AddScoped<FilingReleaseGate>(services => new FilingReleaseGate(
                services.GetRequiredService<AccountsDbContext>(),
                "chart-company-candidate",
                services.GetRequiredService<AuditService>()));

            var app = builder.Build();
            int tenantId;
            int companyId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var tenant = new Tenant { Name = "Chart Company Tenant", Slug = $"chart-company-{Guid.NewGuid():N}" };
                var company = new Company
                {
                    Tenant = tenant,
                    LegalName = "Company Route Example CLG",
                    CompanyType = CompanyType.CompanyLimitedByGuarantee,
                    IncorporationDate = new DateOnly(2025, 1, 1),
                    AnnualReturnDate = new DateOnly(2025, 7, 1),
                    IsCharitableOrganisation = true,
                    ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg
                };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
                tenantId = tenant.Id;
                companyId = company.Id;
            }

            app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                    UserId: 1,
                    TenantId: tenantId,
                    TenantName: "Chart Company Tenant",
                    Email: "chart.owner@example.ie",
                    DisplayName: "Chart Owner",
                    Role: "Owner",
                    AllowedCompanyIds: new HashSet<int> { companyId });
                await next();
            });
            app.MapCompanyEndpoints();
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            return new CompanyHost(app, client, companyId);
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
}
