using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using Xunit;

namespace Accounts.Tests;

// Chart task C5: the charity-variant presentation (members' funds, bank overdraft within creditors,
// finance-lease split) is gated on ChartOfAccountsVariant.CharityClg; every generic output is unchanged.
public sealed partial class CharityPresentationTests
{
    static CharityPresentationTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // Captured from the code before C5. The statements digest covers exactly what the CRO source
    // fingerprint serialises from the statements (trial balance, P&L, balance sheet, cash flow and
    // equity changes, with the fingerprint's own serializer) plus the generated notes. The PDF digest is
    // layout-independent: the sorted words of the accounts package and the CRO filing pack before their
    // notes, with the per-page header and footer removed, so fallback fonts (the PDFs request Helvetica,
    // which neither Windows nor Linux CI has) cannot change it by moving line or page breaks. A change
    // here is a change to every generic company's filed accounts and must be deliberate.
    [Theory]
    [InlineData("micro-ltd",
        "31b6dd4e0e6fe923d22adc6ce4e34a80bb035dd44c5f8c2015b16840fd4077fb",
        "44d8f0f7a74406e8ea72b6391cec759db10aa283485c73f5d32afe8bdb03b258")]
    [InlineData("small-abridged-ltd",
        "e7f8313343bdc7a038c00faac6e6721a6150ce7adb03b19685ab8bbabb63b633",
        "823f4bdf750958df331241fb56604a2a8c08ecf09ad9723d85958f909d1068fe")]
    [InlineData("dac-small",
        "7837044563d26ebeedaa17b83ca5de7759c862324eb480527beff996445d04db",
        "2fdf36857ff1b48233e41c8a6a6b931a43256e55c492f3a9c08325b8ca2cdf61")]
    [InlineData("medium-audit-required",
        "e211d78363baab774b022c30c1c16252cf6491ab3085a289516b738626b98956",
        "17e5f8c63048ba0400dcc493812c347b8bae81e78c9334035d68581dfb82f1a6")]
    [InlineData("clg-charity",
        "26d86e6673c1a6ce2958da5e715cb0c1db8e176b26654ed42fcaf1f9730a769b",
        "04e3984c5756caecc743170ac170d23d8090523ef6f1cce301cc62e97670b140")]
    public async Task GoldenScenarios_OnTheGenericChart_KeepTheirStatementsFingerprintSourceAndPdfText(
        string scenarioCode,
        string statementsSha256,
        string pdfWordsSha256)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            // The audited medium scenario cannot be finalised without a signed auditor's report, so it has
            // no final CRO pack; its accounts review package is still compared.
            var finalise = scenarioCode != "medium-audit-required";
            await using var db = CreateDb();
            var period = await FilingGoldenCorpusScenarioTests.SeedBasicScenarioAsync(db, GoldenCorpusFixture.Scenario(scenarioCode), finalise);
            Assert.Equal(
                ChartOfAccountsVariant.GenericTrading,
                await db.Companies.Where(company => company.Id == period.CompanyId).Select(company => company.ChartOfAccountsVariant).SingleAsync());
            var statements = new FinancialStatementsService(db);
            var balanceSheet = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
            var canonical = JsonSerializer.Serialize(new
            {
                TrialBalance = await statements.GetTrialBalanceAsync(period.CompanyId, period.Id),
                ProfitAndLoss = await statements.GetProfitAndLossAsync(period.CompanyId, period.Id),
                BalanceSheet = balanceSheet,
                CashFlow = await statements.GetCashFlowStatementAsync(period.CompanyId, period.Id),
                EquityChanges = await statements.GetEquityChangesAsync(period.CompanyId, period.Id),
                // Line endings in note templates follow the checkout (CRLF on Windows, LF on CI).
                Notes = (await db.NotesDisclosures
                        .Where(note => note.PeriodId == period.Id)
                        .OrderBy(note => note.NoteNumber)
                        .ThenBy(note => note.Id)
                        .ToListAsync())
                    .Select(note => new { note.NoteNumber, note.Title, Content = note.Content?.ReplaceLineEndings("\n"), note.IsRequired, note.IsIncluded })
                    .ToList()
            });

