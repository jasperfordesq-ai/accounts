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
using Microsoft.AspNetCore.Routing.Patterns;
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
    public async Task SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 30_000m,
            AvgEmployees = 2
        });
        await db.SaveChangesAsync();

        var service = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));

        var result = await service.ClassifyAsync(period.CompanyId, period.Id);

        Assert.Equal(CompanySizeClass.Micro, result.CalculatedClass);
        Assert.True(result.CanUseMicro);
        Assert.True(result.AuditExempt);
        Assert.Contains("Micro", result.AvailableRegimes[0]);
    }

    [Fact]
    public async Task FilingRegime_RecentRepeatedLateCroFilings_RemoveAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.IsFirstYear = false;
        (await db.Companies.SingleAsync(c => c.Id == period.CompanyId)).IncorporationDate = new DateOnly(2024, 1, 1);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        var priorPeriod = new AccountingPeriod
        {
            CompanyId = period.CompanyId,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(priorPeriod);
        await db.SaveChangesAsync();
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = priorPeriod.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        db.FilingHistories.AddRange(
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                FiledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1).AddDays(4)),
                DaysLate = 4,
                PenaltyAmount = 112m
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = priorPeriod.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
                FiledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2).AddDays(1)),
                DaysLate = 1,
                PenaltyAmount = 103m
            });
        await db.SaveChangesAsync();

        var classificationService = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));
        await classificationService.ClassifyAsync(period.CompanyId, priorPeriod.Id);
        await classificationService.ClassifyAsync(period.CompanyId, period.Id);
        var service = new FilingRegimeService(db);

        var result = await service.DetermineAsync(period.CompanyId, period.Id);

        Assert.False(result.AuditExempt);
        Assert.Contains("late CRO filings", result.Summary);
        var saved = await db.FilingRegimes.SingleAsync(f => f.PeriodId == period.Id);
        Assert.False(saved.AuditExempt);
    }

    [Fact]
    public async Task DirectorsReport_UsesReviewedPrincipalActivitiesAndAuditExemption()
    {
        // filing-directors-report-from-service: principal activities are a retained directors'
        // representation, not an inference from IsTrading. Relevant-audit-information wording is
        // omitted for an audit-exempt company.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var company = await db.Companies.FirstAsync(c => c.Id == period.CompanyId);
        company.IsTrading = false; // dormant
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Small, CanUseMicro = false, CanFileAbridged = false, AuditExempt = true });
        db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = period.Id, NoteNumber = 1, Title = "Approval of Financial Statements", Content = "Approved by the directors.", IsRequired = true, IsIncluded = true });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var pdfText = ExtractPdfText(await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.Contains("provision of professional services", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dormant", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normal trading activities", pdfText);
        // Audit-exempt -> no relevant-audit-information statement.
        Assert.DoesNotContain("Relevant Audit Information", pdfText);
    }

    [Fact]
    public async Task FinalOutputs_BlockedForNonAuditExemptEntityUntilAuditorsReportAttached()
    {
        // filing-auditor-report-blocks-final: a non-audit-exempt (e.g. Medium) entity must not generate
        // final statutory outputs until a signed auditor's report is attached.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Medium, CanUseMicro = false, CanFileAbridged = false, AuditExempt = false });
        db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = period.Id, NoteNumber = 1, Title = "Approval of Financial Statements", Content = "Approved by the directors.", IsRequired = true, IsIncluded = true });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        // Blocked while no auditor's report is attached.
        var blockers = await statements.GetFinalOutputReadinessBlockersAsync(period.CompanyId, period.Id);
        Assert.Contains(blockers, b => b.Contains("auditor's report has not been attached"));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));
        Assert.Contains("auditor's report has not been attached", error.Message);

        // Attach the signed auditor's report -> final outputs generate.
        AttachCompleteAuditorReportEvidence(period, "AUD-2025-001");
        await db.SaveChangesAsync();
        var unblocked = await statements.GetFinalOutputReadinessBlockersAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(unblocked, b => b.Contains("auditor's report has not been attached"));
        Assert.NotEmpty(await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));
    }

    [Fact]
    public async Task GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        // Onboarding: €500 share capital subscribed at incorporation, funded by the opening bank
        // balance. The matching opening-balance entry keeps the opening trial balance in step.
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 500,
            NominalValue = 1m,
            TotalValue = 500m,
            IssueDate = period.PeriodStart
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 500m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 6_500m,
            BalanceSheetTotal = 6_250m,
            AvgEmployees = 1
        });
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 500m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        // Categorisation rules so the import auto-codes every row (no manual tidy-up needed).
        db.TransactionRules.AddRange(
            new TransactionRule { CompanyId = companyId, Pattern = "Sales", CategoryId = Cat("4000"), Priority = 1 },
            new TransactionRule { CompanyId = companyId, Pattern = "Office", CategoryId = Cat("6500"), Priority = 2 },
            new TransactionRule { CompanyId = companyId, Pattern = "Light", CategoryId = Cat("6300"), Priority = 3 });
        await db.SaveChangesAsync();

        // Import a real bank-statement CSV through the real ImportService (generic format).
        var csv = MakeGenericCsv(
            ("01/03/2025", "Sales invoice INV001", 4_000m),
            ("10/06/2025", "Sales invoice INV002", 2_500m),
            ("15/04/2025", "Office Supplies purchase", -300m),
            ("20/09/2025", "Light and Heat ESB", -450m));
        var import = await new ImportService(db).ImportCsvAsync(
            companyId, bank.Id, period.Id, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "statement.csv");

        Assert.Equal(4, import.ImportedRows);
        Assert.Equal(4, import.AutoCategorised);
        Assert.Equal(0, import.DuplicateCandidates);

        // Year-end questionnaire — a nil-trading micro with no debtors/creditors/etc. confirms each section.
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"), NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"), NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"), NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"), NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var emit = await ClassifyAdjustNotesAndEmitAsync(db, companyId, period.Id, ElectedRegime.Micro);

        // Regime is Micro, audit-exempt.
        Assert.Equal(ElectedRegime.Micro, emit.Regime.Regime);
        Assert.True(emit.Regime.AuditExempt);

        // Readiness gate is fully satisfied — nothing missing, nothing warned, balance sheet balances.
        Assert.Empty(emit.Readiness.MissingItems);
        Assert.Empty(emit.Readiness.Warnings);
        Assert.True(emit.Readiness.BalanceSheetBalances);
        Assert.Equal(100, emit.Readiness.FilingReadinessPercent);

        // Money is correct and the statements BALANCE.
        Assert.True(emit.BalanceSheet.Balances);
        Assert.Equal(0m, emit.BalanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(6_250m, emit.BalanceSheet.NetAssets);
        Assert.Equal(500m, emit.BalanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(5_750m, emit.BalanceSheet.CapitalAndReserves.ProfitForYear);
        Assert.Equal(6_250m, emit.BalanceSheet.CurrentAssets.Cash);

        // P&L stage runs and reconciles to the worked figures.
        Assert.Equal(6_500m, emit.ProfitAndLoss.Turnover);
        Assert.Equal(5_750m, emit.ProfitAndLoss.ProfitAfterTax);

        // The accounts PDF generated past the readiness gate and is a real PDF.
        Assert.True(emit.Pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(emit.Pdf, 0, 4));

        // The iXBRL is well-formed XML carrying the entity name.
        AssertWellFormedXml(emit.Ixbrl);
        Assert.Contains("Example Micro Limited", emit.Ixbrl);

        // tests-pdf-content-verified: parse the PDF text (not just the %PDF header) and assert the
        // real figures/names/wording — company legal name, the period-end date, the computed
        // net-assets total, and the micro s.280D statutory statement.
        var pdfText = ExtractPdfText(emit.Pdf);
        Assert.Contains("Example Micro Limited", pdfText);
        Assert.Contains(period.PeriodEnd.ToString("dd MMMM yyyy"), pdfText);
        Assert.Contains(emit.BalanceSheet.NetAssets.ToString("N0"), pdfText); // 6,250 == computed BalanceSheet
        Assert.Contains("280D", pdfText);
    }

    [Fact]
    public async Task GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        // A small audit-exempt LTD that owns assets, holds stock and has a bank loan.
        var company = await db.Companies.FirstAsync(c => c.Id == companyId);
        company.LegalName = "Connacht Digital Solutions Limited";
        company.OwnsAssets = true;
        company.HasStock = true;
        company.HasBorrowings = true;
        await db.SaveChangesAsync();

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
        // Size-classification interview is input-driven (REQUIREMENTS §B); these are representative
        // Small-company figures, independent of the worked demo ledger below.
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 2_000_000m,
            BalanceSheetTotal = 1_200_000m,
            AvgEmployees = 25
        });
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.TransactionRules.AddRange(
            new TransactionRule { CompanyId = companyId, Pattern = "Sales", CategoryId = Cat("4000"), Priority = 1 },
            new TransactionRule { CompanyId = companyId, Pattern = "Rent", CategoryId = Cat("6100"), Priority = 2 },
            new TransactionRule { CompanyId = companyId, Pattern = "Plant", CategoryId = Cat("0020"), Priority = 3 },
            new TransactionRule { CompanyId = companyId, Pattern = "Loan", CategoryId = Cat("2700"), Priority = 4 });
        await db.SaveChangesAsync();

        // Cash movements: +10,000 trading, -3,000 rent, -4,000 capex (asset — outside the P&L),
        // +5,000 loan drawdown. Capex and loan are coded to balance-sheet categories.
        var csv = MakeGenericCsv(
            ("01/02/2025", "Sales receipts", 10_000m),
            ("01/03/2025", "Rent paid", -3_000m),
            ("01/04/2025", "Plant and machinery purchase", -4_000m),
            ("01/05/2025", "Bank Loan drawdown", 5_000m));
        var import = await new ImportService(db).ImportCsvAsync(
            companyId, bank.Id, period.Id, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "aib-statement.csv");
        Assert.Equal(4, import.ImportedRows);
        Assert.Equal(4, import.AutoCategorised);

        // Accrual-basis year-end facts entered as entity rows.
        db.Debtors.AddRange(
            new Debtor { PeriodId = period.Id, Name = "Customer X", Amount = 2_000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 300m, Type = DebtorType.Prepayment });
        db.Creditors.AddRange(
            new Creditor { PeriodId = period.Id, Name = "Supplier Y", Amount = 1_500m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = period.Id, Name = "Accountancy fees", Amount = 500m, Type = CreditorType.Accrual, DueWithinYear = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Closing stock", Value = 800m, ValuationMethod = ValuationMethod.Cost });
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = companyId,
            Name = "Plant",
            Category = "Plant & Machinery",
            Cost = 4_000m,
            AcquisitionDate = new DateOnly(2025, 4, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine,
            CapitalAllowanceTreatment = CapitalAllowanceTreatment.PlantAndMachinery12Point5,
            CapitalAllowanceEvidence = "Purchase invoice and exclusive trade-use evidence retained for test.",
            CapitalAllowanceReviewedBy = "Automated test actor (not human acceptance)",
            CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
        });
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
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "payroll"), NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"), NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"), NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var emit = await ClassifyAdjustNotesAndEmitAsync(db, companyId, period.Id, ElectedRegime.SmallAbridged);

        // Small abridged audit-exempt regime.
        Assert.Equal(ElectedRegime.SmallAbridged, emit.Regime.Regime);
        Assert.True(emit.Regime.CanFileAbridged);
        Assert.True(emit.Regime.AuditExempt);

        // Readiness gate satisfied across the richer year-end set.
        Assert.Empty(emit.Readiness.MissingItems);
        Assert.Empty(emit.Readiness.Warnings);
        Assert.True(emit.Readiness.BalanceSheetBalances);

        // The mixed cash/accrual set BALANCES exactly.
        Assert.True(emit.BalanceSheet.Balances);
        Assert.Equal(0m, emit.BalanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(7_446.58m, emit.BalanceSheet.NetAssets);
        Assert.Equal(100m, emit.BalanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(7_346.58m, emit.BalanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(3_246.58m, emit.BalanceSheet.FixedAssets.Total);

        // PDF generates past the gate; iXBRL is well-formed.
        Assert.True(emit.Pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(emit.Pdf, 0, 4));
        Assert.True(emit.CroPack.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(emit.CroPack, 0, 4));
        Assert.True(emit.SignaturePage.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(emit.SignaturePage, 0, 4));
        AssertWellFormedXml(emit.Ixbrl);
        Assert.Contains("Connacht Digital Solutions Limited", emit.Ixbrl);
        Assert.Contains("core:TurnoverGrossRevenue", emit.Ixbrl);
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", emit.Ixbrl);

        Assert.True(emit.Tax.FinalTaxChargeSupported, string.Join("; ", emit.Tax.BlockingReasons));
        Assert.Equal(1_044.18m, emit.Tax.TotalCorporationTax);
        Assert.Contains(emit.Notes, n => n.Title == "Tangible Fixed Assets" && n.IsIncluded);
        Assert.Contains(emit.Notes, n => n.Title == "Creditors: Amounts Falling Due After More Than One Year" && n.IsIncluded);

        // tests-pdf-content-verified: the parsed PDF carries the legal name, period-end date and the
        // computed net-assets total for the richer accrual-basis small company.
        var pdfText = ExtractPdfText(emit.Pdf);
        Assert.Contains("Connacht Digital Solutions Limited", pdfText);
        Assert.Contains(period.PeriodEnd.ToString("dd MMMM yyyy"), pdfText);
        Assert.Contains(emit.BalanceSheet.NetAssets.ToString("N0"), pdfText); // 7,447 == computed BalanceSheet
        Assert.Contains("DIRECTORS' REPORT", pdfText);
        Assert.Contains("PROFIT AND LOSS ACCOUNT", pdfText);

        var croPackText = ExtractPdfText(emit.CroPack);
        Assert.Contains("Abridged Financial Statements for filing with the CRO", croPackText);
        Assert.Contains("DIRECTORS' REPORT", croPackText);
        Assert.Contains("section 352", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PROFIT AND LOSS ACCOUNT", croPackText);

        var signatureText = ExtractPdfText(emit.SignaturePage);
        Assert.Contains("CRO ACCOUNTS CERTIFICATION", signatureText);
        Assert.Contains("A Director", signatureText);
        Assert.Contains("B Secretary", signatureText);
    }

    private sealed record GoldenPathEmission(
        byte[] Pdf,
        byte[] CroPack,
        byte[] SignaturePage,
        string Ixbrl,
        FilingRegimeService.FilingRequirements Regime,
        FinancialStatementsService.ReadinessScore Readiness,
        FinancialStatementsService.BalanceSheet BalanceSheet,
        FinancialStatementsService.ProfitAndLoss ProfitAndLoss,
        TaxComputationService.TaxComputation Tax,
        IReadOnlyList<NotesDisclosure> Notes);

    // Shared tail of the golden path: classify -> determine regime -> generate + approve adjustments ->
    // generate notes -> compute statements -> generate the accounts PDF and iXBRL. The PDF call itself
    // asserts final-output readiness, so this only succeeds when the whole pipeline is filing-ready.
    private static async Task<GoldenPathEmission> ClassifyAdjustNotesAndEmitAsync(
        AccountsDbContext db, int companyId, int periodId, ElectedRegime electedRegime)
    {
        await new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()))
            .ClassifyAsync(companyId, periodId);
        var regime = await new FilingRegimeService(db).DetermineAsync(companyId, periodId, electedRegime);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, periodId);

        // A nil-adjustment conclusion is review evidence, never a zero-value pseudo-journal.
        if (!await db.Adjustments.AnyAsync(a => a.PeriodId == periodId))
        {
            if (!await db.YearEndReviewConfirmations.AnyAsync(r => r.PeriodId == periodId && r.SectionKey == "adjustments"))
            {
                db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation
                {
                    PeriodId = periodId,
                    SectionKey = "adjustments",
                    Confirmed = true,
                    ConfirmedBy = "Accounts reviewer",
                    ConfirmedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }
        // Reviewer approves all proposed adjustments.
        foreach (var adj in await db.Adjustments.Where(a => a.PeriodId == periodId && a.ApprovedAt == null).ToListAsync())
        {
            adj.ApprovedBy = "Accounts reviewer";
            adj.ApprovedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        await SaveSupportedTaxScopeAsync(db, companyId, periodId);

        var period = await db.AccountingPeriods.SingleAsync(candidate => candidate.Id == periodId);
        period.ApprovalDate ??= period.PeriodEnd.AddDays(-1);
        if (electedRegime != ElectedRegime.Micro
            && !await db.YearEndReviewConfirmations.AnyAsync(review =>
                review.PeriodId == periodId
                && review.SectionKey == DirectorsReportService.PrincipalActivitiesReviewKey))
        {
            db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation
            {
                PeriodId = periodId,
                SectionKey = DirectorsReportService.PrincipalActivitiesReviewKey,
                Confirmed = true,
                ConfirmedBy = "Qualified accountant",
                ConfirmedAt = DateTime.UtcNow,
                Note = "The principal activity is the provision of professional services."
            });
        }
        if (!regime.AuditExempt
            && !await db.YearEndReviewConfirmations.AnyAsync(review =>
                review.PeriodId == periodId
                && review.SectionKey == DirectorsReportService.AuditInformationReviewKey))
        {
            db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation
            {
                PeriodId = periodId,
                SectionKey = DirectorsReportService.AuditInformationReviewKey,
                Confirmed = true,
                ConfirmedBy = "Qualified accountant",
                ConfirmedAt = DateTime.UtcNow,
                Note = "Director enquiries and signed audit-information confirmations are retained at reference WP-DR-330."
            });
        }
        if (electedRegime != ElectedRegime.Micro
            && !await db.YearEndReviewConfirmations.AnyAsync(review =>
                review.PeriodId == periodId && review.SectionKey == "note-directors-remuneration"))
        {
            db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation
            {
                PeriodId = periodId,
                SectionKey = "note-directors-remuneration",
                Confirmed = true,
                ConfirmedBy = "Qualified accountant",
                ConfirmedAt = DateTime.UtcNow,
                Note = "Directors' remuneration was reviewed against retained payroll and board records; the disclosure amount is €0.00."
            });
        }
        await db.SaveChangesAsync();

        await new NotesDisclosureService(db).GenerateNotesAsync(companyId, periodId);

        var statements = new FinancialStatementsService(db);
        var readiness = await statements.GetReadinessScoreAsync(companyId, periodId);
        var bs = await statements.GetBalanceSheetAsync(companyId, periodId);
        var pl = await statements.GetProfitAndLossAsync(companyId, periodId);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(companyId, periodId);
        var notes = await db.NotesDisclosures
            .Where(n => n.PeriodId == periodId && n.IsIncluded)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();
        var documents = new DocumentGeneratorService(db, statements);
        var pdf = await documents.GenerateAccountsPackageAsync(companyId, periodId);
        var croPack = await documents.GenerateCroFilingPackAsync(companyId, periodId);
        var signaturePage = await documents.GenerateSignaturePageAsync(companyId, periodId);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(companyId, periodId));
        return new GoldenPathEmission(pdf, croPack, signaturePage, ixbrl, regime, readiness, bs, pl, tax, notes);
    }

    // Extract the rendered text from a generated PDF so tests can assert on real figures, names and
    // statutory wording (tests-pdf-content-verified), not just the %PDF magic bytes. PdfPig is pure
    // managed (no native deps), so this runs on Linux CI. GetWords() reconstructs words from glyphs;
    // joining them with single spaces yields stable, kerning-independent tokens to match against.
    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.Append(' ').Append(string.Join(' ', page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private static string MakeGenericCsv(params (string Date, string Description, decimal Amount)[] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Amount");
        foreach (var row in rows)
            sb.AppendLine($"{row.Date},{row.Description},{row.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        return sb.ToString();
    }

    private static void AssertWellFormedXml(string xhtml)
    {
        // DOCTYPE tolerated; no undeclared HTML entities. Throws if the iXBRL is not well-formed XML.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(xhtml), settings);
        var doc = System.Xml.Linq.XDocument.Load(reader);
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public async Task ExceptionMiddleware_LogsCorrelationIdAndDoesNotLeakSecretsInProduction()
    {
        // G6 (failures diagnosable): an unhandled error must be triageable from a support ticket
        // without a repro — the response carries a correlation id that also appears in the server log,
        // while no exception detail (which may carry secrets/PII) leaks to the client in production.
        var logger = new CapturingLogger<ExceptionMiddleware>();
        var errorReporter = new CapturingErrorReporter();
        const string secret = "Server=db;Password=hunter2-SECRET";
        RequestDelegate next = _ => throw new InvalidOperationException(secret);
        var middleware = new ExceptionMiddleware(next, logger, errorReporter);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/companies/1/periods/2/adjustments/generate";
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/companies/{companyId:int}/periods/{periodId:int}/adjustments/generate"),
            0,
            EndpointMetadataCollection.Empty,
            "adjustment generation"));
        context.TraceIdentifier = "corr-id-7f3a";
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment("Production"));
        context.RequestServices = services.BuildServiceProvider();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);

        body.Position = 0;
        var responseJson = await new StreamReader(body).ReadToEndAsync();
        using var payload = JsonDocument.Parse(responseJson);
        // Client gets the correlation id and a generic message — never the exception or secret.
        Assert.Equal("corr-id-7f3a", payload.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("An internal error occurred. Please try again.", payload.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("hunter2", responseJson);
        Assert.DoesNotContain(secret, responseJson);

        // Exported logs retain safe triage dimensions without serializing the free-form exception,
        // raw entity identifiers, client data, or secrets.
        var logged = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("corr-id-7f3a", logged.Message);
        Assert.Contains("POST", logged.Message);
        Assert.Contains("/api/companies/{id}/periods/{id}/adjustments/generate", logged.Message);
        Assert.Contains("stackFingerprint", logged.Message);
        Assert.DoesNotContain(secret, logged.Message);
        Assert.Null(logged.Exception);

        var reported = Assert.Single(errorReporter.Reports);
        Assert.Equal(secret, reported.Exception.Message);
        Assert.Equal("POST", reported.Context.Method);
        Assert.Equal("/api/companies/{id}/periods/{id}/adjustments/generate", reported.Context.Path);
        Assert.Equal("corr-id-7f3a", reported.Context.CorrelationId);
    }

    [Fact]
    public void AuditExemptionStatement_IsPrintedOnlyWhenConfirmedAvailable()
    {
        Assert.True(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Micro, auditExempt: true));
        Assert.True(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.SmallAbridged, auditExempt: true));
        Assert.False(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Micro, auditExempt: false));
        Assert.False(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Medium, auditExempt: true));
    }

    [Fact]
    public void AccountsPackage_SmallAuditExemptOmitsCashFlowEquityAndAuditorsReport()
    {
        // BL-02 / BL-03 negative: the small audit-exempt package must NOT carry the Medium/Full-only
        // sections, and an audit-exempt company gets no auditor's report.
        var rendered = DocumentGeneratorService.GetIncludedPrimaryStatements(ElectedRegime.Small, DocumentPackagePurpose.StatutoryApproval, auditExempt: true);
        Assert.DoesNotContain("Cash Flow Statement", rendered);
        Assert.DoesNotContain("Statement of Changes in Equity", rendered);
        Assert.DoesNotContain("Independent Auditor's Report", rendered);
    }

    [Fact]
    public void AccountsPackage_AuditorsReportTogglesWithAuditExemption()
    {
        // BL-03: the auditor's report is present exactly when the company is not audit-exempt.
        var audited = DocumentGeneratorService.GetIncludedPrimaryStatements(ElectedRegime.Medium, DocumentPackagePurpose.StatutoryApproval, auditExempt: false);
        var exempt = DocumentGeneratorService.GetIncludedPrimaryStatements(ElectedRegime.Medium, DocumentPackagePurpose.StatutoryApproval, auditExempt: true);
        Assert.Contains("Independent Auditor's Report", audited);
        Assert.DoesNotContain("Independent Auditor's Report", exempt);
    }

    [Fact]
    public void StatutoryPackageStatementMatrix_MatchesIndependentAuditExpectation()
    {
        // Expected-output matrix from PLATFORM_AUDIT_2026-07-10 P0-STAT-001. These are statutory
        // package expectations, not snapshots copied from generated output.
        var cases = new[]
        {
            new
            {
                Regime = ElectedRegime.Micro,
                Purpose = DocumentPackagePurpose.StatutoryApproval,
                AuditExempt = true,
                Expected = new[] { "Balance Sheet", "Profit and Loss Account", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.Micro,
                Purpose = DocumentPackagePurpose.AgmApproval,
                AuditExempt = true,
                Expected = new[] { "Balance Sheet", "Profit and Loss Account", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.Micro,
                Purpose = DocumentPackagePurpose.CroFiling,
                AuditExempt = true,
                Expected = new[] { "Balance Sheet", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.SmallAbridged,
                Purpose = DocumentPackagePurpose.StatutoryApproval,
                AuditExempt = true,
                Expected = new[] { "Directors' Report", "Balance Sheet", "Profit and Loss Account", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.SmallAbridged,
                Purpose = DocumentPackagePurpose.CroFiling,
                AuditExempt = true,
                Expected = new[] { "Directors' Report", "Balance Sheet", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.Small,
                Purpose = DocumentPackagePurpose.CroFiling,
                AuditExempt = true,
                Expected = new[] { "Directors' Report", "Balance Sheet", "Profit and Loss Account", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.Medium,
                Purpose = DocumentPackagePurpose.StatutoryApproval,
                AuditExempt = false,
                Expected = new[] { "Directors' Report", "Independent Auditor's Report", "Balance Sheet", "Profit and Loss Account", "Cash Flow Statement", "Statement of Changes in Equity", "Notes to the Financial Statements" }
            },
            new
            {
                Regime = ElectedRegime.Full,
                Purpose = DocumentPackagePurpose.StatutoryApproval,
                AuditExempt = false,
                Expected = new[] { "Directors' Report", "Independent Auditor's Report", "Balance Sheet", "Profit and Loss Account", "Cash Flow Statement", "Statement of Changes in Equity", "Notes to the Financial Statements" }
            }
        };

        foreach (var item in cases)
        {
            Assert.Equal(
                item.Expected,
                DocumentGeneratorService.GetIncludedPrimaryStatements(item.Regime, item.Purpose, item.AuditExempt));
        }
    }

    [Fact]
    public async Task BankingImportEndpoint_ReplaysSameFileAndKeyWithoutDuplicateRowsOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Idempotent Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        const string key = "bank-import-replay-test-0001";
        const string csvText = "Date,Description,Amount\n01/01/2025,Idempotent receipt,100\n";
        var limits = Options.Create(new ImportLimitConfig());
        var importService = new ImportService(db, limits);
        var audit = new AuditService(db);

        DefaultHttpContext Request()
        {
            var context = AuthenticatedRequest(
                "Accountant",
                HttpMethods.Post,
                $"/api/companies/{period.CompanyId}/bank-accounts/{bank.Id}/import");
            context.Request.Headers[IdempotencyHttpContract.RequestHeader] = key;
            var bytes = Encoding.UTF8.GetBytes(csvText);
            var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "bank.csv")
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/csv"
            };
            context.Request.ContentType = "multipart/form-data; boundary=test";
            context.Request.Form = new FormCollection([], new FormFileCollection { formFile });
            return context;
        }

        var firstContext = Request();
        var first = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            period.Id,
            firstContext.Request,
            importService,
            audit,
            limits,
            db,
            DisabledApiAccess());
        var replayContext = Request();
        var replay = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            period.Id,
            replayContext.Request,
            importService,
            audit,
            limits,
            db,
            DisabledApiAccess());

        var firstResult = Assert.IsType<ImportService.ImportResult>(Assert.IsAssignableFrom<IValueHttpResult>(first).Value);
        var replayResult = Assert.IsType<ImportService.ImportResult>(Assert.IsAssignableFrom<IValueHttpResult>(replay).Value);
        Assert.Equal(firstResult.ImportBatchId, replayResult.ImportBatchId);
        Assert.Equal(firstResult.SourceFileSha256, replayResult.SourceFileSha256);
        Assert.Equal("false", firstContext.Response.Headers[IdempotencyHttpContract.ReplayedHeader]);
        Assert.Equal("true", replayContext.Response.Headers[IdempotencyHttpContract.ReplayedHeader]);
        Assert.Single(await db.ImportBatches.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.ImportedTransactions.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.AuditLogs.IgnoreQueryFilters().Where(log => log.Action == AuditEventCodes.BankCsvImported).ToListAsync());
        Assert.Single(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void ProductionSafety_BlocksMissingAuditIntegritySigningKeyInProduction()
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
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuditIntegrity:SigningKeys"));
    }

    [Fact]
    public void ProductionSafety_BlocksMissingMonitoringConfigurationOutsideDevelopment()
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

        Assert.Contains(failures, f => f.Contains("Monitoring:ErrorTrackingDsn"));
        Assert.Contains(failures, f => f.Contains("Monitoring:StructuredJsonConsole"));
    }

    [Fact]
    public async Task ProductionMonitoring_ErrorSmokeEndpointIsDisabledByDefault()
    {
        var reporter = new CapturingErrorReporter();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/system/monitoring/error-smoke";
        context.TraceIdentifier = "corr-disabled-smoke";
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Owner");

        var result = SystemEndpoints.EmitMonitoringSmokeError(
            context,
            Options.Create(new MonitoringConfig { ErrorSmokeEnabled = false }),
            reporter,
            NullLogger.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(reporter.Reports);
    }

    [Theory]
    [InlineData("Accountant", StatusCodes.Status403Forbidden)]
    [InlineData("Reviewer", StatusCodes.Status403Forbidden)]
    [InlineData("Client", StatusCodes.Status403Forbidden)]
    public async Task ProductionMonitoring_ErrorSmokeEndpointRequiresOwner(string role, int expectedStatusCode)
    {
        var reporter = new CapturingErrorReporter();
        var context = MonitoringSmokeContext(role, "corr-denied-smoke");

        var result = SystemEndpoints.EmitMonitoringSmokeError(
            context,
            Options.Create(new MonitoringConfig { ErrorSmokeEnabled = true }),
            reporter,
            NullLogger.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Empty(reporter.Reports);
    }

    [Fact]
    public async Task ProductionMonitoring_ErrorSmokeEndpointEmitsControlledNonPiiEventForOwner()
    {
        var reporter = new CapturingErrorReporter();
        var logger = new CapturingLogger<AccountsWorkflowTests>();
        var context = MonitoringSmokeContext("Owner", "corr-owner-smoke");

        var result = SystemEndpoints.EmitMonitoringSmokeError(
            context,
            Options.Create(new MonitoringConfig
            {
                ErrorTrackingProvider = "Sentry-compatible",
                ErrorSmokeEnabled = true
            }),
            reporter,
            logger);

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("reported", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("captured-test-event-1", payload.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("corr-owner-smoke", payload.RootElement.GetProperty("correlationId").GetString());

        var report = Assert.Single(reporter.Reports);
        Assert.IsType<MonitoringSmokeException>(report.Exception);
        Assert.Equal("Controlled non-PII monitoring smoke event.", report.Exception.Message);
        Assert.Equal(HttpMethods.Post, report.Context.Method);
        Assert.Equal("/api/system/monitoring/error-smoke", report.Context.Path);
        Assert.Equal("corr-owner-smoke", report.Context.CorrelationId);

        var log = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("corr-owner-smoke", log.Message);
        Assert.Contains("captured-test-event-1", log.Message);
        Assert.DoesNotContain("owner@example.ie", log.Message);
    }

    [Fact]
    public void ProductionSafety_BlocksWeakAuditIntegritySigningKeyInProduction()
    {
        var failures = AuditIntegrityCheckpointService.ValidateConfiguration(new AuditIntegrityConfig
        {
            ActiveKeyId = "weak",
            SigningKeys =
            [
                new AuditIntegritySigningKeyConfig
                {
                    KeyId = "weak",
                    SigningKey = new string('a', 64)
                }
            ]
        });

        Assert.Contains(failures, f => f.Contains("AuditIntegrity:SigningKeys[weak]:SigningKey"));
    }

    [Fact]
    public void ProductionSafety_BlocksDevelopmentAuditIntegritySigningKeyInProduction()
    {
        var failures = AuditIntegrityCheckpointService.ValidateConfiguration(new AuditIntegrityConfig
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
        });

        Assert.Contains(failures, f => f.Contains("committed development audit checkpoint key"));
    }

    [Fact]
    public async Task AuditTrailMiddleware_LogsSuccessfulUnsafeCompanyPeriodRequest()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm A",
            "reviewer@example.ie",
            "Maeve Reviewer",
            "Reviewer");
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal(period.Id, entry.EntityId);
        Assert.Equal("ApiWriteSucceeded", entry.Action);
        Assert.Equal("user:7", entry.UserId);
        Assert.Contains("cro-status", entry.NewValueJson);
    }

    [Fact]
    public async Task AuditService_LogAsync_PersistsEvidenceMetadataAndIntegrityHash()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        await LogAuditWithEvidenceMetadataAsync(
            audit,
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            "FilingRegimeDetermined",
            new { Regime = "Unknown" },
            new { Regime = "Micro" },
            "reviewer@example.ie",
            tenantId: 1,
            requestId: "req-audit-001",
            actorDisplayName: "Maeve Reviewer");

        var entry = await db.AuditLogs.SingleAsync();
        AssertAuditLogValue(entry, "TenantId", 1);
        AssertAuditLogValue(entry, "RequestId", "req-audit-001");
        AssertAuditLogValue(entry, "ActorDisplayName", "Maeve Reviewer");
        Assert.Null(ReadAuditLogValue(entry, "PreviousIntegrityHash"));
        Assert.True(IsHex64(RequiredAuditLogString(entry, "IntegrityHash")));
    }

    [Fact]
    public async Task AuditService_LogAsync_RedactsSensitivePayloadFieldsBeforeStorage()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "UserAccount",
            10,
            "UserUpdated",
            oldValue: new
            {
                Email = "user@example.ie",
                Password = "old-password",
                PasswordHash = "old-hash",
                PasswordSalt = "old-salt",
                Profile = new
                {
                    SectionKey = "tax",
                    CsrfToken = "old-csrf-token"
                }
            },
            newValue: new
            {
                Email = "user@example.ie",
                SectionKey = "tax",
                ApiKey = "new-api-key",
                Authorization = "Bearer new-token",
                Cookie = "accounts_session=secret-cookie",
                Items = new[]
                {
                    new
                    {
                        Secret = "nested-secret",
                        Description = "Safe audit evidence"
                    }
                }
            },
            userId: "reviewer@example.ie");

        var entry = await db.AuditLogs.SingleAsync();
        Assert.Contains("\"Password\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"PasswordHash\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"PasswordSalt\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"CsrfToken\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"ApiKey\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"Authorization\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"Cookie\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"Secret\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"SectionKey\":\"tax\"", entry.OldValueJson);
        Assert.Contains("\"SectionKey\":\"tax\"", entry.NewValueJson);
        Assert.Contains("Safe audit evidence", entry.NewValueJson);
        Assert.DoesNotContain("old-password", entry.OldValueJson);
        Assert.DoesNotContain("old-hash", entry.OldValueJson);
        Assert.DoesNotContain("old-salt", entry.OldValueJson);
        Assert.DoesNotContain("old-csrf-token", entry.OldValueJson);
        Assert.DoesNotContain("new-api-key", entry.NewValueJson);
        Assert.DoesNotContain("Bearer new-token", entry.NewValueJson);
        Assert.DoesNotContain("secret-cookie", entry.NewValueJson);
        Assert.DoesNotContain("nested-secret", entry.NewValueJson);
        Assert.True(IsHex64(RequiredAuditLogString(entry, "IntegrityHash")));
    }

    [Fact]
    public async Task AuditService_LogAsync_ChainsRowsForSameCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        await LogAuditWithEvidenceMetadataAsync(
            audit,
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            "FilingRegimeDetermined",
            null,
            new { Regime = "Micro" },
            "reviewer@example.ie",
            tenantId: 1,
            requestId: "req-chain-001",
            actorDisplayName: "Maeve Reviewer");
        await LogAuditWithEvidenceMetadataAsync(
            audit,
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            "CroDocumentGenerated",
            null,
            new { DocumentType = "AccountsPackage" },
            "reviewer@example.ie",
            tenantId: 1,
            requestId: "req-chain-002",
            actorDisplayName: "Maeve Reviewer");

        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var firstHash = RequiredAuditLogString(entries[0], "IntegrityHash");
        var secondHash = RequiredAuditLogString(entries[1], "IntegrityHash");
        Assert.True(IsHex64(firstHash));
        Assert.True(IsHex64(secondHash));
        Assert.NotEqual(firstHash, secondHash);
        Assert.Equal(firstHash, RequiredAuditLogString(entries[1], "PreviousIntegrityHash"));
    }

    [Fact]
    public async Task AuditIntegrityService_VerifiesValidCompanyHashChain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var verifier = new AuditIntegrityService(db);
        var beforeCheck = DateTime.UtcNow;

        var report = await verifier.VerifyCompanyAsync(period.CompanyId);

        Assert.True(report.IsValid);
        Assert.Equal(2, report.CheckedEntries);
        Assert.Equal(0, report.UncheckedLegacyEntries);
        Assert.Equal(0, report.IssueCount);
        Assert.Empty(report.Issues);
        Assert.Equal(entries[0].Id, report.FirstAuditLogId);
        Assert.Equal(entries[1].Id, report.LastAuditLogId);
        Assert.Equal(entries[0].IntegrityHash, report.FirstHash);
        Assert.Equal(entries[1].IntegrityHash, report.LastHash);
        Assert.True(report.CheckedAtUtc >= beforeCheck);
    }

    [Fact]
    public async Task AuditIntegrityService_DetectsPayloadTamperingAndChainBreak()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        entries[0].NewValueJson = "{\"Regime\":\"Full\"}";
        entries[1].PreviousIntegrityHash = new string('0', 64);
        await db.SaveChangesAsync();
        var verifier = new AuditIntegrityService(db);

        var report = await verifier.VerifyCompanyAsync(period.CompanyId);

        Assert.False(report.IsValid);
        Assert.Equal(2, report.CheckedEntries);
        Assert.Equal(0, report.UncheckedLegacyEntries);
        Assert.Contains(report.Issues, issue =>
            issue.AuditLogId == entries[0].Id
            && issue.Code == AuditIntegrityIssueCodes.HashMismatch
            && issue.Timestamp == entries[0].Timestamp);
        Assert.Contains(report.Issues, issue =>
            issue.AuditLogId == entries[1].Id
            && issue.Code == AuditIntegrityIssueCodes.ChainBreak
            && issue.Expected == entries[0].IntegrityHash
            && issue.Actual == new string('0', 64));
    }

    [Fact]
    public async Task AuditIntegrityService_ReportsLegacyUnhashedEntriesSeparately()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            EntityType = "LegacySeed",
            EntityId = period.Id,
            Action = "Seeded",
            NewValueJson = "{\"Legacy\":true}"
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var verifier = new AuditIntegrityService(db);

        var report = await verifier.VerifyCompanyAsync(period.CompanyId);

        Assert.False(report.IsValid);
        Assert.Equal(1, report.CheckedEntries);
        Assert.Equal(1, report.UncheckedLegacyEntries);
        Assert.Equal(entries[1].Id, report.FirstAuditLogId);
        Assert.Equal(entries[1].Id, report.LastAuditLogId);
        Assert.Contains(report.Issues, issue =>
            issue.AuditLogId == entries[0].Id
            && issue.Code == AuditIntegrityIssueCodes.MissingHash
            && issue.Timestamp == entries[0].Timestamp);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_CreatesSignedCheckpointForValidCompanyChain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var beforeCheckpoint = DateTime.UtcNow;

        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-001");

        Assert.Equal(period.CompanyId, checkpoint.CompanyId);
        Assert.Equal(entries[1].Id, checkpoint.LastAuditLogId);
        Assert.Equal(entries[1].IntegrityHash, checkpoint.LastIntegrityHash);
        Assert.Equal(2, checkpoint.CheckedEntries);
        Assert.Equal("audit-key-2026", checkpoint.KeyId);
        Assert.Equal("owner@example.ie", checkpoint.CreatedByUserId);
        Assert.Equal("Owner User", checkpoint.CreatedByDisplayName);
        Assert.Equal("req-anchor-001", checkpoint.RequestId);
        Assert.True(checkpoint.CreatedAtUtc >= beforeCheckpoint);
        Assert.True(IsHex64(checkpoint.Signature));
        Assert.Same(checkpoint, await db.AuditIntegrityCheckpoints.SingleAsync());

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.True(verification.IsValid);
        Assert.True(verification.HasCheckpoint);
        Assert.Equal(checkpoint.Id, verification.CheckpointId);
        Assert.Equal(checkpoint.LastAuditLogId, verification.LastAuditLogId);
        Assert.Equal(checkpoint.LastIntegrityHash, verification.LastIntegrityHash);
        Assert.Equal(checkpoint.KeyId, verification.KeyId);
        Assert.Equal(0, verification.IssueCount);
        Assert.Empty(verification.Issues);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_DetectsTamperedCheckpointSignature()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-002");
        checkpoint.Signature = new string('0', 64);
        await db.SaveChangesAsync();

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Issues, issue =>
            issue.CheckpointId == checkpoint.Id
            && issue.Code == AuditIntegrityCheckpointIssueCodes.SignatureMismatch
            && issue.Expected is null
            && issue.Actual is null);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_DetectsRewrittenAnchoredAuditLog()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-003");
        var anchoredEntry = await db.AuditLogs.SingleAsync(a => a.Id == checkpoint.LastAuditLogId);
        anchoredEntry.IntegrityHash = new string('a', 64);
        await db.SaveChangesAsync();

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Issues, issue =>
            issue.CheckpointId == checkpoint.Id
            && issue.AuditLogId == checkpoint.LastAuditLogId
            && issue.Code == AuditIntegrityCheckpointIssueCodes.AnchoredHashMismatch
            && issue.Expected == checkpoint.LastIntegrityHash
            && issue.Actual == new string('a', 64));
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_DetectsMissingAnchoredAuditLog()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-missing");
        var anchoredEntry = await db.AuditLogs.SingleAsync(a => a.Id == checkpoint.LastAuditLogId);
        db.AuditLogs.Remove(anchoredEntry);
        await db.SaveChangesAsync();

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Issues, issue =>
            issue.CheckpointId == checkpoint.Id
            && issue.AuditLogId == checkpoint.LastAuditLogId
            && issue.Code == AuditIntegrityCheckpointIssueCodes.AnchoredAuditLogMissing);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_VerifiesLatestCheckpointWhenMultipleExist()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var firstCheckpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-005-a");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");

        var latestCheckpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-005-b");
        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.True(verification.IsValid);
        Assert.NotEqual(firstCheckpoint.Id, verification.CheckpointId);
        Assert.Equal(latestCheckpoint.Id, verification.CheckpointId);
        Assert.Equal(latestCheckpoint.LastAuditLogId, verification.LastAuditLogId);
        Assert.Equal(2, latestCheckpoint.CheckedEntries);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_RefusesCheckpointForInvalidCompanyChain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            EntityType = "LegacySeed",
            EntityId = period.Id,
            Action = "Seeded"
        });
        await db.SaveChangesAsync();
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompanyCheckpointAsync(
                period.CompanyId,
                createdByUserId: "owner@example.ie",
                createdByDisplayName: "Owner User",
                requestId: "req-anchor-004"));

        Assert.Contains("valid audit integrity chain", error.Message);
        Assert.Empty(db.AuditIntegrityCheckpoints);
    }

    [Fact]
    public async Task AuditCheckpointInputs_AllowsOnlyOwnersToCreateCheckpointsWithPlainForbiddenResponse()
    {
        Assert.Null(AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Owner")));
        var denial = AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Accountant"));
        Assert.NotNull(denial);
        Assert.NotNull(AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Reviewer")));
        Assert.NotNull(AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Client")));

        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        await denial!.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuditTrailMiddleware_RecordsRequestCorrelationAndActorMetadataForSucceededAndRejectedWrites()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var successContext = WriteAuditContext(
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status",
            "corr-success-001");
        var rejectedContext = WriteAuditContext(
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments",
            "corr-rejected-001");

        var successMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var rejectedMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        await successMiddleware.InvokeAsync(successContext, db);
        await rejectedMiddleware.InvokeAsync(rejectedContext, db);

        var entries = await db.AuditLogs.Where(a => a.EntityType == "ApiRequest").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("ApiWriteSucceeded", entries[0].Action);
        Assert.Equal("ApiWriteRejected", entries[1].Action);
        AssertAuditLogValue(entries[0], "TenantId", 1);
        AssertAuditLogValue(entries[0], "RequestId", "corr-success-001");
        AssertAuditLogValue(entries[0], "ActorDisplayName", "Maeve Reviewer");
        AssertAuditLogValue(entries[1], "TenantId", 1);
        AssertAuditLogValue(entries[1], "RequestId", "corr-rejected-001");
        AssertAuditLogValue(entries[1], "ActorDisplayName", "Maeve Reviewer");
    }

    [Fact]
    public async Task AuditTrailMiddleware_DoesNotPersistPendingBusinessChangesWhenAuditingRejectedWrite()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using (var seedDb = CreateDbContext(databaseName, root))
        {
            await SeedCompanyPeriodAsync(seedDb, isFirstYear: true);
        }

        await using var requestDb = CreateDbContext(databaseName, root);
        var period = await requestDb.AccountingPeriods.SingleAsync();
        var originalStatus = period.Status;
        var context = WriteAuditContext(
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments",
            "corr-rejected-pending");
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            period.Status = PeriodStatus.Finalised;
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, requestDb);

        await using var verifyDb = CreateDbContext(databaseName, root);
        var persisted = await verifyDb.AccountingPeriods.AsNoTracking().SingleAsync();
        Assert.Equal(originalStatus, persisted.Status);
        Assert.Equal("ApiWriteRejected", (await verifyDb.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task AuditService_LogAsync_UsesDatabaseStableTimestampPrecisionForHash()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        for (var i = 0; i < 25; i++)
        {
            await audit.LogAsync(
                period.CompanyId,
                period.Id,
                "FilingRegime",
                period.Id,
                "FilingRegimeDetermined",
                newValue: new { Sequence = i });
        }

        var entries = await db.AuditLogs.ToListAsync();
        Assert.All(entries, entry => Assert.Equal(0, entry.Timestamp.Ticks % 10));
    }

    [Fact]
    public async Task AuditTrailMiddleware_DoesNotLogSafeReadsButLogsRejectedAndFailedWrites()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var getContext = new DefaultHttpContext();
        getContext.Request.Method = HttpMethods.Get;
        getContext.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/statements/readiness";
        var failedContext = new DefaultHttpContext();
        failedContext.Request.Method = HttpMethods.Post;
        failedContext.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments";
        failedContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm A",
            "reviewer@example.ie",
            "Maeve Reviewer",
            "Reviewer");
        var readMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var failedMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var exceptionContext = new DefaultHttpContext();
        exceptionContext.Request.Method = HttpMethods.Post;
        exceptionContext.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status";
        exceptionContext.Items[AuthContext.ItemKey] = failedContext.Items[AuthContext.ItemKey];
        var exceptionMiddleware = new AuditTrailMiddleware(_ =>
            throw new InvalidOperationException("database password=secret failure"));

        await readMiddleware.InvokeAsync(getContext, db);
        await failedMiddleware.InvokeAsync(failedContext, db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exceptionMiddleware.InvokeAsync(exceptionContext, db));

        var entries = await db.AuditLogs.Where(a => a.EntityType == "ApiRequest").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("ApiWriteRejected", entries[0].Action);
        Assert.Equal(StatusCodes.Status400BadRequest, JsonDocument.Parse(entries[0].NewValueJson!).RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("ApiWriteFailed", entries[1].Action);
        Assert.Contains("InvalidOperationException", entries[1].NewValueJson);
        Assert.DoesNotContain("password=secret", entries[1].NewValueJson);
    }

    [Fact]
    public async Task AuditTrailMiddleware_ExtractsPeriodIdFromImportQueryString()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{period.CompanyId}/bank-accounts/4/import";
        context.Request.QueryString = QueryString.Create("periodId", period.Id.ToString());
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal(period.Id, entry.EntityId);
    }

    [Fact]
    public async Task AuditTrailMiddleware_IgnoresQueryPeriodIdOutsideVerifiedImportRoute()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{period.CompanyId}";
        context.Request.QueryString = QueryString.Create("periodId", period.Id.ToString());
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.Null(entry.PeriodId);
        Assert.Equal(period.CompanyId, entry.EntityId);
    }

    [Fact]
    public async Task ExceptionMiddleware_ReturnsClientSafeBusinessRuleMessages()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestEnvironment("Production"))
                .BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        var exception = new ExceptionMiddleware(
            _ => throw new BusinessRuleException("Confirm the filing regime before generating the CRO filing pack."),
            NullLogger<ExceptionMiddleware>.Instance);

        await exception.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Confirm the filing regime before generating the CRO filing pack.", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExceptionMiddleware_MasksUnexpectedExceptionsInProduction()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestEnvironment("Production"))
                .BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        var exception = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("Npgsql failure for password=secret on cro_filing_packages"),
            NullLogger<ExceptionMiddleware>.Instance);

        await exception.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var error = payload.RootElement.GetProperty("error").GetString();
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An internal error occurred. Please try again.", error);
        Assert.DoesNotContain("Npgsql", error);
        Assert.DoesNotContain("secret", error);
    }

    [Fact]
    public async Task AuditLogEndpoint_ReachesStableThirdPageBeyond125Events()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var baseTimestamp = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var events = Enumerable.Range(1, 127)
            .Select(sequence => new AuditLog
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                EntityType = "ImportedTransaction",
                EntityId = sequence,
                Action = $"transaction.audit.{sequence}",
                UserId = "accountant@example.ie",
                Timestamp = baseTimestamp.AddMinutes((sequence - 1) / 2)
            })
            .ToList();
        db.AuditLogs.AddRange(events);
        await db.SaveChangesAsync();

        var result = await AdjustmentEndpoints.GetAuditLogEndpointAsync(
            period.CompanyId,
            db,
            AuthenticatedRequest(
                "Accountant",
                HttpMethods.Get,
                $"/api/companies/{period.CompanyId}/audit-log/?periodId={period.Id}&page=3&pageSize=50"),
            period.Id,
            3,
            50);

        var payload = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        var payloadType = payload.GetType();
        var items = Assert.IsAssignableFrom<List<AuditLog>>(payloadType.GetProperty("items")!.GetValue(payload));
        var expectedIds = events
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id)
            .Skip(100)
            .Take(50)
            .Select(entry => entry.Id)
            .ToArray();

        Assert.Equal(127, (int)payloadType.GetProperty("total")!.GetValue(payload)!);
        Assert.Equal(3, (int)payloadType.GetProperty("page")!.GetValue(payload)!);
        Assert.Equal(50, (int)payloadType.GetProperty("pageSize")!.GetValue(payload)!);
        Assert.Equal(3, (int)payloadType.GetProperty("totalPages")!.GetValue(payload)!);
        Assert.True((bool)payloadType.GetProperty("hasPreviousPage")!.GetValue(payload)!);
        Assert.False((bool)payloadType.GetProperty("hasNextPage")!.GetValue(payload)!);
        Assert.Equal(27, items.Count);
        Assert.Equal(expectedIds, items.Select(entry => entry.Id));
    }

    [Fact]
    public async Task SizeClassificationSaveEndpoint_LogsDomainAuditWithOldAndNewValues()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Owner",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/size-classification");

        var result = await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            period.CompanyId,
            period.Id,
            new SizeClassificationInput(120_000m, 40_000m, 3, null),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.IsAssignableFrom<IResult>(result);
        var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.SizeClassificationDataSaved);
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal("SizeClassification", entry.EntityType);
        Assert.Equal("user:1", entry.UserId);
        Assert.Null(entry.OldValueJson);
        Assert.Contains("\"Turnover\":120000", entry.NewValueJson);
        Assert.Contains("\"AvgEmployees\":3", entry.NewValueJson);
    }

    [Fact]
    public async Task ClassificationAndFilingRegime_LogStatutoryDecisionAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 40_000m,
            AvgEmployees = 3
        });
        await db.SaveChangesAsync();

        var audit = new AuditService(db);
        var classification = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()), audit);
        await classification.ClassifyAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var filingRegime = new FilingRegimeService(db, new DeadlineService(db), audit);
        await filingRegime.DetermineAsync(period.CompanyId, period.Id, ElectedRegime.Micro, "reviewer@example.ie");

        var classifyAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.SizeClassificationRun);
        Assert.Equal(period.CompanyId, classifyAudit.CompanyId);
        Assert.Equal(period.Id, classifyAudit.PeriodId);
        Assert.Equal("reviewer@example.ie", classifyAudit.UserId);
        Assert.Contains("\"CalculatedClass\":\"Micro\"", classifyAudit.NewValueJson);
        Assert.Contains("\"AuditExempt\":true", classifyAudit.NewValueJson);

        var regimeAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.FilingRegimeDetermined);
        Assert.Equal(period.CompanyId, regimeAudit.CompanyId);
        Assert.Equal(period.Id, regimeAudit.PeriodId);
        Assert.Contains("\"ElectedRegime\":\"Micro\"", regimeAudit.NewValueJson);
        Assert.Contains("\"RequiredStatements\"", regimeAudit.NewValueJson);
    }

    [Fact]
    public async Task DeadlineMarkFiled_LogsPenaltyAndFiledDateAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);

        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, croDeadline.DueDate.AddDays(3), "reviewer@example.ie");

        var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Contains("\"FiledDate\":null", entry.OldValueJson);
        Assert.Contains("\"IsLate\":true", entry.NewValueJson);
        Assert.Contains("\"PenaltyAmount\":109", entry.NewValueJson);
    }

    [Fact]
    public async Task DeadlineMarkFiled_UpsertsFilingHistorySoDuplicateCallsDoNotRemoveAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var service = new DeadlineService(db);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id);
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);
        var firstFiledDate = croDeadline.DueDate.AddDays(3);
        var correctedFiledDate = croDeadline.DueDate.AddDays(4);
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);

        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, firstFiledDate);
        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, firstFiledDate);
        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, correctedFiledDate);

        var histories = await db.FilingHistories
            .Where(h => h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.CRO)
            .ToListAsync();
        var jeopardy = await service.CheckAuditExemptionJeopardyAsync(period.CompanyId);

        var history = Assert.Single(histories);
        Assert.Equal(correctedFiledDate, history.FiledDate);
        Assert.Equal(4, history.DaysLate);
        Assert.False(jeopardy.HasLostExemption);
        Assert.Equal(1, jeopardy.LateFilingCount);
    }

    [Fact]
    public async Task FilingWorkflow_LogsCroAndIxbrlDomainAudits()
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

        var audit = new AuditService(db);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl, audit);

        await workflow.RecordCroDocumentGeneratedAsync(
            period.CompanyId,
            period.Id,
            "accounts",
            "reviewer@example.ie",
            [1, 2, 3]);
        await workflow.ValidateIxbrlAsync(period.CompanyId, period.Id, "reviewer@example.ie");

        var generated = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.CroDocumentGenerated);
        Assert.Equal("CroFilingPackage", generated.EntityType);
        Assert.Equal(period.CompanyId, generated.CompanyId);
        Assert.Equal(period.Id, generated.PeriodId);
        Assert.Contains("\"DocumentType\":\"accounts\"", generated.NewValueJson);

        var ixbrlAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.IxbrlInternalCheckCompleted);
        Assert.Equal("RevenueFilingPackage", ixbrlAudit.EntityType);
        Assert.Equal(period.CompanyId, ixbrlAudit.CompanyId);
        Assert.Equal(period.Id, ixbrlAudit.PeriodId);
        Assert.Contains("\"IxbrlGenerated\":false", ixbrlAudit.NewValueJson);
        Assert.Contains("\"IxbrlValidated\":false", ixbrlAudit.NewValueJson);
        Assert.Contains("filing-ready iXBRL generation is disabled", ixbrlAudit.NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsTaxReviewNotesAndShareCapitalAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var accountantContext = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/tax-balances/CorporationTax");
        var reviewerContext = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/year-end-reviews/tax");

        await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            TaxType.CorporationTax,
            new TaxBalance { Liability = 1_250m, Paid = 250m, Balance = 1_000m },
            db,
            audit,
            accountantContext);
        await YearEndEndpoints.UpdateYearEndReviewEndpointAsync(
            period.CompanyId,
            period.Id,
            "tax",
            new YearEndReviewInput(true, null, "Tax balance agreed to CT computation."),
            db,
            audit,
            reviewerContext);

        var notes = new NotesDisclosureService(db);
        await YearEndEndpoints.GenerateNotesEndpointAsync(period.CompanyId, period.Id, notes, db, audit, accountantContext);
        var note = await db.NotesDisclosures.FirstAsync(n => n.PeriodId == period.Id);
        var requiredNoteUpdate = await YearEndEndpoints.UpdateNoteEndpointAsync(
            period.CompanyId,
            period.Id,
            note.Id,
            new NotesDisclosure
            {
                Title = note.Title,
                Content = note.Content + "\nReviewed by the board.",
                IsIncluded = true
            },
            db,
            audit,
            accountantContext);
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(requiredNoteUpdate));

        var writeGuard = new AccountingWriteGuard(db);
        await YearEndEndpoints.CreateShareCapitalEndpointAsync(
            period.CompanyId,
            new ShareCapital
            {
                ShareClass = "Ordinary",
                NominalValue = 1m,
                NumberIssued = 100,
                IsFullyPaid = true,
                IssueDate = period.PeriodStart
            },
            db,
            writeGuard,
            audit,
            accountantContext);

        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.TaxBalanceUpserted
            && a.EntityType == "TaxBalance"
            && a.PeriodId == period.Id
            && a.NewValueJson!.Contains("\"Balance\":1000")
            && a.NewValueJson.Contains("\"WasCreated\":true"));
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.YearEndReviewConfirmationUpdated
            && a.NewValueJson!.Contains("\"SectionKey\":\"tax\"")
            && a.NewValueJson.Contains("\"Confirmed\":true"));
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.NotesGenerated
            && a.EntityType == "NotesDisclosureBatch"
            && a.NewValueJson!.Contains("\"GeneratedCount\""));
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.NoteDisclosureUpdated);
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.ShareCapitalCreated
            && a.NewValueJson!.Contains("\"TotalValue\":100"));
    }

    [Fact]
    public async Task AdjustmentEvidence_RejectsInvalidEndpointMutationsWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var debit = AddCategory(db, period.CompanyId, "6811", "Audit fees", AccountCategoryType.Expense);
        var credit = AddCategory(db, period.CompanyId, "2101", "Accruals", AccountCategoryType.Liability);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "6812", "Other audit fees", AccountCategoryType.Expense);
        var otherCreditCategory = AddCategory(db, otherPeriod.CompanyId, "2102", "Other accruals", AccountCategoryType.Liability);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();
        var input = new AdjustmentInput(
            Description: "Accrual",
            DebitCategoryId: debit.Id,
            CreditCategoryId: credit.Id,
            Amount: 300m,
            Reason: "Year-end",
            LegalBasis: "FRS 102",
            ImpactOnProfit: -300m,
            ImpactOnAssets: 0m);
        var adjustment = new Adjustment
        {
            PeriodId = period.Id,
            Description = "Original accrual",
            DebitCategoryId = debit.Id,
            CreditCategoryId = credit.Id,
            Amount = 250m,
            Reason = "Original",
            LegalBasis = "FRS 102",
            ImpactOnProfit = -250m,
            ImpactOnAssets = 0m,
            CreatedBy = "Original reviewer"
        };
        var otherAdjustment = new Adjustment
        {
            PeriodId = otherPeriod.Id,
            Description = "Other company accrual",
            DebitCategoryId = otherCategory.Id,
            CreditCategoryId = otherCreditCategory.Id,
            Amount = 900m,
            Reason = "Other company",
            LegalBasis = "FRS 102",
            ImpactOnProfit = -900m,
            ImpactOnAssets = 0m,
            CreatedBy = "Other reviewer"
        };
        db.Adjustments.AddRange(adjustment, otherAdjustment);
        await db.SaveChangesAsync();

        var wrongCategoryResult = await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            input with { DebitCategoryId = otherCategory.Id },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments"),
            apiAccess);
        var invalidCategoryUpdate = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            input with { DebitCategoryId = otherCategory.Id },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        var wrongPeriodUpdate = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            adjustment.Id,
            input,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{otherPeriod.CompanyId}/periods/{otherPeriod.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        var wrongPeriodApprove = await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Reviewer", HttpMethods.Post, $"/api/companies/{otherPeriod.CompanyId}/periods/{otherPeriod.Id}/adjustments/{adjustment.Id}/approve"),
            apiAccess);
        var wrongPeriodDelete = await AdjustmentEndpoints.DeleteAdjustmentEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{otherPeriod.CompanyId}/periods/{otherPeriod.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        var mismatchedCompanyPeriodCreate = await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            input,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments"),
            apiAccess);
        var mismatchedCompanyPeriodUpdate = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherAdjustment.Id,
            input,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/{otherAdjustment.Id}"),
            apiAccess);
        var mismatchedCompanyPeriodApprove = await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherAdjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Reviewer", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/{otherAdjustment.Id}/approve"),
            apiAccess);
        var mismatchedCompanyPeriodDelete = await AdjustmentEndpoints.DeleteAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherAdjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/{otherAdjustment.Id}"),
            apiAccess);

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(wrongCategoryResult));
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(invalidCategoryUpdate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriodUpdate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriodApprove));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriodDelete));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodCreate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodUpdate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodApprove));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodDelete));
        Assert.Equal("Original accrual", (await db.Adjustments.SingleAsync(a => a.Id == adjustment.Id)).Description);
        Assert.Equal("Other company accrual", (await db.Adjustments.SingleAsync(a => a.Id == otherAdjustment.Id)).Description);
        Assert.Equal(2, await db.Adjustments.CountAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Adjustment").ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_LogsSingleAndBulkTransactionCategorisationAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var sales = AddCategory(db, period.CompanyId, "4001", "Sales", AccountCategoryType.Income);
        var fees = AddCategory(db, period.CompanyId, "6811", "Fees", AccountCategoryType.Expense);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(1),
                Description = "Invoice receipt",
                Amount = 500m,
                CategoryId = sales.Id,
                ConfidenceScore = 0.6m
            },
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(2),
                Description = "Bank fee",
                Amount = -10m
            },
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(3),
                Description = "Legal fee",
                Amount = -50m
            });
        await db.SaveChangesAsync();
        var transactions = await db.ImportedTransactions.OrderBy(t => t.Date).ToListAsync();
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transactions[0].Id,
            new CategoriseInput(fees.Id),
            db,
            audit,
            context,
            DisabledApiAccess());
        await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            new BulkCategoriseInput([transactions[1].Id, transactions[2].Id], fees.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        var refreshed = await db.ImportedTransactions.OrderBy(t => t.Date).ToListAsync();
        Assert.All(refreshed, transaction =>
        {
            Assert.Equal(fees.Id, transaction.CategoryId);
            Assert.True(transaction.ManualOverride);
            Assert.Equal(1.0m, transaction.ConfidenceScore);
        });

        var singleAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.TransactionCategorised);
        Assert.Equal(period.CompanyId, singleAudit.CompanyId);
        Assert.Equal(period.Id, singleAudit.PeriodId);
        Assert.Equal("ImportedTransaction", singleAudit.EntityType);
        Assert.Equal("user:1", singleAudit.UserId);
        Assert.Contains("\"CategoryId\":" + sales.Id, singleAudit.OldValueJson);
        Assert.Contains("\"CategoryId\":" + fees.Id, singleAudit.NewValueJson);
        Assert.Contains("\"ManualOverride\":true", singleAudit.NewValueJson);

        var bulkAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.TransactionsBulkCategorised);
        Assert.Equal("ImportedTransactionBatch", bulkAudit.EntityType);
        Assert.Contains("\"RequestedCount\":2", bulkAudit.NewValueJson);
        Assert.Contains("\"UpdatedCount\":2", bulkAudit.NewValueJson);
        Assert.Contains("\"CategoryId\":" + fees.Id, bulkAudit.NewValueJson);
        Assert.Contains("\"Description\":\"Bank fee\"", bulkAudit.NewValueJson);
        Assert.Contains("\"ManualOverride\":true", bulkAudit.NewValueJson);
    }

    [Fact]
    public async Task BankingEvidence_RejectsInvalidCategorisationWithoutMutationOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4002", "Sales", AccountCategoryType.Income);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "4003", "Other sales", AccountCategoryType.Income);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        var otherBank = new BankAccount
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.AddRange(bank, otherBank);
        await db.SaveChangesAsync();
        var transaction = new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Receipt",
            Amount = 250m,
            CategoryId = category.Id
        };
        var otherTransaction = new ImportedTransaction
        {
            BankAccountId = otherBank.Id,
            PeriodId = otherPeriod.Id,
            Date = otherPeriod.PeriodStart.AddDays(1),
            Description = "Other receipt",
            Amount = 999m,
            CategoryId = otherCategory.Id
        };
        db.ImportedTransactions.AddRange(transaction, otherTransaction);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        var invalidCategory = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transaction.Id,
            new CategoriseInput(otherCategory.Id),
            db,
            audit,
            context,
            DisabledApiAccess());
        var wrongPeriod = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            transaction.Id,
            new CategoriseInput(otherCategory.Id),
            db,
            audit,
            context,
            DisabledApiAccess());
        var partialBulk = await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            new BulkCategoriseInput([transaction.Id, otherTransaction.Id], category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(invalidCategory));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriod));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(partialBulk));
        Assert.Equal(category.Id, (await db.ImportedTransactions.SingleAsync(t => t.Id == transaction.Id)).CategoryId);
        Assert.Equal(otherCategory.Id, (await db.ImportedTransactions.SingleAsync(t => t.Id == otherTransaction.Id)).CategoryId);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType.StartsWith("ImportedTransaction")).ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_CategoriseDoesNotPersistWhenAuditWriteFails()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        int companyId;
        int periodId;
        int transactionId;
        int categoryId;

        await using (var setup = CreateDbContext(databaseName, root))
        {
            var period = await SeedCompanyPeriodAsync(setup, isFirstYear: true);
            var category = AddCategory(setup, period.CompanyId, "4005", "Sales", AccountCategoryType.Income);
            var bank = new BankAccount
            {
                CompanyId = period.CompanyId,
                Name = "Current account",
                OpeningBalance = 0m
            };
            setup.BankAccounts.Add(bank);
            await setup.SaveChangesAsync();
            var transaction = new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(1),
                Description = "Receipt",
                Amount = 250m
            };
            setup.ImportedTransactions.Add(transaction);
            await setup.SaveChangesAsync();
            companyId = period.CompanyId;
            periodId = period.Id;
            transactionId = transaction.Id;
            categoryId = category.Id;
        }

        await using (var failingAuditDb = CreateAuditFailingDbContext(databaseName, root))
        {
            var context = new DefaultHttpContext();
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BankingEndpoints.CategoriseTransactionEndpointAsync(
                    companyId,
                    periodId,
                    transactionId,
                    new CategoriseInput(categoryId),
                    failingAuditDb,
                    new AuditService(failingAuditDb),
                    context,
                    DisabledApiAccess()));
        }

        await using var verify = CreateDbContext(databaseName, root);
        var persisted = await verify.ImportedTransactions.SingleAsync(t => t.Id == transactionId);
        Assert.Null(persisted.CategoryId);
        Assert.False(persisted.ManualOverride);
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_BulkCategoriseRejectsMissingTransactionIdsWithoutMutationOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4006", "Sales", AccountCategoryType.Income);
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        var result = await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            new BulkCategoriseInput(null!, category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType.StartsWith("ImportedTransaction")).ToListAsync());
    }

    [Fact]
    public async Task YearEndEvidence_LogsReceivablePayableAndInventoryAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Debtor { Name = "Trade debtor", Amount = 500m, Type = DebtorType.Trade, Notes = "Invoice A" },
            db,
            audit,
            context);
        var debtor = await db.Debtors.SingleAsync(d => d.PeriodId == period.Id);
        await YearEndEndpoints.UpdateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            debtor.Id,
            new Debtor { Name = "Trade debtor revised", Amount = 450m, Type = DebtorType.Other, Notes = "Board agreed" },
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteDebtorEndpointAsync(period.CompanyId, period.Id, debtor.Id, db, audit, context);

        await YearEndEndpoints.CreateCreditorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Creditor { Name = "Trade creditor", Amount = 700m, Type = CreditorType.Trade, DueWithinYear = true, Notes = "Supplier" },
            db,
            audit,
            context);
        var creditor = await db.Creditors.SingleAsync(c => c.PeriodId == period.Id);
        await YearEndEndpoints.UpdateCreditorEndpointAsync(
            period.CompanyId,
            period.Id,
            creditor.Id,
            new Creditor { Name = "Accrual", Amount = 725m, Type = CreditorType.Accrual, DueWithinYear = false, Notes = "Legal fees" },
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteCreditorEndpointAsync(period.CompanyId, period.Id, creditor.Id, db, audit, context);

        await YearEndEndpoints.CreateInventoryEndpointAsync(
            period.CompanyId,
            period.Id,
            new Inventory { Description = "Finished goods", Value = 1_200m, ValuationMethod = ValuationMethod.Cost },
            db,
            audit,
            context);
        var inventory = await db.Inventories.SingleAsync(i => i.PeriodId == period.Id);
        await YearEndEndpoints.UpdateInventoryEndpointAsync(
            period.CompanyId,
            period.Id,
            inventory.Id,
            new Inventory { Description = "Finished goods write-down", Value = 1_050m, ValuationMethod = ValuationMethod.LowerOfCostAndNrv },
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteInventoryEndpointAsync(period.CompanyId, period.Id, inventory.Id, db, audit, context);

        var debtorAudits = await db.AuditLogs.Where(a => a.EntityType == "Debtor").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.DebtorCreated, AuditEventCodes.DebtorUpdated, AuditEventCodes.DebtorDeleted],
            debtorAudits.Select(a => a.Action).ToArray());
        Assert.All(debtorAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(debtorAudits[0].OldValueJson);
        Assert.Contains("\"Amount\":500", debtorAudits[0].NewValueJson);
        Assert.Contains("\"Amount\":500", debtorAudits[1].OldValueJson);
        Assert.Contains("\"Amount\":450", debtorAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", debtorAudits[2].NewValueJson);

        var creditorAudits = await db.AuditLogs.Where(a => a.EntityType == "Creditor").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.CreditorCreated, AuditEventCodes.CreditorUpdated, AuditEventCodes.CreditorDeleted],
            creditorAudits.Select(a => a.Action).ToArray());
        Assert.Contains("\"DueWithinYear\":true", creditorAudits[0].NewValueJson);
        Assert.Contains("\"DueWithinYear\":true", creditorAudits[1].OldValueJson);
        Assert.Contains("\"DueWithinYear\":false", creditorAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", creditorAudits[2].NewValueJson);

        var inventoryAudits = await db.AuditLogs.Where(a => a.EntityType == "Inventory").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.InventoryCreated, AuditEventCodes.InventoryUpdated, AuditEventCodes.InventoryDeleted],
            inventoryAudits.Select(a => a.Action).ToArray());
        Assert.Contains("\"Value\":1200", inventoryAudits[0].NewValueJson);
        Assert.Contains("\"ValuationMethod\":\"Cost\"", inventoryAudits[1].OldValueJson);
        Assert.Contains("\"Value\":1050", inventoryAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", inventoryAudits[2].NewValueJson);
    }

    [Fact]
    public async Task UpdateDividend_PersistsChangesAndLogsAudit()
    {
        // BL-25: dividends gained an update (PUT) endpoint so a recorded dividend can be corrected
        // in place rather than deleted and re-created.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/dividends/1");

        db.Dividends.Add(new Dividend { PeriodId = period.Id, Amount = 1_000m, DateDeclared = new DateOnly(2025, 6, 1) });
        await db.SaveChangesAsync();
        var dividend = await db.Dividends.SingleAsync(d => d.PeriodId == period.Id);

        await YearEndEndpoints.UpdateDividendEndpointAsync(
            period.CompanyId,
            period.Id,
            dividend.Id,
            new Dividend { Amount = 1_500m, DateDeclared = new DateOnly(2025, 6, 1), DatePaid = new DateOnly(2025, 7, 1) },
            db,
            audit,
            context);

        var updated = await db.Dividends.SingleAsync(d => d.Id == dividend.Id);
        Assert.Equal(1_500m, updated.Amount);
        Assert.Equal(new DateOnly(2025, 7, 1), updated.DatePaid);
        Assert.Contains(
            await db.AuditLogs.ToListAsync(),
            a => a.EntityType == "Dividend" && a.Action == AuditEventCodes.DividendUpdated);
    }

    [Fact]
    public async Task YearEndEvidence_LogsPayrollDividendAndGoingConcernAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/payroll");

        await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            period.CompanyId,
            period.Id,
            new PayrollSummary
            {
                GrossWages = 10_000m,
                EmployerPrsi = 1_100m,
                PensionContributions = 500m,
                StaffCount = 4
            },
            db,
            audit,
            context);
        await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            period.CompanyId,
            period.Id,
            new PayrollSummary
            {
                GrossWages = 12_000m,
                EmployerPrsi = 1_320m,
                PensionContributions = 600m,
                StaffCount = 5
            },
            db,
            audit,
            context);

        await YearEndEndpoints.CreateDividendEndpointAsync(
            period.CompanyId,
            period.Id,
            new Dividend
            {
                Amount = 1_500m,
                DateDeclared = period.PeriodEnd.AddDays(-14),
                DatePaid = period.PeriodEnd.AddDays(-7)
            },
            db,
            audit,
            context);
        var dividend = await db.Dividends.SingleAsync(d => d.PeriodId == period.Id);
        await YearEndEndpoints.DeleteDividendEndpointAsync(period.CompanyId, period.Id, dividend.Id, db, audit, context);

        await YearEndEndpoints.UpdateGoingConcernEndpointAsync(
            period.CompanyId,
            period.Id,
            new GoingConcernInput(false, "Directors will provide support for at least twelve months."),
            db,
            audit,
            context);
        await YearEndEndpoints.UpdateGoingConcernEndpointAsync(
            period.CompanyId,
            period.Id,
            new GoingConcernInput(true, "   "),
            db,
            audit,
            context);

        var payrollAudits = await db.AuditLogs.Where(a => a.EntityType == "PayrollSummary").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditEventCodes.PayrollSummaryUpserted, AuditEventCodes.PayrollSummaryUpserted], payrollAudits.Select(a => a.Action).ToArray());
        Assert.All(payrollAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(payrollAudits[0].OldValueJson);
        Assert.Contains("\"GrossWages\":10000", payrollAudits[0].NewValueJson);
        Assert.Contains("\"EmployerPrsi\":1100", payrollAudits[0].NewValueJson);
        Assert.Contains("\"PensionContributions\":500", payrollAudits[0].NewValueJson);
        Assert.Contains("\"StaffCount\":4", payrollAudits[0].NewValueJson);
        Assert.Contains("\"WasCreated\":true", payrollAudits[0].NewValueJson);
        Assert.Contains("\"GrossWages\":10000", payrollAudits[1].OldValueJson);
        Assert.Contains("\"EmployerPrsi\":1100", payrollAudits[1].OldValueJson);
        Assert.Contains("\"PensionContributions\":500", payrollAudits[1].OldValueJson);
        Assert.Contains("\"StaffCount\":4", payrollAudits[1].OldValueJson);
        Assert.Contains("\"GrossWages\":12000", payrollAudits[1].NewValueJson);
        Assert.Contains("\"EmployerPrsi\":1320", payrollAudits[1].NewValueJson);
        Assert.Contains("\"PensionContributions\":600", payrollAudits[1].NewValueJson);
        Assert.Contains("\"StaffCount\":5", payrollAudits[1].NewValueJson);
        Assert.Contains("\"WasCreated\":false", payrollAudits[1].NewValueJson);

        var dividendAudits = await db.AuditLogs.Where(a => a.EntityType == "Dividend").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditEventCodes.DividendCreated, AuditEventCodes.DividendDeleted], dividendAudits.Select(a => a.Action).ToArray());
        Assert.Null(dividendAudits[0].OldValueJson);
        Assert.Contains("\"Amount\":1500", dividendAudits[0].NewValueJson);
        Assert.Contains("\"DateDeclared\"", dividendAudits[0].NewValueJson);
        Assert.Contains("\"Amount\":1500", dividendAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", dividendAudits[1].NewValueJson);

        var goingConcernAudits = await db.AuditLogs
            .Where(a => a.EntityType == "AccountingPeriod" && a.Action == AuditEventCodes.GoingConcernUpdated)
            .OrderBy(a => a.Id)
            .ToListAsync();
        Assert.Equal(2, goingConcernAudits.Count);
        Assert.All(goingConcernAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal(period.Id, a.EntityId);
        });
        Assert.Contains("\"GoingConcernConfirmed\":true", goingConcernAudits[0].OldValueJson);
        Assert.Contains("\"GoingConcernConfirmed\":false", goingConcernAudits[0].NewValueJson);
        Assert.Contains("Directors will provide support", goingConcernAudits[0].NewValueJson);
        Assert.Contains("\"GoingConcernConfirmed\":false", goingConcernAudits[1].OldValueJson);
        Assert.Contains("Directors will provide support", goingConcernAudits[1].OldValueJson);
        Assert.Contains("\"GoingConcernConfirmed\":true", goingConcernAudits[1].NewValueJson);
        Assert.Contains("\"GoingConcernNote\":null", goingConcernAudits[1].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsFixedAssetAndLoanAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/fixed-assets");

        await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset
            {
                Name = "Laptop",
                Category = "Computer Equipment",
                Cost = 2_000m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                UsefulLifeYears = 3,
                DepreciationMethod = DepreciationMethod.StraightLine
            },
            db,
            writeGuard,
            audit,
            context);
        var asset = await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId);
        await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            period.CompanyId,
            asset.Id,
            new FixedAsset
            {
                Name = "Laptop revised",
                Category = "Computer Equipment",
                Cost = 2_200m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                DisposalDate = period.PeriodEnd,
                DisposalProceeds = 150m,
                UsefulLifeYears = 4,
                DepreciationMethod = DepreciationMethod.ReducingBalance
            },
            db,
            writeGuard,
            audit,
            context);
        await YearEndEndpoints.DeleteFixedAssetEndpointAsync(period.CompanyId, asset.Id, db, writeGuard, audit, context);

        await YearEndEndpoints.CreateLoanEndpointAsync(
            period.CompanyId,
            new Loan
            {
                Lender = "AIB",
                OriginalAmount = 10_000m,
                Balance = 8_500m,
                DrawdownDate = period.PeriodStart,
                BalanceAsOfDate = period.PeriodEnd,
                InterestRate = 5.25m,
                IsDirectorLoan = false,
                DueWithinYear = 3_000m,
                DueAfterYear = 5_500m
            },
            db,
            writeGuard,
            audit,
            context);
        var loan = await db.Loans.SingleAsync(l => l.CompanyId == period.CompanyId);
        await YearEndEndpoints.UpdateLoanEndpointAsync(
            period.CompanyId,
            loan.Id,
            new Loan
            {
                Lender = "AIB revised",
                OriginalAmount = 10_000m,
                Balance = 8_000m,
                DrawdownDate = period.PeriodStart,
                BalanceAsOfDate = period.PeriodEnd,
                InterestRate = 5.5m,
                IsDirectorLoan = false,
                DueWithinYear = 2_500m,
                DueAfterYear = 5_500m
            },
            db,
            writeGuard,
            audit,
            context);
        await YearEndEndpoints.DeleteLoanEndpointAsync(period.CompanyId, loan.Id, db, writeGuard, audit, context);

        var assetAudits = await db.AuditLogs.Where(a => a.EntityType == "FixedAsset").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.FixedAssetCreated, AuditEventCodes.FixedAssetUpdated, AuditEventCodes.FixedAssetDeleted],
            assetAudits.Select(a => a.Action).ToArray());
        Assert.All(assetAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Null(a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(assetAudits[0].OldValueJson);
        Assert.Contains("\"Name\":\"Laptop\"", assetAudits[0].NewValueJson);
        Assert.Contains("\"Cost\":2000", assetAudits[0].NewValueJson);
        Assert.Contains("\"DepreciationMethod\":\"StraightLine\"", assetAudits[0].NewValueJson);
        Assert.Contains("\"Cost\":2000", assetAudits[1].OldValueJson);
        Assert.Contains("\"Cost\":2200", assetAudits[1].NewValueJson);
        Assert.Contains("\"DepreciationMethod\":\"ReducingBalance\"", assetAudits[1].NewValueJson);
        Assert.Contains("\"DisposalProceeds\":150", assetAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", assetAudits[2].NewValueJson);

        var loanAudits = await db.AuditLogs.Where(a => a.EntityType == "Loan").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.LoanCreated, AuditEventCodes.LoanUpdated, AuditEventCodes.LoanDeleted],
            loanAudits.Select(a => a.Action).ToArray());
        Assert.All(loanAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Null(a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(loanAudits[0].OldValueJson);
        Assert.Contains("\"Lender\":\"AIB\"", loanAudits[0].NewValueJson);
        Assert.Contains("\"Balance\":8500", loanAudits[0].NewValueJson);
        Assert.Contains("\"DueAfterYear\":5500", loanAudits[0].NewValueJson);
        Assert.Contains("\"Balance\":8500", loanAudits[1].OldValueJson);
        Assert.Contains("\"Lender\":\"AIB revised\"", loanAudits[1].NewValueJson);
        Assert.Contains("\"Balance\":8000", loanAudits[1].NewValueJson);
        Assert.Contains("\"InterestRate\":5.5", loanAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", loanAudits[2].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsLoanSnapshotAndDirectorLoanAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "AIB",
            OriginalAmount = 10_000m,
            Balance = 9_000m,
            DrawdownDate = period.PeriodStart,
            BalanceAsOfDate = period.PeriodEnd,
            InterestRate = 5.25m,
            DueWithinYear = 3_000m,
            DueAfterYear = 6_000m
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/loan-balance-snapshots");
        var spoofedEnteredAt = new DateTime(2000, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        await YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 8_000m,
                Drawdowns = 2_000m,
                Repayments = 1_000m,
                ClosingBalance = 9_000m,
                DueWithinYear = 3_000m,
                DueAfterYear = 6_000m,
                Notes = "Bank statement support",
                EnteredBy = "Spoofed payload reviewer",
                EnteredAt = spoofedEnteredAt
            },
            db,
            audit,
            context);
        var snapshot = await db.LoanBalanceSnapshots.SingleAsync(s => s.PeriodId == period.Id);
        Assert.Equal("Example User", snapshot.EnteredBy);
        Assert.NotEqual(spoofedEnteredAt, snapshot.EnteredAt);
        await YearEndEndpoints.UpdateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            snapshot.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 9_000m,
                Drawdowns = 500m,
                Repayments = 1_500m,
                ClosingBalance = 8_000m,
                DueWithinYear = 2_000m,
                DueAfterYear = 6_000m,
                Notes = "Updated bank confirmation",
                EnteredBy = "Spoofed update reviewer"
            },
            db,
            audit,
            context);
        var updatedSnapshot = await db.LoanBalanceSnapshots.SingleAsync(s => s.PeriodId == period.Id);
        Assert.Equal("Example User", updatedSnapshot.EnteredBy);
        Assert.NotEqual(spoofedEnteredAt, updatedSnapshot.EnteredAt);
        await YearEndEndpoints.DeleteLoanBalanceSnapshotEndpointAsync(period.CompanyId, period.Id, snapshot.Id, db, audit, context);

        await YearEndEndpoints.CreateDirectorLoanEndpointAsync(
            period.CompanyId,
            period.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 0m,
                Advances: 1_000m,
                Repayments: 200m,
                ClosingBalance: 800m,
                InterestRate: 5m,
                InterestCharged: 40m,
                IsDocumented: true,
                LoanTerms: " Repayable on demand ",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        var directorLoan = await db.DirectorLoans.SingleAsync(d => d.PeriodId == period.Id);
        await YearEndEndpoints.UpdateDirectorLoanEndpointAsync(
            period.CompanyId,
            period.Id,
            directorLoan.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 800m,
                Advances: 200m,
                Repayments: 500m,
                ClosingBalance: 500m,
                InterestRate: 5.5m,
                InterestCharged: 25m,
                IsDocumented: false,
                LoanTerms: "Board minute required",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteDirectorLoanEndpointAsync(period.CompanyId, period.Id, directorLoan.Id, db, audit, context);

        var snapshotAudits = await db.AuditLogs.Where(a => a.EntityType == "LoanBalanceSnapshot").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.LoanBalanceSnapshotCreated, AuditEventCodes.LoanBalanceSnapshotUpdated, AuditEventCodes.LoanBalanceSnapshotDeleted],
            snapshotAudits.Select(a => a.Action).ToArray());
        Assert.All(snapshotAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(snapshotAudits[0].OldValueJson);
        Assert.Contains("\"LoanId\":" + loan.Id, snapshotAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":9000", snapshotAudits[0].NewValueJson);
        Assert.Contains("\"Notes\":\"Bank statement support\"", snapshotAudits[0].NewValueJson);
        Assert.Contains("\"EnteredBy\":\"Example User\"", snapshotAudits[0].NewValueJson);
        Assert.DoesNotContain("Spoofed", snapshotAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":9000", snapshotAudits[1].OldValueJson);
        Assert.Contains("\"ClosingBalance\":8000", snapshotAudits[1].NewValueJson);
        Assert.Contains("\"DueWithinYear\":2000", snapshotAudits[1].NewValueJson);
        Assert.Contains("\"EnteredBy\":\"Example User\"", snapshotAudits[1].NewValueJson);
        Assert.DoesNotContain("Spoofed", snapshotAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", snapshotAudits[2].NewValueJson);

        var directorLoanAudits = await db.AuditLogs.Where(a => a.EntityType == "DirectorLoan").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.DirectorLoanCreated, AuditEventCodes.DirectorLoanUpdated, AuditEventCodes.DirectorLoanDeleted],
            directorLoanAudits.Select(a => a.Action).ToArray());
        Assert.All(directorLoanAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(directorLoanAudits[0].OldValueJson);
        Assert.Contains("\"DirectorId\":" + director.Id, directorLoanAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":800", directorLoanAudits[0].NewValueJson);
        Assert.Contains("\"LoanTerms\":\"Repayable on demand\"", directorLoanAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":800", directorLoanAudits[1].OldValueJson);
        Assert.Contains("\"ClosingBalance\":500", directorLoanAudits[1].NewValueJson);
        Assert.Contains("\"IsDocumented\":false", directorLoanAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", directorLoanAudits[2].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsDisclosureFactAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/post-balance-sheet-events");

        await YearEndEndpoints.CreatePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            period.Id,
            new PostBalanceSheetEvent
            {
                Description = "Major customer insolvency",
                EventDate = period.PeriodEnd.AddDays(14),
                IsAdjusting = false,
                FinancialImpact = 12_500m,
                ActionRequired = "Disclose as non-adjusting event"
            },
            db,
            audit,
            context);
        var postBalanceSheetEvent = await db.PostBalanceSheetEvents.SingleAsync(e => e.PeriodId == period.Id);
        await YearEndEndpoints.DeletePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            period.Id,
            postBalanceSheetEvent.Id,
            db,
            audit,
            context);

        await YearEndEndpoints.CreateRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            new RelatedPartyTransaction
            {
                PartyName = "Director Services Limited",
                Relationship = "Director-controlled company",
                TransactionType = "Management fee",
                Amount = 4_200m,
                BalanceOwed = 800m,
                Terms = "30 days"
            },
            db,
            audit,
            context);
        var relatedPartyTransaction = await db.RelatedPartyTransactions.SingleAsync(r => r.PeriodId == period.Id);
        await YearEndEndpoints.DeleteRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            relatedPartyTransaction.Id,
            db,
            audit,
            context);

        await YearEndEndpoints.CreateContingentLiabilityEndpointAsync(
            period.CompanyId,
            period.Id,
            new ContingentLiability
            {
                Description = "Bank guarantee",
                Nature = "Guarantee",
                EstimatedAmount = 25_000m,
                Likelihood = "Possible"
            },
            db,
            audit,
            context);
        var contingentLiability = await db.ContingentLiabilities.SingleAsync(c => c.PeriodId == period.Id);
        await YearEndEndpoints.DeleteContingentLiabilityEndpointAsync(
            period.CompanyId,
            period.Id,
            contingentLiability.Id,
            db,
            audit,
            context);

        var eventAudits = await db.AuditLogs.Where(a => a.EntityType == "PostBalanceSheetEvent").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.PostBalanceSheetEventCreated, AuditEventCodes.PostBalanceSheetEventDeleted],
            eventAudits.Select(a => a.Action).ToArray());
        Assert.All(eventAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(eventAudits[0].OldValueJson);
        Assert.Contains("\"Description\":\"Major customer insolvency\"", eventAudits[0].NewValueJson);
        Assert.Contains("\"IsAdjusting\":false", eventAudits[0].NewValueJson);
        Assert.Contains("\"FinancialImpact\":12500", eventAudits[0].NewValueJson);
        Assert.Contains("\"Description\":\"Major customer insolvency\"", eventAudits[1].OldValueJson);
        Assert.Contains("\"FinancialImpact\":12500", eventAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", eventAudits[1].NewValueJson);

        var relatedPartyAudits = await db.AuditLogs.Where(a => a.EntityType == "RelatedPartyTransaction").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.RelatedPartyTransactionCreated, AuditEventCodes.RelatedPartyTransactionDeleted],
            relatedPartyAudits.Select(a => a.Action).ToArray());
        Assert.Null(relatedPartyAudits[0].OldValueJson);
        Assert.Contains("\"PartyName\":\"Director Services Limited\"", relatedPartyAudits[0].NewValueJson);
        Assert.Contains("\"Amount\":4200", relatedPartyAudits[0].NewValueJson);
        Assert.Contains("\"BalanceOwed\":800", relatedPartyAudits[0].NewValueJson);
        Assert.Contains("\"PartyName\":\"Director Services Limited\"", relatedPartyAudits[1].OldValueJson);
        Assert.Contains("\"Amount\":4200", relatedPartyAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", relatedPartyAudits[1].NewValueJson);

        var contingentLiabilityAudits = await db.AuditLogs.Where(a => a.EntityType == "ContingentLiability").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.ContingentLiabilityCreated, AuditEventCodes.ContingentLiabilityDeleted],
            contingentLiabilityAudits.Select(a => a.Action).ToArray());
        Assert.Null(contingentLiabilityAudits[0].OldValueJson);
        Assert.Contains("\"Description\":\"Bank guarantee\"", contingentLiabilityAudits[0].NewValueJson);
        Assert.Contains("\"EstimatedAmount\":25000", contingentLiabilityAudits[0].NewValueJson);
        Assert.Contains("\"Likelihood\":\"Possible\"", contingentLiabilityAudits[0].NewValueJson);
        Assert.Contains("\"Description\":\"Bank guarantee\"", contingentLiabilityAudits[1].OldValueJson);
        Assert.Contains("\"EstimatedAmount\":25000", contingentLiabilityAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", contingentLiabilityAudits[1].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsOpeningBalanceAndCustomNoteAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "3001", "Retained earnings", AccountCategoryType.Equity);
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 5,
            Title = "Accounting policies",
            Content = "Generated policy note.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{category.Id}");

        await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            category.Id,
            new OpeningBalanceInput(0m, 1_000m, " Opening reserves per prior year accounts ", "Spoofed reviewer", true),
            db,
            audit,
            context);
        await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            category.Id,
            new OpeningBalanceInput(0m, 1_250m, "Revised prior year accounts", "Spoofed reviewer", false),
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteOpeningBalanceEndpointAsync(period.CompanyId, period.Id, category.Id, db, audit, context);

        await YearEndEndpoints.CreateNoteEndpointAsync(
            period.CompanyId,
            period.Id,
            new NotesDisclosure
            {
                Title = "Custom guarantees note",
                Content = "The company provided a guarantee after year end.",
                IsRequired = true,
                IsIncluded = true
            },
            db,
            audit,
            context);
        var customNote = await db.NotesDisclosures.SingleAsync(n => n.PeriodId == period.Id && n.Title == "Custom guarantees note");
        Assert.False(customNote.IsRequired);
        await YearEndEndpoints.DeleteNoteEndpointAsync(period.CompanyId, period.Id, customNote.Id, db, audit, context);

        var openingBalanceAudits = await db.AuditLogs.Where(a => a.EntityType == "OpeningBalance").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.OpeningBalanceUpserted, AuditEventCodes.OpeningBalanceUpserted, AuditEventCodes.OpeningBalanceDeleted],
            openingBalanceAudits.Select(a => a.Action).ToArray());
        Assert.All(openingBalanceAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user:1", a.UserId);
        });
        Assert.Null(openingBalanceAudits[0].OldValueJson);
        Assert.Contains("\"AccountCategoryId\":" + category.Id, openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"Credit\":1000", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"SourceNote\":\"Opening reserves per prior year accounts\"", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"EnteredBy\":\"Example User\"", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"Reviewed\":true", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"WasCreated\":true", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"Credit\":1000", openingBalanceAudits[1].OldValueJson);
        Assert.Contains("\"Credit\":1250", openingBalanceAudits[1].NewValueJson);
        Assert.Contains("\"Reviewed\":false", openingBalanceAudits[1].NewValueJson);
        Assert.Contains("\"WasCreated\":false", openingBalanceAudits[1].NewValueJson);
        Assert.Contains("\"Credit\":1250", openingBalanceAudits[2].OldValueJson);
        Assert.Contains("\"Deleted\":true", openingBalanceAudits[2].NewValueJson);

        var noteAudits = await db.AuditLogs.Where(a => a.EntityType == "NotesDisclosure").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.NoteDisclosureCreated, AuditEventCodes.NoteDisclosureDeleted],
            noteAudits.Select(a => a.Action).ToArray());
        Assert.Null(noteAudits[0].OldValueJson);
        Assert.Contains("\"NoteNumber\":6", noteAudits[0].NewValueJson);
        Assert.Contains("\"Title\":\"Custom guarantees note\"", noteAudits[0].NewValueJson);
        Assert.Contains("\"IsRequired\":false", noteAudits[0].NewValueJson);
        Assert.Contains("\"ContentLength\":48", noteAudits[0].NewValueJson);
        Assert.Contains("\"NoteNumber\":6", noteAudits[1].OldValueJson);
        Assert.Contains("\"Title\":\"Custom guarantees note\"", noteAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", noteAudits[1].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidenceSemanticAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/debtors");

        var result = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new Debtor { Name = "Wrong company debtor", Amount = 100m, Type = DebtorType.Other },
            db,
            audit,
            context);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
        Assert.Empty(await db.Debtors.Where(d => d.PeriodId == otherPeriod.Id).ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Debtor").ToListAsync());
    }

    [Fact]
    public async Task YearEndEvidenceMoreSemanticAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/payroll");
        var existingOtherDividend = new Dividend
        {
            PeriodId = otherPeriod.Id,
            Amount = 2_000m,
            DateDeclared = otherPeriod.PeriodEnd
        };
        db.Dividends.Add(existingOtherDividend);
        await db.SaveChangesAsync();

        var payrollResult = await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new PayrollSummary { GrossWages = 10_000m, StaffCount = 4 },
            db,
            audit,
            context);
        var dividendResult = await YearEndEndpoints.CreateDividendEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new Dividend { Amount = 1_500m, DateDeclared = otherPeriod.PeriodEnd },
            db,
            audit,
            context);
        var deleteDividendResult = await YearEndEndpoints.DeleteDividendEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            existingOtherDividend.Id,
            db,
            audit,
            context);
        var goingConcernResult = await YearEndEndpoints.UpdateGoingConcernEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new GoingConcernInput(false, "Wrong company period"),
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(payrollResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(dividendResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteDividendResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(goingConcernResult).StatusCode);
        Assert.Empty(await db.PayrollSummaries.Where(p => p.PeriodId == otherPeriod.Id).ToListAsync());
        var remainingOtherDividend = await db.Dividends.SingleAsync(d => d.PeriodId == otherPeriod.Id);
        Assert.Equal(existingOtherDividend.Id, remainingOtherDividend.Id);
        Assert.Equal(2_000m, remainingOtherDividend.Amount);
        Assert.True((await db.AccountingPeriods.FindAsync(otherPeriod.Id))!.GoingConcernConfirmed);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("PayrollSummary", entityTypes);
        Assert.DoesNotContain("Dividend", entityTypes);
        Assert.DoesNotContain("AccountingPeriod", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceFixedAssetAndLoanAudits_RejectWrongCompanyUpdatesAndDeletesWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/fixed-assets/1");
        var otherAsset = new FixedAsset
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other company van",
            Category = "Motor Vehicles",
            Cost = 7_500m,
            AcquisitionDate = otherPeriod.PeriodStart,
            UsefulLifeYears = 5
        };
        var otherLoan = new Loan
        {
            CompanyId = otherPeriod.CompanyId,
            Lender = "Other bank",
            OriginalAmount = 20_000m,
            Balance = 18_000m,
            DrawdownDate = otherPeriod.PeriodStart,
            BalanceAsOfDate = otherPeriod.PeriodEnd,
            InterestRate = 4.5m,
            DueWithinYear = 6_000m,
            DueAfterYear = 12_000m
        };
        db.FixedAssets.Add(otherAsset);
        db.Loans.Add(otherLoan);
        await db.SaveChangesAsync();

        var updateAssetResult = await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            period.CompanyId,
            otherAsset.Id,
            new FixedAsset
            {
                Name = "Cross-company asset",
                Category = "Computer Equipment",
                Cost = 100m,
                AcquisitionDate = period.PeriodStart,
                UsefulLifeYears = 3
            },
            db,
            writeGuard,
            audit,
            context);
        var deleteAssetResult = await YearEndEndpoints.DeleteFixedAssetEndpointAsync(
            period.CompanyId,
            otherAsset.Id,
            db,
            writeGuard,
            audit,
            context);
        var updateLoanResult = await YearEndEndpoints.UpdateLoanEndpointAsync(
            period.CompanyId,
            otherLoan.Id,
            new Loan
            {
                Lender = "Cross-company loan",
                OriginalAmount = 1_000m,
                Balance = 900m,
                DrawdownDate = period.PeriodStart,
                BalanceAsOfDate = period.PeriodEnd,
                InterestRate = 3m,
                DueWithinYear = 900m
            },
            db,
            writeGuard,
            audit,
            context);
        var deleteLoanResult = await YearEndEndpoints.DeleteLoanEndpointAsync(
            period.CompanyId,
            otherLoan.Id,
            db,
            writeGuard,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateAssetResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteAssetResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteLoanResult).StatusCode);

        var remainingAsset = await db.FixedAssets.SingleAsync(a => a.Id == otherAsset.Id);
        var remainingLoan = await db.Loans.SingleAsync(l => l.Id == otherLoan.Id);
        Assert.Equal("Other company van", remainingAsset.Name);
        Assert.Equal(7_500m, remainingAsset.Cost);
        Assert.Equal("Other bank", remainingLoan.Lender);
        Assert.Equal(18_000m, remainingLoan.Balance);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("FixedAsset", entityTypes);
        Assert.DoesNotContain("Loan", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceLoanEvidenceAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "AIB",
            OriginalAmount = 10_000m,
            Balance = 9_000m,
            DrawdownDate = period.PeriodStart,
            BalanceAsOfDate = period.PeriodEnd,
            InterestRate = 5.25m,
            DueWithinYear = 3_000m,
            DueAfterYear = 6_000m
        };
        var otherLoan = new Loan
        {
            CompanyId = otherPeriod.CompanyId,
            Lender = "Other bank",
            OriginalAmount = 5_000m,
            Balance = 4_000m,
            DrawdownDate = otherPeriod.PeriodStart,
            BalanceAsOfDate = otherPeriod.PeriodEnd,
            InterestRate = 4.5m,
            DueWithinYear = 1_000m,
            DueAfterYear = 3_000m
        };
        var otherDirector = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == otherPeriod.CompanyId && o.Role == OfficerRole.Director);
        var otherDirectorLoan = new DirectorLoan
        {
            PeriodId = otherPeriod.Id,
            DirectorId = otherDirector.Id,
            OpeningBalance = 0m,
            Advances = 2_000m,
            Repayments = 500m,
            ClosingBalance = 1_500m,
            LoanTerms = "Other company support"
        };
        db.Loans.Add(loan);
        db.Loans.Add(otherLoan);
        db.DirectorLoans.Add(otherDirectorLoan);
        await db.SaveChangesAsync();
        var existingSnapshot = new LoanBalanceSnapshot
        {
            LoanId = loan.Id,
            PeriodId = period.Id,
            OpeningBalance = 9_000m,
            Drawdowns = 0m,
            Repayments = 1_000m,
            ClosingBalance = 8_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 6_000m,
            Notes = "Existing snapshot"
        };
        db.LoanBalanceSnapshots.Add(existingSnapshot);
        await db.SaveChangesAsync();
        var wrongCompanyLoanSnapshot = new LoanBalanceSnapshot
        {
            LoanId = otherLoan.Id,
            PeriodId = period.Id,
            OpeningBalance = 4_000m,
            Drawdowns = 0m,
            Repayments = 500m,
            ClosingBalance = 3_500m,
            DueWithinYear = 500m,
            DueAfterYear = 3_000m,
            Notes = "Inconsistent cross-company fixture"
        };
        db.LoanBalanceSnapshots.Add(wrongCompanyLoanSnapshot);
        await db.SaveChangesAsync();
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/loan-balance-snapshots");

        var createSnapshotWrongPeriodResult = await YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 9_000m,
                Drawdowns = 0m,
                Repayments = 1_000m,
                ClosingBalance = 8_000m,
                DueWithinYear = 2_000m,
                DueAfterYear = 6_000m
            },
            db,
            audit,
            context);
        var createSnapshotWrongCompanyLoanResult = await YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            new LoanBalanceSnapshot
            {
                LoanId = otherLoan.Id,
                OpeningBalance = 0m,
                Drawdowns = 0m,
                Repayments = 0m,
                ClosingBalance = 0m
            },
            db,
            audit,
            context);
        var updateSnapshotWrongPeriodResult = await YearEndEndpoints.UpdateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            existingSnapshot.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 8_000m,
                Drawdowns = 0m,
                Repayments = 1_000m,
                ClosingBalance = 7_000m,
                DueWithinYear = 1_000m,
                DueAfterYear = 6_000m
            },
            db,
            audit,
            context);
        var updateSnapshotWrongCompanyLoanResult = await YearEndEndpoints.UpdateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            existingSnapshot.Id,
            new LoanBalanceSnapshot
            {
                LoanId = otherLoan.Id,
                OpeningBalance = 8_000m,
                Drawdowns = 0m,
                Repayments = 1_000m,
                ClosingBalance = 7_000m,
                DueWithinYear = 1_000m,
                DueAfterYear = 6_000m
            },
            db,
            audit,
            context);
        var deleteSnapshotWrongPeriodResult = await YearEndEndpoints.DeleteLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            existingSnapshot.Id,
            db,
            audit,
            context);
        var deleteSnapshotWrongCompanyLoanResult = await YearEndEndpoints.DeleteLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            wrongCompanyLoanSnapshot.Id,
            db,
            audit,
            context);
        var createDirectorLoanWrongPeriodResult = await YearEndEndpoints.CreateDirectorLoanEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 0m,
                Advances: 1_000m,
                Repayments: 0m,
                ClosingBalance: 1_000m,
                InterestRate: 5m,
                InterestCharged: 0m,
                IsDocumented: true,
                LoanTerms: "Wrong period",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        var updateDirectorLoanWrongPeriodResult = await YearEndEndpoints.UpdateDirectorLoanEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherDirectorLoan.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 0m,
                Advances: 1_000m,
                Repayments: 0m,
                ClosingBalance: 1_000m,
                InterestRate: 5m,
                InterestCharged: 0m,
                IsDocumented: true,
                LoanTerms: "Wrong period",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        var deleteDirectorLoanResult = await YearEndEndpoints.DeleteDirectorLoanEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherDirectorLoan.Id,
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createSnapshotWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createSnapshotWrongCompanyLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateSnapshotWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateSnapshotWrongCompanyLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteSnapshotWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteSnapshotWrongCompanyLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createDirectorLoanWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateDirectorLoanWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteDirectorLoanResult).StatusCode);
        Assert.Empty(await db.LoanBalanceSnapshots.Where(s => s.PeriodId == otherPeriod.Id).ToListAsync());
        var periodSnapshots = await db.LoanBalanceSnapshots.Where(s => s.PeriodId == period.Id).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal([existingSnapshot.Id, wrongCompanyLoanSnapshot.Id], periodSnapshots.Select(s => s.Id).ToArray());
        var remainingSnapshot = periodSnapshots.Single(s => s.Id == existingSnapshot.Id);
        Assert.Equal(8_000m, remainingSnapshot.ClosingBalance);
        Assert.Equal("Existing snapshot", remainingSnapshot.Notes);
        Assert.NotNull(await db.LoanBalanceSnapshots.FindAsync(wrongCompanyLoanSnapshot.Id));
        var otherDirectorLoans = await db.DirectorLoans.Where(d => d.PeriodId == otherPeriod.Id).ToListAsync();
        Assert.Single(otherDirectorLoans);
        Assert.Equal(otherDirectorLoan.Id, otherDirectorLoans[0].Id);
        Assert.Empty(await db.DirectorLoans.Where(d => d.PeriodId == period.Id).ToListAsync());

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("LoanBalanceSnapshot", entityTypes);
        Assert.DoesNotContain("DirectorLoan", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceDisclosureFactAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherEvent = new PostBalanceSheetEvent
        {
            PeriodId = otherPeriod.Id,
            Description = "Other company event",
            EventDate = otherPeriod.PeriodEnd.AddDays(1)
        };
        var otherRelatedParty = new RelatedPartyTransaction
        {
            PeriodId = otherPeriod.Id,
            PartyName = "Other party",
            Relationship = "Director",
            TransactionType = "Loan",
            Amount = 1_000m
        };
        var otherContingentLiability = new ContingentLiability
        {
            PeriodId = otherPeriod.Id,
            Description = "Other guarantee",
            Nature = "Guarantee",
            EstimatedAmount = 5_000m
        };
        db.PostBalanceSheetEvents.Add(otherEvent);
        db.RelatedPartyTransactions.Add(otherRelatedParty);
        db.ContingentLiabilities.Add(otherContingentLiability);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/post-balance-sheet-events");

        var createEventResult = await YearEndEndpoints.CreatePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new PostBalanceSheetEvent { Description = "Wrong period event", EventDate = otherPeriod.PeriodEnd },
            db,
            audit,
            context);
        var deleteEventResult = await YearEndEndpoints.DeletePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherEvent.Id,
            db,
            audit,
            context);
        var createRelatedPartyResult = await YearEndEndpoints.CreateRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new RelatedPartyTransaction { PartyName = "Wrong party", Relationship = "Director", TransactionType = "Loan", Amount = 1_000m },
            db,
            audit,
            context);
        var deleteRelatedPartyResult = await YearEndEndpoints.DeleteRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherRelatedParty.Id,
            db,
            audit,
            context);
        var createContingentLiabilityResult = await YearEndEndpoints.CreateContingentLiabilityEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new ContingentLiability { Description = "Wrong liability", Nature = "Guarantee", EstimatedAmount = 5_000m },
            db,
            audit,
            context);
        var deleteContingentLiabilityResult = await YearEndEndpoints.DeleteContingentLiabilityEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherContingentLiability.Id,
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createEventResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteEventResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createRelatedPartyResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteRelatedPartyResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createContingentLiabilityResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteContingentLiabilityResult).StatusCode);
        var remainingEvent = await db.PostBalanceSheetEvents.SingleAsync(e => e.PeriodId == otherPeriod.Id);
        var remainingRelatedParty = await db.RelatedPartyTransactions.SingleAsync(r => r.PeriodId == otherPeriod.Id);
        var remainingContingentLiability = await db.ContingentLiabilities.SingleAsync(c => c.PeriodId == otherPeriod.Id);
        Assert.Equal(otherEvent.Id, remainingEvent.Id);
        Assert.Equal("Other company event", remainingEvent.Description);
        Assert.Equal(otherPeriod.PeriodEnd.AddDays(1), remainingEvent.EventDate);
        Assert.Equal(otherRelatedParty.Id, remainingRelatedParty.Id);
        Assert.Equal("Other party", remainingRelatedParty.PartyName);
        Assert.Equal(1_000m, remainingRelatedParty.Amount);
        Assert.Equal(otherContingentLiability.Id, remainingContingentLiability.Id);
        Assert.Equal("Other guarantee", remainingContingentLiability.Description);
        Assert.Equal(5_000m, remainingContingentLiability.EstimatedAmount);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("PostBalanceSheetEvent", entityTypes);
        Assert.DoesNotContain("RelatedPartyTransaction", entityTypes);
        Assert.DoesNotContain("ContingentLiability", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceOpeningBalanceAndCustomNoteAudits_RejectInvalidOwnershipWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "3002", "Opening equity", AccountCategoryType.Equity);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "3003", "Other company equity", AccountCategoryType.Equity);
        var otherOpeningBalance = new OpeningBalance
        {
            PeriodId = otherPeriod.Id,
            AccountCategoryId = otherCategory.Id,
            Credit = 2_000m,
            SourceNote = "Other company opening balance",
            EnteredBy = "Other reviewer",
            Reviewed = true
        };
        var crossCompanyCategoryOpeningBalance = new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = otherCategory.Id,
            Credit = 3_000m,
            SourceNote = "Cross-company category fixture",
            EnteredBy = "Prior import",
            Reviewed = true
        };
        var otherNote = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 1,
            Title = "Other company note",
            Content = "Other disclosure",
            IsIncluded = true
        };
        db.OpeningBalances.Add(otherOpeningBalance);
        db.OpeningBalances.Add(crossCompanyCategoryOpeningBalance);
        db.NotesDisclosures.Add(otherNote);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/opening-balances/{category.Id}");

        var upsertWrongPeriodResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            category.Id,
            new OpeningBalanceInput(0m, 1_000m, "Wrong period", null, true),
            db,
            audit,
            context);
        var upsertWrongCategoryResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            otherCategory.Id,
            new OpeningBalanceInput(0m, 1_000m, "Wrong category", null, true),
            db,
            audit,
            context);
        var deleteWrongPeriodResult = await YearEndEndpoints.DeleteOpeningBalanceEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherCategory.Id,
            db,
            audit,
            context);
        var deleteWrongCategoryResult = await YearEndEndpoints.DeleteOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            otherCategory.Id,
            db,
            audit,
            context);
        var createNoteWrongPeriodResult = await YearEndEndpoints.CreateNoteEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new NotesDisclosure { Title = "Wrong company note", Content = "Should not persist", IsIncluded = true },
            db,
            audit,
            context);
        var deleteNoteWrongPeriodResult = await YearEndEndpoints.DeleteNoteEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherNote.Id,
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(upsertWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(upsertWrongCategoryResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteWrongCategoryResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createNoteWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteNoteWrongPeriodResult).StatusCode);
        var remainingPeriodBalance = await db.OpeningBalances.SingleAsync(o => o.PeriodId == period.Id);
        Assert.Equal(crossCompanyCategoryOpeningBalance.Id, remainingPeriodBalance.Id);
        Assert.Equal(3_000m, remainingPeriodBalance.Credit);
        Assert.Equal("Cross-company category fixture", remainingPeriodBalance.SourceNote);
        var remainingOpeningBalance = await db.OpeningBalances.SingleAsync(o => o.PeriodId == otherPeriod.Id);
        Assert.Equal(otherOpeningBalance.Id, remainingOpeningBalance.Id);
        Assert.Equal(2_000m, remainingOpeningBalance.Credit);
        Assert.Equal("Other company opening balance", remainingOpeningBalance.SourceNote);
        Assert.Empty(await db.NotesDisclosures.Where(n => n.PeriodId == period.Id).ToListAsync());
        var remainingNote = await db.NotesDisclosures.SingleAsync(n => n.PeriodId == otherPeriod.Id);
        Assert.Equal(otherNote.Id, remainingNote.Id);
        Assert.Equal("Other company note", remainingNote.Title);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("OpeningBalance", entityTypes);
        Assert.DoesNotContain("NotesDisclosure", entityTypes);
    }

    [Fact]
    public void SecurityHeaders_EmitCspOnApiAndHstsOverHttps()
    {
        // BL-31: the backend (not just the frontend proxy) now emits a Content-Security-Policy on
        // /api responses and HSTS over HTTPS, as defence in depth for direct API access.
        var apiHttps = new DefaultHttpContext();
        apiHttps.Request.Scheme = "https";
        apiHttps.Request.Path = "/api/companies";
        SecurityHeadersMiddleware.ApplyTo(apiHttps);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'", apiHttps.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Contains("max-age=", apiHttps.Response.Headers["Strict-Transport-Security"].ToString());
        Assert.Equal("nosniff", apiHttps.Response.Headers["X-Content-Type-Options"].ToString());

        // Plain HTTP, non-/api: no HSTS (don't advertise over http) and no API CSP.
        var plain = new DefaultHttpContext();
        plain.Request.Scheme = "http";
        plain.Request.Path = "/health";
        SecurityHeadersMiddleware.ApplyTo(plain);
        Assert.False(plain.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.False(plain.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public void RateLimitClientKey_IgnoresForwardedForUnlessExplicitlyTrusted()
    {
        // BL-31: a spoofable X-Forwarded-For must not partition the rate limiter unless the deployment
        // has explicitly opted in (behind a trusted proxy); otherwise an attacker rotates XFF to evade it.
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 198.51.100.9";

        Assert.Equal("203.0.113.7", RateLimitClientKey.FromHttpContext(context, trustForwardedFor: false));
        Assert.Equal("10.0.0.1", RateLimitClientKey.FromHttpContext(context, trustForwardedFor: true));
    }

    [Fact]
    public async Task CharityInfoEndpoint_AuditsCreateAndUpdateWithRouteOwnedFields()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new CharityReportingService(db);
        var guard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();

        var create = await CharityEndpoints.SaveCharityInfoEndpointAsync(
            period.CompanyId,
            new CharityInfo
            {
                Id = 12_345,
                CompanyId = 99_999,
                CharityNumber = "CHY-CREATE",
                CharityType = "CLG",
                GrossIncome = 100_000m,
                CharitableObjectives = "Community development",
                GovernanceCodeCompliant = true,
                GovernanceCodeNote = "Board review complete",
                GovernanceEvidenceReference = "GOV-CREATE-001",
                GovernanceEvidenceArtifact = Encoding.UTF8.GetBytes("governance create evidence")
            },
            service,
            guard,
            audit,
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/charity/info"),
            apiAccess);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(create));
        var created = await db.CharityInfos.SingleAsync();
        Assert.NotEqual(12_345, created.Id);
        Assert.Equal(period.CompanyId, created.CompanyId);

        var update = await CharityEndpoints.SaveCharityInfoEndpointAsync(
            period.CompanyId,
            new CharityInfo
            {
                CompanyId = 88_888,
                CharityNumber = "CHY-UPDATE",
                CharityType = "CLG",
                GrossIncome = 750_000m,
                CharitableObjectives = "Community development and education",
                GovernanceCodeCompliant = true,
                GovernanceCodeNote = "Updated board review complete",
                GovernanceEvidenceReference = "GOV-UPDATE-001",
                GovernanceEvidenceArtifact = Encoding.UTF8.GetBytes("governance update evidence"),
                TrusteeRemunerationPaid = true,
                TrusteeRemunerationAmount = 500m
            },
            service,
            guard,
            audit,
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/charity/info"),
            apiAccess);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(update));
        var audits = await db.AuditLogs.Where(a => a.EntityType == "CharityInfo").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditEventCodes.CharityInfoCreated, AuditEventCodes.CharityInfoUpdated], audits.Select(a => a.Action).ToArray());
        Assert.Null(audits[0].OldValueJson);
        Assert.Contains("\"CharityNumber\":\"CHY-CREATE\"", audits[0].NewValueJson);
        Assert.Contains("\"CharityNumber\":\"CHY-CREATE\"", audits[1].OldValueJson);
        Assert.Contains("\"CharityNumber\":\"CHY-UPDATE\"", audits[1].NewValueJson);
        Assert.Contains("\"SorpTier\":2", audits[1].NewValueJson);
        Assert.All(audits, auditLog =>
        {
            Assert.Equal(period.CompanyId, auditLog.CompanyId);
            Assert.Null(auditLog.PeriodId);
            Assert.Equal("user:1", auditLog.UserId);
        });
    }

    [Fact]
    public async Task CharityFundEvidence_LogsCreateUpdateDeleteAuditsAndRecalculatesClosingBalance()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();
        var createContext = AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds");

        var create = await CharityEndpoints.CreateFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            new FundBalance
            {
                Id = 12_345,
                PeriodId = 99_999,
                FundName = "Restricted grant",
                FundType = "Restricted",
                OpeningBalance = 100m,
                IncomingResources = 250m,
                ResourcesExpended = 40m,
                Transfers = -10m,
                GainsLosses = 5m,
                Notes = "Grant award letter"
            },
            db,
            audit,
            createContext,
            apiAccess);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(create));
        var fund = await db.FundBalances.SingleAsync();
        Assert.NotEqual(12_345, fund.Id);
        Assert.Equal(period.Id, fund.PeriodId);
        Assert.Equal(305m, fund.ClosingBalance);

        var updateContext = AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds/{fund.Id}");
        var update = await CharityEndpoints.UpdateFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            fund.Id,
            new FundBalance
            {
                FundName = "Restricted grant revised",
                FundType = "Restricted",
                OpeningBalance = 100m,
                IncomingResources = 300m,
                ResourcesExpended = 75m,
                Transfers = -15m,
                GainsLosses = 0m,
                Notes = "Trustee-approved revision"
            },
            db,
            audit,
            updateContext,
            apiAccess);
        var deleteContext = AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds/{fund.Id}");
        var delete = await CharityEndpoints.DeleteFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            fund.Id,
            db,
            audit,
            deleteContext,
            apiAccess);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(update));
        Assert.Equal(StatusCodes.Status204NoContent, ResultStatusCode(delete));
        Assert.Empty(await db.FundBalances.ToListAsync());

        var audits = await db.AuditLogs.Where(a => a.EntityType == "FundBalance").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.FundBalanceCreated, AuditEventCodes.FundBalanceUpdated, AuditEventCodes.FundBalanceDeleted],
            audits.Select(a => a.Action).ToArray());
        Assert.Null(audits[0].OldValueJson);
        Assert.Contains("\"FundName\":\"Restricted grant\"", audits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":305", audits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":305", audits[1].OldValueJson);
        Assert.Contains("\"ClosingBalance\":310", audits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", audits[2].NewValueJson);
        Assert.All(audits, auditLog =>
        {
            Assert.Equal(period.CompanyId, auditLog.CompanyId);
            Assert.Equal(period.Id, auditLog.PeriodId);
            Assert.Equal("user:1", auditLog.UserId);
        });
    }

    [Fact]
    public async Task CharityFundEvidence_GuardsMismatchedAndLockedPeriodWritesWithoutMutationOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            ClosingBalance = 100m
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();

        var mismatchedCreate = await CharityEndpoints.CreateFundBalanceEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new FundBalance { FundName = "Wrong period", FundType = "Restricted", OpeningBalance = 1m },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/charity/funds"),
            apiAccess);
        var mismatchedUpdate = await CharityEndpoints.UpdateFundBalanceEndpointAsync(
            otherPeriod.CompanyId,
            period.Id,
            (await db.FundBalances.SingleAsync()).Id,
            new FundBalance { FundName = "Wrong company", FundType = "Restricted", OpeningBalance = 2m },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{otherPeriod.CompanyId}/periods/{period.Id}/charity/funds/{(await db.FundBalances.SingleAsync()).Id}"),
            apiAccess);
        var invalidCreate = await CharityEndpoints.CreateFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            new FundBalance { FundName = " ", FundType = "Restricted", OpeningBalance = 1m },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds"),
            apiAccess);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var lockedDelete = await CharityEndpoints.DeleteFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            (await db.FundBalances.SingleAsync()).Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds/{(await db.FundBalances.SingleAsync()).Id}"),
            apiAccess);

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCreate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedUpdate));
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(invalidCreate));
        Assert.Equal(StatusCodes.Status409Conflict, ResultStatusCode(lockedDelete));
        var fund = await db.FundBalances.SingleAsync();
        Assert.Equal("General fund", fund.FundName);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "FundBalance").ToListAsync());
    }

    [Fact]
    public void RateLimitClientKey_UsesForwardedClientIpFromFrontendProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.12");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.24, 172.18.0.10";

        var key = RateLimitClientKey.FromHttpContext(context, trustForwardedFor: true);

        Assert.Equal("203.0.113.24", key);
    }

    [Fact]
    public void RateLimitClientKey_IgnoresForwardedClientIpUnlessTrusted()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.12");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.24, 172.18.0.10";

        var key = RateLimitClientKey.FromHttpContext(context);

        Assert.Equal("172.18.0.12", key);
    }

    [Fact]
    public void RateLimitClientKey_FallsBackToRemoteIpWhenForwardedForIsInvalid()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.12");
        context.Request.Headers["X-Forwarded-For"] = "not an ip";

        var key = RateLimitClientKey.FromHttpContext(context, trustForwardedFor: true);

        Assert.Equal("172.18.0.12", key);
    }

}
