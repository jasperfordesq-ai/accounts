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
    public async Task FilingRegime_MicroClassification_DefaultsToMicroRequirements()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        await db.SaveChangesAsync();

        await new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()))
            .ClassifyAsync(period.CompanyId, period.Id);
        var service = new FilingRegimeService(db);

        var result = await service.DetermineAsync(period.CompanyId, period.Id);

        Assert.Equal(ElectedRegime.Micro, result.Regime);
        Assert.True(result.CanUseMicro);
        Assert.Contains(result.RequiredStatements, s => s.Contains("s.280D"));
    }

    [Fact]
    public async Task FilingRegimeService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = otherPeriod.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        await db.SaveChangesAsync();
        var service = new FilingRegimeService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.DetermineAsync(period.CompanyId, otherPeriod.Id));

        Assert.Empty(await db.FilingRegimes.Where(f => f.PeriodId == otherPeriod.Id).ToListAsync());
    }

    [Fact]
    public async Task BalanceSheet_IncludesPayeAndRctTaxBalancesInCreditors()
    {
        // BL-32: PAYE/PRSI and RCT liabilities are creditors falling due within one year.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var payrollExpense = AddCategory(db, period.CompanyId, "6000", "Wages & Salaries", AccountCategoryType.Expense);
        var payrollTaxesPayable = AddCategory(db, period.CompanyId, "2300", "PAYE / PRSI / RCT Payable", AccountCategoryType.Liability);
        db.TaxBalances.AddRange(
            new TaxBalance { PeriodId = period.Id, TaxType = TaxType.Paye, Liability = 500m, Paid = 0m, Balance = 500m },
            new TaxBalance { PeriodId = period.Id, TaxType = TaxType.Rct, Liability = 300m, Paid = 0m, Balance = 300m });
        db.Adjustments.AddRange(
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Posted PAYE liability",
                DebitCategoryId = payrollExpense.Id,
                CreditCategoryId = payrollTaxesPayable.Id,
                Amount = 500m,
                ImpactOnProfit = -500m,
                Source = AdjustmentSource.Manual
            },
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Posted RCT liability",
                DebitCategoryId = payrollExpense.Id,
                CreditCategoryId = payrollTaxesPayable.Id,
                Amount = 300m,
                ImpactOnProfit = -300m,
                Source = AdjustmentSource.Manual
            });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(800m, bs.CreditorsWithinYear.TaxCreditors);
    }

    [Fact]
    public async Task BalanceSheet_DoesNotDoubleCountTaxCreditorAndTaxBalance()
    {
        // accounting-tax-creditor-double-count [HUMAN DECISION: TaxBalances is the single source of tax].
        // The same €125 CT liability is recorded BOTH as a TaxBalance and (redundantly) as a
        // Creditors.Type==Tax row. Tax owed is taken from TaxBalances only, so the tax-creditor line is
        // €125 (not €250) and the balance sheet still balances.
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
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 6, 1),
            Description = "Sales invoice INV001",
            Amount = 1_000m,
            CategoryId = Cat("4000")
        });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 125m,
            Paid = 0m,
            Balance = 125m
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Corporation tax payable",
            Amount = 125m,
            Type = CreditorType.Tax,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, period.Id);

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(companyId, period.Id);

        Assert.Equal(125m, bs.CreditorsWithinYear.TaxCreditors); // X, not 2X
        Assert.Equal(0m, bs.CapitalAndReserves.UnexplainedDifference);
        Assert.True(bs.Balances);
    }

    [Fact]
    public async Task PreFilingConsistency_PassesWhenConsistentAndReportsCorporationTaxDivergence()
    {
        // validation-pre-filing-consistency-pass: one consistency pass over the primary statements. A
        // consistent set returns no issues; a set whose entered CT diverges from the CT computation
        // reports a specific issue (reserves and share capital still tie between BS and SOCIE).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        db.ShareCapitals.Add(new ShareCapital { CompanyId = companyId, ShareClass = "Ordinary", NumberIssued = 100, NominalValue = 1m, TotalValue = 100m, IssueDate = period.PeriodStart });
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 100m, OpeningBalanceDate = period.PeriodStart };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = period.Id, AccountCategoryId = Cat("3000"), Credit = 100m, EnteredBy = "r", Reviewed = true });
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 6, 1), Description = "Sales invoice", Amount = 1_000m, CategoryId = Cat("4000") });
        await db.SaveChangesAsync();
        await SaveSupportedTaxScopeAsync(db, companyId, period.Id);

        var statements = new FinancialStatementsService(db);

        // Consistent set (no CT entered): no consistency issues.
        var consistent = await statements.GetPreFilingConsistencyIssuesAsync(companyId, period.Id);
        Assert.Empty(consistent);

        // Enter a CT that diverges from the computation -> a specific issue is reported.
        var computedCt = (await new TaxComputationService(db, statements).ComputeAsync(companyId, period.Id)).TotalCorporationTax;
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = computedCt + 250m, Paid = 0m, Balance = computedCt + 250m });
        await db.SaveChangesAsync();

        var divergent = await statements.GetPreFilingConsistencyIssuesAsync(companyId, period.Id);
        Assert.Contains(divergent, i => i.Contains("does not match the supported corporation tax computation"));
        // Reserves and share capital still tie between the balance sheet and SOCIE.
        Assert.DoesNotContain(divergent, i => i.Contains("Reserves disagree"));
        Assert.DoesNotContain(divergent, i => i.Contains("Share capital disagrees"));
    }

    [Fact]
    public async Task CharitySofa_ReconcilesToBalanceSheetNetAssets()
    {
        // filing-charity-pdf-and-reconciliation: the SoFA total closing funds must reconcile to the
        // balance-sheet net assets; a mismatch is surfaced as an unreconciled difference.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FundBalances.AddRange(
            new FundBalance { PeriodId = period.Id, FundName = "General", FundType = "Unrestricted", OpeningBalance = 1_000m, IncomingResources = 5_000m, ResourcesExpended = 1_000m, ClosingBalance = 5_000m },
            new FundBalance { PeriodId = period.Id, FundName = "Grant", FundType = "Restricted", OpeningBalance = 0m, IncomingResources = 2_000m, ResourcesExpended = 2_000m, ClosingBalance = 0m });
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);

        // Total closing funds = 5,000. Reconciles when net assets match.
        var matched = await service.ReconcileSofaToNetAssetsAsync(period.CompanyId, period.Id, 5_000m);
        Assert.True(matched.Reconciles);
        Assert.Equal(0m, matched.Difference);
        Assert.Equal(5_000m, matched.TotalClosingFunds);

        // A mismatch is surfaced and does not reconcile.
        var mismatched = await service.ReconcileSofaToNetAssetsAsync(period.CompanyId, period.Id, 4_200m);
        Assert.False(mismatched.Reconciles);
        Assert.Equal(800m, mismatched.Difference);
    }

    [Fact]
    public async Task AbridgedSmallCroPack_IncludesDirectorsReportButMicroDoesNot()
    {
        // filing-abridged-cro-directors-report: the SmallAbridged CRO pack includes the directors'
        // report (its doc-comment says so); the micro CRO pack does not.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var regime = new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.SmallAbridged, CanFileAbridged = true, AuditExempt = true };
        db.FilingRegimes.Add(regime);
        db.CompanyOfficers.Add(new CompanyOfficer
        {
            CompanyId = period.CompanyId,
            Name = "C Retired Director",
            Role = OfficerRole.Director,
            AppointedDate = new DateOnly(2024, 6, 1),
            ResignedDate = new DateOnly(2025, 6, 30)
        });
        db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = period.Id, NoteNumber = 1, Title = "Approval of Financial Statements", Content = "Approved by the directors.", IsRequired = true, IsIncluded = true });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));
        var abridged = ExtractPdfText(await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));
        Assert.Contains("DIRECTORS' REPORT", abridged);
        Assert.Contains("C Retired Director", abridged);
        Assert.Contains("appointed 01 June 2024", abridged);
        Assert.Contains("resigned 30 June 2025", abridged);

        // Micro CRO pack omits the directors' report.
        regime.ElectedRegime = ElectedRegime.Micro;
        regime.CanUseMicro = true;
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        var micro = ExtractPdfText(await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));
        Assert.DoesNotContain("DIRECTORS' REPORT", micro);
    }

    [Fact]
    public async Task ApprovalDate_PersistedAtFinalisationAndStampedOnSignaturePage()
    {
        // filing-approval-date-persisted: the board-approval date is persisted at finalisation and
        // stamped on the outputs, so regenerating later reproduces the same date (not DateTime.Now).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Micro, CanUseMicro = true, CanFileAbridged = true, AuditExempt = true });
        db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = period.Id, NoteNumber = 1, Title = "Approval of Financial Statements", Content = "Approved by the directors.", IsRequired = true, IsIncluded = true });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        period.ApprovalDate = null;
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var context = AuthenticatedRequest("Reviewer", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var boardDate = new DateOnly(2026, 1, 15);

        var result = await PeriodStatusEndpoint.UpdateAsync(
            period.CompanyId, period.Id,
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null, boardDate),
            db, new AuditService(db), statements, context, DisabledApiAccess());

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(boardDate, reloaded.ApprovalDate);

        // The persisted board-approval date is stamped on the signature page, not the render date.
        var documents = new DocumentGeneratorService(db, statements);
        var signaturePage = ExtractPdfText(await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id));
        Assert.Contains("15 January 2026", signaturePage);
        Assert.DoesNotContain(DateTime.Now.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture), signaturePage);
    }

    [Fact]
    public async Task Finalising_PersistsClosingReservesSnapshot()
    {
        // accounting-retained-earnings-snapshot: finalising a period captures its closing reserves so a
        // later period can read a fixed opening-reserves figure.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Micro, CanUseMicro = true, CanFileAbridged = true, AuditExempt = true });
        db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = period.Id, NoteNumber = 1, Title = "Approval of Financial Statements", Content = "Approved by the directors.", IsRequired = true, IsIncluded = true });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var bs = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.True(bs.Balances);

        var context = AuthenticatedRequest("Reviewer", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var result = await PeriodStatusEndpoint.UpdateAsync(
            period.CompanyId, period.Id,
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null),
            db, new AuditService(db), statements, context, DisabledApiAccess());

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(bs.CapitalAndReserves.RetainedEarnings, reloaded.ClosingRetainedEarnings);
    }

    [Fact]
    public async Task AdjustmentRegeneration_BlockedWhenALaterPeriodIsFinalisedOrFiled()
    {
        // accounting-depreciation-regeneration-order: regenerating an earlier period would drift the
        // depreciation/CA roll-forward of a later period that is already finalised or filed. Block it.
        await using var db = CreateDbContext();
        var period2024 = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period2024.CompanyId;
        // Re-date the seeded period to 2024 and add a later 2025 period.
        period2024.PeriodStart = new DateOnly(2024, 1, 1);
        period2024.PeriodEnd = new DateOnly(2024, 12, 31);
        var period2025 = new AccountingPeriod { CompanyId = companyId, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false, Status = PeriodStatus.Filed };
        db.AccountingPeriods.Add(period2025);
        await db.SaveChangesAsync();

        var service = new AdjustmentService(db);

        // Regenerating the earlier (2024) period is blocked while 2025 is filed.
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.GenerateAutoAdjustmentsAsync(companyId, period2024.Id));
        Assert.Contains("later period is already finalised or filed", error.Message);

        // Reopen the later period -> regenerating the earlier one is allowed again.
        period2025.Status = PeriodStatus.Draft;
        await db.SaveChangesAsync();
        var summary = await service.GenerateAutoAdjustmentsAsync(companyId, period2024.Id);
        Assert.NotNull(summary);
    }

    [Fact]
    public async Task IxbrlReviewPrototype_RetainsRevenuePrivateProfitAndLossForEveryRegimeAndIsConspicuouslyDraft()
    {
        // CRO presentation exemptions never remove Revenue-required private filing data. Until the
        // full Revenue tagging contract exists, every generated XHTML remains an unmistakable review
        // prototype and every regime retains the P&L facts needed for external manual completion.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Micro, CanUseMicro = true, AuditExempt = true });
        await db.SaveChangesAsync();
        var service = new IxbrlService(db, new FinancialStatementsService(db));

        var microIxbrl = Encoding.UTF8.GetString(await service.GenerateIxbrlAsync(companyId, period.Id));
        Assert.Contains("Profit and Loss Account", microIxbrl);
        Assert.Contains("core:TurnoverGrossRevenue", microIxbrl);
        Assert.Contains("data-artifact-status=\"draft-not-for-filing\"", microIxbrl);
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", microIxbrl);
        Assert.Contains("DRAFT - NOT FOR FILING - INCOMPLETE REVIEW PROTOTYPE", microIxbrl);
        Assert.Contains("Balance Sheet", microIxbrl);
        AssertWellFormedXml(microIxbrl);

        // Small -> the P&L is published.
        var regime = await db.FilingRegimes.SingleAsync(r => r.PeriodId == period.Id);
        regime.ElectedRegime = ElectedRegime.Small;
        await db.SaveChangesAsync();
        var smallIxbrl = Encoding.UTF8.GetString(await service.GenerateIxbrlAsync(companyId, period.Id));
        Assert.Contains("Profit and Loss Account", smallIxbrl);
        Assert.Contains("core:TurnoverGrossRevenue", smallIxbrl);

        // SmallAbridged is also private Revenue data, not the reduced CRO public presentation.
        regime.ElectedRegime = ElectedRegime.SmallAbridged;
        await db.SaveChangesAsync();
        var abridgedIxbrl = Encoding.UTF8.GetString(await service.GenerateIxbrlAsync(companyId, period.Id));
        Assert.Contains("Profit and Loss Account", abridgedIxbrl);
        Assert.Contains("core:TurnoverGrossRevenue", abridgedIxbrl);
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", abridgedIxbrl);
        AssertWellFormedXml(abridgedIxbrl);
    }

    [Fact]
    public async Task Ixbrl_SubtotalsCrossAddFromRoundedComponents()
    {
        // accounting-ixbrl-rounding-subtotals: tagged subtotals must equal the sum of their rounded
        // components. With Stock 0.40 and Cash 0.40, independent rounding gives Stock=0, Cash=0 but a
        // separately-rounded Total current assets of round(0.80)=1 — a ROS/CRO calc-check reject.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Stock", Value = 0.40m, ValuationMethod = ValuationMethod.Cost });
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 6, 1), Description = "Receipt", Amount = 0.40m, CategoryId = Cat("4000") });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(companyId, period.Id));

        int Fact(string concept)
        {
            var m = Regex.Match(ixbrl, $"name=\"{Regex.Escape(concept)}\" contextRef=\"instant\"[^>]*>(-?\\d+)<");
            Assert.True(m.Success, $"iXBRL fact {concept} not found");
            return int.Parse(m.Groups[1].Value);
        }

        // The Total current assets subtotal cross-adds with its rounded components.
        Assert.Equal(Fact("core:Stocks") + Fact("core:Debtors") + Fact("core:CashBankInHand"), Fact("core:CurrentAssets"));
    }

    [Fact]
    public async Task Readiness_WarnsWhenEnteredVatDoesNotReconcileToControlAccounts()
    {
        // accounting-vat-paye-reconciliation: an entered VAT figure must reconcile to the VAT control
        // accounts (output VAT on 2200 less input VAT on 1300). Readiness warns (blocks final outputs)
        // when they diverge, and the warning clears once the entered figure matches the source.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        // Output VAT collected: credit VAT Payable (2200) by 230.
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 6, 1), Description = "VAT on sales", Amount = 230m, CategoryId = Cat("2200") });
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.Vat, Liability = 230m, Paid = 0m, Balance = 230m });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var reconciled = await statements.GetReadinessScoreAsync(companyId, period.Id);
        Assert.DoesNotContain(reconciled.Warnings, w => w.Contains("does not reconcile to the VAT control accounts"));

        // Diverge the entered VAT from the source -> warning fires.
        var vatBalance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id && t.TaxType == TaxType.Vat);
        vatBalance.Liability = 500m;
        vatBalance.Balance = 500m;
        await db.SaveChangesAsync();
        var divergent = await statements.GetReadinessScoreAsync(companyId, period.Id);
        Assert.Contains(divergent.Warnings, w => w.Contains("does not reconcile to the VAT control accounts"));
    }

    [Fact]
    public async Task Readiness_WarnsWhenEnteredCorporationTaxDivergesFromComputation()
    {
        // accounting-pl-tax-charge-unreconciled: the entered CT liability is the P&L tax charge but was
        // never reconciled to the CT computation. Readiness must warn (which blocks final outputs) when
        // they diverge by more than €1, and the warning must clear once the entered figure matches.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
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
        await SaveSupportedTaxScopeAsync(db, companyId, period.Id);

        var statements = new FinancialStatementsService(db);
        var computedCt = (await new TaxComputationService(db, statements).ComputeAsync(companyId, period.Id)).TotalCorporationTax;

        // Enter a CT liability that diverges from the computation by > €1.
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = computedCt + 100m,
            Paid = 0m,
            Balance = computedCt + 100m
        });
        await db.SaveChangesAsync();

        var divergent = await statements.GetReadinessScoreAsync(companyId, period.Id);
        Assert.Contains(divergent.Warnings, w => w.Contains("does not match the supported corporation tax computation"));

        // Correct the entered figure to match the computation — the warning clears.
        var ct = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id && t.TaxType == TaxType.CorporationTax);
        ct.Liability = computedCt;
        ct.Balance = computedCt;
        await db.SaveChangesAsync();

        var reconciled = await statements.GetReadinessScoreAsync(companyId, period.Id);
        Assert.DoesNotContain(reconciled.Warnings, w => w.Contains("does not match the supported corporation tax computation"));
    }

    [Fact]
    public async Task BalanceSheet_NoShareCapital_HasNoPlugAndBlocksReadinessExceptForCLG()
    {
        // accounting-share-capital-and-dividends-reserves: a company with no recorded share capital must
        // report €0 (not a fabricated €1 plug) and be blocked at readiness — unless it is a company
        // limited by guarantee, which legitimately has no share capital.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true); // CompanyType.Private by default
        var statements = new FinancialStatementsService(db);

        var bs = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(0m, bs.CapitalAndReserves.ShareCapital); // no €1 plug

        var readiness = await statements.GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.Contains("Share capital not recorded", readiness.MissingItems);

        var company = await db.Companies.FirstAsync(c => c.Id == period.CompanyId);
        company.CompanyType = CompanyType.CompanyLimitedByGuarantee;
        await db.SaveChangesAsync();
        var clgReadiness = await statements.GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain("Share capital not recorded", clgReadiness.MissingItems);
    }

    [Fact]
    public async Task CashFlow_ClosingCashTiesToBalanceSheetCashAcrossYears()
    {
        // accounting-cashflow-vs-bs-cash-tie: the cash-flow closing cash must reconcile to the balance
        // sheet cash. For year-2+ the cash-flow opening cash now carries forward prior years' movement
        // (multi-account), so closing cash ties to the balance-sheet cash for a cash-consistent set.
        await using var db = CreateDbContext();
        var period2025 = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        var companyId = period2025.CompanyId;
        var period2024 = new AccountingPeriod { CompanyId = companyId, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true };
        db.AccountingPeriods.Add(period2024);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        // Two bank accounts to exercise multi-account aggregation.
        var bankA = new BankAccount { CompanyId = companyId, Name = "Account A", OpeningBalance = 0m };
        var bankB = new BankAccount { CompanyId = companyId, Name = "Account B", OpeningBalance = 0m };
        db.BankAccounts.AddRange(bankA, bankB);
        await db.SaveChangesAsync();

        void AddTxn(BankAccount bank, int periodId, DateOnly date, decimal amount, string code) =>
            db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = periodId, Date = date, Description = "txn", Amount = amount, CategoryId = Cat(code) });

        AddTxn(bankA, period2024.Id, new DateOnly(2024, 6, 1), 2_000m, "4000");
        AddTxn(bankB, period2024.Id, new DateOnly(2024, 7, 1), -400m, "6500");
        AddTxn(bankA, period2025.Id, new DateOnly(2025, 6, 1), 1_500m, "4000");
        AddTxn(bankB, period2025.Id, new DateOnly(2025, 7, 1), -300m, "6500");
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bs2025 = await statements.GetBalanceSheetAsync(companyId, period2025.Id);
        var cf2025 = await statements.GetCashFlowStatementAsync(companyId, period2025.Id);

        // Opening cash carries forward 2024's net movement (2,000 - 400 = 1,600).
        Assert.Equal(1_600m, cf2025.OpeningCash);
        // Closing cash ties to the balance-sheet cash (1,600 + 1,500 - 300 = 2,800).
        Assert.Equal(2_800m, bs2025.CurrentAssets.Cash);
        Assert.Equal(bs2025.CurrentAssets.Cash, cf2025.ClosingCash);
    }

    [Fact]
    public async Task Readiness_MultiYearPeriodBalancesWithoutManualOpeningRows()
    {
        // tests-multiyear-balance-asserted: a year-2 period with NO manual opening rows must be seen as
        // balanced by the readiness gate (BalanceSheetBalances == true, no "does not balance" warning)
        // and have UnexplainedDifference == 0 — this fails on the pre-movement-basis code.
        await using var db = CreateDbContext();
        var period2025 = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        var companyId = period2025.CompanyId;
        var period2024 = new AccountingPeriod { CompanyId = companyId, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true };
        db.AccountingPeriods.Add(period2024);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period2024.Id, Date = new DateOnly(2024, 6, 1), Description = "2024 sales", Amount = 3_000m, CategoryId = Cat("4000") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period2024.Id, Date = new DateOnly(2024, 7, 1), Description = "2024 costs", Amount = -800m, CategoryId = Cat("6500") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period2025.Id, Date = new DateOnly(2025, 6, 1), Description = "2025 sales", Amount = 2_500m, CategoryId = Cat("4000") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period2025.Id, Date = new DateOnly(2025, 7, 1), Description = "2025 costs", Amount = -600m, CategoryId = Cat("6500") });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bs2025 = await statements.GetBalanceSheetAsync(companyId, period2025.Id);
        var readiness2025 = await statements.GetReadinessScoreAsync(companyId, period2025.Id);

        Assert.Equal(0m, bs2025.CapitalAndReserves.UnexplainedDifference);
        Assert.True(bs2025.Balances);
        Assert.True(readiness2025.BalanceSheetBalances);
        Assert.DoesNotContain(readiness2025.Warnings, w => w.Contains("Balance sheet does not balance"));
    }

    [Fact]
    public async Task ProfitAndLoss_TreatsCapexAsCapitalNotRevenueExpense()
    {
        // BL-32: a fixed-asset purchase coded to an asset account is capital — it does not reduce
        // profit; only the depreciation charge does.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var cats = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);
        int Cat(string code) => cats.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 2, 1), Description = "Sales", Amount = 10_000m, CategoryId = Cat("4000") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Laptop purchase", Amount = -2_000m, CategoryId = Cat("0050") });
        await db.SaveChangesAsync();

        var pl = await new FinancialStatementsService(db).GetProfitAndLossAsync(period.CompanyId, period.Id);
        Assert.Equal(10_000m, pl.Turnover);
        Assert.Equal(0m, pl.TotalOverheads);
        Assert.Equal(10_000m, pl.OperatingProfit);
    }

    [Fact]
    public async Task TaxComputation_SurfacesTradingLossInsteadOfDiscardingIt()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCat = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var expenseCat = AddCategory(db, period.CompanyId, "6000", "Office costs", AccountCategoryType.Expense);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Loss-making period: 1,000 income vs 5,000 expense => 4,000 trading loss.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Sale", Amount = 1_000m, CategoryId = salesCat.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Office cost", Amount = -5_000m, CategoryId = expenseCat.Id });
        await db.SaveChangesAsync();

        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);

        Assert.Equal(0m, tax.TaxableProfit);
        Assert.Equal(0m, tax.TotalCorporationTax);
        Assert.Equal(4_000m, tax.TradingLossAvailable);
        Assert.Contains("carry forward", tax.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaxComputation_AppliesTwentyFivePercentToNonTradingIncome()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var trading = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var rental = AddCategory(db, period.CompanyId, "4500", "Rental income", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // 10,000 trading income (12.5%) + 4,000 non-trading rental income (25%).
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Trading sales", Amount = 10_000m, CategoryId = trading.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Rent received", Amount = 4_000m, CategoryId = rental.Id });
        await db.SaveChangesAsync();

        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);

        Assert.Equal(14_000m, tax.TaxableProfit);
        Assert.Equal(1_250m, tax.CorporationTaxAt125); // 10,000 trading @ 12.5%
        Assert.Equal(1_000m, tax.CorporationTaxAt25);  // 4,000 non-trading @ 25% (s.21A TCA 1997)
        Assert.Equal(2_250m, tax.TotalCorporationTax);
    }

    [Fact]
    public async Task TaxComputation_TradingLossDoesNotShelterNonTradingIncomeFrom25Percent()
    {
        // BL-04: a trading loss must not silently absorb passive (Case III/V) income. Absent an
        // elected s.396A claim, the non-trading income is charged at 25% in full and the trading
        // loss is surfaced for carry-forward — taxing in the high-stakes (do-not-under-tax) direction.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var trading = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var expense = AddCategory(db, period.CompanyId, "6000", "Office costs", AccountCategoryType.Expense);
        var rental = AddCategory(db, period.CompanyId, "4500", "Rental income", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Trading: 1,000 sales vs 11,000 costs => 10,000 trading loss.
        // Non-trading: 4,000 rental profit, which must remain taxable at 25%.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Trading sales", Amount = 1_000m, CategoryId = trading.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Office costs", Amount = -11_000m, CategoryId = expense.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 3), Description = "Rent received", Amount = 4_000m, CategoryId = rental.Id });
        await db.SaveChangesAsync();

        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);

        // The 4,000 of passive income is taxed at 25% even though the trade is loss-making.
        Assert.Equal(1_000m, tax.CorporationTaxAt25);
        Assert.Equal(0m, tax.CorporationTaxAt125);
        Assert.Equal(1_000m, tax.TotalCorporationTax);
        // The full 10,000 trading loss is carried forward — the rental does not reduce it.
        Assert.Equal(10_000m, tax.TradingLossAvailable);
        // Only the non-trading income is taxable this period.
        Assert.Equal(4_000m, tax.TaxableProfit);
        Assert.Contains("25%", tax.Notes);
        Assert.Contains("carry forward", tax.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProfitAndLoss_IncludesNonTurnoverIncomeAsOtherIncomeAndTaxesIt()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var sales = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var rental = AddCategory(db, period.CompanyId, "8000", "Rental income", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Trading sales (4xxx turnover) plus rental income coded outside turnover (8xxx).
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Trading sales", Amount = 10_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Rent received", Amount = 4_000m, CategoryId = rental.Id });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var pl = await statements.GetProfitAndLossAsync(period.CompanyId, period.Id);

        Assert.Equal(10_000m, pl.Turnover);        // only 4xxx counts as turnover
        Assert.Equal(4_000m, pl.OtherIncome);      // 8xxx rental is other income, not dropped
        Assert.Equal(14_000m, pl.ProfitBeforeTax); // both reach profit before tax

        // The non-turnover, non-trading income is taxed at 25%, the trading balance at 12.5%.
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        Assert.Equal(14_000m, tax.TaxableProfit);
        Assert.Equal(1_250m, tax.CorporationTaxAt125);
        Assert.Equal(1_000m, tax.CorporationTaxAt25);
    }

    [Fact]
    public async Task Statements_UsePostedLoanLedgerWithSnapshotsAsSupportingEvidence()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);
        int Cat(string code) => categories.Single(category => category.Code == code).Id;
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 70m,
            OpeningBalanceDate = period.PeriodStart
        };
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Current Bank",
            OriginalAmount = 100m,
            Balance = 60m,
            DueWithinYear = 15m,
            DueAfterYear = 45m,
            DrawdownDate = period.PeriodStart.AddYears(-1),
            BalanceAsOfDate = period.PeriodEnd
        };
        db.Loans.Add(loan);
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        db.OpeningBalances.AddRange(
            new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = Cat("2600"),
                Credit = 15m,
                SourceNote = "Reviewed current loan take-on",
                EnteredBy = "Accounts reviewer",
                Reviewed = true
            },
            new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = Cat("2700"),
                Credit = 55m,
                SourceNote = "Reviewed long-term loan take-on",
                EnteredBy = "Accounts reviewer",
                Reviewed = true
            });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddMonths(3),
            Description = "Loan repayment",
            Amount = -10m,
            CategoryId = Cat("2700")
        });

        var snapshotType = Type.GetType("Accounts.Api.Entities.LoanBalanceSnapshot, Accounts.Api")
            ?? throw new InvalidOperationException("LoanBalanceSnapshot is required for period-specific loan cash flow.");
        var snapshot = Activator.CreateInstance(snapshotType)!;
        SetRequiredValue(snapshot, "LoanId", loan.Id);
        SetRequiredValue(snapshot, "PeriodId", period.Id);
        SetRequiredValue(snapshot, "OpeningBalance", 70m);
        SetRequiredValue(snapshot, "Drawdowns", 0m);
        SetRequiredValue(snapshot, "Repayments", 10m);
        SetRequiredValue(snapshot, "ClosingBalance", 60m);
        SetRequiredValue(snapshot, "DueWithinYear", 15m);
        SetRequiredValue(snapshot, "DueAfterYear", 45m);
        db.Add(snapshot);
        await db.SaveChangesAsync();
        Assert.Equal(15m, await db.LoanBalanceSnapshots
            .Where(s => s.PeriodId == period.Id && s.Loan.CompanyId == period.CompanyId)
            .SumAsync(s => s.DueWithinYear));

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await service.GetCashFlowStatementAsync(period.CompanyId, period.Id);

        Assert.Equal(15m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(45m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(10m, cashFlow.LoanRepayments);
    }

    [Fact]
    public async Task Readiness_UsesActualBalanceSheetEquation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 0m,
            BalanceSheetTotal = 100m,
            AvgEmployees = 0
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = bankCategory.Id,
            Debit = 100m,
            SourceNote = "Deliberately unmatched opening cash for balance-equation test",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.False(readiness.BalanceSheetBalances);
        Assert.Contains(readiness.Warnings, w => w.Contains("Balance sheet does not balance"));
    }

    [Fact]
    public async Task Readiness_RequiresGoingConcernAssessmentAndReviewedNotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.GoingConcernConfirmed = false;
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 0m,
            BalanceSheetTotal = 1m,
            AvgEmployees = 0
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.Contains("Going concern assessment not completed", readiness.MissingItems);
        Assert.Contains("Notes to the financial statements not generated or reviewed", readiness.MissingItems);
    }

    [Fact]
    public async Task Readiness_RequiresExplicitReviewOfNilYearEndSections()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.Contains("Debtors and other receivables not reviewed", readiness.MissingItems);
        Assert.Contains("Creditors, accruals and payables not reviewed", readiness.MissingItems);
        Assert.Contains("Payroll and staff status not confirmed", readiness.MissingItems);
        Assert.Contains("Dividends not reviewed", readiness.MissingItems);
        Assert.Contains("Post balance sheet events, related parties, or contingencies not reviewed", readiness.MissingItems);
        Assert.Contains("Going concern assessment not completed", readiness.MissingItems);
    }

    [Fact]
    public async Task Readiness_AcceptsExplicitNilReviewConfirmations()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.DoesNotContain("Debtors and other receivables not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Creditors, accruals and payables not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Payroll and staff status not confirmed", readiness.MissingItems);
        Assert.DoesNotContain("Tax balances not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Dividends not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Post balance sheet events, related parties, or contingencies not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Going concern assessment not completed", readiness.MissingItems);
    }

    [Fact]
    public async Task MicroStatutoryPack_IncludesProfitAndLossWhileReducedCroCopyOmitsIt()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IncorporationDate = new DateOnly(2024, 1, 1);
        period.IsFirstYear = false;
        var priorPeriod = new AccountingPeriod
        {
            CompanyId = period.CompanyId,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(priorPeriod);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var bank = await db.BankAccounts.SingleAsync(b => b.CompanyId == period.CompanyId);
        var sales = await db.AccountCategories.SingleAsync(c => c.CompanyId == period.CompanyId && c.Code == "4000");
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = priorPeriod.Id,
            Date = new DateOnly(2024, 3, 1),
            Description = "Prior-year customer receipt",
            Amount = 75m,
            CategoryId = sales.Id
        });
        await db.SaveChangesAsync();
        await SaveSupportedTaxScopeAsync(db, period.CompanyId, period.Id);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var approvalPack = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var agmPack = await documents.GenerateAgmApprovalPackAsync(period.CompanyId, period.Id);
        var croPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);

        Assert.NotEmpty(approvalPack);
        Assert.NotEmpty(agmPack);
        Assert.NotEmpty(croPack);
        Assert.NotEqual(approvalPack.Length, croPack.Length);

        var approvalText = ExtractPdfText(approvalPack);
        var agmText = ExtractPdfText(agmPack);
        var croText = ExtractPdfText(croPack);
        Assert.Contains("PROFIT AND LOSS ACCOUNT", approvalText);
        Assert.Contains("PROFIT AND LOSS ACCOUNT", agmText);
        Assert.DoesNotContain("PROFIT AND LOSS ACCOUNT", croText);
        Assert.Contains("2024", approvalText);
        Assert.Contains("2024", agmText);
        Assert.Contains("75", approvalText);
        Assert.Contains("75", agmText);
        Assert.Contains("FINANCIAL STATEMENTS", approvalText);
        Assert.Contains("CRO FILING PACK - MICRO COMPANY", croText);
    }

    [Fact]
    public async Task AbridgedSmallCroPack_PdfContainsSection352WordingNameAndPeriodEnd()
    {
        // tests-pdf-content-verified (abridged branch): a SmallAbridged CRO filing pack must carry the
        // s.352 abridged-filing exemption wording, the company legal name and the period-end date.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.SmallAbridged,
            CanUseMicro = false,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var croPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);

        var pdfText = ExtractPdfText(croPack);
        Assert.Contains("Example Micro Limited", pdfText);
        Assert.Contains(period.PeriodEnd.ToString("dd MMMM yyyy"), pdfText);
        Assert.Contains("352", pdfText); // s.352 Companies Act 2014 abridged-filing exemption
    }

    [Fact]
    public async Task MediumAccountsPdf_RendersExtraStatementsAndIsLargerThanSmall()
    {
        // BL-18: the Medium PDF actually generates past the readiness gate (so the new section
        // composers don't throw) and, carrying the extra statements, is larger than the small PDF
        // for the same underlying data.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var regime = new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Medium,
            CanUseMicro = false,
            CanFileAbridged = false,
            AuditExempt = false
        };
        db.FilingRegimes.Add(regime);
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        // A non-audit-exempt entity must attach a signed auditor's report before final outputs generate.
        AttachCompleteAuditorReportEvidence(period, "AUD-MEDIUM-2025-001");
        await db.SaveChangesAsync();

        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));

        var mediumPdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        Assert.NotEmpty(mediumPdf);

        // Regenerate the same data as a small audit-exempt pack (no cash flow, SOCIE or auditor's report).
        regime.ElectedRegime = ElectedRegime.Small;
        regime.AuditExempt = true;
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        var smallPdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        Assert.NotEmpty(smallPdf);

        Assert.True(mediumPdf.Length > smallPdf.Length,
            $"Expected the Medium pack ({mediumPdf.Length} bytes) to be larger than the small pack ({smallPdf.Length} bytes) because it carries the cash flow, equity and auditor's report sections.");
    }

    [Fact]
    public async Task Notes_MediumRegimeAddsFullerDisclosureSetBeyondSmall()
    {
        // BL-13: Medium/Full regimes require notes a small company does not — turnover analysis, tax
        // on profit, financial instruments and capital commitments — rendered even when nil.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Medium,
            CanUseMicro = false,
            CanFileAbridged = false,
            AuditExempt = false
        });
        await db.SaveChangesAsync();

        var mediumNotes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        foreach (var title in new[] { "Turnover", "Tax on Profit on Ordinary Activities", "Financial Instruments", "Capital Commitments" })
            Assert.Contains(mediumNotes, n => n.Title == title);

        // The same company on the small regime does not get the Medium/Full-only notes.
        var regime = await db.FilingRegimes.SingleAsync(r => r.PeriodId == period.Id);
        regime.ElectedRegime = ElectedRegime.Small;
        await db.SaveChangesAsync();
        var smallNotes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(smallNotes, n => n.Title == "Financial Instruments");
        Assert.DoesNotContain(smallNotes, n => n.Title == "Capital Commitments");
    }

    [Fact]
    public void FullApprovalPacks_IncludeProfitAndLossForMicroAndSmallAbridgedButReducedCroCopiesDoNot()
    {
        Assert.True(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.SmallAbridged, DocumentPackagePurpose.StatutoryApproval));
        Assert.True(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.SmallAbridged, DocumentPackagePurpose.AgmApproval));
        Assert.False(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.SmallAbridged, DocumentPackagePurpose.CroFiling));
        Assert.True(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.Micro, DocumentPackagePurpose.StatutoryApproval));
        Assert.True(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.Micro, DocumentPackagePurpose.AgmApproval));
        Assert.False(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.Micro, DocumentPackagePurpose.CroFiling));

        var approvalSubtitle = DocumentGeneratorService.PackageRegimeSubtitle(ElectedRegime.SmallAbridged, DocumentPackagePurpose.AgmApproval);
        var croSubtitle = DocumentGeneratorService.PackageRegimeSubtitle(ElectedRegime.SmallAbridged, DocumentPackagePurpose.CroFiling);

        Assert.DoesNotContain("Abridged", approvalSubtitle);
        Assert.DoesNotContain("CRO", approvalSubtitle);
        Assert.Contains("FRS 102 Section 1A", approvalSubtitle);
        Assert.Contains("Full statutory accounts", approvalSubtitle);
        Assert.Contains("Abridged", croSubtitle);
        Assert.Contains("CRO", croSubtitle);
        Assert.Contains("derived from", croSubtitle);

        var microApprovalSubtitle = DocumentGeneratorService.PackageRegimeSubtitle(ElectedRegime.Micro, DocumentPackagePurpose.StatutoryApproval);
        var microCroSubtitle = DocumentGeneratorService.PackageRegimeSubtitle(ElectedRegime.Micro, DocumentPackagePurpose.CroFiling);
        Assert.Contains("Full statutory accounts", microApprovalSubtitle);
        Assert.Contains("FRS 105", microApprovalSubtitle);
        Assert.Contains("Reduced CRO filing copy", microCroSubtitle);
        Assert.Contains("FRS 105", microCroSubtitle);
    }

    [Fact]
    public async Task CroFilingPack_RequiresConfirmedFilingRegime()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));

        Assert.Contains("Confirm the filing regime", error.Message);
    }

    [Fact]
    public async Task CroFilingPack_BlocksWhenReadinessItemsRemainOpen()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));

        Assert.Contains("Cannot generate final CRO filing pack", error.Message);
        // €1 share-capital plug removed: an empty company's balance sheet now correctly balances at 0,
        // so assert a still-guaranteed open blocker instead (size classification is the first one).
        Assert.Contains("Size classification not completed", error.Message);
    }

    [Fact]
    public async Task SignaturePage_BlocksWhenReadinessItemsRemainOpen()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateSignaturePageAsync(period.CompanyId, period.Id));

        Assert.Contains("Cannot generate final CRO signature page", error.Message);
        // €1 share-capital plug removed: an empty company's balance sheet now correctly balances at 0,
        // so assert a still-guaranteed open blocker instead (size classification is the first one).
        Assert.Contains("Size classification not completed", error.Message);
    }

    [Fact]
    public async Task SignaturePage_RejectsResignedOfficerSignatories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var officers = await db.CompanyOfficers
            .Where(o => o.CompanyId == period.CompanyId)
            .ToListAsync();
        foreach (var officer in officers)
            officer.ResignedDate = period.PeriodStart.AddDays(-1);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateSignaturePageAsync(period.CompanyId, period.Id));

        Assert.Contains("active director", error.Message);
    }

    [Theory]
    [InlineData("accounts package")]
    [InlineData("AGM approval pack")]
    public async Task FinalApprovalPacks_BlockWhenReadinessItemsRemainOpen(string packageName)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = packageName == "accounts package"
            ? await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id))
            : await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAgmApprovalPackAsync(period.CompanyId, period.Id));

        Assert.Contains($"Cannot generate final {packageName}", error.Message);
        // €1 share-capital plug removed: an empty company's balance sheet now correctly balances at 0,
        // so assert a still-guaranteed open blocker instead (size classification is the first one).
        Assert.Contains("Size classification not completed", error.Message);
        Assert.Contains("No transactions imported", error.Message);
    }

    [Fact]
    public async Task ReviewAccountsPackage_AllowsIncompleteSafeTestPeriodButRemainsMarkedDraft()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));

        var reviewPdf = await documents.GenerateAccountsReviewPackageAsync(period.CompanyId, period.Id);
        var reviewText = ExtractPdfText(reviewPdf);

        Assert.Contains("DRAFT", reviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT FOR FILING", reviewText, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));
    }

    [Fact]
    public async Task FinalOutputs_BlockWhenReadinessWarningsRemainOpen()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var uncategorisedBankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Uncategorised current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(uncategorisedBankAccount);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = uncategorisedBankAccount.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(10),
            Description = "Uncategorised filing blocker",
            Amount = 42m
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.Contains("transactions not yet categorised", error.Message);
    }

    [Theory]
    [InlineData(nameof(DocumentGeneratorService.GenerateAccountsPackageAsync))]
    [InlineData(nameof(DocumentGeneratorService.GenerateAgmApprovalPackAsync))]
    [InlineData(nameof(DocumentGeneratorService.GenerateCroFilingPackAsync))]
    [InlineData(nameof(DocumentGeneratorService.GenerateSignaturePageAsync))]
    public async Task FinalDocumentServices_RejectMismatchedCompanyPeriod(string methodName)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));
        var method = typeof(DocumentGeneratorService).GetMethod(methodName, [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<byte[]>>(method.Invoke(documents, [period.CompanyId, otherPeriod.Id]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void DocumentGeneratorService_RequiresCompanyIdForFinalDocumentMethods()
    {
        var methodNames = new HashSet<string>
        {
            nameof(DocumentGeneratorService.GenerateAccountsPackageAsync),
            nameof(DocumentGeneratorService.GenerateAgmApprovalPackAsync),
            nameof(DocumentGeneratorService.GenerateCroFilingPackAsync),
            nameof(DocumentGeneratorService.GenerateSignaturePageAsync)
        };
        var methods = typeof(DocumentGeneratorService)
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
    public async Task OpeningTrialBalanceTakeOn_BalancedReviewedBalancesClearOpeningReadinessWarnings()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
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
            SourceNote = "Opening trial balance per prior signed accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.DoesNotContain(readiness.MissingItems, item => item.Contains("opening balances", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(readiness.Warnings, warning => warning.Contains("Opening balances do not agree", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProfitAndLoss_IncludesPostedYearEndJournalsButNotTaxProvisionTwice()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var sundryCategory = AddCategory(db, period.CompanyId, "7900", "Sundry Expenses", AccountCategoryType.Expense);
        var accrualCategory = AddCategory(db, period.CompanyId, "2100", "Accruals", AccountCategoryType.Liability);
        var taxChargeCategory = AddCategory(db, period.CompanyId, "8000", "Corporation Tax Charge", AccountCategoryType.Expense);
        var taxPayableCategory = AddCategory(db, period.CompanyId, "2400", "Corporation Tax Payable", AccountCategoryType.Liability);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bankAccount.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 3, 1),
                Description = "Customer receipt",
                Amount = 1_000m,
                CategoryId = salesCategory.Id
            },
            new ImportedTransaction
            {
                BankAccountId = bankAccount.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 3, 2),
                Description = "Sundry expense",
                Amount = -200m,
                CategoryId = sundryCategory.Id
            });
        db.Adjustments.AddRange(
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Manual year-end correction",
                DebitCategoryId = sundryCategory.Id,
                CreditCategoryId = accrualCategory.Id,
                Amount = 50m,
                ImpactOnProfit = -50m,
                Source = AdjustmentSource.Manual,
                IsAuto = false
            },
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Corporation tax provision",
                DebitCategoryId = taxChargeCategory.Id,
                CreditCategoryId = taxPayableCategory.Id,
                Amount = 100m,
                ImpactOnProfit = -100m,
                Source = AdjustmentSource.Auto,
                IsAuto = true
            });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 100m,
            Paid = 0m,
            Balance = 100m
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var profitAndLoss = await service.GetProfitAndLossAsync(period.CompanyId, period.Id);

        Assert.Equal(1_000m, profitAndLoss.Turnover);
        Assert.Equal(250m, profitAndLoss.TotalOverheads);
        Assert.Equal(-50m, profitAndLoss.TotalYearEndAdjustments);
        Assert.Equal(750m, profitAndLoss.ProfitBeforeTax);
        Assert.Equal(100m, profitAndLoss.TaxCharge);
        Assert.Equal(650m, profitAndLoss.ProfitAfterTax);
    }

    [Fact]
    public async Task FilingWorkflow_BlocksWhenCroCertificationSignatoriesAreMissing()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.CompanyOfficers.RemoveRange(db.CompanyOfficers.Where(o => o.CompanyId == period.CompanyId));
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        Assert.Contains("No active director recorded for CRO accounts certification.", status.BlockingIssues);
        Assert.Contains("No active company secretary recorded for CRO accounts certification.", status.BlockingIssues);
    }

    [Fact]
    public async Task FilingWorkflow_DoesNotApproveCroFilingWhileBlockersRemain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer"));

        Assert.Contains("Generate and retain the CRO filing artifacts", error.Message);
    }

    [Fact]
    public async Task Ixbrl_ProfitAndLossIncludesInterestAndProfitBeforeTax()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var sales = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bankCharges = AddCategory(db, period.CompanyId, "6900", "Bank Charges & Interest", AccountCategoryType.Expense);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Sales", Amount = 10_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Bank interest", Amount = -500m, CategoryId = bankCharges.Id });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bytes = await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id);
        var xhtml = System.Text.Encoding.UTF8.GetString(bytes);

        // The filed P&L must show interest payable and a profit-before-tax subtotal so it
        // reconciles: operating profit 10,000 - interest 500 = profit before tax 9,500.
        Assert.Contains("core:InterestPayableSimilarChargesFinanceCosts", xhtml);
        Assert.Contains("core:ProfitLossOnOrdinaryActivitiesBeforeTax", xhtml);
        Assert.Contains("Profit before taxation", xhtml);
        Assert.Contains(">9500<", xhtml);
    }

    [Fact]
    public async Task Ixbrl_EmitsPriorYearComparativesEntityMetadataAndWellFormedXml()
    {
        // BL-08 / BL-09: the filed iXBRL must carry prior-year comparatives and entity/report
        // metadata, parse as well-formed XML, and tag values that equal the statement figures.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Comparatives Limited",
            CroNumber = "445566",
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

        var priorPeriod = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true };
        var currentPeriod = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(priorPeriod, currentPeriod);
        AddCategory(db, company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset);
        AddCategory(db, company.Id, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var sales = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = priorPeriod.Id, Date = new DateOnly(2024, 6, 1), Description = "Prior sales", Amount = 6_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = currentPeriod.Id, Date = new DateOnly(2025, 6, 1), Description = "Current sales", Amount = 9_000m, CategoryId = sales.Id });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var xhtml = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(company.Id, currentPeriod.Id));

        // Well-formed XML: DOCTYPE tolerated, no undeclared HTML entities such as &nbsp;.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(xhtml), settings);
        var doc = System.Xml.Linq.XDocument.Load(reader);

        // Prior-year comparatives.
        Assert.Contains("xbrli:context id=\"prior\"", xhtml);
        Assert.Contains("xbrli:context id=\"priorInstant\"", xhtml);
        Assert.Contains("contextRef=\"prior\"", xhtml);

        // Entity/report metadata.
        Assert.Contains("bus:EntityCurrentLegalOrRegisteredName", xhtml);
        Assert.Contains("bus:StartDateForPeriodCoveredByReport", xhtml);
        Assert.Contains("Comparatives Limited", xhtml);

        // Tagged values equal the statement figures (current and prior).
        var currentBs = await statements.GetBalanceSheetAsync(company.Id, currentPeriod.Id);
        var currentPl = await statements.GetProfitAndLossAsync(company.Id, currentPeriod.Id);
        var priorPl = await statements.GetProfitAndLossAsync(company.Id, priorPeriod.Id);

        System.Xml.Linq.XNamespace ix = "http://www.xbrl.org/2013/inlineXBRL";
        decimal Fact(string concept, string ctx) =>
            decimal.Parse(doc.Descendants(ix + "nonFraction")
                .Single(e => (string?)e.Attribute("name") == concept && (string?)e.Attribute("contextRef") == ctx).Value,
                System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Math.Round(currentPl.Turnover, 0), Fact("core:TurnoverGrossRevenue", "current"));
        Assert.Equal(Math.Round(currentBs.NetAssets, 0), Fact("core:NetAssetsLiabilities", "instant"));
        Assert.Equal(Math.Round(priorPl.Turnover, 0), Fact("core:TurnoverGrossRevenue", "prior"));
    }

    [Fact]
    public async Task FilingWorkflow_MarkGeneratedEndpointCannotSatisfyCroDocumentReadiness()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<IxbrlService>();
        builder.Services.AddScoped<FilingWorkflowService>();

        await using var app = builder.Build();
        int companyId;
        int periodId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
            db.FilingRegimes.Add(new FilingRegime
            {
                PeriodId = period.Id,
                ElectedRegime = ElectedRegime.Micro,
                CanUseMicro = true,
                CanFileAbridged = true,
                AuditExempt = true
            });
            db.NotesDisclosures.Add(new NotesDisclosure
            {
                PeriodId = period.Id,
                NoteNumber = 1,
                Title = "Approval of Financial Statements",
                Content = "Approved by the directors.",
                IsRequired = true,
                IsIncluded = true
            });
            await db.SaveChangesAsync();
            await MakePeriodReadyForCroDocumentsAsync(db, period);
            companyId = period.CompanyId;
            periodId = period.Id;
        }

        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");
            await next();
        });
        app.MapFilingWorkflowEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var accounts = await client.PostAsJsonAsync(
                $"/api/companies/{companyId}/periods/{periodId}/filing/mark-generated",
                new { documentType = "accounts" });
            var signature = await client.PostAsJsonAsync(
                $"/api/companies/{companyId}/periods/{periodId}/filing/mark-generated",
                new { documentType = "signature" });

            Assert.NotEqual(HttpStatusCode.OK, accounts.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, signature.StatusCode);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var statements = scope.ServiceProvider.GetRequiredService<FinancialStatementsService>();
            var ixbrl = scope.ServiceProvider.GetRequiredService<IxbrlService>();
            var workflow = new FilingWorkflowService(db, statements, ixbrl);
            var status = await workflow.GetStatusAsync(companyId, periodId);

            Assert.Contains("CRO accounts PDF not generated", status.BlockingIssues);
            Assert.Contains("CRO signature page not generated", status.BlockingIssues);
            Assert.False(status.ReadyToFile);
            Assert.Null(await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId));
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public async Task DocumentEndpoints_GetCroDownloadsDoNotRecordGeneratedFlags()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<DocumentGeneratorService>();
            builder.Services.AddScoped<DirectorsReportService>();
            builder.Services.AddScoped<IxbrlService>();
            builder.Services.AddScoped<FilingWorkflowService>();
            builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
            builder.Services.AddScoped<ApiAccessService>();

        await using var app = builder.Build();
        int companyId;
        int periodId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
            db.FilingRegimes.Add(new FilingRegime
            {
                PeriodId = period.Id,
                ElectedRegime = ElectedRegime.Micro,
                CanUseMicro = true,
                CanFileAbridged = true,
                AuditExempt = true
            });
            db.NotesDisclosures.Add(new NotesDisclosure
            {
                PeriodId = period.Id,
                NoteNumber = 1,
                Title = "Approval of Financial Statements",
                Content = "Approved by the directors.",
                IsRequired = true,
                IsIncluded = true
            });
            await db.SaveChangesAsync();
            await MakePeriodReadyForCroDocumentsAsync(db, period);
            companyId = period.CompanyId;
            periodId = period.Id;
        }

        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");
            await next();
        });
        app.MapDocumentEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var croPack = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack");
            var signature = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/signature-page");

            Assert.True(croPack.StatusCode == HttpStatusCode.OK, await croPack.Content.ReadAsStringAsync());
            Assert.True(signature.StatusCode == HttpStatusCode.OK, await signature.Content.ReadAsStringAsync());

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var package = await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId);

            Assert.Null(package);
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public async Task DocumentEndpoints_ReadOnlyDownloadsDoNotAdvanceCroGeneratedFlags()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<DocumentGeneratorService>();
        builder.Services.AddScoped<DirectorsReportService>();
        builder.Services.AddScoped<IxbrlService>();
        builder.Services.AddScoped<FilingWorkflowService>();

        await using var app = builder.Build();
        int companyId;
        int periodId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
            db.FilingRegimes.Add(new FilingRegime
            {
                PeriodId = period.Id,
                ElectedRegime = ElectedRegime.Micro,
                CanUseMicro = true,
                CanFileAbridged = true,
                AuditExempt = true
            });
            db.NotesDisclosures.Add(new NotesDisclosure
            {
                PeriodId = period.Id,
                NoteNumber = 1,
                Title = "Approval of Financial Statements",
                Content = "Approved by the directors.",
                IsRequired = true,
                IsIncluded = true
            });
            await db.SaveChangesAsync();
            await MakePeriodReadyForCroDocumentsAsync(db, period);
            companyId = period.CompanyId;
            periodId = period.Id;
        }

        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
            {
                AllowedCompanyIds = new HashSet<int> { companyId }
            };
            await next();
        });
        app.MapDocumentEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var croPack = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack");
            var signature = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/signature-page");

            Assert.True(croPack.StatusCode == HttpStatusCode.OK, await croPack.Content.ReadAsStringAsync());
            Assert.True(signature.StatusCode == HttpStatusCode.OK, await signature.Content.ReadAsStringAsync());

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Null(await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId));
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public async Task FilingWorkflow_TreatsOverdueDeadlinesAsWarningsNotReadinessBlockers()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        Assert.Contains(status.WarningIssues, w => w.Contains("CRO deadline passed"));
        Assert.DoesNotContain(status.BlockingIssues, b => b.Contains("CRO deadline passed"));
    }

    [Fact]
    public async Task FilingWorkflow_RequiresPaymentBeforeCroAcceptance()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        await FinaliseReleaseTestPeriodAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", retainedFinalArtifact: [1, 2, 3]);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature", retainedFinalArtifact: [4, 5, 6]);
        await BindTrustedCroApprovalAsync(db, period);
        await workflow.UpdateCroStatusAsync(
            period.CompanyId,
            period.Id,
            FilingStatus.Submitted,
            "Reviewer",
            submissionReference: "  CORE-2026-0001  ");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer"));
        Assert.Contains("Confirm CORE payment", error.Message);

        await workflow.ConfirmCroPaymentAsync(period.CompanyId, period.Id, "Reviewer");
        var accepted = await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer");

        Assert.Equal(FilingStatus.Accepted, accepted.FilingStatus);
        Assert.True(accepted.PaymentCompleted);
    }

    [Fact]
    public async Task CroApproval_FreeFormReviewerFailsClosedWithoutCapturingSignatories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Micro, CanUseMicro = true, CanFileAbridged = true, AuditExempt = true });
        db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = period.Id, NoteNumber = 1, Title = "Approval of Financial Statements", Content = "Approved by the directors.", IsRequired = true, IsIncluded = true });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        await FinaliseReleaseTestPeriodAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(db, statements, new IxbrlService(db, statements));
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", retainedFinalArtifact: [1, 2, 3]);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature", retainedFinalArtifact: [4, 5, 6]);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer"));
        Assert.Contains("Free-form reviewer names", error.Message, StringComparison.OrdinalIgnoreCase);

        db.ChangeTracker.Clear();
        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Equal(FilingStatus.PackageGenerated, package.FilingStatus);
        Assert.Null(package.ApprovedBy);
        Assert.Null(package.SignedByDirector);
        Assert.Null(package.SignedBySecretary);
        Assert.Null(package.SignedAt);
    }

    [Fact]
    public async Task FilingWorkflow_RejectsCroSubmissionWithoutCoreReferenceBeforeMutatingPackage()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        await FinaliseReleaseTestPeriodAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", retainedFinalArtifact: [1, 2, 3]);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature", retainedFinalArtifact: [4, 5, 6]);
        await BindTrustedCroApprovalAsync(db, period);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Submitted,
                "Reviewer",
                submissionReference: "   "));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("CORE submission reference is required", error.Message);
        Assert.Equal(FilingStatus.Approved, package.FilingStatus);
        Assert.Null(package.SubmittedBy);
        Assert.Null(package.SubmittedAt);
        Assert.Null(package.CroSubmissionReference);
    }

    [Fact]
    public async Task FilingWorkflow_RejectsCroAcceptanceWhenSubmittedPackageHasNoCoreReference()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            FilingStatus = FilingStatus.Submitted,
            AccountsPdfGenerated = true,
            SignaturePageGenerated = true,
            PaymentCompleted = true,
            SubmittedBy = "Reviewer",
            SubmittedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            CroSubmissionReference = "   "
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer"));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("CORE submission reference is required", error.Message);
        Assert.Equal(FilingStatus.Submitted, package.FilingStatus);
        Assert.Equal(FilingPackageStatus.Draft, package.Status);
    }

    [Fact]
    public async Task FilingWorkflow_RequiresCharityReportsReferenceAndAcceptanceBeforeDeadlineFiling()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2026, 1, 1);
        period.PeriodEnd = new DateOnly(2026, 12, 31);
        period.Status = PeriodStatus.Review;
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        company.CompanyType = CompanyType.CompanyLimitedByGuarantee;
        foreach (var director in await db.CompanyOfficers.Where(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director).ToListAsync())
            director.AppointedDate = period.PeriodStart;
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        var governanceArtifact = Encoding.UTF8.GetBytes("signed governance review");
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = period.CompanyId,
            CharityNumber = "CHY-12345",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            CharitableObjectives = "Community education",
            PrincipalActivities = "Training and support",
            GovernanceCodeCompliant = true,
            GovernanceCodeNote = "Board review complete.",
            GovernanceEvidenceReference = "GOV-2026-001",
            GovernanceReviewedBy = "Reviewer",
            GovernanceReviewedAtUtc = DateTime.UtcNow,
            GovernanceEvidenceArtifact = governanceArtifact,
            GovernanceEvidenceArtifactSha256 = FilingReleaseGate.ComputeSha256(governanceArtifact)
        });
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            IncomingResources = 1_000m,
            ResourcesExpended = 900m,
            ClosingBalance = 200m
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        await FinaliseReleaseTestPeriodAsync(db, period);
        await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        var audit = new AuditService(db);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl, audit);
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);

        var approveBeforeReports = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer"));
        Assert.Contains("Generate the Charity SoFA", approveBeforeReports.Message);

        var balanceSheet = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var fund = await db.FundBalances.SingleAsync(f => f.PeriodId == period.Id);
        fund.OpeningBalance = 0m;
        fund.IncomingResources = balanceSheet.NetAssets;
        fund.ResourcesExpended = 0m;
        fund.Transfers = 0m;
        fund.GainsLosses = 0m;
        fund.ClosingBalance = balanceSheet.NetAssets;
        await db.SaveChangesAsync();
        await new CharityReportingService(db).RecordTrusteeReviewAsync(
            period.CompanyId,
            period.Id,
            true,
            "TRUSTEE-REVIEW-2026-001",
            Encoding.UTF8.GetBytes("signed trustee review"),
            "Reviewer");
        await workflow.RecordCharityReportGeneratedAsync(period.CompanyId, period.Id, "sofa", "reviewer@example.ie");
        await workflow.RecordCharityReportGeneratedAsync(period.CompanyId, period.Id, "trustees-report", "reviewer@example.ie");
        var approved = await BindTrustedCharityApprovalAsync(db, period);
        var approvedStatus = approved.FilingStatus;

        var submitWithoutReference = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Submitted, "Reviewer", auditUserId: "reviewer@example.ie"));
        Assert.Contains("Charity annual return reference is required", submitWithoutReference.Message);

        var submitted = await workflow.UpdateCharityStatusAsync(
            period.CompanyId,
            period.Id,
            FilingStatus.Submitted,
            "Reviewer",
            annualReturnReference: "  CRA-AR-2025-001  ",
            auditUserId: "reviewer@example.ie");
        var submittedStatus = submitted.FilingStatus;
        var accepted = await workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer", auditUserId: "reviewer@example.ie");
        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        await new DeadlineService(db, audit).MarkFiledAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Charity,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "reviewer@example.ie");

        var history = await db.FilingHistories.SingleAsync(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity);
        var reportAudits = await db.AuditLogs.Where(a => a.Action == AuditEventCodes.CharityReportGenerated).ToListAsync();
        var statusAudits = await db.AuditLogs.Where(a => a.Action == AuditEventCodes.CharityFilingStatusChanged).ToListAsync();

        Assert.Equal(FilingStatus.Approved, approvedStatus);
        Assert.Equal(FilingStatus.Submitted, submittedStatus);
        Assert.Equal(FilingStatus.Accepted, accepted.FilingStatus);
        Assert.Equal(FilingPackageStatus.Accepted, accepted.Status);
        Assert.Equal("CRA-AR-2025-001", accepted.AnnualReturnReference);
        Assert.Equal(FilingStatus.Accepted, status.Charity.Status);
        Assert.True(status.Charity.SofaGenerated);
        Assert.True(status.Charity.TrusteesReportGenerated);
        Assert.Equal("CRA-AR-2025-001", status.Charity.AnnualReturnReference);
        Assert.Equal("CRA-AR-2025-001", history.FilingReference);
        Assert.Equal(2, reportAudits.Count);
        Assert.Equal(2, statusAudits.Count);
    }

    [Fact]
    public async Task FilingWorkflow_RechecksFinalReadinessBeforeCroSubmission()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        await FinaliseReleaseTestPeriodAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", retainedFinalArtifact: [1, 2, 3]);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature", retainedFinalArtifact: [4, 5, 6]);
        await BindTrustedCroApprovalAsync(db, period);

        var lateDebit = AddCategory(db, period.CompanyId, "7900", "Sundry Expenses", AccountCategoryType.Expense);
        var lateCredit = AddCategory(db, period.CompanyId, "2100", "Accruals", AccountCategoryType.Liability);
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "Late unapproved adjustment",
            DebitCategoryId = lateDebit.Id,
            CreditCategoryId = lateCredit.Id,
            Amount = 100m,
            ImpactOnProfit = -100m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Reviewer"
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Submitted,
                "Reviewer",
                submissionReference: "CORE-READINESS-TEST"));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("Financial readiness is incomplete", error.Message);
        Assert.Contains("adjustments pending approval", error.Message);
        Assert.Equal(FilingStatus.Approved, package.FilingStatus);
        Assert.Null(package.SubmittedBy);
        Assert.Null(package.SubmittedAt);
    }

    [Fact]
    public async Task FilingWorkflow_InvalidatesCroApprovalWhenOfficerEvidenceChanges()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        await FinaliseReleaseTestPeriodAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", retainedFinalArtifact: [1, 2, 3]);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature", retainedFinalArtifact: [4, 5, 6]);
        await BindTrustedCroApprovalAsync(db, period);

        var secretary = await db.CompanyOfficers.SingleAsync(o =>
            o.CompanyId == period.CompanyId
            && (o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary));
        secretary.ResignedDate = period.PeriodEnd;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Submitted,
                "Reviewer",
                submissionReference: "CORE-BLOCKER-TEST"));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("artifacts and qualified-accountant approval are stale", error.Message);
        Assert.Equal(FilingStatus.Approved, package.FilingStatus);
        Assert.Null(package.SubmittedBy);
        Assert.Null(package.SubmittedAt);
    }

    [Fact]
    public void Deadline_MoveToNextWorkingDay_SkipsIrishPublicHolidays()
    {
        Assert.Equal(new DateOnly(2026, 3, 18), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 3, 17)));
        Assert.Equal(new DateOnly(2026, 4, 7), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 4, 6)));
        Assert.Equal(new DateOnly(2026, 12, 29), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 12, 25)));
    }

    [Fact]
    public async Task SeedData_RemovesNonCharitySampleCompaniesWhenSampleCompanySeedingIsDisabled()
    {
        await using var db = CreateDbContext();

        await SeedData.SeedAsync(db, seedDemoUsers: false, seedSampleCompanies: true);
        Assert.True(await db.Companies.AnyAsync(c => c.CroNumber == "654321"));
        Assert.True(await db.Companies.AnyAsync(c => c.CroNumber == "789012"));

        db.ChangeTracker.Clear();
        await SeedData.SeedAsync(db, seedDemoUsers: false, seedSampleCompanies: false);

        Assert.True(await db.Companies.AnyAsync(c =>
            c.CroNumber == "567890"
            && c.LegalName == "Green Valley Community Development CLG"
            && c.IsCharitableOrganisation));
        Assert.False(await db.Companies.AnyAsync(c => c.CroNumber == "654321"));
        Assert.False(await db.Companies.AnyAsync(c => c.CroNumber == "789012"));
    }

    [Fact]
    public async Task SeedData_PopulatesCompleteSizeDecisionEvidenceForFrontendContracts()
    {
        await using var db = CreateDbContext();
        await SeedData.SeedAsync(db, seedDemoUsers: false, seedSampleCompanies: false);

        var classification = await db.SizeClassifications.SingleAsync();
        Assert.True(classification.PeriodLengthInYears > 0m);
        Assert.True(classification.AnnualisedTurnover > 0m);
        Assert.Equal("SI-301-2024", classification.ThresholdScheduleCode);
        Assert.Equal(64, classification.DecisionInputFingerprintSha256?.Length);
        Assert.True(classification.RawCurrentMicroQualified);
        Assert.True(classification.RawCurrentSmallQualified);
        Assert.True(classification.RawCurrentMediumQualified);
    }

    [Fact]
    public async Task FilingWorkflow_IxbrlGenerationFailureDoesNotPersistGeneratedSuccess()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();
        await SaveSupportedTaxScopeAsync(db, period.CompanyId, period.Id);

        var statements = new FinancialStatementsService(db);
        var ixbrl = new FailingIxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var result = await workflow.ValidateIxbrlAsync(period.CompanyId, period.Id);

        Assert.False(result.IxbrlGenerated);
        Assert.False(result.IxbrlValidated);
        Assert.Contains("iXBRL generation failed. Check server logs and retry.", result.IxbrlValidationErrors);
        Assert.DoesNotContain("password=secret", result.IxbrlValidationErrors);
    }

    [Fact]
    public async Task IxbrlService_UsesCurrentIrishRevenueTaxonomyAndCorrectPeriodContexts()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var service = new IxbrlService(db, statements);

        var xhtml = Encoding.UTF8.GetString(await service.GenerateIxbrlAsync(period.CompanyId, period.Id));

        Assert.Contains("ie-FRS-102-2025-01-01.xsd", xhtml);
        Assert.Contains("xmlns:ie-common=\"https://xbrl.frc.org.uk/ireland/common/2025-01-01\"", xhtml);
        Assert.Contains("name=\"core:NetAssetsLiabilities\" contextRef=\"instant\"", xhtml);
        Assert.Contains("name=\"core:TurnoverGrossRevenue\" contextRef=\"current\"", xhtml);
        Assert.Contains("name=\"core:ProfitLossForPeriod\" contextRef=\"current\"", xhtml);
        Assert.DoesNotContain("name=\"core:TurnoverGrossRevenue\" contextRef=\"instant\"", xhtml);
        Assert.DoesNotContain("name=\"core:ProfitLossForPeriod\" contextRef=\"instant\"", xhtml);
    }

    [Fact]
    public async Task IxbrlService_UsesRevenueAcceptedIrishExtension2023TaxonomyFor2023Periods()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var service = new IxbrlService(db, new FinancialStatementsService(db));

        var xhtml = Encoding.UTF8.GetString(await service.GenerateIxbrlAsync(period.CompanyId, period.Id));

        Assert.Contains("ie-FRS-102-2023-01-01.xsd", xhtml);
        Assert.Contains("xmlns:ie-common=\"https://xbrl.frc.org.uk/ireland/common/2023-01-01\"", xhtml);
    }

    [Fact]
    public async Task IxbrlService_RejectsPeriodsBeforeRevenueAcceptedFrs102EffectiveDate()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2018, 1, 1);
        period.PeriodEnd = new DateOnly(2018, 12, 31);
        await db.SaveChangesAsync();

        var service = new IxbrlService(db, new FinancialStatementsService(db));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GenerateIxbrlAsync(period.CompanyId, period.Id));

        Assert.Contains("no Revenue-accepted taxonomy is pinned", error.Message);
        Assert.Contains("2018-01-01", error.Message);
    }

    [Fact]
    public async Task IxbrlService_RejectsMismatchedCompanyPeriodForRawGeneration()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new IxbrlService(db, new FinancialStatementsService(db));
        var method = typeof(IxbrlService).GetMethod(nameof(IxbrlService.GenerateIxbrlAsync), [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<byte[]>>(method.Invoke(service, [period.CompanyId, otherPeriod.Id]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void IxbrlService_RequiresCompanyIdForRawGeneration()
    {
        var methods = typeof(IxbrlService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(IxbrlService.GenerateIxbrlAsync))
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
    public async Task FilingWorkflow_ReviewPrototypeChecksCannotCreateOrValidateAFilingArtifact()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var result = await workflow.ValidateIxbrlAsync(period.CompanyId, period.Id);

        Assert.False(result.IxbrlGenerated);
        Assert.False(result.IxbrlValidated);
        Assert.Contains(RevenueIxbrlGenerationPolicy.ManualHandoffReason, result.IxbrlValidationErrors);
        Assert.Null(result.IxbrlArtifact);
        Assert.Null(result.IxbrlSha256);
    }

    [Fact]
    public async Task FinalIxbrlDownload_IsDisabledUntilCompleteRevenueGenerationIsImplemented()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var service = new IxbrlService(db, statements);
        var method = typeof(IxbrlService).GetMethod("GenerateFinalIxbrlAsync", [typeof(int), typeof(int)]);
        Assert.True(method is not null, "Public iXBRL downloads should use a final-filing method that enforces readiness and internal checks.");

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [period.CompanyId, period.Id]));
        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(async () => await task);

        Assert.Equal(RevenueIxbrlGenerationPolicy.ManualHandoffReason, error.Message);
    }

    [Fact]
    public async Task FinalIxbrlDownload_RejectsMismatchedCompanyPeriodBeforeReadinessChecks()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var service = new IxbrlService(db, statements);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateFinalIxbrlAsync(period.CompanyId, otherPeriod.Id));
    }

    [Theory]
    [InlineData(nameof(TaxComputationService.ComputeAsync), typeof(TaxComputationService.TaxComputation))]
    [InlineData(nameof(TaxComputationService.GetCt1SupportDataAsync), typeof(TaxComputationService.Ct1SupportData))]
    public async Task RevenueServices_RejectMismatchedCompanyPeriod(string methodName, Type resultType)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        var method = typeof(TaxComputationService).GetMethod(methodName, [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var taskType = typeof(Task<>).MakeGenericType(resultType);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [period.CompanyId, otherPeriod.Id]));
        Assert.IsAssignableFrom(taskType, task);
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void TaxComputationService_RequiresCompanyIdForRevenueOutputs()
    {
        var methods = typeof(TaxComputationService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name is nameof(TaxComputationService.ComputeAsync) or nameof(TaxComputationService.GetCt1SupportDataAsync))
            .Select(m => new { m.Name, Parameters = m.GetParameters().Select(p => p.ParameterType).ToArray() })
            .ToList();

        foreach (var methodName in new[] { nameof(TaxComputationService.ComputeAsync), nameof(TaxComputationService.GetCt1SupportDataAsync) })
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
    public async Task ValidateIxbrlEndpoint_ReturnsRevenueWorkflowStatusContract()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/validate-ixbrl");
        const string idempotencyKey = "revenue-validation-replay-test-0001";
        context.Request.Headers[IdempotencyHttpContract.RequestHeader] = idempotencyKey;

        var result = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            period.Id,
            workflow,
            db,
            context,
            DisabledApiAccess());

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var revenue = Assert.IsType<FilingWorkflowService.RevenueFilingStatus>(valueResult.Value);
        Assert.False(revenue.IxbrlReady);
        Assert.False(revenue.IxbrlInternalChecksPassed);
        Assert.False(revenue.IxbrlValid);
        Assert.Equal("manual-handoff-only", revenue.GenerationSupport);
        Assert.True(revenue.ManualHandoffRequired);
        Assert.False(revenue.ReviewPrototypeChecksPassed);
        Assert.Contains("filing-ready iXBRL generation is disabled", revenue.ValidationErrors);
        Assert.Contains("Corporation-tax scope questionnaire has not been prepared", revenue.ValidationErrors);
        Assert.Contains("no retained corporation-tax loss movement", revenue.ValidationErrors);

        var replayContext = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/validate-ixbrl");
        replayContext.Request.Headers[IdempotencyHttpContract.RequestHeader] = idempotencyKey;
        var replay = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            period.Id,
            workflow,
            db,
            replayContext,
            DisabledApiAccess());
        var replayRevenue = Assert.IsType<FilingWorkflowService.RevenueFilingStatus>(
            Assert.IsAssignableFrom<IValueHttpResult>(replay).Value);
        Assert.Equal(revenue, replayRevenue);
        Assert.Equal("false", context.Response.Headers[IdempotencyHttpContract.ReplayedHeader]);
        Assert.Equal("true", replayContext.Response.Headers[IdempotencyHttpContract.ReplayedHeader]);
        Assert.Single(await db.RevenueFilingPackages.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ValidateIxbrlEndpoint_DoesNotMutateRevenuePackageForMismatchedCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/filing/validate-ixbrl");

        var result = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            workflow,
            db,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.RevenueFilingPackages.ToListAsync());
    }

    [Fact]
    public async Task ValidateIxbrlEndpoint_DeniesReviewerBeforeRevenueMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/validate-ixbrl");

        var result = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            period.Id,
            workflow,
            db,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(result));
        Assert.Empty(await db.RevenueFilingPackages.ToListAsync());
    }

    [Fact]
    public void FilingWorkflowService_RequiresCompanyIdForIxbrlValidation()
    {
        var methods = typeof(FilingWorkflowService)
            .GetMethods()
            .Where(m => m.Name == nameof(FilingWorkflowService.ValidateIxbrlAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length >= 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length >= 1
            && parameters[0] == typeof(int)
            && (parameters.Length == 1 || parameters[1] != typeof(int)));
    }

    [Fact]
    public async Task DirectorsReportData_RejectsMismatchedCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new DirectorsReportService(db, new FinancialStatementsService(db));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateAsync(period.CompanyId, otherPeriod.Id));
    }

    [Fact]
    public async Task DirectorsReportData_SurfacesProfitAndLossFailures()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Small,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var service = new DirectorsReportService(db, new FailingProfitAndLossStatementsService(db));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GenerateAsync(period.CompanyId, period.Id));

        Assert.Contains("profit and loss", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("€0", error.Message);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsCroBeforeAcceptedWorkflowWithoutMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await PrepareFinalisedReleaseTestPeriodAsync(db, period);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, croDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.CRO);
        Assert.Contains("CRO filing package is missing", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.CRO).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsAcceptedCroPackageWithoutCoreReferenceBeforeMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);
        var acceptedPackage = await db.CroFilingPackages.SingleAsync(package => package.PeriodId == period.Id);
        acceptedPackage.CroSubmissionReference = " ";
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, croDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.CRO);
        Assert.Contains("CORE submission reference is required", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.Null(deadline.FilingReference);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.CRO).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsRevenueWhileFilingReadyGenerationIsDisabledWithoutMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var revenueDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Revenue);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.Revenue, revenueDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        Assert.Contains("filing-ready iXBRL generation is disabled", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_LegacyGeneratedBooleanCannotBypassDisabledRevenueGeneration()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await SeedRevenueInternalIxbrlChecksPassedAsync(db, period.Id, ct1Reference: null);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var revenueDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Revenue);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.Revenue, revenueDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        var package = await db.RevenueFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("filing-ready iXBRL generation is disabled", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Null(package.Ct1Reference);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsCharityBeforeReportingEvidenceWithoutMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        await db.SaveChangesAsync();
        await PrepareFinalisedReleaseTestPeriodAsync(db, period);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.Charity, charityDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Charity);
        Assert.Contains("charity filing package is missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsRevenueReferenceWhileGenerationIsDisabledAndPreservesState()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await SeedRevenueInternalIxbrlChecksPassedAsync(db, period.Id, ct1Reference: null);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var revenueDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Revenue);
        var filedDate = revenueDeadline.DueDate.AddDays(3);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() => service.MarkFiledAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Revenue,
            filedDate,
            "reviewer@example.ie",
            "  ROS-CT1-2025-0001  "));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        var package = await db.RevenueFilingPackages.SingleAsync(p => p.PeriodId == period.Id);

        Assert.Contains("filing-ready iXBRL generation is disabled", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Null(package.Ct1Reference);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsLegacyAcceptedCharityPackageWithoutBoundSourceEvidence()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = period.CompanyId,
            CharityNumber = "CHY-12345",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            CharitableObjectives = "Community education",
            PrincipalActivities = "Training and support"
        });
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            IncomingResources = 1_000m,
            ResourcesExpended = 900m,
            ClosingBalance = 200m
        });
        db.CharityFilingPackages.Add(new CharityFilingPackage
        {
            PeriodId = period.Id,
            FilingStatus = FilingStatus.Accepted,
            Status = FilingPackageStatus.Accepted,
            SofaGenerated = true,
            TrusteesReportGenerated = true,
            AnnualReturnReference = "CRA-AR-2025-001",
            SubmittedBy = "Reviewer",
            SubmittedAt = new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc),
            AcceptedBy = "Reviewer",
            AcceptedAt = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        await PrepareFinalisedReleaseTestPeriodAsync(db, period);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);
        var filedDate = charityDeadline.DueDate.AddDays(3);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() => service.MarkFiledAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Charity,
            filedDate,
            "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Charity);
        Assert.Contains("deterministic SORP, governance, trustee and reconciled-accounting source fingerprint", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.Null(deadline.FilingReference);
        Assert.Empty(await db.FilingHistories.Where(history =>
            history.CompanyId == period.CompanyId
            && history.PeriodId == period.Id
            && history.DeadlineType == DeadlineType.Charity).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), auditLog => auditLog.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsCharityBeforeAcceptedAnnualReturnPackageEvenWhenReportingDataExists()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = period.CompanyId,
            CharityNumber = "CHY-12345",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            CharitableObjectives = "Community education",
            PrincipalActivities = "Training and support"
        });
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            IncomingResources = 1_000m,
            ResourcesExpended = 900m,
            ClosingBalance = 200m
        });
        await db.SaveChangesAsync();
        await PrepareFinalisedReleaseTestPeriodAsync(db, period);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            service.MarkFiledAsync(
                period.CompanyId,
                period.Id,
                DeadlineType.Charity,
                charityDeadline.DueDate.AddDays(3),
                "reviewer@example.ie",
                "CRA-AR-2025-001"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Charity);
        Assert.Contains("charity filing package is missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(deadline.FiledDate);
        Assert.Null(deadline.FilingReference);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsFutureFiledDateBeforeMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var service = new DeadlineService(
            db,
            audit,
            timeProvider: new FixedUtcTimeProvider(new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.Zero)));
        await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var futureFiledDate = new DateOnly(2026, 6, 8);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, futureFiledDate, "reviewer@example.ie"));

        var croDeadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.CRO);
        Assert.Contains("future", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(croDeadline.FiledDate);
        Assert.Empty(await db.FilingHistories.ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_AllowsIrishTodayWhenUtcDateIsStillYesterday()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var irishToday = new DateOnly(2026, 6, 7);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = irishToday.AddDays(-10)
        });
        await db.SaveChangesAsync();
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);
        var service = new DeadlineService(
            db,
            audit: null,
            timeProvider: new FixedUtcTimeProvider(new DateTimeOffset(2026, 6, 6, 23, 30, 0, TimeSpan.Zero)));

        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, irishToday);

        var deadline = await db.FilingDeadlines.SingleAsync();
        Assert.Equal(irishToday, deadline.FiledDate);
        Assert.Single(await db.FilingHistories.ToListAsync());
    }

    [Fact]
    public async Task DeadlinePersistence_RejectsMismatchedCompanyPeriodRows()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = otherPeriod.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = dueDate
        });
        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.FilingDeadlines.ToListAsync());
        Assert.Empty(await db.FilingHistories.ToListAsync());
    }

    [Fact]
    public async Task DeadlineJeopardy_CountsDistinctLateCroFilingObligations()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        db.FilingHistories.AddRange(
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = dueDate,
                FiledDate = dueDate.AddDays(1),
                DaysLate = 1
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = dueDate.AddDays(1),
                FiledDate = dueDate.AddDays(3),
                DaysLate = 2
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.Revenue,
                DueDate = dueDate,
                FiledDate = dueDate.AddDays(10),
                DaysLate = 10
            });
        await db.SaveChangesAsync();
        var service = new DeadlineService(db);

        var jeopardy = await service.CheckAuditExemptionJeopardyAsync(period.CompanyId);

        Assert.Equal(1, jeopardy.LateFilingCount);
        Assert.True(jeopardy.IsAtRisk);
        Assert.False(jeopardy.HasLostExemption);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task NotesEndpoints_RejectOversizedCustomContentBeforePersistence(string action)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var oversizedContent = new string('x', 20_001);
        var context = AuthenticatedRequest(
            "Accountant",
            action == "create" ? HttpMethods.Post : HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/notes");

        IResult result;
        if (action == "create")
        {
            result = await YearEndEndpoints.CreateNoteEndpointAsync(
                period.CompanyId,
                period.Id,
                new NotesDisclosure
                {
                    Title = "Oversized custom note",
                    Content = oversizedContent,
                    IsIncluded = true
                },
                db,
                audit,
                context);
        }
        else
        {
            var note = new NotesDisclosure
            {
                PeriodId = period.Id,
                NoteNumber = 1,
                Title = "Existing custom note",
                Content = "Original content",
                IsIncluded = true
            };
            db.NotesDisclosures.Add(note);
            await db.SaveChangesAsync();

            result = await YearEndEndpoints.UpdateNoteEndpointAsync(
                period.CompanyId,
                period.Id,
                note.Id,
                new NotesDisclosure
                {
                    Title = note.Title,
                    Content = oversizedContent,
                    IsIncluded = true
                },
                db,
                audit,
                context);
        }

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        if (action == "create")
        {
            Assert.Empty(await db.NotesDisclosures.Where(n => n.PeriodId == period.Id).ToListAsync());
        }
        else
        {
            var saved = await db.NotesDisclosures.SingleAsync(n => n.PeriodId == period.Id);
            Assert.Equal("Original content", saved.Content);
        }
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "NotesDisclosure").ToListAsync());
    }

    [Fact]
    public async Task AdjustmentEvidence_LogsManualAdjustmentLifecycleAndClearsApprovalOnUpdate()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var debit = AddCategory(db, period.CompanyId, "6810", "Accountancy Fees", AccountCategoryType.Expense);
        var credit = AddCategory(db, period.CompanyId, "2100", "Accruals", AccountCategoryType.Liability);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();

        var createInput = new AdjustmentInput(
            Description: " Audit fee accrual ",
            DebitCategoryId: debit.Id,
            CreditCategoryId: credit.Id,
            Amount: 1_500m,
            Reason: " Year-end invoice received after close ",
            LegalBasis: " FRS 102 accruals concept ",
            ImpactOnProfit: -1_500m,
            ImpactOnAssets: 0m);

        await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            createInput,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments"),
            apiAccess);
        var adjustment = await db.Adjustments.SingleAsync(a => a.PeriodId == period.Id);

        await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Reviewer", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}/approve"),
            apiAccess);

        var updateInput = createInput with
        {
            Description = "Audit and tax fee accrual",
            Amount = 1_750m,
            Reason = "Invoice updated after partner review",
            ImpactOnProfit = -1_750m
        };

        await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            updateInput,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        await AdjustmentEndpoints.DeleteAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}"),
            apiAccess);

        var audits = await db.AuditLogs
            .Where(a => a.EntityType == "Adjustment")
            .OrderBy(a => a.Id)
            .ToListAsync();

        Assert.Equal(
            [
                AuditEventCodes.AdjustmentCreated,
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.AdjustmentDeleted
            ],
            audits.Select(a => a.Action).ToArray());
        Assert.All(audits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(audits[0].OldValueJson);
        Assert.Contains("\"Description\":\"Audit fee accrual\"", audits[0].NewValueJson);
        Assert.Contains("\"Amount\":1500", audits[0].NewValueJson);
        Assert.Contains("\"ApprovedBy\":\"Example User\"", audits[1].NewValueJson);
        Assert.Contains("\"ApprovedBy\":\"Example User\"", audits[2].OldValueJson);
        Assert.Contains("\"Amount\":1750", audits[2].NewValueJson);
        Assert.Contains("\"ApprovedBy\":null", audits[2].NewValueJson);
        Assert.Contains("\"Amount\":1750", audits[3].OldValueJson);
        Assert.Contains("\"Deleted\":true", audits[3].NewValueJson);

        Assert.Empty(db.Adjustments);
    }

    [Fact]
    public async Task AdjustmentEvidence_GuardsListGenerateAndSummaryAgainstMismatchedCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherDebit = AddCategory(db, otherPeriod.CompanyId, "6814", "Other fees", AccountCategoryType.Expense);
        var otherCredit = AddCategory(db, otherPeriod.CompanyId, "2104", "Other accruals", AccountCategoryType.Liability);
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = otherPeriod.Id,
            Description = "Other period adjustment",
            DebitCategoryId = otherDebit.Id,
            CreditCategoryId = otherCredit.Id,
            Amount = 900m,
            Reason = "Other company",
            LegalBasis = "FRS 102",
            ImpactOnProfit = -900m,
            ImpactOnAssets = 0m,
            CreatedBy = "Other reviewer"
        });
        await db.SaveChangesAsync();

        var context = AuthenticatedRequest("Accountant", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments");
        var apiAccess = DisabledApiAccess();

        var list = await AdjustmentEndpoints.ListAdjustmentsEndpointAsync(period.CompanyId, otherPeriod.Id, db, context, null, null);
        var generated = await AdjustmentEndpoints.GenerateAdjustmentsEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new AdjustmentService(db),
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/generate"),
            apiAccess);
        var summary = await AdjustmentEndpoints.GetAdjustmentSummaryEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/summary"));

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(list));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(generated));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(summary));
        Assert.Single(await db.Adjustments.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Adjustment").ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_BlocksCategorisationWhenPeriodIsLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4004", "Sales", AccountCategoryType.Income);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var transaction = new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Receipt",
            Amount = 250m
        };
        db.ImportedTransactions.Add(transaction);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        var result = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transaction.Id,
            new CategoriseInput(category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status409Conflict, ResultStatusCode(result));
        Assert.Null((await db.ImportedTransactions.SingleAsync(t => t.Id == transaction.Id)).CategoryId);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "ImportedTransaction").ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_ListTransactionsRequiresCompanyOwnedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/transactions"),
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
    }

    [Fact]
    public async Task BankingEvidence_ListTransactionsHidesCompanyWhenClientIsNotAssigned()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Receipt",
            Amount = 10m
        });
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest("Client", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{period.Id}/transactions");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { otherPeriod.CompanyId }
        };

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            db,
            context,
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
    }

    [Fact]
    public async Task BankingEvidence_PersistenceRejectsRulesWithCrossCompanyCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "4999", "Other company sales", AccountCategoryType.Income);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        db.TransactionRules.Add(new TransactionRule
        {
            CompanyId = period.CompanyId,
            Pattern = "Stripe",
            CategoryId = otherCategory.Id,
            Priority = 1
        });
        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.TransactionRules.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_PersistenceRejectsAutoCategorisationRulesWithCrossCompanyCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "6901", "Other company charges", AccountCategoryType.Expense);
        db.TransactionRules.Add(new TransactionRule
        {
            CompanyId = period.CompanyId,
            Pattern = "Stripe",
            CategoryId = otherCategory.Id,
            Priority = 1
        });
        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.TransactionRules.ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_PersistenceRejectsCrossCompanyTransactionCategoryMetadata()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "7777", "Other Company Secret Category", AccountCategoryType.Expense);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Contaminated category",
            Amount = -10m,
            CategoryId = otherCategory.Id
        });
        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task UpsertTaxBalance_RejectsInconsistentOrNegativeTriple()
    {
        // accounting-tax-balance-internal-consistency: the upsert previously stored the triple verbatim,
        // so Balance != Liability - Paid (or a negative liability/paid) mis-stated creditors and
        // profit-after-tax. The endpoint must reject an inconsistent/negative triple and accept a
        // consistent one (including a legitimate overpayment producing a negative Balance).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        IStatusCodeHttpResult StatusOf(IResult r) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r);
        HttpContext Ctx() => AuthenticatedRequest("Accountant", HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/tax-balances/CorporationTax");

        var inconsistent = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = 1_000m, Paid = 200m, Balance = 900m }, db, audit, Ctx());
        var negative = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = -50m, Paid = 0m, Balance = -50m }, db, audit, Ctx());

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(inconsistent).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(negative).StatusCode);
        Assert.Empty(await db.TaxBalances.ToListAsync());

        // A consistent triple (Balance == Liability - Paid) persists.
        var consistent = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = 1_000m, Paid = 200m, Balance = 800m }, db, audit, Ctx());
        Assert.Equal(StatusCodes.Status200OK, StatusOf(consistent).StatusCode);
        Assert.Equal(800m, (await db.TaxBalances.SingleAsync()).Balance);

        // An overpayment (refund due) is consistent and allowed: -20 == 100 - 120.
        var overpaid = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = 100m, Paid = 120m, Balance = -20m }, db, audit, Ctx());
        Assert.Equal(StatusCodes.Status200OK, StatusOf(overpaid).StatusCode);
        Assert.Equal(-20m, (await db.TaxBalances.SingleAsync()).Balance);
    }

    [Fact]
    public async Task YearEndEvidenceNotes_RejectWrongPeriodAndProtectCustomNotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var requiredNote = new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Accounting policies",
            Content = "Required generated note.",
            IsRequired = true,
            IsIncluded = true
        };
        var customNote = new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 9,
            Title = "Custom covenant note",
            Content = "Custom disclosure should survive regeneration.",
            IsRequired = false,
            IsIncluded = true
        };
        var otherNote = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 1,
            Title = "Other company note",
            Content = "Do not mutate",
            IsIncluded = true
        };
        db.NotesDisclosures.AddRange(requiredNote, customNote, otherNote);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/notes/generate");
        var notes = new NotesDisclosureService(db);

        var deleteRequiredResult = await YearEndEndpoints.DeleteNoteEndpointAsync(
            period.CompanyId,
            period.Id,
            requiredNote.Id,
            db,
            audit,
            context);
        var generateWrongPeriodResult = await YearEndEndpoints.GenerateNotesEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            notes,
            db,
            audit,
            context);
        var updateWrongPeriodResult = await YearEndEndpoints.UpdateNoteEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherNote.Id,
            new NotesDisclosure { Title = "Wrong company update", Content = "Mutated", IsIncluded = false },
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteRequiredResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(generateWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateWrongPeriodResult).StatusCode);
        Assert.NotNull(await db.NotesDisclosures.FindAsync(requiredNote.Id));
        var unchangedOtherNote = await db.NotesDisclosures.SingleAsync(n => n.Id == otherNote.Id);
        Assert.Equal("Other company note", unchangedOtherNote.Title);
        Assert.True(unchangedOtherNote.IsIncluded);

        await YearEndEndpoints.GenerateNotesEndpointAsync(period.CompanyId, period.Id, notes, db, audit, context);

        var remainingCustomNote = await db.NotesDisclosures.SingleAsync(n => n.Id == customNote.Id);
        Assert.Equal("Custom covenant note", remainingCustomNote.Title);
        Assert.False(remainingCustomNote.IsRequired);
        Assert.True(remainingCustomNote.NoteNumber > 1);
        Assert.Contains(await db.NotesDisclosures.Where(n => n.PeriodId == period.Id).ToListAsync(), n =>
            n.IsRequired && n.Title == "Accounting Policies");
    }

    [Fact]
    public async Task NotesDisclosureService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherRequired = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 1,
            Title = "Other generated note",
            Content = "Do not remove",
            IsRequired = true,
            IsIncluded = true
        };
        var otherCustom = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 2,
            Title = "Other custom note",
            Content = "Do not renumber",
            IsRequired = false,
            IsIncluded = true
        };
        db.NotesDisclosures.AddRange(otherRequired, otherCustom);
        await db.SaveChangesAsync();
        var service = new NotesDisclosureService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateNotesAsync(period.CompanyId, otherPeriod.Id));

        var unchanged = await db.NotesDisclosures
            .Where(n => n.PeriodId == otherPeriod.Id)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();
        Assert.Equal(["Other generated note", "Other custom note"], unchanged.Select(n => n.Title).ToArray());
        Assert.Equal([1, 2], unchanged.Select(n => n.NoteNumber).ToArray());
    }

    [Fact]
    public void NotesEndpoints_UseCompanyAwareGenerationAndReads()
    {
        var source = YearEndEndpointsSource();

        Assert.Contains("GenerateNotesAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateNotesAsync(periodId)", source);
        Assert.Contains("p.Id == periodId && p.CompanyId == companyId", source);
    }

    [Fact]
    public async Task CharityReporting_SofaRejectsPeriodFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = otherPeriod.Id,
            FundName = "Other company restricted fund",
            FundType = "Restricted",
            OpeningBalance = 0m,
            IncomingResources = 1_000m,
            ClosingBalance = 1_000m
        });
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateSofaAsync(period.CompanyId, otherPeriod.Id));
    }

    [Fact]
    public void EndpointInputs_PeriodStatusFinaliseDoesNotRequireCallerSuppliedLockedBy()
    {
        var period = new AccountingPeriod
        {
            CompanyId = 1,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Review
        };

        var result = EndpointInputs.ValidatePeriodStatusUpdate(
            period,
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null),
            AuthenticatedRole("Reviewer"));

        Assert.Null(result);
    }

    [Theory]
    [InlineData(PeriodStatus.Finalised, "accounts finalisation")]
    [InlineData(PeriodStatus.Filed, "accounts filing")]
    public async Task PeriodStatusEndpoint_RejectsFinaliseOrFileWhenReadinessBlockersRemain(
        PeriodStatus targetStatus,
        string outputName)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(targetStatus, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains($"Cannot generate final {outputName}", error.Message);
        Assert.Contains("readiness blockers", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenFilingDeadlinesRemainUnfiled()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains("Cannot mark period as filed", error.Message);
        Assert.Contains("CRO filing has not been recorded as filed", error.Message);
        Assert.Contains("Revenue filing has not been recorded as filed", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenApplicableCharityDeadlineIsUnfiled()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        foreach (var deadline in deadlines.Where(d => d.DeadlineType is DeadlineType.CRO or DeadlineType.Revenue))
        {
            deadline.FiledDate = deadline.DueDate;
            deadline.FilingReference = deadline.DeadlineType == DeadlineType.Revenue
                ? "ROS-CT1-2025-0001"
                : "CORE-2025-0001";
        }
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains("Cannot mark period as filed", error.Message);
        Assert.Contains("Charity filing has not been recorded as filed", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenCroDeadlineReferenceMissing()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        foreach (var deadline in deadlines.Where(d => d.DeadlineType is DeadlineType.CRO or DeadlineType.Revenue))
        {
            deadline.FiledDate = deadline.DueDate;
            deadline.FilingReference = deadline.DeadlineType == DeadlineType.Revenue
                ? "ROS-CT1-2025-0001"
                : null;
        }
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains("Cannot mark period as filed", error.Message);
        Assert.Contains("CRO filing reference has not been recorded", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenDeadlineRowsLackBoundReleaseEvidence()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        foreach (var deadline in deadlines)
        {
            deadline.FiledDate = deadline.DueDate;
            deadline.FilingReference = deadline.DeadlineType switch
            {
                DeadlineType.Revenue => "ROS-CT1-2025-0001",
                DeadlineType.Charity => "CRA-AR-2025-0001",
                _ => "CORE-2025-0001"
            };
        }
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Contains(RevenueIxbrlGenerationPolicy.ManualHandoffReason, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

}
