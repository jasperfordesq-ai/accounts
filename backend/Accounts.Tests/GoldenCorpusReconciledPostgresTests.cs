using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

internal sealed record ReconciledGoldenCompany(string LegalName, string CroNumber, string TaxReference);

internal sealed record ReconciledGoldenPeriod(
    string Code,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TurnoverSizeInput,
    decimal BalanceSheetSizeInput,
    int AverageEmployees,
    decimal ClosingInventory,
    decimal CorporationTaxLiability,
    decimal CorporationTaxPaid,
    decimal DividendPaid);

internal sealed record ReconciledGoldenOpeningEquity(
    decimal ShareCapital,
    decimal RetainedEarnings,
    decimal Bank);

internal sealed record ReconciledGoldenFixedAsset(
    string Name,
    string Category,
    decimal Cost,
    decimal ResidualValue,
    DateOnly AcquisitionDate,
    DateOnly DisposalDate,
    decimal DisposalProceeds,
    int UsefulLifeYears);

internal sealed record ReconciledGoldenTransaction(
    string PeriodCode,
    DateOnly Date,
    string Description,
    decimal Amount,
    string CategoryCode);

internal sealed record ReconciledGoldenPayroll(
    decimal GrossWages,
    decimal DirectorsFees,
    decimal EmployerPrsi,
    decimal PensionContributions,
    int StaffCount);

internal sealed record ReconciledGoldenLoan(
    string Lender,
    decimal OriginalAmount,
    decimal ClosingBalance,
    DateOnly DrawdownDate,
    DateOnly BalanceAsOfDate,
    decimal InterestRate,
    decimal DueWithinYear,
    decimal DueAfterYear);

internal sealed record ReconciledGoldenAdjustment(
    string Description,
    string DebitCategoryCode,
    string CreditCategoryCode,
    decimal Amount,
    string Reason,
    string AccountingStandard);

internal sealed record ReconciledGoldenExpected(
    decimal PriorTurnover,
    decimal PriorCostOfSales,
    decimal PriorProfitAfterTax,
    decimal PriorStock,
    decimal PriorFixedAssets,
    decimal PriorCash,
    decimal PriorNetAssets,
    decimal CurrentTurnover,
    decimal CurrentPassiveIncome,
    decimal CurrentPayrollExpense,
    decimal CurrentCostOfSales,
    decimal CurrentLoanInterestAccrual,
    decimal CurrentInterestPayable,
    decimal CurrentProfitAfterTax,
    decimal CurrentStock,
    decimal CurrentFixedAssets,
    decimal CurrentCash,
    decimal CurrentLoanDueWithinYear,
    decimal CurrentLoanDueAfterYear,
    decimal CurrentNetAssets,
    decimal CurrentOpeningCash,
    decimal CurrentNetIncreaseInCash,
    decimal CurrentClosingCash,
    decimal CurrentTaxCharge,
    decimal CurrentTaxCreditors,
    decimal CurrentDividendPaid,
    decimal CurrentOpeningRetainedEarnings,
    decimal CurrentClosingRetainedEarnings);

internal sealed record ReconciledGoldenDocument(
    int SchemaVersion,
    string FixtureId,
    string ScenarioCode,
    string ExpectationDerivation,
    string IndependentReviewStatus,
    string ExternalValidationStatus,
    ReconciledGoldenCompany Company,
    IReadOnlyList<ReconciledGoldenPeriod> Periods,
    ReconciledGoldenOpeningEquity OpeningEquity,
    ReconciledGoldenFixedAsset FixedAsset,
    IReadOnlyList<ReconciledGoldenTransaction> Transactions,
    ReconciledGoldenPayroll Payroll,
    ReconciledGoldenLoan Loan,
    ReconciledGoldenAdjustment ManualAdjustment,
    ReconciledGoldenExpected Expected);

