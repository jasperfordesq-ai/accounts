using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public class FilingReadinessProfileTests
{
    [Fact]
    public async Task ReadinessProfile_ForCorePrivateCompany_IsSourceBackedAndRequiresAccountantReview()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.Private, CompanySizeClass.Small);
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: null);

        var service = new FilingReadinessProfileService(db);

        var profile = await service.GetProfileAsync(period.CompanyId, period.Id);

        Assert.True(profile.SupportedPath);
        Assert.True(profile.AccountantReviewRequired);
        Assert.Equal("Required", profile.AccountantReviewState);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review" && e.Required && !e.Satisfied);
        Assert.Contains(profile.BlockingIssues, i => i.Code == "accountant-review-required");
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == IrishStatutoryRuleSources.FrcFrs102.SourceId);
    }

    [Fact]
    public async Task ReadinessProfile_BuildsAccountantSignOffPacketFromEvidenceAndWorkflowState()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.Private, CompanySizeClass.Small);
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: null);
        await SeedRevenuePackageAsync(db, period.Id, internalChecksPassed: true, externallyValidated: false);

        var service = new FilingReadinessProfileService(db);

        var profile = await service.GetProfileAsync(period.CompanyId, period.Id);

        Assert.True(
            profile.BlockingIssues.All(issue => issue.Code != "corporation-tax-scope-required"),
            string.Join(" | ", profile.BlockingIssues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal("blocked", profile.SignOffPacket.State);
        Assert.Equal("Blocked before accountant review", profile.SignOffPacket.StateLabel);
        Assert.False(profile.SignOffPacket.ReadyForAccountantApproval);
        Assert.False(profile.SignOffPacket.ReadyForExternalFiling);
        Assert.Null(profile.SignOffPacket.ApprovedBy);
        Assert.Contains("approve-cro-pack", profile.SignOffPacket.AllowedNextActions);
        Assert.Contains(profile.SignOffPacket.OpenBlockers, message => message.Contains("named qualified accountant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.SignOffPacket.OpenBlockers, message => message.Contains("filing-ready iXBRL generation is disabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.SignOffPacket.OpenWarnings, message => message.Contains("External ROS/iXBRL validation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.SignOffPacket.Steps, step => step.Code == "accountant-approval" && step.State == "pending");
        Assert.Contains(profile.SignOffPacket.Steps, step => step.Code == "external-validation" && step.State == "warning");
        Assert.Contains(
            profile.SignOffPacket.Steps.Single(step => step.Code == "accountant-approval").Sources,
            source => source.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
    }

    [Fact]
    public async Task ReadinessProfile_ForMicroRegime_TiesFilingRegimeEvidenceToFrs105()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.Private, CompanySizeClass.Micro);
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: "Qualified Accountant");
        await SeedRevenuePackageAsync(db, period.Id, internalChecksPassed: true, externallyValidated: true);

        var service = new FilingReadinessProfileService(db);

        var profile = await service.GetProfileAsync(period.CompanyId, period.Id);

        var filingRegimeEvidence = Assert.Single(profile.RequiredEvidence, e => e.Code == "filing-regime");
        Assert.Contains(filingRegimeEvidence.Sources, source => source.SourceId == IrishStatutoryRuleSources.FrcFrs105.SourceId);
        var statutoryBasisStep = Assert.Single(profile.SignOffPacket.Steps, step => step.Code == "statutory-basis");
        Assert.Contains(statutoryBasisStep.Sources, source => source.SourceId == IrishStatutoryRuleSources.FrcFrs105.SourceId);
        Assert.Contains(profile.SourceReferences, source => source.SourceId == IrishStatutoryRuleSources.FrcFrs105.SourceId);
    }

    [Fact]
    public async Task ReadinessProfile_ForSmallAbridgedRegime_RequiresSourceBackedSection352AbridgementEvidence()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.Private, CompanySizeClass.Small);
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: "Qualified Accountant");
        await SeedRevenuePackageAsync(db, period.Id, internalChecksPassed: true, externallyValidated: true);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);

        var abridgementEvidence = Assert.Single(profile.RequiredEvidence, e => e.Code == "cro-abridgement-election");
        Assert.True(abridgementEvidence.Required);
        Assert.True(abridgementEvidence.Satisfied);
        Assert.Contains("Section 352", abridgementEvidence.Detail);
        Assert.Contains(abridgementEvidence.Sources, source => source.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
        Assert.Contains(abridgementEvidence.Sources, source => source.SourceId == IrishStatutoryRuleSources.FrcFrs102.SourceId);
        Assert.Contains(profile.SignOffPacket.Steps, step =>
            step.Code == "statutory-basis"
            && step.State == "complete"
            && step.Detail.Contains("abridgement", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(CompanyType.PublicLimitedCompany)]
    [InlineData(CompanyType.PrivateUnlimited)]
    public async Task ReadinessProfile_ForUnsupportedCompanyTypes_FailsClosedToManualHandoff(CompanyType companyType)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, companyType, CompanySizeClass.Small);
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: "Qualified Accountant");

        var service = new FilingReadinessProfileService(db);

        var profile = await service.GetProfileAsync(period.CompanyId, period.Id);

        Assert.False(profile.SupportedPath);
        Assert.True(profile.ManualProfessionalReviewRequired);
        Assert.Empty(profile.AllowedNextActions);
        Assert.Contains(profile.BlockingIssues, i => i.Code == "unsupported-company-type");
        Assert.Equal("manual-handoff", profile.SignOffPacket.State);
        Assert.False(profile.SignOffPacket.ReadyForAccountantApproval);
        Assert.False(profile.SignOffPacket.ReadyForExternalFiling);
        Assert.Contains(profile.SignOffPacket.Steps, step => step.Code == "supported-path" && step.State == "blocked");
    }

    [Fact]
    public async Task ApprovalGuard_ForUnsupportedCompanyType_BlocksNormalCroApprovalPath()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.PublicLimitedCompany, CompanySizeClass.Small);
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: null);

        var service = new FilingReadinessProfileService(db);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AssertCanApproveCroPackAsync(period.CompanyId, period.Id));
        Assert.Contains("Manual professional handoff is required", ex.Message);
        Assert.Contains("PublicLimitedCompany", ex.Message);
    }

    [Fact]
    public async Task ReadinessProfile_ForRegulatedOrGroupEntities_FailsClosed()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.Private, CompanySizeClass.Small);
        period.Company.IsCreditInstitution = true;
        period.Company.IsGroupMember = true;
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: "Qualified Accountant");
        await db.SaveChangesAsync();

        var service = new FilingReadinessProfileService(db);

        var profile = await service.GetProfileAsync(period.CompanyId, period.Id);

        Assert.False(profile.SupportedPath);
        Assert.True(profile.ManualProfessionalReviewRequired);
        Assert.Contains(profile.BlockingIssues, i => i.Code == "regulated-entity-manual-handoff");
        Assert.Contains(profile.BlockingIssues, i => i.Code == "group-context-manual-handoff");
    }

    [Fact]
    public async Task ReadinessProfile_TurnsUnpostableTaxLedgerIntoEvidenceBlocker()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, CompanyType.Private, CompanySizeClass.Small);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current", OpeningBalance = 0m };
        var sales = new AccountCategory
        {
            CompanyId = period.CompanyId,
            Code = "4000",
            Name = "Sales",
            Type = AccountCategoryType.Income,
            TaxTreatment = TaxTreatment.Deductible
        };
        db.BankAccounts.Add(bank);
        db.AccountCategories.Add(sales);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddMonths(1),
            Description = "Sale without bank-control setup",
            Amount = 1_000m,
            CategoryId = sales.Id
        });
        await db.SaveChangesAsync();

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);

        var taxEvidence = Assert.Single(profile.RequiredEvidence, item => item.Code == "corporation-tax-scope");
        Assert.False(taxEvidence.Satisfied);
        Assert.Contains("bank control account", taxEvidence.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(profile.BlockingIssues, item => item.Code == "corporation-tax-scope-required");
    }

    [Fact]
    public async Task RevenueTaxonomySelector_UsesRevenueAcceptedIrishExtension2025ForCurrentPeriods()
    {
        var selection = RevenueIxbrlTaxonomySelector.Select(
            new DateOnly(2026, 1, 1),
            ElectedRegime.Small);

        Assert.True(selection.AcceptedByRevenue);
        Assert.Equal("2025-01-01", selection.TaxonomyDate);
        Assert.Contains("/FRS-102/2025-01-01/", selection.SchemaRef);
        Assert.DoesNotContain("2026-01-01", selection.SchemaRef);
        Assert.Contains(selection.Sources, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
    }

    [Theory]
    [InlineData(2023, "irish-extension-2023-frs-102", "2023-01-01", 2023)]
    [InlineData(2022, "irish-extension-2022-frs-102", "2022-01-01", 2019)]
    [InlineData(2019, "irish-extension-2022-frs-102", "2022-01-01", 2019)]
    public void RevenueTaxonomySelector_UsesLatestRevenueAcceptedFrs102TaxonomyForPeriodDate(
        int periodStartYear,
        string expectedKey,
        string expectedTaxonomyDate,
        int expectedEffectiveYear)
    {
        var selection = RevenueIxbrlTaxonomySelector.Select(
            new DateOnly(periodStartYear, 1, 1),
            ElectedRegime.Small);

        Assert.True(selection.AcceptedByRevenue);
        Assert.Equal(expectedKey, selection.TaxonomyKey);
        Assert.Equal(expectedTaxonomyDate, selection.TaxonomyDate);
        Assert.Contains($"/FRS-102/{expectedTaxonomyDate}/", selection.SchemaRef);
        Assert.Equal(new DateOnly(expectedEffectiveYear, 1, 1), selection.EffectiveForPeriodsStartingOnOrAfter);
        Assert.Contains(selection.Sources, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
    }

    [Fact]
    public async Task ReadinessProfile_ForPeriodBeforeRevenueAcceptedFrs102EffectiveDate_FailsClosedToManualHandoff()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(
            db,
            CompanyType.Private,
            CompanySizeClass.Small,
            new DateOnly(2018, 1, 1),
            new DateOnly(2018, 12, 31));
        await SeedOfficersAsync(db, period.CompanyId);
        await SeedCroPackageAsync(db, period.Id, approvedBy: "Qualified Accountant");
        await SeedRevenuePackageAsync(db, period.Id, internalChecksPassed: true, externallyValidated: true);

        var selection = RevenueIxbrlTaxonomySelector.Select(period.PeriodStart, ElectedRegime.SmallAbridged);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(period.CompanyId, period.Id);

        Assert.False(selection.AcceptedByRevenue);
        Assert.Equal("manual-revenue-taxonomy-review-required", selection.TaxonomyKey);
        Assert.Equal(new DateOnly(2019, 1, 1), selection.EffectiveForPeriodsStartingOnOrAfter);
        Assert.Contains(selection.Sources, source => source.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
        Assert.False(profile.SupportedPath);
        Assert.True(profile.ManualProfessionalReviewRequired);
        Assert.Empty(profile.AllowedNextActions);
        Assert.Contains(profile.BlockingIssues, issue => issue.Code == "taxonomy-not-revenue-accepted");
        Assert.Equal("manual-handoff", profile.SignOffPacket.State);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    private static async Task<AccountingPeriod> SeedCompanyPeriodAsync(
        AccountsDbContext db,
        CompanyType companyType,
        CompanySizeClass sizeClass,
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null)
    {
        var company = new Company
        {
            LegalName = "Example Accounts Limited",
            CompanyType = companyType,
            CroNumber = "123456",
            TaxReference = "1234567A",
            IncorporationDate = new DateOnly(2020, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            Company = company,
            PeriodStart = periodStart ?? new DateOnly(2026, 1, 1),
            PeriodEnd = periodEnd ?? new DateOnly(2026, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        var (turnover, balanceSheetTotal, employees) = sizeClass switch
        {
            CompanySizeClass.Micro => (800_000m, 400_000m, 8),
            CompanySizeClass.Small => (1_000_000m, 500_000m, 8),
            _ => throw new ArgumentOutOfRangeException(nameof(sizeClass), sizeClass, "This readiness fixture supports only micro and small raw statutory inputs.")
        };
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = turnover,
            BalanceSheetTotal = balanceSheetTotal,
            AvgEmployees = employees,
            ThresholdElectionEffectiveFrom = new DateOnly(2024, 1, 1)
        });
        await db.SaveChangesAsync();

        var classificationService = new SizeClassificationService(
            db,
            Options.Create(new SizeThresholdConfig()));
        await classificationService.ClassifyAsync(company.Id, period.Id, "Automated readiness fixture");
        await new FilingRegimeService(db).DetermineAsync(
            company.Id,
            period.Id,
            sizeClass == CompanySizeClass.Micro ? ElectedRegime.Micro : null,
            "Automated readiness fixture");

        await new TaxComputationService(db, new FinancialStatementsService(db)).SaveScopeReviewAsync(
            company.Id,
            period.Id,
            new TaxComputationService.CorporationTaxScopeReviewInput(
                IsCloseCompany: false,
                IsServiceCompany: null,
                HasGroupOrConsortiumRelief: false,
                HasChargeableGains: false,
                HasForeignIncomeOrTaxCredits: false,
                HasExceptedTrade: false,
                HasOtherReliefsOrSpecialRegimes: false,
                DeclaredPassiveIncomePresent: false,
                PassiveIncomeClassificationReviewed: false,
                LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                BroughtForwardTradingLoss: 0m,
                BroughtForwardLossEvidence: null,
                EvidenceNote: "Automated test scope evidence; not professional acceptance."),
            "Automated test actor");

        return period;
    }

    private static async Task SeedOfficersAsync(AccountsDbContext db, int companyId)
    {
        db.CompanyOfficers.AddRange(
            new CompanyOfficer
            {
                CompanyId = companyId,
                Name = "Aisling Director",
                Role = OfficerRole.Director,
                AppointedDate = new DateOnly(2020, 1, 1)
            },
            new CompanyOfficer
            {
                CompanyId = companyId,
                Name = "Brian Secretary",
                Role = OfficerRole.CompanySecretary,
                AppointedDate = new DateOnly(2020, 1, 1)
            });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCroPackageAsync(AccountsDbContext db, int periodId, string? approvedBy)
    {
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = periodId,
            AccountsPdfGenerated = true,
            SignaturePageGenerated = true,
            FilingStatus = approvedBy is null ? FilingStatus.PackageGenerated : FilingStatus.Approved,
            ApprovedBy = approvedBy,
            ApprovedAt = approvedBy is null ? null : DateTime.UtcNow,
            SignedByDirector = "Aisling Director",
            SignedBySecretary = "Brian Secretary",
            SignedAt = approvedBy is null ? null : DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRevenuePackageAsync(
        AccountsDbContext db,
        int periodId,
        bool internalChecksPassed,
        bool externallyValidated)
    {
        db.RevenueFilingPackages.Add(new RevenueFilingPackage
        {
            PeriodId = periodId,
            FilingStatus = internalChecksPassed ? FilingStatus.ReadyForReview : FilingStatus.PackageGenerated,
            IxbrlGenerated = internalChecksPassed,
            IxbrlValidated = externallyValidated,
            IxbrlValidationErrors = internalChecksPassed
                ? "Internal checks passed. External ROS/iXBRL validation is still required."
                : "Internal checks pending."
        });
        await db.SaveChangesAsync();
    }
}
