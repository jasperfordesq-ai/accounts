using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
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

        Assert.Equal("ready-for-accountant-review", profile.SignOffPacket.State);
        Assert.Equal("Ready for accountant review", profile.SignOffPacket.StateLabel);
        Assert.True(profile.SignOffPacket.ReadyForAccountantApproval);
        Assert.False(profile.SignOffPacket.ReadyForExternalFiling);
        Assert.Null(profile.SignOffPacket.ApprovedBy);
        Assert.Contains("approve-cro-pack", profile.SignOffPacket.AllowedNextActions);
        Assert.Contains(profile.SignOffPacket.OpenBlockers, message => message.Contains("named qualified accountant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.SignOffPacket.OpenWarnings, message => message.Contains("External ROS/iXBRL validation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.SignOffPacket.Steps, step => step.Code == "accountant-approval" && step.State == "blocked");
        Assert.Contains(profile.SignOffPacket.Steps, step => step.Code == "external-validation" && step.State == "warning");
        Assert.Contains(
            profile.SignOffPacket.Steps.Single(step => step.Code == "accountant-approval").Sources,
            source => source.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId);
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
        CompanySizeClass sizeClass)
    {
        var company = new Company
        {
            LegalName = "Example Accounts Limited",
            CompanyType = companyType,
            CroNumber = "123456",
            TaxReference = "1234567A",
            IncorporationDate = new DateOnly(2020, 1, 1),
            ArdMonth = 9,
            IsTrading = true
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

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

        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = sizeClass,
            Turnover = 1_000_000m,
            BalanceSheetTotal = 500_000m,
            AvgEmployees = 8
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            CanUseMicro = sizeClass == CompanySizeClass.Micro,
            CanFileAbridged = sizeClass <= CompanySizeClass.Small,
            AuditExempt = sizeClass <= CompanySizeClass.Small,
            ElectedRegime = sizeClass switch
            {
                CompanySizeClass.Micro => ElectedRegime.Micro,
                CompanySizeClass.Small => ElectedRegime.SmallAbridged,
                CompanySizeClass.Medium => ElectedRegime.Medium,
                _ => ElectedRegime.Full
            },
            RequiredStatementsJson = "[]",
            RequiredNotesJson = "[]"
        });
        await db.SaveChangesAsync();

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