internal static class ReconciledGoldenFixture
{
    public const string RelativePath = "Fixtures/golden-corpus-reconciled-small-v1.json";
    public const string PinnedSha256 = "8ef6f0c8ab6009370a7a1e78d5a13f941fcfde169b2c852d687269bd076326fb";

    private static readonly Lazy<(byte[] Bytes, ReconciledGoldenDocument Document)> Loaded = new(Load);

    public static ReconciledGoldenDocument Document => Loaded.Value.Document;
    public static string ComputeSha256() =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Loaded.Value.Bytes));

    private static (byte[] Bytes, ReconciledGoldenDocument Document) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-corpus-reconciled-small-v1.json");
        var bytes = File.ReadAllBytes(path);
        var document = JsonSerializer.Deserialize<ReconciledGoldenDocument>(bytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Reconciled golden fixture {path} is empty or invalid.");
        return (bytes, document);
    }
}

public sealed class ReconciledGoldenFixtureIntegrityTests
{
    [Fact]
    public void ImmutableReconciledFixture_CoversEveryResidualBreadthFactAndKeepsHumanGatesOpen()
    {
        var fixture = ReconciledGoldenFixture.Document;

        Assert.Equal(ReconciledGoldenFixture.PinnedSha256, ReconciledGoldenFixture.ComputeSha256());
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("small-abridged-ltd", fixture.ScenarioCode);
        Assert.Equal("pending-qualified-accountant", fixture.IndependentReviewStatus);
        Assert.Equal("pending-real-ros-validator", fixture.ExternalValidationStatus);
        Assert.Contains("independently", fixture.ExpectationDerivation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["current", "prior"], fixture.Periods.Select(period => period.Code).Order(StringComparer.Ordinal));
        Assert.Contains(fixture.Transactions, row => row.CategoryCode == "4200" && row.Amount > 0);
        Assert.Contains(fixture.Transactions, row => row.CategoryCode is "6000" or "6010" or "6020");
        Assert.Contains(fixture.Transactions, row => row.CategoryCode is "2600" or "2700");
        Assert.Equal(fixture.OpeningEquity.Bank, fixture.OpeningEquity.ShareCapital + fixture.OpeningEquity.RetainedEarnings);
        Assert.Equal(fixture.Loan.ClosingBalance, fixture.Loan.DueWithinYear + fixture.Loan.DueAfterYear);
        Assert.Equal(
            decimal.Round(fixture.Loan.ClosingBalance * fixture.Loan.InterestRate / 100m, 2, MidpointRounding.AwayFromZero),
            fixture.Expected.CurrentLoanInterestAccrual);
        Assert.Equal(
            fixture.Payroll.GrossWages + fixture.Payroll.DirectorsFees + fixture.Payroll.EmployerPrsi + fixture.Payroll.PensionContributions,
            fixture.Expected.CurrentPayrollExpense);
    }
}