            var documents = new DocumentGeneratorService(db, statements);
            // The CRO number is random per fixture run, so it is masked. The notes (whose templates carry
            // checkout-dependent line endings) are compared through their content above.
            var croNumber = await db.Companies.Where(company => company.Id == period.CompanyId).Select(company => company.CroNumber).SingleAsync();
            var accountsPdf = await documents.GenerateAccountsReviewPackageAsync(period.CompanyId, period.Id);
            var words = StatementWords(accountsPdf, croNumber!)
                .Concat(finalise ? StatementWords(await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id), croNumber!) : [])
                .Order(StringComparer.Ordinal)
                .ToList();
            var pdfText = string.Join(" ", words);

            // Ordered wording of the generic capital presentation (a single table row each, so no wrap).
            var balanceSheetPage = BalanceSheetPageText(ExtractPdfText(accountsPdf));
            Assert.Contains("CAPITAL AND RESERVES Called up share capital", balanceSheetPage, StringComparison.Ordinal);
            Assert.Contains("Profit and loss account", balanceSheetPage, StringComparison.Ordinal);
            Assert.Contains("SHAREHOLDERS' FUNDS", balanceSheetPage, StringComparison.Ordinal);
            Assert.DoesNotContain("MEMBERS' FUNDS", balanceSheetPage, StringComparison.Ordinal);
            Assert.DoesNotContain("Bank overdraft", balanceSheetPage, StringComparison.Ordinal);

            // The API response carries no charity presentation members for a generic company.
            var apiJson = JsonSerializer.Serialize(balanceSheet, AccountsWorkflowTests.ApiJson);
            Assert.DoesNotContain("bankOverdraft", apiJson, StringComparison.Ordinal);
            Assert.DoesNotContain("membersFunds", apiJson, StringComparison.Ordinal);
            Assert.True(
                statementsSha256 == Sha256(canonical) && pdfWordsSha256 == Sha256(pdfText),
                $"{scenarioCode}: statements {Sha256(canonical)}, PDF words {Sha256(pdfText)}\n{canonical}\n{pdfText}");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task CharityBalanceSheet_PresentsTheOverdraftWithinCreditors_TheLeaseSplit_AndMembersFunds()
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg);

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(1_200m, balanceSheet.FixedAssets.Total);
        // Cash at bank shows only the debit-balance cash account (1230 cash in hand). The overdrawn
        // 1200 bank (1,300) and the 2200 overdraft account (320) are a bank overdraft within creditors,
        // never netted against the cash in hand.
        Assert.Equal(200m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(200m, balanceSheet.CurrentAssets.Total);
        Assert.Equal(1_620m, balanceSheet.CreditorsWithinYear.BankOverdraft);
        // Finance lease due within one year (2210: 300 drawn less 150 repaid) and arrears (2215: 100)
        // are other creditors within one year; 2300 (800) is due after more than one year.
        Assert.Equal(250m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(1_870m, balanceSheet.CreditorsWithinYear.Total);
        Assert.Equal(-1_670m, balanceSheet.NetCurrentAssets);
        Assert.Equal(800m, balanceSheet.CreditorsAfterYear.Other);
        Assert.Equal(0m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(800m, balanceSheet.CreditorsAfterYear.Total);
        Assert.Equal(-1_270m, balanceSheet.NetAssets);
        Assert.True(balanceSheet.Balances);

        // Members' funds: 1,000 brought forward, a deficit of 2,520 and 250 restricted funds.
        var funds = Assert.IsType<FinancialStatementsService.MembersFundsSection>(balanceSheet.MembersFunds);
        Assert.Equal(1_000m, funds.FundsBroughtForward);
        Assert.Equal(-2_520m, funds.SurplusForYear);
        Assert.Equal(0m, funds.OtherMovements);
        Assert.Equal(250m, funds.RestrictedFunds);
        Assert.Equal(-1_270m, funds.Total);
        Assert.Equal(balanceSheet.CapitalAndReserves.Total, funds.Total);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.ShareCapital);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(balanceSheet, AccountsWorkflowTests.ApiJson));
        Assert.Equal(1_620m, json.RootElement.GetProperty("creditorsWithinYear").GetProperty("bankOverdraft").GetDecimal());
        Assert.Equal(1_000m, json.RootElement.GetProperty("membersFunds").GetProperty("fundsBroughtForward").GetDecimal());

        // The same accounts and postings on a company on the generic chart keep today's presentation
        // (net cash, the overdraft account in other creditors, no members' funds) and the same net assets.
        await using var genericDb = CreateDb();
        var generic = await SeedOverdrawnCharityAsync(genericDb, ChartOfAccountsVariant.GenericTrading);
        var genericSheet = await new FinancialStatementsService(genericDb).GetBalanceSheetAsync(generic.Company.Id, generic.Period.Id);
        Assert.Equal(-1_100m, genericSheet.CurrentAssets.Cash);
        Assert.Null(genericSheet.CreditorsWithinYear.BankOverdraft);
        Assert.Equal(570m, genericSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(570m, genericSheet.CreditorsWithinYear.Total);
        Assert.Null(genericSheet.MembersFunds);
        Assert.Equal(balanceSheet.NetCurrentAssets, genericSheet.NetCurrentAssets);
        Assert.Equal(balanceSheet.NetAssets, genericSheet.NetAssets);
        Assert.Equal(balanceSheet.CapitalAndReserves, genericSheet.CapitalAndReserves);
        var genericJson = JsonSerializer.Serialize(genericSheet, AccountsWorkflowTests.ApiJson);
        Assert.DoesNotContain("bankOverdraft", genericJson, StringComparison.Ordinal);
        Assert.DoesNotContain("membersFunds", genericJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CharityCashFlow_TreatsTheBankAndOverdraftMovementsAsTheChangeInCash_NotAsOperatingFlows()
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg);
        var statements = new FinancialStatementsService(db);

        var cashFlow = await statements.GetCashFlowStatementAsync(fixture.Company.Id, fixture.Period.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, fixture.Period.Id);

        // Cash and cash equivalents = cash at bank and in hand less the bank overdraft.
        Assert.Equal(1_000m, cashFlow.OpeningCash);
        Assert.Equal(-1_420m, cashFlow.ClosingCash);
        Assert.Equal(balanceSheet.CurrentAssets.Cash - balanceSheet.CreditorsWithinYear.BankOverdraft, cashFlow.ClosingCash);
        Assert.Equal(-2_420m, cashFlow.NetIncreaseInCash);
        // The 200 moved to cash in hand and the 300 drawn on the overdraft account are not cash flows:
        // operating cash is the donations, the activity costs and the overdraft interest only.
        Assert.Equal(-2_500m, cashFlow.OperatingProfit);
        Assert.Equal(-20m, Assert.Single(cashFlow.OperatingAdjustments, item => item.Description == "Interest paid").Amount);
        Assert.DoesNotContain(cashFlow.OperatingAdjustments, item => item.Description.StartsWith("Non-cash", StringComparison.Ordinal));
        Assert.Equal(-2_520m, cashFlow.NetCashFromOperating);
        Assert.Equal(150m, cashFlow.LoanRepayments);
        Assert.Equal(250m, cashFlow.OtherFinancing);
        Assert.Equal(100m, cashFlow.NetCashFromFinancing);
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, -1_420, false)]
    [InlineData(ChartOfAccountsVariant.GenericTrading, -1_100, true)]
    public async Task CashFlow_CountsTheOverdraftAccountAsCash_OnlyOnTheCharityChart(
        ChartOfAccountsVariant variant,
        int closingCash,
        bool overdraftDrawingIsAWorkingCapitalMovement)
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, variant, withCashInHand: false);

        var cashFlow = await new FinancialStatementsService(db).GetCashFlowStatementAsync(fixture.Company.Id, fixture.Period.Id);

        // Generic chart, as before: cash is the cash at bank line only, so the 300 drawn on the 2200
        // overdraft account is an operating (working-capital) movement and its interest is not paid cash.
        Assert.Equal(1_000m, cashFlow.OpeningCash);
        Assert.Equal(closingCash, cashFlow.ClosingCash);
        Assert.Equal(
            overdraftDrawingIsAWorkingCapitalMovement,
            cashFlow.OperatingAdjustments.Any(item => item.Description.StartsWith("Non-cash", StringComparison.Ordinal) && item.Amount == 300m));
        Assert.Equal(
            !overdraftDrawingIsAWorkingCapitalMovement,
            cashFlow.OperatingAdjustments.Any(item => item.Description == "Interest paid" && item.Amount == -20m));
    }

    [Fact]
    public async Task RestrictedFunds_PersistAcrossTheYearEnd_AndAreNotFoldedIntoMembersFundsBroughtForward()
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg);
        var next = await AddNextPeriodAsync(db, fixture);
        var statements = new FinancialStatementsService(db);
        var yearOne = await statements.GetBalanceSheetAsync(fixture.Company.Id, fixture.Period.Id);

        var sources = await statements.GetStatementSourcesAsync(fixture.Company.Id, next.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(fixture.Company.Id, next.Id);
        var equity = await statements.GetEquityChangesAsync(fixture.Company.Id, next.Id);
        var readiness = await statements.GetReadinessScoreAsync(fixture.Company.Id, next.Id);

        // The 250 restricted grant stays on 3200; 3000 carries only the unrestricted funds
        // (1,000 brought forward less the 2,520 deficit).
        var restricted = Assert.Single(sources, source => source.Code == "3200");
        Assert.Equal(250m, restricted.OpeningCredit - restricted.OpeningDebit);
        var members = Assert.Single(sources, source => source.Code == "3000");
        Assert.Equal(-1_520m, members.OpeningCredit - members.OpeningDebit);

        var funds = Assert.IsType<FinancialStatementsService.MembersFundsSection>(balanceSheet.MembersFunds);
        Assert.Equal(-1_520m, funds.FundsBroughtForward);
        Assert.Equal(0m, funds.SurplusForYear);
        Assert.Equal(0m, funds.OtherMovements);
        Assert.Equal(250m, funds.RestrictedFunds);
        Assert.Equal(yearOne.MembersFunds!.Total, funds.Total);
        Assert.Equal(yearOne.NetAssets, balanceSheet.NetAssets);
        Assert.True(balanceSheet.Balances);

        // The statement of changes in equity opens at last year's closing funds, restricted included.
        Assert.Equal(yearOne.CapitalAndReserves.Total, equity.OpeningTotal);
        Assert.Equal(balanceSheet.CapitalAndReserves.RetainedEarnings, equity.ClosingRetainedEarnings);
        Assert.Equal(0m, equity.OtherReserveMovements);
        Assert.DoesNotContain(readiness.Warnings.Concat(readiness.MissingItems), item => item.Contains("Reserves disagree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenericChart_StillClosesThePriorYearsDividendsAndAnyRestrictedFundsIntoRetainedEarnings()
    {
        await using var db = CreateDb();
        var tenant = new Tenant { Name = "Generic Carry Firm", Slug = $"generic-c5-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Generic Carry Example Limited",
            CroNumber = "C50001",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2020, 1, 1)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = true };
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current account", OpeningBalance = 1_000m, OpeningBalanceDate = new DateOnly(2025, 1, 1) };
        db.AddRange(period, bank);
        await db.SaveChangesAsync();
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(category => category.Code, category => category.Id);
        Assert.DoesNotContain(
            await db.AccountCategories.Where(category => category.CompanyId == company.Id).ToListAsync(),
            category => CategoryClassification.Line(category) == AccountStatementLine.RestrictedFunds);
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = ids["3100"],
            Credit = 1_000m,
            SourceNote = "Take-on",
            EnteredBy = "Reviewer",
            Reviewed = true,
            ReviewedBy = "Reviewer",
            ReviewedAt = ReviewedAt
        });
        // A generic company may still create a RestrictedFunds account explicitly.
        var restrictedFunds = new AccountCategory
        {
            CompanyId = company.Id,
            Code = "3300",
            Name = "Restricted reserve",
            Type = AccountCategoryType.Equity,
            StatementLine = AccountStatementLine.RestrictedFunds,
            CashFlowClass = AccountCashFlowClass.OtherFinancing
        };
        db.AccountCategories.Add(restrictedFunds);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            Transaction(bank, period, new DateOnly(2025, 2, 1), "Sales", 2_000m, ids["4000"]),
            Transaction(bank, period, new DateOnly(2025, 6, 1), "Dividend paid", -300m, ids["3200"]),
            Transaction(bank, period, new DateOnly(2025, 7, 1), "Restricted grant", 400m, restrictedFunds.Id));
        await db.SaveChangesAsync();
        var next = await AddNextPeriodAsync(db, new Fixture(company, period));

        var sources = await new FinancialStatementsService(db).GetStatementSourcesAsync(company.Id, next.Id);

        // Generic 3200 is Dividends Paid and the explicit 3300 is not persisted on the generic chart: both
        // still close into 3100 retained earnings (1,000 + 2,000 - 300 + 400), exactly as before C5.
        Assert.DoesNotContain(sources, source => source.Code is "3200" or "3300");
        var retained = Assert.Single(sources, source => source.Code == "3100");
        Assert.Equal(3_100m, retained.OpeningCredit - retained.OpeningDebit);
    }

    private static async Task<AccountingPeriod> AddNextPeriodAsync(AccountsDbContext db, Fixture fixture)
    {
        var next = new AccountingPeriod
        {
            CompanyId = fixture.Company.Id,
            PeriodStart = fixture.Period.PeriodEnd.AddDays(1),
            PeriodEnd = fixture.Period.PeriodEnd.AddYears(1),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(next);
        await db.SaveChangesAsync();
        return next;
    }

    [Fact]
    public async Task CharityWithoutAnOverdraft_HasANilBankOverdraft_AndNoOverdraftLineInTheNotes()
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg, overdrawn: false);

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(fixture.Company.Id, fixture.Period.Id);
        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);

        // Bank 1,900 and cash in hand 200; lease creditors 250.
        Assert.Equal(2_100m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.BankOverdraft);
        Assert.Equal(250m, balanceSheet.CreditorsWithinYear.Total);
        var creditors = Assert.Single(notes, note => note.Code == StatutoryNoteCodes.CurrentCreditors);
        Assert.StartsWith("Trade creditors:", creditors.Content);
        Assert.DoesNotContain("Bank overdraft", creditors.Content);
    }

    [Fact]
    public async Task CharityNotes_ShowTheBankOverdraftAndMembersFunds()
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg);

        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);

        var creditors = Assert.Single(notes, note => note.Code == StatutoryNoteCodes.CurrentCreditors);
        Assert.StartsWith("Bank overdraft: €1,620.00\nTrade creditors:", creditors.Content);
        Assert.Contains("Total amounts falling due within one year: €1,870.00", creditors.Content);
        var reserves = Assert.Single(notes, note => note.Code == StatutoryNoteCodes.Reserves);
        Assert.Equal(
            "Members' funds brought forward: €1,000.00\nSurplus/(deficit) for the financial year: €-2,520.00\nRestricted funds: €250.00\nTotal members' funds: €-1,270.00",
            reserves.Content);
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg)]
    [InlineData(ChartOfAccountsVariant.GenericTrading)]
    public async Task AccountsPdf_UsesTheMembersFundsAndOverdraftPresentation_OnlyForTheCharityChart(ChartOfAccountsVariant variant)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await using var db = CreateDb();
            var period = await SeedCharityPdfScenarioAsync(db, variant);
            var statements = new FinancialStatementsService(db);
            var text = ExtractPdfText(await new DocumentGeneratorService(db, statements).GenerateAccountsReviewPackageAsync(period.CompanyId, period.Id));
            var balanceSheetPage = BalanceSheetPageText(text);
            var balanceSheet = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
            Assert.Equal(1_450m, balanceSheet.NetAssets);

            if (variant == ChartOfAccountsVariant.CharityClg)
            {
                Assert.Contains("Cash at bank and in hand 2,000 0", balanceSheetPage, StringComparison.Ordinal);
                Assert.Contains(
                    "CREDITORS: amounts falling due within one year Bank overdraft (550) 0 Trade and other creditors (300) 0 (850) 0 NET CURRENT ASSETS 1,150 0",
                    balanceSheetPage,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "NET ASSETS 1,450 0 MEMBERS' FUNDS Members' funds brought forward 2,000 0 Surplus/(deficit) for the financial year (800) 0 Restricted funds 250 0 TOTAL MEMBERS' FUNDS 1,450 0",
                    balanceSheetPage,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("Called up share capital", text, StringComparison.Ordinal);
                Assert.DoesNotContain("SHAREHOLDERS' FUNDS", text, StringComparison.Ordinal);
                Assert.DoesNotContain("CAPITAL AND RESERVES", balanceSheetPage, StringComparison.Ordinal);
                Assert.Contains("Bank overdraft: €550.00", text, StringComparison.Ordinal);
                Assert.Contains("Members' funds brought forward: €2,000.00", text, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("Cash at bank and in hand 1,450 0", balanceSheetPage, StringComparison.Ordinal);
                Assert.Contains(
                    "CREDITORS: amounts falling due within one year (300) 0 NET CURRENT ASSETS 1,150 0",
                    balanceSheetPage,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "NET ASSETS 1,450 0 CAPITAL AND RESERVES Called up share capital 0 0 Profit and loss account 1,450 0 SHAREHOLDERS' FUNDS 1,450 0",
                    balanceSheetPage,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("Bank overdraft", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Members' funds", text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg)]
    [InlineData(ChartOfAccountsVariant.GenericTrading)]
    public async Task StatementOfChangesInEquityPdf_UsesMembersFundsLabels_OnlyForTheCharityChart(ChartOfAccountsVariant variant)
    {
        await using var db = CreateDb();
        var period = await FilingGoldenCorpusScenarioTests.SeedBasicScenarioAsync(db, GoldenCorpusFixture.Scenario("medium-audit-required"), finalise: false);
        var company = await db.Companies.SingleAsync(item => item.Id == period.CompanyId);
        company.ChartOfAccountsVariant = variant;
        // No share capital: the scenario's 100 take-on is re-pointed to a members' funds reserve.
        var reserve = new AccountCategory
        {
            CompanyId = company.Id,
            Code = "3050",
            Name = "Members' funds brought forward",
            Type = AccountCategoryType.Equity,
            StatementLine = AccountStatementLine.Reserves,
            CashFlowClass = AccountCashFlowClass.OtherFinancing
        };
        db.AccountCategories.Add(reserve);
        await db.SaveChangesAsync();
        foreach (var opening in await db.OpeningBalances.Where(item => item.PeriodId == period.Id && item.AccountCategory.Code == "3000").ToListAsync())
            opening.AccountCategoryId = reserve.Id;
        await db.SaveChangesAsync();

        var text = ExtractPdfText(await new DocumentGeneratorService(db, new FinancialStatementsService(db)).GenerateAccountsReviewPackageAsync(period.CompanyId, period.Id));
        var start = text.IndexOf("STATEMENT OF CHANGES IN", StringComparison.Ordinal);
        var socie = text[start..text.IndexOf("Balance at end of year", start, StringComparison.Ordinal)];

        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            Assert.StartsWith("STATEMENT OF CHANGES IN MEMBERS' FUNDS", socie, StringComparison.Ordinal);
            Assert.Contains("Members' funds €", socie, StringComparison.Ordinal);
            Assert.Contains("Surplus/(deficit) for the financial year", socie, StringComparison.Ordinal);
            Assert.DoesNotContain("Retained earnings", socie, StringComparison.Ordinal);
            // A company limited by guarantee shows no (nil) share capital column and no dividends row.
            Assert.DoesNotContain("Share capital", socie, StringComparison.Ordinal);
            Assert.DoesNotContain("Dividends paid", socie, StringComparison.Ordinal);
        }
        else
        {
            Assert.StartsWith("STATEMENT OF CHANGES IN EQUITY", socie, StringComparison.Ordinal);
            Assert.Contains("Retained earnings €", socie, StringComparison.Ordinal);
            Assert.Contains("Profit for the financial year", socie, StringComparison.Ordinal);
            Assert.DoesNotContain("Members' funds", socie, StringComparison.Ordinal);
            Assert.Contains("Share capital €", socie, StringComparison.Ordinal);
        }
    }

    // Wording task: the income and expenditure presentation, the directors' report and the notes use
    // surplus/deficit and members' funds wording on the charity chart only.
    private static async Task<AccountingPeriod> SeedMediumWithVariantAsync(AccountsDbContext db, ChartOfAccountsVariant variant, bool withoutShareCapital = false)
    {
        var period = await FilingGoldenCorpusScenarioTests.SeedBasicScenarioAsync(db, GoldenCorpusFixture.Scenario("medium-audit-required"), finalise: false);
        var company = await db.Companies.SingleAsync(item => item.Id == period.CompanyId);
        company.ChartOfAccountsVariant = variant;
        if (withoutShareCapital)
        {
            // The scenario's 100 take-on on 3000 is re-pointed to a reserve, so the company has no share capital.
            var reserve = new AccountCategory
            {
                CompanyId = company.Id,
                Code = "3050",
                Name = "Reserves brought forward",
                Type = AccountCategoryType.Equity,
                StatementLine = AccountStatementLine.Reserves,
                CashFlowClass = AccountCashFlowClass.OtherFinancing
            };
            db.AccountCategories.Add(reserve);
            await db.SaveChangesAsync();
            foreach (var opening in await db.OpeningBalances.Where(item => item.PeriodId == period.Id && item.AccountCategory.Code == "3000").ToListAsync())
                opening.AccountCategoryId = reserve.Id;
        }
        await db.SaveChangesAsync();
        // The scenario generated its notes on the generic chart; regenerate them for the chosen variant.
        await new NotesDisclosureService(db).GenerateNotesAsync(company.Id, period.Id);
        return period;
    }

    private static string Section(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing '{from}'.");
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing '{to}' after '{from}'.");
        return text[start..end];
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg)]
    [InlineData(ChartOfAccountsVariant.GenericTrading)]
    public async Task AccountsPdf_UsesIncomeAndExpenditureAndSurplusWording_OnlyForTheCharityChart(ChartOfAccountsVariant variant)
    {
        await using var db = CreateDb();
        var period = await SeedMediumWithVariantAsync(db, variant);
        var text = ExtractPdfText(await new DocumentGeneratorService(db, new FinancialStatementsService(db)).GenerateAccountsReviewPackageAsync(period.CompanyId, period.Id));
        var pnl = Section(text, variant == ChartOfAccountsVariant.CharityClg ? "INCOME AND EXPENDITURE ACCOUNT" : "PROFIT AND LOSS ACCOUNT", "CASH FLOW STATEMENT");

        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            Assert.DoesNotContain("Profit and Loss Account", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Income and Expenditure Account", text, StringComparison.Ordinal);
            Assert.Contains("Income ", pnl, StringComparison.Ordinal);
            Assert.DoesNotContain("Turnover", pnl, StringComparison.Ordinal);
            Assert.Contains("Surplus/(deficit) before taxation", pnl, StringComparison.Ordinal);
            Assert.Contains("Surplus/(deficit) for the financial year", pnl, StringComparison.Ordinal);
            // The tax line is kept only when there is tax (generic output always shows it).
            var taxCharge = (await new FinancialStatementsService(db).GetProfitAndLossAsync(period.CompanyId, period.Id)).TaxCharge;
            Assert.Equal(taxCharge != 0, pnl.Contains("Tax on surplus", StringComparison.Ordinal));
            Assert.DoesNotContain("rofit", pnl, StringComparison.OrdinalIgnoreCase);
            var cashFlowStatement = Section(text, "CASH FLOW STATEMENT", "STATEMENT OF CHANGES IN");
            Assert.Contains("Operating surplus/(deficit)", cashFlowStatement, StringComparison.Ordinal);
            Assert.DoesNotContain("Operating profit", cashFlowStatement, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Turnover ", pnl, StringComparison.Ordinal);
            Assert.Contains("Profit before taxation", pnl, StringComparison.Ordinal);
            Assert.Contains("Tax on profit", pnl, StringComparison.Ordinal);
            Assert.Contains("Profit for the financial year", pnl, StringComparison.Ordinal);
            Assert.DoesNotContain("INCOME AND EXPENDITURE", text, StringComparison.Ordinal);
            Assert.DoesNotContain("urplus", pnl, StringComparison.Ordinal);
            var cashFlowStatement = Section(text, "CASH FLOW STATEMENT", "STATEMENT OF CHANGES IN");
            Assert.Contains("Operating profit", cashFlowStatement, StringComparison.Ordinal);
            Assert.DoesNotContain("urplus", cashFlowStatement, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg)]
    [InlineData(ChartOfAccountsVariant.GenericTrading)]
    public async Task DirectorsReport_UsesSurplusWordingAndNoDividends_OnlyForTheCharityChart(ChartOfAccountsVariant variant)
    {
        await using var db = CreateDb();
        var period = await SeedMediumWithVariantAsync(db, variant);
        var statements = new FinancialStatementsService(db);
        var report = await new DirectorsReportService(db, statements).GenerateAsync(period.CompanyId, period.Id);
        var text = ExtractPdfText(await new DocumentGeneratorService(db, statements).GenerateAccountsReviewPackageAsync(period.CompanyId, period.Id));
        var directorsReport = Section(text, "DIRECTORS' REPORT", "Accounting Records");

        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            Assert.Matches(@"^The (surplus|deficit) for the financial year after taxation was €", report.ResultsAndDividends);
            Assert.Contains("limited by guarantee, has no share capital and pays no dividends", report.ResultsAndDividends, StringComparison.Ordinal);
            Assert.DoesNotContain("rofit", report.ResultsAndDividends, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ividends were", report.ResultsAndDividends, StringComparison.Ordinal);
            Assert.DoesNotContain("hareholder", report.ResultsAndDividends, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Results", directorsReport, StringComparison.Ordinal);
            Assert.DoesNotContain("Results and Dividends", directorsReport, StringComparison.Ordinal);
            Assert.DoesNotContain("rofit", directorsReport, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Matches(@"^The (profit|loss) for the financial year after corporation tax was €", report.ResultsAndDividends);
            Assert.Contains("No dividends were paid during the financial year.", report.ResultsAndDividends, StringComparison.Ordinal);
            Assert.Contains("Results and Dividends", directorsReport, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg)]
    [InlineData(ChartOfAccountsVariant.GenericTrading)]
    public async Task Notes_UseSurplusAndMembersFundsWording_AndOmitShareCapitalAndDividends_OnlyForTheCharityChart(ChartOfAccountsVariant variant)
    {
        await using var db = CreateDb();
        var period = await SeedMediumWithVariantAsync(db, variant, withoutShareCapital: true);
        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            // A charity company is a company limited by guarantee, which has no share capital.
            (await db.Companies.SingleAsync(item => item.Id == period.CompanyId)).CompanyType = CompanyType.CompanyLimitedByGuarantee;
            await db.SaveChangesAsync();
        }
        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        NotesDisclosure Note(string code) => Assert.Single(notes, note => note.Code == code);

        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            Assert.Equal("Members' Funds", Note(StatutoryNoteCodes.Reserves).Title);
            Assert.Equal("Income", Note(StatutoryNoteCodes.Turnover).Title);
            Assert.StartsWith("Income for the financial year: ", Note(StatutoryNoteCodes.Turnover).Content, StringComparison.Ordinal);
            Assert.Equal("Tax on Surplus on Ordinary Activities", Note(StatutoryNoteCodes.TaxOnProfit).Title);
            Assert.Contains("income and expenditure account", Note(StatutoryNoteCodes.TaxOnProfit).Content, StringComparison.Ordinal);
            Assert.False(Note(StatutoryNoteCodes.ShareCapital).IsIncluded);
            Assert.Equal(NoteChecklistState.NotApplicable, Note(StatutoryNoteCodes.ShareCapital).ChecklistState);
            Assert.False(Note(StatutoryNoteCodes.Dividends).IsIncluded);
            Assert.Equal(NoteChecklistState.NotApplicable, Note(StatutoryNoteCodes.Dividends).ChecklistState);
        }
        else
        {
            Assert.Equal("Capital and Reserves", Note(StatutoryNoteCodes.Reserves).Title);
            Assert.Equal("Turnover", Note(StatutoryNoteCodes.Turnover).Title);
            Assert.StartsWith("Turnover for the financial year: ", Note(StatutoryNoteCodes.Turnover).Content, StringComparison.Ordinal);
            Assert.Equal("Tax on Profit on Ordinary Activities", Note(StatutoryNoteCodes.TaxOnProfit).Title);
            Assert.Contains("profit and loss account", Note(StatutoryNoteCodes.TaxOnProfit).Content, StringComparison.Ordinal);
            Assert.Equal("Dividends", Note(StatutoryNoteCodes.Dividends).Title);
            Assert.Equal(NoteChecklistState.ExplicitReview, Note(StatutoryNoteCodes.ShareCapital).ChecklistState);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CharityDirectorsReport_StatesRecordedDividendsInsteadOfNoDividends(bool paid)
    {
        await using var db = CreateDb();
        var period = await SeedMediumWithVariantAsync(db, ChartOfAccountsVariant.CharityClg);
        db.Dividends.Add(new Dividend
        {
            PeriodId = period.Id,
            Amount = 125m,
            DateDeclared = period.PeriodStart.AddDays(10),
            DatePaid = paid ? period.PeriodStart.AddDays(20) : null
        });
        await db.SaveChangesAsync();

        var text = (await new DirectorsReportService(db, new FinancialStatementsService(db)).GenerateAsync(period.CompanyId, period.Id)).ResultsAndDividends;

        Assert.Matches(@"^The (surplus|deficit) for the financial year after taxation was €", text);
        Assert.DoesNotContain("pays no dividends", text, StringComparison.Ordinal);
        Assert.Contains(paid
            ? "Dividends of €125.00 were paid during the financial year."
            : "Dividends of €125.00 were declared during the financial year but remained unpaid", text, StringComparison.Ordinal);
    }

    private sealed record Fixture(Company Company, AccountingPeriod Period);

    // Charity-chart accounts on a company of the given variant: bank opening 1,000 and members' funds
    // 1,000 brought forward; donations 2,000; restricted grant 250; 200 moved to cash in hand (optional);
    // lease repayment 150; leased equipment 1,200 financed 300 within one year (2210), 100 arrears (2215)
    // and 800 after one year (2300). Overdrawn (default): 300 drawn on the 2200 overdraft account,
    // activity costs 4,500 (bank ends 1,300 overdrawn) and overdraft interest 20 charged to 2200;
    // otherwise activity costs 1,000 and no 2200 activity.
    private static async Task<Fixture> SeedOverdrawnCharityAsync(AccountsDbContext db, ChartOfAccountsVariant variant, bool withCashInHand = true, bool overdrawn = true)
    {
        var tenant = new Tenant { Name = "Charity Presentation Firm", Slug = $"charity-c5-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Charity Presentation Example CLG",
            CroNumber = "C50000",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsCharitableOrganisation = true,
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
            OpeningBalance = 1_000m,
            OpeningBalanceDate = new DateOnly(2025, 1, 1)
        };
        db.AddRange(period, bank);
        db.AccountCategories.AddRange(ChartDefinitions.CharityClg.CreateCategories(company.Id));
        await db.SaveChangesAsync();
        var ids = await db.AccountCategories
            .Where(category => category.CompanyId == company.Id)
            .ToDictionaryAsync(category => category.Code, category => category.Id);

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = ids["3000"],
            Credit = 1_000m,
            SourceNote = "Members' funds take-on",
            EnteredBy = "Reviewer",
            Reviewed = true,
            ReviewedBy = "Reviewer",
            ReviewedAt = ReviewedAt
        });
        if (withCashInHand)
            db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 3, 1), "Cash withdrawn for petty cash", -200m, ids["1230"]));
        db.ImportedTransactions.AddRange(
            Transaction(bank, period, new DateOnly(2025, 2, 1), "Donations received", 2_000m, ids["4000"]),
            Transaction(bank, period, new DateOnly(2025, 2, 2), "Restricted grant", 250m, ids["3200"]),
            Transaction(bank, period, new DateOnly(2025, 4, 1), "Finance lease repayment", -150m, ids["2210"]),
            Transaction(bank, period, new DateOnly(2025, 5, 1), "Charitable activity costs", overdrawn ? -4_500m : -1_000m, ids["6000"]));
        if (overdrawn)
        {
            db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 3, 2), "Transfer from overdraft facility", 300m, ids["2200"]));
            db.Adjustments.Add(Journal(period, "Overdraft interest charged", ids["8100"], ids["2200"], 20m));
        }
        db.Adjustments.AddRange(
            Journal(period, "Leased equipment — due within one year", ids["0010"], ids["2210"], 300m),
            Journal(period, "Leased equipment — arrears", ids["0010"], ids["2215"], 100m),
            Journal(period, "Leased equipment — due after one year", ids["0010"], ids["2300"], 800m));
        await db.SaveChangesAsync();
        return new Fixture(company, period);
    }

    // The golden clg-charity scenario (generic categories) with charity-style rows added: cash in hand
    // 2,000 and members' funds 2,000 taken on; donations 3,000 and a restricted grant 250 received;
    // activity costs leave the bank 550 overdrawn; leased equipment 1,000 financed 300 within one year
    // and 700 after one year.
    private static async Task<AccountingPeriod> SeedCharityPdfScenarioAsync(AccountsDbContext db, ChartOfAccountsVariant variant)
    {
        var scenario = GoldenCorpusFixture.Scenario("clg-charity");
        var period = await FilingGoldenCorpusScenarioTests.SeedBasicScenarioAsync(db, scenario, finalise: false);
        var company = await db.Companies.SingleAsync(item => item.Id == period.CompanyId);
        company.ChartOfAccountsVariant = variant;
        var bank = await db.BankAccounts.SingleAsync(item => item.CompanyId == company.Id);
        var income = await db.AccountCategories.SingleAsync(item => item.CompanyId == company.Id && item.Code == "4000");
        var receipt = await db.ImportedTransactions.Where(item => item.PeriodId == period.Id).SumAsync(item => item.Amount);

        AccountCategory Row(string code, string name, AccountCategoryType type, AccountStatementLine line, AccountCashFlowClass cashFlow) =>
            new() { CompanyId = company.Id, Code = code, Name = name, Type = type, StatementLine = line, CashFlowClass = cashFlow };
        var cashInHand = Row("1230", "Cash in hand", AccountCategoryType.Asset, AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash);
        var leasedEquipment = Row("0010", "Leased equipment", AccountCategoryType.Asset, AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing);
        var leaseWithinYear = Row("2210", "Finance lease — due within one year", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan);
        var leaseAfterYear = Row("2300", "Finance lease — due after one year", AccountCategoryType.Liability, AccountStatementLine.OtherCreditorsAfterYear, AccountCashFlowClass.Loan);
        var membersFunds = Row("3000", "Members' funds brought forward", AccountCategoryType.Equity, AccountStatementLine.Reserves, AccountCashFlowClass.OtherFinancing);
        var restricted = Row("3200", "Restricted funds", AccountCategoryType.Equity, AccountStatementLine.RestrictedFunds, AccountCashFlowClass.OtherFinancing);
        var activities = Row("6000", "Charitable activities", AccountCategoryType.Expense, AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating);
        db.AccountCategories.AddRange(cashInHand, leasedEquipment, leaseWithinYear, leaseAfterYear, membersFunds, restricted, activities);
        await db.SaveChangesAsync();

        foreach (var (category, debit, credit) in new[] { (cashInHand, 2_000m, 0m), (membersFunds, 0m, 2_000m) })
        {
            db.OpeningBalances.Add(new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = category.Id,
                Debit = debit,
                Credit = credit,
                SourceNote = "Take-on",
                EnteredBy = "Reviewer",
                Reviewed = true,
                ReviewedBy = "Reviewer",
                ReviewedAt = ReviewedAt
            });
        }
        var date = scenario.CurrentYear.PeriodStart.AddMonths(3);
        db.ImportedTransactions.AddRange(
            Transaction(bank, period, date, "Donations received", 3_000m, income.Id),
            Transaction(bank, period, date, "Restricted grant", 250m, restricted.Id),
            Transaction(bank, period, date, "Charitable activity costs", -(receipt + 3_800m), activities.Id));
        db.Adjustments.AddRange(
            Journal(period, "Leased equipment — due within one year", leasedEquipment.Id, leaseWithinYear.Id, 300m),
            Journal(period, "Leased equipment — due after one year", leasedEquipment.Id, leaseAfterYear.Id, 700m));
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(company.Id, period.Id);
        return period;
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

    // The words of every page before the notes heading, without the repeated page header (the words
    // all pages start with) and the "n of m" footer.
    private static List<string> StatementWords(byte[] pdf, string croNumber)
    {
        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages()
            .Select(page => page.GetWords().Select(word => word.Text.Replace(croNumber, "<cro>", StringComparison.Ordinal)).ToList())
            .ToList();
        Assert.True(pages.Count > 1, "Expected a multi-page document.");
        var header = 0;
        while (pages.All(page => page.Count > header && page[header] == pages[0][header]))
            header++;
        var words = new List<string>();
        foreach (var page in pages)
        {
            var body = page.Skip(header).ToList();
            if (body.Count >= 3 && body[^2] == "of" && int.TryParse(body[^1], out _) && int.TryParse(body[^3], out _))
                body.RemoveRange(body.Count - 3, 3);
            var notes = body.FindIndex(word => word == "NOTES");
            if (notes >= 0 && body.Skip(notes).Take(5).SequenceEqual(["NOTES", "TO", "THE", "FINANCIAL", "STATEMENTS"]))
            {
                words.AddRange(body.Take(notes));
                return words;
            }
            words.AddRange(body);
        }
        Assert.Fail("No notes section.");
        return words;
    }

    private static string BalanceSheetPageText(string text)
    {
        var start = text.IndexOf("BALANCE SHEET as at", StringComparison.Ordinal);
        Assert.True(start >= 0, "No balance sheet page.");
        var end = text.IndexOf("The financial statements were approved", start, StringComparison.Ordinal);
        Assert.True(end > start, "No balance sheet approval line.");
        return text[start..end];
    }

    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.Append(' ').Append(string.Join(' ', page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"charity-c5-{Guid.NewGuid():N}").Options);
}
