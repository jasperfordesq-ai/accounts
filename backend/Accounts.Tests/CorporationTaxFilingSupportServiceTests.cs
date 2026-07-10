using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxFilingSupportServiceTests
{
    [Fact]
    public async Task ReviewPaymentAndVoid_AreServerAttributedAuditedAndRemainSupportOnly()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db, isFirstYear: true);
        var service = await ServiceAsync(db, fixture);

        var before = await service.GetAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Null(before.Review);
        Assert.Contains(before.FilingSupport.BlockingReasons, item => item.Contains("No retained preliminary-tax basis review", StringComparison.Ordinal));

        var reviewed = await service.SaveReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            FirstPeriodReview(),
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-review");
        Assert.Equal("Fixture Preparer", reviewed.Review?.PreparedBy);
        Assert.False(reviewed.FilingSupport.IsCompleteCt1Return);
        Assert.False(reviewed.FilingSupport.DirectRosSubmissionSupported);
        Assert.False(reviewed.Worksheet.IsCompleteCt1Return);
        Assert.True(reviewed.Worksheet.QualifiedAccountantReviewRequired);

        var paid = await service.RecordPaymentAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            new(
                new DateOnly(2025, 11, 23),
                100m,
                CorporationTaxPaymentKind.PreliminarySecondOrSingle,
                "Retained ROS payment confirmation fixture PTL2-001.",
                "ROS-PTL2-001"),
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-payment");
        var payment = Assert.Single(paid.Payments);
        Assert.Equal("Fixture Preparer", payment.RecordedBy);
        Assert.Equal(100m, paid.FilingSupport.PreliminaryTaxPaymentsRecorded);

        var corrected = await service.VoidPaymentAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            payment.Id,
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-void");
        Assert.Empty(corrected.Payments);
        var retained = await db.CorporationTaxPaymentRecords.IgnoreQueryFilters().SingleAsync();
        Assert.True(retained.IsVoided);
        Assert.Equal("Fixture Preparer", retained.VoidedBy);
        Assert.Contains("active Corporation Tax tracker", retained.VoidReason, StringComparison.Ordinal);

        Assert.Equal(3, await db.AuditLogs.CountAsync(item =>
            item.Action == AuditEventCodes.CorporationTaxFilingSupportReviewUpserted
            || item.Action == AuditEventCodes.CorporationTaxPaymentEvidenceRecorded
            || item.Action == AuditEventCodes.CorporationTaxPaymentEvidenceVoided));
    }

    [Fact]
    public async Task NonFirstPeriod_RequiresExactPriorDatesAmountsAndEvidence()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db, isFirstYear: false);
        var service = await ServiceAsync(db, fixture);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            FirstPeriodReview(),
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-invalid"));
        Assert.Contains("non-first accounting period", exception.Message, StringComparison.OrdinalIgnoreCase);

        var saved = await service.SaveReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            FirstPeriodReview() with
            {
                PriorPeriodStart = new DateOnly(2024, 1, 1),
                PriorPeriodEnd = new DateOnly(2024, 12, 31),
                PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 = 1_000m,
                PriorPeriodSection239IncomeTax = 0m,
                PriorLiabilityEvidenceReference = "Signed prior CT1 retained evidence fixture 2024."
            },
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-valid");
        Assert.Equal(new DateOnly(2024, 1, 1), saved.Review?.PriorPeriodStart);
        Assert.Equal(CorporationTaxFilingSupportCalculator.CompanyPaymentClass.Small, saved.FilingSupport.CompanyClass);
    }

    [Fact]
    public async Task DuplicateOrFuturePaymentEvidence_FailsClosedWithoutSecondMutation()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db, isFirstYear: true);
        var service = await ServiceAsync(db, fixture);
        await service.SaveReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            FirstPeriodReview(),
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-review");
        var input = new CorporationTaxFilingSupportService.PaymentInput(
            new DateOnly(2025, 11, 23),
            100m,
            CorporationTaxPaymentKind.PreliminarySecondOrSingle,
            "Retained ROS payment confirmation fixture PTL2-duplicate.",
            null);
        await service.RecordPaymentAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            input,
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-one");

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordPaymentAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            input,
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-two"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordPaymentAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            input with { PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) },
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-future"));
        Assert.Single(await db.CorporationTaxPaymentRecords.ToListAsync());
    }

    [Fact]
    public async Task ReadAndExportEndpoints_ReturnSupportOnlyHeadersAndNeverAFileableCt1()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db, isFirstYear: true);
        var service = await ServiceAsync(db, fixture);
        await service.SaveReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            FirstPeriodReview(),
            "user:fixture-preparer",
            "Fixture Preparer",
            "request-review");
        var context = AuthenticatedContext(fixture.Company.TenantId!.Value, fixture.Company.Id);

        var result = await RevenueEndpoints.GetCorporationTaxFilingSupportEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            null,
            service,
            db,
            context);
        var ok = Assert.IsType<Ok<CorporationTaxFilingSupportService.Response>>(result);
        Assert.False(ok.Value!.FilingSupport.IsCompleteCt1Return);
        Assert.Equal("false", context.Response.Headers["X-Complete-CT1-Return"]);
        Assert.Equal("false", context.Response.Headers["X-Direct-ROS-Submission-Supported"]);
        Assert.Equal("true", context.Response.Headers["X-Qualified-Accountant-Review-Required"]);

        var exportContext = AuthenticatedContext(fixture.Company.TenantId!.Value, fixture.Company.Id);
        var export = await RevenueEndpoints.ExportCorporationTaxSupportWorksheetEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            exportContext);
        var file = Assert.IsType<FileContentHttpResult>(export);
        Assert.Equal("CORPORATION_TAX_SUPPORT_ONLY_NOT_CT1_period_" + fixture.Period.Id + ".csv", file.FileDownloadName);
        Assert.Contains("NOT A CT1 RETURN", System.Text.Encoding.UTF8.GetString(file.FileContents.Span), StringComparison.Ordinal);
    }

    private static CorporationTaxFilingSupportService.ReviewInput FirstPeriodReview() => new(
        PriorPeriodStart: null,
        PriorPeriodEnd: null,
        PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239: null,
        PriorPeriodSection239IncomeTax: null,
        CurrentPeriodSection239IncomeTax: 0m,
        PriorLiabilityEvidenceReference: null,
        HasInterestLimitationRule: false,
        UsesNotionalGroupPaymentAllocation: false,
        HasDirtOrOtherWithholdingCredits: false,
        HasOtherPreliminaryTaxAdjustments: false,
        HasMandatoryElectronicFilingExemption: false,
        EvidenceNote: "Retained first-period preliminary-tax review fixture evidence.");

    private static async Task<CorporationTaxFilingSupportService> ServiceAsync(AccountsDbContext db, Fixture fixture)
    {
        var statements = new FinancialStatementsService(db);
        var tax = new TaxComputationService(db, statements);
        await tax.SaveScopeReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            new(
                IsCloseCompany: false,
                IsServiceCompany: false,
                HasGroupOrConsortiumRelief: false,
                HasChargeableGains: false,
                HasForeignIncomeOrTaxCredits: false,
                HasExceptedTrade: false,
                HasOtherReliefsOrSpecialRegimes: false,
                DeclaredPassiveIncomePresent: false,
                PassiveIncomeClassificationReviewed: true,
                LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                BroughtForwardTradingLoss: 0m,
                BroughtForwardLossEvidence: null,
                EvidenceNote: "Retained bounded tax-scope evidence for filing-support fixture."),
            "Fixture Preparer");
        return new CorporationTaxFilingSupportService(db, tax, new AuditService(db));
    }

    private static async Task<Fixture> SeedAsync(AccountsDbContext db, bool isFirstYear)
    {
        var tenant = new Tenant { Name = "Tax Filing Support Firm", Slug = $"tax-support-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Tax Filing Support Limited",
            CroNumber = "765432",
            TaxReference = "1234567AB",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2024, 1, 1),
            IsTrading = true
        };
        var period = new AccountingPeriod
        {
            Company = company,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = isFirstYear
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return new Fixture(company, period);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"tax-filing-support-{Guid.NewGuid():N}")
            .Options;
        return new AccountsDbContext(options);
    }

    private static DefaultHttpContext AuthenticatedContext(int tenantId, int companyId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            901,
            tenantId,
            "Tax Filing Support Firm",
            "tax-fixture@example.invalid",
            "Fixture Preparer",
            "Accountant",
            new HashSet<int> { companyId });
        return context;
    }

    private sealed record Fixture(Company Company, AccountingPeriod Period);
}