public sealed partial class GoldenCorpusPostgresReleaseTests
{
    [PostgresFact]
    public async Task ReconciledSmallAbridgedScenario_CoversTwoPeriodLedgerYearEndTaxNotesComparativesAndIxbrl()
    {
        var fixture = ReconciledGoldenFixture.Document;
        var priorInput = fixture.Periods.Single(period => period.Code == "prior");
        var currentInput = fixture.Periods.Single(period => period.Code == "current");
        var expected = fixture.Expected;
        await using var db = new AccountsDbContext(
            options ?? throw new InvalidOperationException("ACCOUNTS_POSTGRES_TEST_CONNECTION is required."));

        var tenant = new Tenant { Name = "Reconciled golden firm", Slug = $"reconciled-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = fixture.Company.LegalName,
            CroNumber = fixture.Company.CroNumber,
            TaxReference = fixture.Company.TaxReference,
            CompanyType = CompanyType.Private,
            IncorporationDate = priorInput.PeriodStart,
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            HasStock = true,
            OwnsAssets = true,
            IsEmployer = true,
            HasBorrowings = true,
            RegisteredOfficeAddress1 = "1 Reconciliation Street",
            RegisteredOfficeCity = "Galway",
            RegisteredOfficeCounty = "Galway"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.CompanyOfficers.AddRange(
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "Maeve Director",
                Role = OfficerRole.Director,
                AppointedDate = priorInput.PeriodStart
            },
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "Niall Secretary",
                Role = OfficerRole.Secretary,
                AppointedDate = priorInput.PeriodStart
            });
        var prior = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = priorInput.PeriodStart,
            PeriodEnd = priorInput.PeriodEnd,
            IsFirstYear = true
        };
        var current = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = currentInput.PeriodStart,
            PeriodEnd = currentInput.PeriodEnd,
            IsFirstYear = false
        };
        db.AccountingPeriods.AddRange(prior, current);
        await db.SaveChangesAsync();

        await FilingGoldenCorpusScenarioTests.SaveClassificationInputsThroughEndpointAsync(
            db,
            tenant.Id,
            company.Id,
            prior.Id,
            new GoldenCorpusYear(
                priorInput.PeriodStart,
                priorInput.PeriodEnd,
                priorInput.TurnoverSizeInput,
                priorInput.BalanceSheetSizeInput,
                priorInput.AverageEmployees));
        await FilingGoldenCorpusScenarioTests.SaveClassificationInputsThroughEndpointAsync(
            db,
            tenant.Id,
            company.Id,
            current.Id,
            new GoldenCorpusYear(
                currentInput.PeriodStart,
                currentInput.PeriodEnd,
                currentInput.TurnoverSizeInput,
                currentInput.BalanceSheetSizeInput,
                currentInput.AverageEmployees));
        var classifier = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));
        Assert.Equal(CompanySizeClass.Small, (await classifier.ClassifyAsync(company.Id, prior.Id)).CalculatedClass);
        Assert.Equal(CompanySizeClass.Small, (await classifier.ClassifyAsync(company.Id, current.Id)).CalculatedClass);
        var filing = await new FilingRegimeService(db).DetermineAsync(company.Id, current.Id, ElectedRegime.SmallAbridged);
        Assert.Equal(ElectedRegime.SmallAbridged, filing.Regime);

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        int Category(string code) => categories.Single(category => category.Code == code).Id;
        categories.Single(category => category.Code == "4200").IsNonTradingIncome = true;
        await db.SaveChangesAsync();

        var accountant = AuthenticatedContext(tenant.Id, "Accountant", HttpMethods.Post, "/api/golden/reconciled");
        var reviewer = AuthenticatedContext(tenant.Id, "Reviewer", HttpMethods.Post, "/api/golden/reconciled/review");
        var audit = new AuditService(db);
        var guard = new AccountingWriteGuard(db);

        AssertSuccessful(await YearEndEndpoints.CreateShareCapitalEndpointAsync(
            company.Id,
            new ShareCapital
            {
                ShareClass = "Ordinary",
                NumberIssued = 100,
                NominalValue = 1m,
                IssueDate = prior.PeriodStart
            },
            db,
            guard,
            audit,
            accountant));
        await UpsertOpeningBalanceAsync(db, audit, accountant, company.Id, prior.Id, Category("3000"), fixture.OpeningEquity.ShareCapital);
        await UpsertOpeningBalanceAsync(db, audit, accountant, company.Id, prior.Id, Category("3100"), fixture.OpeningEquity.RetainedEarnings);

        var bank = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Golden current account",
            OpeningBalance = fixture.OpeningEquity.Bank,
            OpeningBalanceDate = prior.PeriodStart
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        foreach (var row in fixture.Transactions)
        {
            var period = row.PeriodCode == "prior" ? prior : current;
            db.ImportedTransactions.Add(new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = row.Date,
                Description = row.Description,
                Amount = row.Amount,
                CategoryId = Category(row.CategoryCode)
            });
        }

        AssertSuccessful(await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            company.Id,
            new FixedAsset
            {
                Name = fixture.FixedAsset.Name,
                Category = fixture.FixedAsset.Category,
                Cost = fixture.FixedAsset.Cost,
                ResidualValue = fixture.FixedAsset.ResidualValue,
                AcquisitionDate = fixture.FixedAsset.AcquisitionDate,
                DisposalDate = fixture.FixedAsset.DisposalDate,
                DisposalProceeds = fixture.FixedAsset.DisposalProceeds,
                UsefulLifeYears = fixture.FixedAsset.UsefulLifeYears,
                DepreciationMethod = DepreciationMethod.StraightLine
            },
            db,
            guard,
            audit,
            accountant));

        foreach (var periodInput in fixture.Periods)
        {
            var period = periodInput.Code == "prior" ? prior : current;
            AssertSuccessful(await YearEndEndpoints.CreateInventoryEndpointAsync(
                company.Id,
                period.Id,
                new Inventory
                {
                    Description = "Closing finished goods",
                    Value = periodInput.ClosingInventory,
                    ValuationMethod = ValuationMethod.LowerOfCostAndNrv
                },
                db,
                audit,
                accountant));
            AssertSuccessful(await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
                company.Id,
                period.Id,
                TaxType.CorporationTax,
                new TaxBalance
                {
                    Liability = periodInput.CorporationTaxLiability,
                    Paid = periodInput.CorporationTaxPaid,
                    Balance = periodInput.CorporationTaxLiability - periodInput.CorporationTaxPaid
                },
                db,
                audit,
                accountant));
            AssertSuccessful(await YearEndEndpoints.CreateDividendEndpointAsync(
                company.Id,
                period.Id,
                new Dividend
                {
                    Amount = periodInput.DividendPaid,
                    DateDeclared = fixture.Transactions
                        .Single(row => row.PeriodCode == periodInput.Code && row.CategoryCode == "3200")
                        .Date.AddMonths(-1),
                    DatePaid = fixture.Transactions
                        .Single(row => row.PeriodCode == periodInput.Code && row.CategoryCode == "3200")
                        .Date
                },
                db,
                audit,
                accountant));
        }

        AssertSuccessful(await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            company.Id,
            current.Id,
            new PayrollSummary
            {
                GrossWages = fixture.Payroll.GrossWages,
                DirectorsFees = fixture.Payroll.DirectorsFees,
                EmployerPrsi = fixture.Payroll.EmployerPrsi,
                PensionContributions = fixture.Payroll.PensionContributions,
                StaffCount = fixture.Payroll.StaffCount
            },
            db,
            audit,
            accountant));
        AssertSuccessful(await YearEndEndpoints.CreateLoanEndpointAsync(
            company.Id,
            new Loan
            {
                Lender = fixture.Loan.Lender,
                OriginalAmount = fixture.Loan.OriginalAmount,
                Balance = fixture.Loan.ClosingBalance,
                DrawdownDate = fixture.Loan.DrawdownDate,
                BalanceAsOfDate = fixture.Loan.BalanceAsOfDate,
                InterestRate = fixture.Loan.InterestRate,
                DueWithinYear = fixture.Loan.DueWithinYear,
                DueAfterYear = fixture.Loan.DueAfterYear
            },
            db,
            guard,
            audit,
            accountant));

        var adjustment = fixture.ManualAdjustment;
        AssertSuccessful(await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            company.Id,
            current.Id,
            new AdjustmentInput(
                adjustment.Description,
                Category(adjustment.DebitCategoryCode),
                Category(adjustment.CreditCategoryCode),
                adjustment.Amount,
                adjustment.Reason,
                adjustment.AccountingStandard,
                ImpactOnProfit: 999_999m,
                ImpactOnAssets: 999_999m),
            db,
            audit,
            accountant,
            DisabledApiAccess()));
        var manualId = await db.Adjustments
            .Where(row => row.PeriodId == current.Id && !row.IsAuto)
            .Select(row => row.Id)
            .SingleAsync();
        reviewer.Request.Path = $"/api/companies/{company.Id}/periods/{current.Id}/adjustments/{manualId}/approve";
        AssertSuccessful(await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            company.Id,
            current.Id,
            manualId,
            db,
            audit,
            reviewer,
            DisabledApiAccess()));

        var adjustments = new AdjustmentService(db);
        await adjustments.GenerateAutoAdjustmentsAsync(company.Id, prior.Id);
        await adjustments.GenerateAutoAdjustmentsAsync(company.Id, current.Id);
        await FilingGoldenCorpusScenarioTests.ApproveAdjustmentsThroughEndpointAsync(db, tenant.Id, company.Id, prior.Id);
        await FilingGoldenCorpusScenarioTests.ApproveAdjustmentsThroughEndpointAsync(db, tenant.Id, company.Id, current.Id);

        var statements = new FinancialStatementsService(db);
        var priorPl = await statements.GetProfitAndLossAsync(company.Id, prior.Id);
        var priorBs = await statements.GetBalanceSheetAsync(company.Id, prior.Id);
        var currentPl = await statements.GetProfitAndLossAsync(company.Id, current.Id);
        var currentBs = await statements.GetBalanceSheetAsync(company.Id, current.Id);
        var currentCashFlow = await statements.GetCashFlowStatementAsync(company.Id, current.Id);
        var currentEquity = await statements.GetEquityChangesAsync(company.Id, current.Id);

        Assert.Equal(expected.PriorTurnover, priorPl.Turnover);
        Assert.Equal(expected.PriorCostOfSales, priorPl.CostOfSales);
        Assert.Equal(expected.PriorProfitAfterTax, priorPl.ProfitAfterTax);
        Assert.Equal(expected.PriorStock, priorBs.CurrentAssets.Stock);
        Assert.Equal(expected.PriorFixedAssets, priorBs.FixedAssets.Total);
        Assert.Equal(expected.PriorCash, priorBs.CurrentAssets.Cash);
        Assert.Equal(expected.PriorNetAssets, priorBs.NetAssets);
        Assert.True(priorBs.Balances);

        Assert.Equal(expected.CurrentTurnover, currentPl.Turnover);
        Assert.Equal(expected.CurrentPassiveIncome, currentPl.OtherIncome);
        Assert.Equal(expected.CurrentCostOfSales, currentPl.CostOfSales);
        Assert.Equal(expected.CurrentInterestPayable, currentPl.InterestPayable);
        Assert.Equal(expected.CurrentProfitAfterTax, currentPl.ProfitAfterTax);
        Assert.Equal(expected.CurrentTaxCharge, currentPl.TaxCharge);
        Assert.Equal(expected.CurrentStock, currentBs.CurrentAssets.Stock);
        Assert.Equal(expected.CurrentFixedAssets, currentBs.FixedAssets.Total);
        Assert.Equal(expected.CurrentCash, currentBs.CurrentAssets.Cash);
        Assert.Equal(expected.CurrentLoanDueWithinYear, currentBs.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(expected.CurrentLoanDueAfterYear, currentBs.CreditorsAfterYear.Loans);
        Assert.Equal(expected.CurrentTaxCreditors, currentBs.CreditorsWithinYear.TaxCreditors);
        Assert.Equal(expected.CurrentNetAssets, currentBs.NetAssets);
        Assert.True(currentBs.Balances);
        Assert.Equal(expected.CurrentOpeningCash, currentCashFlow.OpeningCash);
        Assert.Equal(expected.CurrentNetIncreaseInCash, currentCashFlow.NetIncreaseInCash);
        Assert.Equal(expected.CurrentClosingCash, currentCashFlow.ClosingCash);
        Assert.Equal(expected.CurrentDividendPaid, currentEquity.DividendsPaid);
        Assert.Equal(expected.CurrentOpeningRetainedEarnings, currentEquity.OpeningRetainedEarnings);
        Assert.Equal(expected.CurrentClosingRetainedEarnings, currentEquity.ClosingRetainedEarnings);

        var tax = new TaxComputationService(db, statements);
        var computation = await tax.SaveScopeReviewAsync(
            company.Id,
            current.Id,
            new TaxComputationService.CorporationTaxScopeReviewInput(
                IsCloseCompany: false,
                IsServiceCompany: null,
                HasGroupOrConsortiumRelief: false,
                HasChargeableGains: false,
                HasForeignIncomeOrTaxCredits: false,
                HasExceptedTrade: false,
                HasOtherReliefsOrSpecialRegimes: false,
                DeclaredPassiveIncomePresent: true,
                PassiveIncomeClassificationReviewed: true,
                LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                BroughtForwardTradingLoss: 0m,
                BroughtForwardLossEvidence: null,
                EvidenceNote: "Automated reconciled corpus scope input; not qualified-accountant acceptance."),
            "Automated reconciled corpus actor");
        Assert.Equal(expected.CurrentPassiveIncome, computation.PassiveNonTradingIncome);
        var ct1Support = await tax.GetCt1SupportDataAsync(company.Id, current.Id);
        Assert.Equal(expected.CurrentPayrollExpense, ct1Support.TotalEmployeeCosts);
        Assert.False(ct1Support.IsCompleteCt1Return);

        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(company.Id, current.Id);
        Assert.Contains(notes, note => note.Code == StatutoryNoteCodes.FixedAssets);
        Assert.Contains(notes, note => note.Code == StatutoryNoteCodes.Inventories);
        Assert.Contains(notes, note => note.Code == StatutoryNoteCodes.LongTermCreditors);
        Assert.Contains(notes, note => note.Code == StatutoryNoteCodes.Employees);
        Assert.Contains(notes, note => note.Code == StatutoryNoteCodes.ShareCapital);

        var ixbrlBytes = await new IxbrlService(db, statements).GenerateIxbrlAsync(company.Id, current.Id);
        var ixbrl = Encoding.UTF8.GetString(ixbrlBytes);
        Assert.Contains("contextRef=\"prior\"", ixbrl, StringComparison.Ordinal);
        Assert.Contains($">{expected.PriorTurnover:0}<", ixbrl, StringComparison.Ordinal);
        Assert.Contains($">{expected.CurrentTurnover:0}<", ixbrl, StringComparison.Ordinal);
        Assert.Contains("data-artifact-status=\"draft-not-for-filing\"", ixbrl, StringComparison.Ordinal);
        Assert.Equal(64, FilingReleaseGate.ComputeSha256(ixbrlBytes).Length);
        Assert.False(await db.RevenueFilingPackages.AnyAsync(row => row.PeriodId == current.Id));
    }

    private static async Task UpsertOpeningBalanceAsync(
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        int companyId,
        int periodId,
        int categoryId,
        decimal credit)
    {
        AssertSuccessful(await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            companyId,
            periodId,
            categoryId,
            new OpeningBalanceInput(
                Debit: 0m,
                Credit: credit,
                SourceNote: "Immutable reconciled corpus opening-equity input.",
                EnteredBy: "Ignored payload identity",
                Reviewed: true),
            db,
            audit,
            context));
    }

    private static DefaultHttpContext AuthenticatedContext(int tenantId, string role, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            701,
            tenantId,
            "Reconciled golden firm",
            "reconciled-fixture@example.invalid",
            "Automated reconciled corpus actor (not human acceptance)",
            role);
        return context;
    }

    private static ApiAccessService DisabledApiAccess() => new(
        Options.Create(new ApiAccessConfig { Enabled = false }),
        new ReconciledGoldenEnvironment());

    private static void AssertSuccessful(IResult result)
    {
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
            ?? StatusCodes.Status200OK;
        Assert.InRange(status, StatusCodes.Status200OK, 299);
    }

    private sealed class ReconciledGoldenEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
