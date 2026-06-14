using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class FilingWorkflowEndpoints
{
    public static void MapFilingWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/filing").WithTags("Filing Workflow");

        group.MapGet("/status", async (int companyId, int periodId, FilingWorkflowService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var status = await service.GetStatusAsync(companyId, periodId);
            return Results.Ok(status);
        });

        group.MapPut("/cro-status", async (int companyId, int periodId, FilingStatusInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            var result = await service.UpdateCroStatusAsync(
                companyId,
                periodId,
                input.Status,
                AuthenticatedIdentity.ReviewerDisplayName(user),
                input.Reason,
                input.SubmissionReference,
                AuthenticatedIdentity.AuditUserId(user));
            return Results.Ok(result);
        });

        group.MapPost("/cro-payment", async (int companyId, int periodId, CroPaymentInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            var result = await service.ConfirmCroPaymentAsync(
                companyId,
                periodId,
                AuthenticatedIdentity.ReviewerDisplayName(user),
                AuthenticatedIdentity.AuditUserId(user));
            return Results.Ok(result);
        });

        group.MapPost("/charity-report-generated", async (int companyId, int periodId, CharityReportGeneratedInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            await service.RecordCharityReportGeneratedAsync(companyId, periodId, input.ReportType, AuditUserId(context));
            var status = await service.GetStatusAsync(companyId, periodId);
            return Results.Ok(status.Charity);
        });

        group.MapPut("/charity-status", async (int companyId, int periodId, CharityFilingStatusInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            var result = await service.UpdateCharityStatusAsync(
                companyId,
                periodId,
                input.Status,
                AuthenticatedIdentity.ReviewerDisplayName(user),
                input.Reason,
                input.AnnualReturnReference,
                AuthenticatedIdentity.AuditUserId(user));
            return Results.Ok(result);
        });

        group.MapPost("/mark-generated", async (int companyId, int periodId, MarkGeneratedInput input, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            _ = input;
            return Results.StatusCode(StatusCodes.Status410Gone);
        });

        group.MapPost("/validate-ixbrl", ValidateIxbrlEndpointAsync);
    }

    public static async Task<IResult> ValidateIxbrlEndpointAsync(
        int companyId,
        int periodId,
        FilingWorkflowService service,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        await service.ValidateIxbrlAsync(companyId, periodId, AuditUserId(context));
        var status = await service.GetStatusAsync(companyId, periodId);
        return Results.Ok(status.Revenue);
    }

    private static string? AuditUserId(HttpContext context)
    {
        var user = AuthContext.GetUser(context);
        return user is null ? null : AuthenticatedIdentity.AuditUserId(user);
    }
}

public record FilingStatusInput(FilingStatus Status, string? By, string? Reason, string? SubmissionReference);
public record CharityFilingStatusInput(FilingStatus Status, string? Reason, string? AnnualReturnReference);
public record CharityReportGeneratedInput(string ReportType);
public record MarkGeneratedInput(string DocumentType);
public record CroPaymentInput(string? By);
