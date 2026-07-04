using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using Xunit;

namespace Accounts.Tests;

public class FilingGoldenCorpusScenarioTests
{
    static FilingGoldenCorpusScenarioTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness()
    {
        await using var db = CreateDbContext();
        var period = await SeedBasicScenarioAsync(
            db,
            "Dublin Community Support CLG",
            CompanyType.CompanyLimitedByGuarantee,
            CompanySizeClass.Small,
            ElectedRegime.Small,
            auditExempt: true,
            charity: true);
        await MarkGeneratedReviewedAndExternallyValidatedAsync(db, period, includeCharityReports: true);

        var statements = new FinancialStatementsService(db);
        var pdf = await new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var pdfText = ExtractPdfText(pdf);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id));
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        var notes = await db.NotesDisclosures.Where(n => n.PeriodId == period.Id && n.IsIncluded).ToListAsync();

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Contains("Dublin Community Support CLG", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Community support and education.", pdfText, StringComparison.OrdinalIgnoreCase);
        AssertWellFormedXml(ixbrl);
        Assert.Contains("Dublin Community Support CLG", ixbrl);
        Assert.Equal(62.50m, tax.TotalCorporationTax);
        Assert.Contains(notes, n => n.Title == "Accounting Policies" && n.IsIncluded);
        Assert.True(profile.SupportedPath);
        Assert.False(profile.ManualProfessionalReviewRequired);
        Assert.DoesNotContain(profile.BlockingIssues, issue => issue.Code.Contains("charity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "charity-number" && e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "charity-reports" && e.Satisfied);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId);
        Assert.Equal("ready-for-external-filing", profile.SignOffPacket.State);
        Assert.True(profile.SignOffPacket.ReadyForExternalFiling);
        Assert.Equal("Qualified Accountant", profile.SignOffPacket.ApprovedBy);
        var charityStep = Assert.Single(profile.SignOffPacket.Steps, step => step.Code == "charity-reporting");
        Assert.Equal("complete", charityStep.State);
        Assert.Contains(charityStep.Sources, s => s.SourceId == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId);
    }

    [Fact]
    public async Task GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence()
    {
        await using var db = CreateDbContext();
        var period = await SeedBasicScenarioAsync(
            db,
            "Midlands Manufacturing Limited",
            CompanyType.Private,
            CompanySizeClass.Medium,
            ElectedRegime.Medium,
            auditExempt: false,
            charity: false);
        await MarkGeneratedReviewedAndExternallyValidatedAsync(db, period, includeCharityReports: false);

        var statements = new FinancialStatementsService(db);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        var notes = await db.NotesDisclosures.Where(n => n.PeriodId == period.Id && n.IsIncluded).ToListAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.True(profile.SupportedPath);
        Assert.Equal(CompanySizeClass.Medium, profile.SizeClass);
        Assert.Equal(ElectedRegime.Medium, profile.ElectedRegime);
        Assert.False(profile.AuditExempt);
        Assert.True(profile.ManualProfessionalReviewRequired);
        var auditEvidence = Assert.Single(profile.RequiredEvidence, e => e.Code == "audit-report");
        Assert.True(auditEvidence.Required);
        Assert.False(auditEvidence.Satisfied);
        Assert.Contains(auditEvidence.Sources, s => s.SourceId == "cro-medium-company");
        Assert.Contains(auditEvidence.Sources, s => s.SourceId == "cro-auditors-report");
        var auditorBlocker = Assert.Single(profile.BlockingIssues, issue => issue.Code == "auditor-handoff-required");
        Assert.Contains(auditorBlocker.Sources, s => s.SourceId == "cro-medium-company");
        Assert.Contains(auditorBlocker.Sources, s => s.SourceId == "cro-auditors-report");
        Assert.Contains(profile.SourceReferences, s => s.SourceId == "cro-medium-company" && s.Url.EndsWith("/medium-company/"));
        Assert.Contains(profile.SourceReferences, s => s.SourceId == "cro-auditors-report" && s.Url.EndsWith("/auditors-report/"));
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.RevenueIxbrlContents.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.FrcFrs102.SourceId);
        Assert.Equal(62.50m, tax.TotalCorporationTax);
        Assert.Contains(notes, n => n.Title == "Turnover" && n.IsIncluded);
        Assert.Contains(notes, n => n.Title == "Tax on Profit on Ordinary Activities" && n.IsIncluded);
        Assert.Contains("auditor", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("signed auditor's report", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approve-cro-pack", profile.AllowedNextActions);
        Assert.DoesNotContain("mark-cro-submitted", profile.AllowedNextActions);
        Assert.Equal("manual-handoff", profile.SignOffPacket.State);
        var auditorStep = Assert.Single(profile.SignOffPacket.Steps, step => step.Code == "auditor-handoff");
        Assert.Equal("blocked", auditorStep.State);
        Assert.Contains(auditorStep.Sources, s => s.SourceId == IrishStatutoryRuleSources.CroAuditorsReport.SourceId);
        Assert.Contains(profile.SignOffPacket.OpenBlockers, message => message.Contains("signed auditor report", StringComparison.OrdinalIgnoreCase));

        period.AuditorsReportReceived = true;
        period.AuditorsReportReference = "AUD-2026-MIDLANDS-001";
        await db.SaveChangesAsync();

        var unblockedProfile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var satisfiedAuditEvidence = Assert.Single(unblockedProfile.RequiredEvidence, e => e.Code == "audit-report");
        Assert.True(satisfiedAuditEvidence.Satisfied);
        Assert.Contains("AUD-2026-MIDLANDS-001", satisfiedAuditEvidence.Detail);
        Assert.DoesNotContain(unblockedProfile.BlockingIssues, issue => issue.Code == "auditor-handoff-required");
        var unblockedAuditorStep = Assert.Single(unblockedProfile.SignOffPacket.Steps, step => step.Code == "auditor-handoff");
        Assert.Equal("complete", unblockedAuditorStep.State);
        Assert.Contains("AUD-2026-MIDLANDS-001", unblockedAuditorStep.Detail);

        var documents = new DocumentGeneratorService(db, statements);
        var pdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var pdfText = ExtractPdfText(pdf);
        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Contains("Midlands Manufacturing Limited", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INDEPENDENT AUDITOR'S REPORT", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AUD-2026-MIDLANDS-001", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROFIT AND LOSS ACCOUNT", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASH FLOW STATEMENT", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STATEMENT OF CHANGES IN EQUITY", pdfText, StringComparison.OrdinalIgnoreCase);

        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id));
        AssertWellFormedXml(ixbrl);
        Assert.Contains("Midlands Manufacturing Limited", ixbrl);
        Assert.Contains("core:TurnoverGrossRevenue", ixbrl);
        Assert.Contains("core:ProfitLossOnOrdinaryActivitiesBeforeTax", ixbrl);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    private static async Task<AccountingPeriod> SeedBasicScenarioAsync(
        AccountsDbContext db,
        string legalName,
        CompanyType companyType,
        CompanySizeClass sizeClass,
        ElectedRegime regime,
        bool auditExempt,
        bool charity)
    {
        var tenant = new Tenant { Name = "Golden Corpus Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = legalName,
            CompanyType = companyType,
            CroNumber = Guid.NewGuid().ToString("N")[..8],
            TaxReference = "1234567A",
            IncorporationDate = new DateOnly(2022, 1, 1),
            ArdMonth = 9,
            IsTrading = true,
            IsCharitableOrganisation = charity,
            RegisteredOfficeAddress1 = "1 Statutory Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.CompanyOfficers.AddRange(
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "Aisling Director",
                Role = OfficerRole.Director,
                AppointedDate = new DateOnly(2022, 1, 1)
            },
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "Brian Secretary",
                Role = OfficerRole.Secretary,
                AppointedDate = new DateOnly(2022, 1, 1)
            });

        if (charity)
        {
            db.CharityInfos.Add(new CharityInfo
            {
                CompanyId = company.Id,
                CharityNumber = "CHY-2026-001",
                CharityType = "CLG",
                GrossIncome = 500m,
                SorpTier = 1,
                CharitableObjectives = "Community support and education.",
                PrincipalActivities = "Local community support programmes."
            });
        }

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            Company = company,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        var bankCategory = AddCategory(db, company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var incomeCategory = AddCategory(db, company.Id, "4000", charity ? "Donations and grants" : "Sales / Revenue", AccountCategoryType.Income);
        var shareCapitalCategory = companyType == CompanyType.CompanyLimitedByGuarantee
            ? null
            : AddCategory(db, company.Id, "3000", "Share Capital", AccountCategoryType.Equity);

        var openingShareCapital = companyType == CompanyType.CompanyLimitedByGuarantee ? 0m : 100m;
        if (openingShareCapital > 0)
        {
            db.ShareCapitals.Add(new ShareCapital
            {
                CompanyId = company.Id,
                ShareClass = "Ordinary",
                NumberIssued = 100,
                NominalValue = 1m,
                TotalValue = openingShareCapital,
                IssueDate = period.PeriodStart
            });
            db.OpeningBalances.Add(new OpeningBalance
            {
                PeriodId = period.Id,
                AccountCategoryId = shareCapitalCategory!.Id,
                Credit = openingShareCapital,
                SourceNote = "Issued ordinary share capital at period start.",
                EnteredBy = "Accounts reviewer",
                Reviewed = true,
                ReviewedBy = "Accounts reviewer",
                ReviewedAt = DateTime.UtcNow
            });
        }

        var bankAccount = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current Account",
            OpeningBalance = openingShareCapital,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2026, 3, 1),
            Description = charity ? "Community grant receipt" : "Customer receipt",
            Amount = 500m,
            CategoryId = incomeCategory.Id
        });
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = sizeClass,
            Turnover = sizeClass == CompanySizeClass.Medium ? 18_000_000m : 500m,
            BalanceSheetTotal = sizeClass == CompanySizeClass.Medium ? 8_000_000m : 500m,
            AvgEmployees = sizeClass == CompanySizeClass.Medium ? 120 : 2
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            CanUseMicro = regime == ElectedRegime.Micro,
            CanFileAbridged = regime is ElectedRegime.Micro or ElectedRegime.SmallAbridged,
            AuditExempt = auditExempt,
            ElectedRegime = regime,
            RequiredStatementsJson = "[]",
            RequiredNotesJson = "[]"
        });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "No year-end adjustment required",
            Amount = 0m,
            ImpactOnProfit = 0m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Accounts reviewer",
            ApprovedBy = "Accounts reviewer",
            ApprovedAt = DateTime.UtcNow
        });
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

        if (charity)
        {
            db.FundBalances.Add(new FundBalance
            {
                PeriodId = period.Id,
                FundName = "Unrestricted funds",
                FundType = "Unrestricted",
                OpeningBalance = 0m,
                IncomingResources = 500m,
                ResourcesExpended = 0m,
                ClosingBalance = 500m
            });
        }

        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(company.Id, period.Id);
        _ = bankCategory;
        return period;
    }

    private static async Task MarkGeneratedReviewedAndExternallyValidatedAsync(
        AccountsDbContext db,
        AccountingPeriod period,
        bool includeCharityReports)
    {
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            AccountsPdfGenerated = true,
            SignaturePageGenerated = true,
            FilingStatus = FilingStatus.Approved,
            ApprovedBy = "Qualified Accountant",
            ApprovedAt = DateTime.UtcNow,
            SignedByDirector = "Aisling Director",
            SignedBySecretary = "Brian Secretary",
            SignedAt = DateTime.UtcNow
        });
        db.RevenueFilingPackages.Add(new RevenueFilingPackage
        {
            PeriodId = period.Id,
            IxbrlGenerated = true,
            IxbrlValidated = true,
            IxbrlValidationErrors = "Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing."
        });
        if (includeCharityReports)
        {
            db.CharityFilingPackages.Add(new CharityFilingPackage
            {
                PeriodId = period.Id,
                SofaGenerated = true,
                TrusteesReportGenerated = true,
                FilingStatus = FilingStatus.ReadyForReview
            });
        }
        await db.SaveChangesAsync();
    }

    private static AccountCategory AddCategory(
        AccountsDbContext db,
        int companyId,
        string code,
        string name,
        AccountCategoryType type)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type,
            IsSystem = true
        };
        db.AccountCategories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static YearEndReviewConfirmation NilReview(int periodId, string sectionKey) => new()
    {
        PeriodId = periodId,
        SectionKey = sectionKey,
        Confirmed = true,
        ConfirmedBy = "Accounts reviewer",
        Note = "Nil position reviewed."
    };

    private static void AssertWellFormedXml(string xhtml)
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(xhtml), settings);
        var doc = System.Xml.Linq.XDocument.Load(reader);
        Assert.NotNull(doc.Root);
    }

    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }
}
