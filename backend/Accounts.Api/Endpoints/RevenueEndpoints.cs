using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class RevenueEndpoints
{
    public static void MapRevenueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/revenue").WithTags("Revenue & Tax");

        group.MapGet("/tax-computation", async (int companyId, int periodId, TaxComputationService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.ComputeAsync(companyId, periodId);
            context.Response.Headers["X-Tax-Output-Kind"] = TaxComputationService.OutputKind;
            context.Response.Headers["X-Complete-CT1-Return"] = "false";
            return Results.Ok(result);
        })
            .Produces<TaxComputationService.TaxComputation>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/ct1-support", async (int companyId, int periodId, TaxComputationService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetCt1SupportDataAsync(companyId, periodId);
            context.Response.Headers["X-Tax-Output-Kind"] = TaxComputationService.OutputKind;
            context.Response.Headers["X-Complete-CT1-Return"] = "false";
            return Results.Ok(result);
        })
            .Produces<TaxComputationService.Ct1SupportData>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/scope-review", GetCorporationTaxScopeReviewEndpointAsync);

        group.MapPut("/scope-review", UpsertCorporationTaxScopeReviewEndpointAsync);

        group.MapGet("/filing-support", GetCorporationTaxFilingSupportEndpointAsync)
            .Produces<CorporationTaxFilingSupportService.Response>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPut("/filing-support", UpsertCorporationTaxFilingSupportReviewEndpointAsync);
        group.MapPost("/filing-support/payments", RecordCorporationTaxPaymentEndpointAsync);
        group.MapDelete("/filing-support/payments/{paymentId:int}", VoidCorporationTaxPaymentEndpointAsync);
        group.MapGet("/ct1-support/worksheet", GetCorporationTaxSupportWorksheetEndpointAsync);
        group.MapGet("/ct1-support/worksheet.csv", ExportCorporationTaxSupportWorksheetEndpointAsync);

        group.MapGet("/ixbrl", async (int companyId, int periodId, IxbrlService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var ixbrl = await service.GenerateReviewIxbrlAsync(companyId, periodId);
            return Results.File(ixbrl, "application/xhtml+xml", $"DRAFT_NOT_FOR_FILING_financial_statements_{periodId}.xhtml");
        });

        group.MapGet("/ixbrl/final", async (int companyId, int periodId, [FromServices] FilingReleaseGate gate, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var user = AuthContext.GetUser(context);
            var artifact = await gate.GetFinalArtifactAsync(
                companyId,
                periodId,
                FilingReleaseArtifact.RevenueIxbrl,
                user is null ? null : AuthenticatedIdentity.AuditUserId(user));
            context.Response.Headers["X-Artifact-Sha256"] = artifact.Sha256;
            context.Response.Headers["X-Release-Candidate"] = artifact.ReleaseCandidate;
            return Results.File(artifact.Content, artifact.MediaType, artifact.FileName);
        });
    }

    public static async Task<IResult> GetCorporationTaxScopeReviewEndpointAsync(
        int companyId,
        int periodId,
        TaxComputationService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var review = await db.CorporationTaxScopeReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PeriodId == periodId);
        var lossRecord = await db.CorporationTaxLossRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PeriodId == periodId);
        TaxComputationService.TaxComputation? computation = null;
        string? computationFailure = null;
        try
        {
            computation = await service.ComputeAsync(companyId, periodId);
        }
        catch (BusinessRuleException exception)
        {
            computationFailure = exception.Message;
        }
        return Results.Ok(new { Review = review, LossRecord = lossRecord, Computation = computation, ComputationFailure = computationFailure });
    }

    public static async Task<IResult> UpsertCorporationTaxScopeReviewEndpointAsync(
        int companyId,
        int periodId,
        TaxComputationService.CorporationTaxScopeReviewInput input,
        TaxComputationService service,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var oldValue = await db.CorporationTaxScopeReviews
            .AsNoTracking()
            .Where(item => item.PeriodId == periodId)
            .Select(item => new
            {
                item.IsCloseCompany,
                item.IsServiceCompany,
                item.HasGroupOrConsortiumRelief,
                item.HasChargeableGains,
                item.HasForeignIncomeOrTaxCredits,
                item.HasExceptedTrade,
                item.HasOtherReliefsOrSpecialRegimes,
                item.DeclaredPassiveIncomePresent,
                item.PassiveIncomeClassificationReviewed,
                item.LossTreatment,
                item.BroughtForwardTradingLoss,
                item.BroughtForwardLossEvidence,
                item.PreparedBy,
                item.PreparedAtUtc,
                item.EvidenceNote
            })
            .FirstOrDefaultAsync();
        var computation = await service.SaveScopeReviewAsync(
            companyId,
            periodId,
            input,
            AuthenticatedIdentity.ReviewerDisplayName(user));
        var review = await db.CorporationTaxScopeReviews.SingleAsync(item => item.PeriodId == periodId);
        await audit.LogAsync(
            companyId,
            periodId,
            "CorporationTaxScopeReview",
            review.Id,
            AuditEventCodes.CorporationTaxScopeReviewUpserted,
            oldValue,
            new
            {
                review.IsCloseCompany,
                review.IsServiceCompany,
                review.HasGroupOrConsortiumRelief,
                review.HasChargeableGains,
                review.HasForeignIncomeOrTaxCredits,
                review.HasExceptedTrade,
                review.HasOtherReliefsOrSpecialRegimes,
                review.DeclaredPassiveIncomePresent,
                review.PassiveIncomeClassificationReviewed,
                review.LossTreatment,
                review.BroughtForwardTradingLoss,
                review.BroughtForwardLossEvidence,
                review.PreparedBy,
                review.PreparedAtUtc,
                review.EvidenceNote,
                computation.SupportStatus,
                computation.FinalTaxChargeSupported,
                computation.CalculationSha256,
                computation.BlockingReasons
            },
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(new { Review = review, Computation = computation });
    }

    public static async Task<IResult> GetCorporationTaxFilingSupportEndpointAsync(
        int companyId,
        int periodId,
        [FromQuery(Name = "asOf")] DateOnly? asOfDate,
        CorporationTaxFilingSupportService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var response = await service.GetAsync(companyId, periodId, asOfDate, context.RequestAborted);
        ApplyFilingSupportHeaders(context, response.FilingSupport.OutputKind, response.FilingSupport.CalculationSha256);
        return Results.Ok(response);
    }

    public static async Task<IResult> UpsertCorporationTaxFilingSupportReviewEndpointAsync(
        int companyId,
        int periodId,
        CorporationTaxFilingSupportService.ReviewInput input,
        CorporationTaxFilingSupportService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var response = await service.SaveReviewAsync(
            companyId,
            periodId,
            input,
            AuthenticatedIdentity.AuditUserId(user),
            AuthenticatedIdentity.ReviewerDisplayName(user),
            context.TraceIdentifier,
            context.RequestAborted);
        ApplyFilingSupportHeaders(context, response.FilingSupport.OutputKind, response.FilingSupport.CalculationSha256);
        return Results.Ok(response);
    }

    public static async Task<IResult> RecordCorporationTaxPaymentEndpointAsync(
        int companyId,
        int periodId,
        CorporationTaxFilingSupportService.PaymentInput input,
        CorporationTaxFilingSupportService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var response = await service.RecordPaymentAsync(
            companyId,
            periodId,
            input,
            AuthenticatedIdentity.AuditUserId(user),
            AuthenticatedIdentity.ReviewerDisplayName(user),
            context.TraceIdentifier,
            context.RequestAborted);
        ApplyFilingSupportHeaders(context, response.FilingSupport.OutputKind, response.FilingSupport.CalculationSha256);
        return Results.Ok(response);
    }

    public static async Task<IResult> VoidCorporationTaxPaymentEndpointAsync(
        int companyId,
        int periodId,
        int paymentId,
        CorporationTaxFilingSupportService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var response = await service.VoidPaymentAsync(
            companyId,
            periodId,
            paymentId,
            AuthenticatedIdentity.AuditUserId(user),
            AuthenticatedIdentity.ReviewerDisplayName(user),
            context.TraceIdentifier,
            context.RequestAborted);
        ApplyFilingSupportHeaders(context, response.FilingSupport.OutputKind, response.FilingSupport.CalculationSha256);
        return Results.Ok(response);
    }

    public static async Task<IResult> GetCorporationTaxSupportWorksheetEndpointAsync(
        int companyId,
        int periodId,
        CorporationTaxFilingSupportService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var response = await service.GetAsync(companyId, periodId, cancellationToken: context.RequestAborted);
        ApplyFilingSupportHeaders(context, response.Worksheet.OutputKind, response.Worksheet.WorksheetSha256);
        return Results.Ok(response.Worksheet);
    }

    public static async Task<IResult> ExportCorporationTaxSupportWorksheetEndpointAsync(
        int companyId,
        int periodId,
        CorporationTaxFilingSupportService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var response = await service.GetAsync(companyId, periodId, cancellationToken: context.RequestAborted);
        ApplyFilingSupportHeaders(context, response.Worksheet.OutputKind, response.Worksheet.WorksheetSha256);
        return Results.File(
            CorporationTaxSupportWorksheetExporter.ExportUtf8(response.Worksheet),
            CorporationTaxSupportWorksheetExporter.MediaType,
            CorporationTaxSupportWorksheetExporter.FileName(periodId));
    }

    private static void ApplyFilingSupportHeaders(HttpContext context, string outputKind, string sha256)
    {
        context.Response.Headers["X-Tax-Output-Kind"] = outputKind;
        context.Response.Headers["X-Complete-CT1-Return"] = "false";
        context.Response.Headers["X-Direct-ROS-Submission-Supported"] = "false";
        context.Response.Headers["X-Qualified-Accountant-Review-Required"] = "true";
        context.Response.Headers["X-Working-Paper-SHA256"] = sha256;
    }
}
