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
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.True(profile.ManualProfessionalReviewRequired);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "audit-report" && e.Required && !e.Satisfied);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "auditor-handoff-required");
        Assert.Contains("auditor", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mark-cro-submitted", profile.AllowedNextActions);
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

        var bankAccount = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current Account",
            OpeningBalance = 0m,
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
