using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    public async Task GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates()
    {
        await using var db = CreateDbContext();
        var scenario = GoldenCorpusFixture.Scenario("micro-ltd");
        var period = await SeedBasicScenarioAsync(db, scenario);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var accountsPdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var croPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
        var signaturePage = await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id));
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        var notes = await db.NotesDisclosures.Where(n => n.PeriodId == period.Id && n.IsIncluded).ToListAsync();

        var accountsText = ExtractPdfText(accountsPdf);
        var croPackText = ExtractPdfText(croPack);
        var signatureText = ExtractPdfText(signaturePage);

        Assert.True(accountsPdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(accountsPdf, 0, 4));
        Assert.Contains("Example Micro Limited", accountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STATEMENT REQUIRED BY SECTION 280D", accountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FRS 105", accountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROFIT AND LOSS ACCOUNT", accountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Turnover", accountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cost of sales", accountsText, StringComparison.OrdinalIgnoreCase);

        Assert.True(croPack.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(croPack, 0, 4));
        Assert.Contains("Example Micro Limited", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section 352", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section 353", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DIRECTORS' REPORT", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Turnover", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cost of sales", croPackText, StringComparison.OrdinalIgnoreCase);

        Assert.True(signaturePage.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(signaturePage, 0, 4));
        Assert.Contains("CRO ACCOUNTS CERTIFICATION", signatureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Aisling Director", signatureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Brian Secretary", signatureText, StringComparison.OrdinalIgnoreCase);

        AssertWellFormedXml(ixbrl);
        Assert.Contains("Example Micro Limited", ixbrl);
        Assert.Contains("DRAFT - NOT FOR FILING - INCOMPLETE REVIEW PROTOTYPE", ixbrl);
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", ixbrl);
        Assert.Contains("core:TurnoverGrossRevenue", ixbrl);

        Assert.Equal(scenario.WorkflowFacts.ExpectedCorporationTax, tax.TotalCorporationTax);
        Assert.Contains(notes, n => n.Title == "Accounting Policies" && n.IsIncluded);
        Assert.True(profile.SupportedPath);
        Assert.False(profile.RevenueIxbrlGenerationSupported);
        Assert.True(profile.RevenueManualHandoffRequired);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
        Assert.Equal(CompanySizeClass.Micro, profile.SizeClass);
        Assert.Equal(ElectedRegime.Micro, profile.ElectedRegime);
        Assert.True(profile.AuditExempt);
        Assert.False(profile.ManualProfessionalReviewRequired);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.FrcFrs105.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "cro-accounts-pdf" && !e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "cro-signature-page" && !e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "cro-signatories" && e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review" && !e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "external-ros-validation" && !e.Satisfied);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
        Assert.DoesNotContain("mark-cro-submitted", profile.AllowedNextActions);
        Assert.Equal("blocked", profile.SignOffPacket.State);

        await RecordGeneratedMachineArtifactsAsync(db, period);

        var approvedProfile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        Assert.Contains(approvedProfile.RequiredEvidence, e => e.Code == "cro-accounts-pdf" && e.Satisfied);
        Assert.Contains(approvedProfile.RequiredEvidence, e => e.Code == "cro-signature-page" && e.Satisfied);
        Assert.Contains(approvedProfile.RequiredEvidence, e => e.Code == "cro-signatories" && e.Satisfied);
        Assert.Contains(approvedProfile.RequiredEvidence, e => e.Code == "accountant-review" && !e.Satisfied);
        Assert.Contains(approvedProfile.RequiredEvidence, e => e.Code == "external-ros-validation" && !e.Satisfied);
        Assert.Equal("blocked", approvedProfile.SignOffPacket.State);
        Assert.False(approvedProfile.SignOffPacket.ReadyForExternalFiling);
        Assert.Contains(approvedProfile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
        Assert.Contains("approve-cro-pack", approvedProfile.AllowedNextActions);
        Assert.DoesNotContain("mark-cro-submitted", approvedProfile.AllowedNextActions);
    }

    [Fact]
    public async Task GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness()
    {
        await using var db = CreateDbContext();
        var scenario = GoldenCorpusFixture.Scenario("dac-small");
        var period = await SeedBasicScenarioAsync(db, scenario);
        await RecordGeneratedMachineArtifactsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var pdf = await new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var pdfText = ExtractPdfText(pdf);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id));
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        var notes = await db.NotesDisclosures.Where(n => n.PeriodId == period.Id && n.IsIncluded).ToListAsync();

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Contains("Atlantic Manufacturing DAC", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DIRECTORS' REPORT", pdfText, StringComparison.OrdinalIgnoreCase);
        AssertWellFormedXml(ixbrl);
        Assert.Contains("Atlantic Manufacturing DAC", ixbrl);
        Assert.Contains("bus:EntityCurrentLegalOrRegisteredName", ixbrl);
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", ixbrl);
        Assert.Equal(scenario.WorkflowFacts.ExpectedCorporationTax, tax.TotalCorporationTax);
        Assert.Contains(notes, n => n.Title == "Accounting Policies" && n.IsIncluded);
        Assert.True(profile.SupportedPath);
        Assert.False(profile.RevenueIxbrlGenerationSupported);
        Assert.True(profile.RevenueManualHandoffRequired);
        Assert.Equal(CompanyType.DesignatedActivityCompany, profile.CompanyType);
        Assert.Equal(CompanySizeClass.Small, profile.SizeClass);
        Assert.Equal(ElectedRegime.Small, profile.ElectedRegime);
        Assert.True(profile.AuditExempt);
        Assert.False(profile.ManualProfessionalReviewRequired);
        Assert.DoesNotContain(profile.BlockingIssues, issue => issue.Code.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review" && !e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "cro-signatories" && e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "external-ros-validation" && !e.Satisfied);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.FrcFrs102.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
        Assert.Equal("blocked", profile.SignOffPacket.State);
        Assert.False(profile.SignOffPacket.ReadyForExternalFiling);
        Assert.Contains("approve-cro-pack", profile.AllowedNextActions);
        Assert.DoesNotContain("mark-cro-submitted", profile.AllowedNextActions);
    }

    [Fact]
    public async Task GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness()
    {
        await using var db = CreateDbContext();
        var scenario = GoldenCorpusFixture.Scenario("clg-charity");
        var period = await SeedBasicScenarioAsync(db, scenario);
        await RecordGeneratedMachineArtifactsAsync(db, period);

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
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", ixbrl);
        Assert.Equal(scenario.WorkflowFacts.ExpectedCorporationTax, tax.TotalCorporationTax);
        Assert.Contains(notes, n => n.Title == "Accounting Policies" && n.IsIncluded);
        Assert.True(profile.SupportedPath);
        Assert.False(profile.RevenueIxbrlGenerationSupported);
        Assert.True(profile.RevenueManualHandoffRequired);
        Assert.False(profile.ManualProfessionalReviewRequired);
        Assert.DoesNotContain(profile.BlockingIssues, issue => issue.Code.Contains("charity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "charity-number" && e.Satisfied);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "charity-reports" && !e.Satisfied);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
        Assert.Equal("blocked", profile.SignOffPacket.State);
        Assert.False(profile.SignOffPacket.ReadyForExternalFiling);
        Assert.Null(profile.SignOffPacket.ApprovedBy);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review" && !e.Satisfied);
        var charityStep = Assert.Single(profile.SignOffPacket.Steps, step => step.Code == "charity-reporting");
        Assert.Equal("blocked", charityStep.State);
        Assert.Contains(charityStep.Sources, s => s.SourceId == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId);
    }

    [Fact]
    public async Task GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness()
    {
        await using var db = CreateDbContext();
        var scenario = GoldenCorpusFixture.Scenario("small-abridged-ltd");
        var period = await SeedBasicScenarioAsync(db, scenario);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var fullAccountsPdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var abridgedCroPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
        var signaturePage = await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id));

        await RecordGeneratedMachineArtifactsAsync(db, period);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        var notes = await db.NotesDisclosures.Where(n => n.PeriodId == period.Id && n.IsIncluded).ToListAsync();

        var fullAccountsText = ExtractPdfText(fullAccountsPdf);
        var croPackText = ExtractPdfText(abridgedCroPack);
        var signatureText = ExtractPdfText(signaturePage);

        Assert.True(fullAccountsPdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(fullAccountsPdf, 0, 4));
        Assert.Contains("Connacht Digital Solutions Limited", fullAccountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DIRECTORS' REPORT", fullAccountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROFIT AND LOSS ACCOUNT", fullAccountsText, StringComparison.OrdinalIgnoreCase);

        Assert.True(abridgedCroPack.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(abridgedCroPack, 0, 4));
        Assert.Contains("Abridged Financial Statements for filing with the CRO", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section 352", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DIRECTORS' REPORT", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Turnover", croPackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cost of sales", croPackText, StringComparison.OrdinalIgnoreCase);

        Assert.True(signaturePage.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(signaturePage, 0, 4));
        Assert.Contains("CRO ACCOUNTS CERTIFICATION", signatureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Aisling Director", signatureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Brian Secretary", signatureText, StringComparison.OrdinalIgnoreCase);

        AssertWellFormedXml(ixbrl);
        Assert.Contains("Connacht Digital Solutions Limited", ixbrl);
        Assert.Contains("DRAFT - NOT FOR FILING - INCOMPLETE REVIEW PROTOTYPE", ixbrl);
        Assert.Contains("core:TurnoverGrossRevenue", ixbrl);

        Assert.True(profile.SupportedPath);
        Assert.False(profile.RevenueIxbrlGenerationSupported);
        Assert.True(profile.RevenueManualHandoffRequired);
        Assert.Equal(CompanySizeClass.Small, profile.SizeClass);
        Assert.Equal(ElectedRegime.SmallAbridged, profile.ElectedRegime);
        Assert.True(profile.AuditExempt);
        Assert.False(profile.ManualProfessionalReviewRequired);
        var abridgementEvidence = Assert.Single(profile.RequiredEvidence, e => e.Code == "cro-abridgement-election");
        Assert.True(abridgementEvidence.Satisfied);
        Assert.Contains(abridgementEvidence.Sources, s => s.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
        Assert.Contains(abridgementEvidence.Sources, s => s.SourceId == IrishStatutoryRuleSources.FrcFrs102.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.FrcFrs102.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
        Assert.Equal("blocked", profile.SignOffPacket.State);
        Assert.Contains(profile.SignOffPacket.Steps, step => step.Code == "statutory-basis" && step.Detail.Contains("abridgement", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("approve-cro-pack", profile.AllowedNextActions);
        Assert.DoesNotContain("mark-cro-submitted", profile.AllowedNextActions);

        Assert.Equal(scenario.WorkflowFacts.ExpectedCorporationTax, tax.TotalCorporationTax);
        Assert.Contains(notes, n => n.Title == "Accounting Policies" && n.IsIncluded);
    }

    [Fact]
    public async Task GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence()
    {
        await using var db = CreateDbContext();
        var scenario = GoldenCorpusFixture.Scenario("medium-audit-required");
        var period = await SeedBasicScenarioAsync(db, scenario, finalise: false);

        var statements = new FinancialStatementsService(db);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        var notes = await db.NotesDisclosures.Where(n => n.PeriodId == period.Id && n.IsIncluded).ToListAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.True(profile.SupportedPath);
        Assert.False(profile.RevenueIxbrlGenerationSupported);
        Assert.True(profile.RevenueManualHandoffRequired);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "ixbrl-generation-manual-handoff");
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
        Assert.Equal(scenario.WorkflowFacts.ExpectedCorporationTax, tax.TotalCorporationTax);
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

        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id));
        AssertWellFormedXml(ixbrl);
        foreach (var phrase in scenario.ExpectedIxbrlPhrases)
            Assert.Contains(phrase, ixbrl, StringComparison.OrdinalIgnoreCase);

        // A machine test cannot manufacture a signed auditor opinion. The audited scenario is
        // deliberately retained as a negative path until genuine evidence is supplied.
        var retainedPeriod = await db.AccountingPeriods.AsNoTracking().SingleAsync(item => item.Id == period.Id);
        Assert.False(retainedPeriod.AuditorsReportReceived);
        Assert.Null(retainedPeriod.AuditorsReportReference);
        Assert.Null(retainedPeriod.AuditorsReportArtifact);
        Assert.Null(retainedPeriod.AuditorsReportSha256);
        Assert.Null(await db.CroFilingPackages.AsNoTracking().SingleOrDefaultAsync(item => item.PeriodId == period.Id));
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    internal static async Task<AccountingPeriod> SeedBasicScenarioAsync(
        AccountsDbContext db,
        GoldenCorpusScenario scenario,
        bool finalise = true)
    {
        var tenant = new Tenant { Name = "Golden Corpus Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = scenario.LegalName,
            CompanyType = scenario.ParsedCompanyType,
            CroNumber = Guid.NewGuid().ToString("N")[..8],
            TaxReference = "1234567A",
            IncorporationDate = scenario.PriorYear.PeriodStart,
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true,
            IsCharitableOrganisation = scenario.IsCharity,
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
                AppointedDate = scenario.PriorYear.PeriodStart
            },
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "Brian Secretary",
                Role = OfficerRole.Secretary,
                AppointedDate = scenario.PriorYear.PeriodStart
            });

        if (scenario.IsCharity)
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

        var priorPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            Company = company,
            PeriodStart = scenario.PriorYear.PeriodStart,
            PeriodEnd = scenario.PriorYear.PeriodEnd,
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(priorPeriod);
        await db.SaveChangesAsync();
        await SaveClassificationInputsThroughEndpointAsync(
            db,
            tenant.Id,
            company.Id,
            priorPeriod.Id,
            scenario.PriorYear);
        var classificationService = new SizeClassificationService(
            db,
            Options.Create(new SizeThresholdConfig()));
        await classificationService.ClassifyAsync(company.Id, priorPeriod.Id);

        var period = await new PeriodChronologyService(db).CreateAsync(new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = scenario.CurrentYear.PeriodStart,
            PeriodEnd = scenario.CurrentYear.PeriodEnd,
            IsFirstYear = false
        });

        var bankCategory = AddCategory(db, company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var incomeCategory = AddCategory(db, company.Id, "4000", scenario.IsCharity ? "Donations and grants" : "Sales / Revenue", AccountCategoryType.Income);
        var shareCapitalCategory = scenario.ParsedCompanyType == CompanyType.CompanyLimitedByGuarantee
            ? null
            : AddCategory(db, company.Id, "3000", "Share Capital", AccountCategoryType.Equity);

        var openingShareCapital = scenario.WorkflowFacts.OpeningShareCapital;
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
            await UpsertOpeningBalanceThroughEndpointAsync(
                db,
                tenant.Id,
                company.Id,
                period.Id,
                shareCapitalCategory!.Id,
                openingShareCapital);
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
            Date = scenario.CurrentYear.PeriodStart.AddMonths(2),
            Description = scenario.IsCharity ? "Community grant receipt" : "Customer receipt",
            Amount = scenario.WorkflowFacts.CashReceipt,
            CategoryId = incomeCategory.Id
        });
        await SaveClassificationInputsThroughEndpointAsync(
            db,
            tenant.Id,
            company.Id,
            period.Id,
            scenario.CurrentYear);
        await db.SaveChangesAsync();
        var automatedReviewInputs = new (string SectionKey, string Note)[]
        {
            ("adjustments", "Automated nil-position workflow input; not independent review evidence."),
            ("debtors", "Automated nil-position workflow input; not independent review evidence."),
            ("creditors", "Automated nil-position workflow input; not independent review evidence."),
            ("inventory", "Automated nil-position workflow input; not independent review evidence."),
            ("payroll", "Automated nil-position workflow input; not independent review evidence."),
            ("tax", "Automated nil-position workflow input; not independent review evidence."),
            ("dividends", "Automated nil-position workflow input; not independent review evidence."),
            ("post-balance-sheet-events", "Automated nil-position workflow input; not independent review evidence."),
            ("related-parties", "Automated nil-position workflow input; not independent review evidence."),
            ("contingent-liabilities", "Automated nil-position workflow input; not independent review evidence."),
            ("going-concern", "Automated nil-position workflow input; not independent review evidence."),
            (
                DirectorsReportService.PrincipalActivitiesReviewKey,
                scenario.IsCharity
                    ? "Community support and education."
                    : "The principal activity is the provision of professional services."),
            (
                DirectorsReportService.AuditInformationReviewKey,
                "Automated workflow fixture only; no signed director or auditor evidence is represented by this test input."),
            (
                "note-directors-remuneration",
                "Automated workflow fixture amount €0.00; this is not an independently reviewed remuneration disclosure."),
            (
                "note-financial-instruments",
                "Automated workflow fixture disclosure; no human financial-instruments approval is represented."),
            (
                "note-capital-commitments",
                "Automated workflow fixture amount €0.00; no retained human confirmation is represented."),
            (
                "note-deferred-tax",
                "Automated workflow fixture disclosure; no human deferred-tax approval is represented.")
        };
        foreach (var (sectionKey, note) in automatedReviewInputs)
        {
            await RecordYearEndReviewThroughEndpointAsync(
                db,
                tenant.Id,
                company.Id,
                period.Id,
                sectionKey,
                note);
        }

        if (scenario.IsCharity)
        {
            db.FundBalances.Add(new FundBalance
            {
                PeriodId = period.Id,
                FundName = "Unrestricted funds",
                FundType = "Unrestricted",
                OpeningBalance = 0m,
                IncomingResources = scenario.WorkflowFacts.CashReceipt,
                ResourcesExpended = 0m,
                ClosingBalance = scenario.WorkflowFacts.ExpectedNetAssets
            });
        }

        await db.SaveChangesAsync();
        var classification = await classificationService.ClassifyAsync(company.Id, period.Id);
        Assert.Equal(scenario.ParsedSizeClass, classification.CalculatedClass);
        var filingRequirements = await new FilingRegimeService(db)
            .DetermineAsync(company.Id, period.Id, scenario.ParsedRegime);
        Assert.Equal(scenario.ParsedRegime, filingRequirements.Regime);
        Assert.Equal(scenario.ParsedSizeClass <= CompanySizeClass.Small, filingRequirements.AuditExempt);
        await new NotesDisclosureService(db).GenerateNotesAsync(company.Id, period.Id);
        if (finalise)
            await FinaliseThroughEndpointAsync(db, tenant.Id, period, scenario.CurrentYear.PeriodEnd.AddDays(-16));
        _ = bankCategory;
        return period;
    }

    internal static async Task RecordGeneratedMachineArtifactsAsync(
        AccountsDbContext db,
        AccountingPeriod period)
    {
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var workflow = new FilingWorkflowService(
            db,
            statements,
            new IxbrlService(db, statements),
            releaseGate: new FilingReleaseGate(db, "golden-corpus-machine-candidate"));
        var accounts = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
        var signaturePage = await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id);

        await workflow.RecordCroDocumentGeneratedAsync(
            period.CompanyId,
            period.Id,
            "accounts",
            "golden-corpus-machine",
            accounts);
        var package = await workflow.RecordCroDocumentGeneratedAsync(
            period.CompanyId,
            period.Id,
            "signature",
            "golden-corpus-machine",
            signaturePage);

        Assert.Equal(FilingReleaseGate.ComputeSha256(accounts), package.AccountsPdfSha256);
        Assert.Equal(FilingReleaseGate.ComputeSha256(signaturePage), package.SignaturePageSha256);
        Assert.Null(package.SignedByDirector);
        Assert.Null(package.SignedBySecretary);
        Assert.Null(package.SignedAt);
    }

    internal static async Task FinaliseThroughEndpointAsync(
        AccountsDbContext db,
        int tenantId,
        AccountingPeriod period,
        DateOnly approvalDate)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/status";
        context.Request.Headers[IdempotencyHttpContract.RequestHeader] =
            $"golden-finalise-{period.CompanyId}-{period.Id}-{Guid.NewGuid():N}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            100,
            tenantId,
            "Golden Corpus Firm",
            "fixture-owner@example.invalid",
            "Automated corpus actor",
            "Owner");

        var result = await PeriodStatusEndpoint.UpdateAsync(
            period.CompanyId,
            period.Id,
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null, approvalDate),
            db,
            new AuditService(db),
            new FinancialStatementsService(db),
            context,
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false }),
                new TestEnvironment()));

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
            ?? StatusCodes.Status200OK;
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Equal(approvalDate, period.ApprovalDate);
        Assert.Equal(PeriodStatus.Finalised, period.Status);
    }

    internal static async Task ApproveAdjustmentsThroughEndpointAsync(
        AccountsDbContext db,
        int tenantId,
        int companyId,
        int periodId)
    {
        var ids = await db.Adjustments
            .Where(item => item.PeriodId == periodId && item.ApprovedAt == null)
            .Select(item => item.Id)
            .ToListAsync();
        foreach (var id in ids)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = $"/api/companies/{companyId}/periods/{periodId}/adjustments/{id}/approve";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                101,
                tenantId,
                "Golden Corpus Firm",
                "fixture-reviewer@example.invalid",
                "Automated corpus reviewer",
                "Reviewer");
            var result = await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
                companyId,
                periodId,
                id,
                db,
                new AuditService(db),
                context,
                new ApiAccessService(
                    Options.Create(new ApiAccessConfig { Enabled = false }),
                    new TestEnvironment()));
            var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
                ?? StatusCodes.Status200OK;
            Assert.Equal(StatusCodes.Status200OK, statusCode);
        }
    }

    internal static async Task SaveClassificationInputsThroughEndpointAsync(
        AccountsDbContext db,
        int tenantId,
        int companyId,
        int periodId,
        GoldenCorpusYear inputs)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{companyId}/periods/{periodId}/size-classification";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            102,
            tenantId,
            "Golden Corpus Firm",
            "fixture-accountant@example.invalid",
            "Automated corpus accountant",
            "Accountant");
        var result = await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            companyId,
            periodId,
            new SizeClassificationInput(
                inputs.Turnover,
                inputs.BalanceSheetTotal,
                inputs.AverageEmployees,
                PriorYearClass: null),
            db,
            new AuditService(db),
            context,
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false }),
                new TestEnvironment()));
        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
            ?? StatusCodes.Status200OK;
        Assert.Equal(StatusCodes.Status200OK, statusCode);
    }

    private static async Task UpsertOpeningBalanceThroughEndpointAsync(
        AccountsDbContext db,
        int tenantId,
        int companyId,
        int periodId,
        int categoryId,
        decimal credit)
    {
        var context = AutomatedWorkflowContext(
            tenantId,
            "Accountant",
            $"/api/companies/{companyId}/periods/{periodId}/opening-balances/{categoryId}");
        var result = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            companyId,
            periodId,
            categoryId,
            new OpeningBalanceInput(
                Debit: 0m,
                Credit: credit,
                SourceNote: "Automated corpus opening-share input; not human acceptance evidence.",
                EnteredBy: "Ignored payload identity",
                Reviewed: true),
            db,
            new AuditService(db),
            context);
        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
            ?? StatusCodes.Status200OK;
        Assert.Equal(StatusCodes.Status200OK, statusCode);
    }

    private static async Task RecordYearEndReviewThroughEndpointAsync(
        AccountsDbContext db,
        int tenantId,
        int companyId,
        int periodId,
        string sectionKey,
        string note)
    {
        var context = AutomatedWorkflowContext(
            tenantId,
            "Reviewer",
            $"/api/companies/{companyId}/periods/{periodId}/year-end-reviews/{sectionKey}");
        var result = await YearEndEndpoints.UpdateYearEndReviewEndpointAsync(
            companyId,
            periodId,
            sectionKey,
            new YearEndReviewInput(
                Confirmed: true,
                ConfirmedBy: "Ignored payload identity",
                Note: note),
            db,
            new AuditService(db),
            context);
        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
            ?? StatusCodes.Status200OK;
        Assert.Equal(StatusCodes.Status200OK, statusCode);
    }

    private static DefaultHttpContext AutomatedWorkflowContext(
        int tenantId,
        string role,
        string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = path;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            103,
            tenantId,
            "Golden Corpus Firm",
            "fixture-workflow@example.invalid",
            "Automated corpus workflow actor (not human acceptance)",
            role);
        return context;
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
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.Append(' ').Append(string.Join(' ', page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
