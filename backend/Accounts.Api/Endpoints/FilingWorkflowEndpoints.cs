using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;

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

        group.MapGet("/readiness-profile", async (int companyId, int periodId, FilingReadinessProfileService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var profile = await service.GetProfileAsync(companyId, periodId);
            return Results.Ok(profile);
        })
            .Produces<FilingReadinessProfile>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/cro-status", async (int companyId, int periodId, FilingStatusInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess, [FromServices] IdempotencyService? idempotency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            return await ExecuteCommandAsync(
                context,
                db,
                idempotency,
                user,
                IdempotencyOperations.CroStatus,
                new { companyId, periodId, input.Status, input.Reason, input.SubmissionReference },
                _ => service.UpdateCroStatusAsync(
                    companyId,
                    periodId,
                    input.Status,
                    AuthenticatedIdentity.ReviewerDisplayName(user),
                    input.Reason,
                    input.SubmissionReference,
                    AuthenticatedIdentity.AuditUserId(user)),
                "CroFilingPackage",
                result => result.Id);
        });

        group.MapPost("/cro-payment", async (int companyId, int periodId, CroPaymentInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess, [FromServices] IdempotencyService? idempotency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            return await ExecuteCommandAsync(
                context,
                db,
                idempotency,
                user,
                IdempotencyOperations.CroPayment,
                new { companyId, periodId },
                _ => service.ConfirmCroPaymentAsync(
                    companyId,
                    periodId,
                    AuthenticatedIdentity.ReviewerDisplayName(user),
                    AuthenticatedIdentity.AuditUserId(user)),
                "CroFilingPackage",
                result => result.Id);
        });

        group.MapPost("/charity-report-generated", async (int companyId, int periodId, CharityReportGeneratedInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess, [FromServices] IdempotencyService? idempotency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            idempotency ??= new IdempotencyService(db);
            var command = await IdempotencyHttpContract.ExecuteAsync(
                context,
                idempotency,
                user,
                IdempotencyOperations.CharityReportGenerated,
                new { companyId, periodId, input.ReportType },
                async _ =>
                {
                    var package = await service.RecordCharityReportGeneratedAsync(
                        companyId,
                        periodId,
                        input.ReportType,
                        AuthenticatedIdentity.AuditUserId(user));
                    var status = await service.GetStatusAsync(companyId, periodId);
                    return new IdempotencyOperationOutcome<FilingWorkflowService.CharityFilingStatus>(
                        status.Charity!,
                        "CharityFilingPackage",
                        package.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                });
            return command.Error ?? IdempotencyHttpContract.JsonResult(command.Execution!);
        });

        group.MapPut("/charity-status", async (int companyId, int periodId, CharityFilingStatusInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess, [FromServices] IdempotencyService? idempotency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            return await ExecuteCommandAsync(
                context,
                db,
                idempotency,
                user,
                IdempotencyOperations.CharityStatus,
                new { companyId, periodId, input.Status, input.Reason, input.AnnualReturnReference },
                _ => service.UpdateCharityStatusAsync(
                    companyId,
                    periodId,
                    input.Status,
                    AuthenticatedIdentity.ReviewerDisplayName(user),
                    input.Reason,
                    input.AnnualReturnReference,
                    AuthenticatedIdentity.AuditUserId(user)),
                "CharityFilingPackage",
                result => result.Id);
        });

        group.MapPost("/revenue-external-validation", async (int companyId, int periodId, RevenueExternalValidationInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess, [FromServices] IdempotencyService? idempotency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            return await ExecuteCommandAsync(
                context,
                db,
                idempotency,
                user,
                IdempotencyOperations.RevenueExternalValidation,
                new { companyId, periodId, input.ArtifactSha256, input.ExternalReference },
                _ => service.RecordExternalRevenueValidationAsync(
                    companyId,
                    periodId,
                    input.ArtifactSha256,
                    input.ExternalReference,
                    AuthenticatedIdentity.AuditUserId(user)),
                "RevenueFilingPackage",
                result => result.Id);
        });

        group.MapPut("/revenue-status", async (int companyId, int periodId, RevenueFilingStatusInput input, FilingWorkflowService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess, [FromServices] IdempotencyService? idempotency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            return await ExecuteCommandAsync(
                context,
                db,
                idempotency,
                user,
                IdempotencyOperations.RevenueStatus,
                new { companyId, periodId, input.Status, input.Reason, input.FilingReference },
                _ => service.UpdateRevenueStatusAsync(
                    companyId,
                    periodId,
                    input.Status,
                    AuthenticatedIdentity.ReviewerDisplayName(user),
                    input.Reason,
                    input.FilingReference,
                    AuthenticatedIdentity.AuditUserId(user)),
                "RevenueFilingPackage",
                result => result.Id);
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
        ApiAccessService apiAccess,
        [FromServices] IdempotencyService? idempotency = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        idempotency ??= new IdempotencyService(db);
        var command = await IdempotencyHttpContract.ExecuteAsync(
            context,
            idempotency,
            user,
            IdempotencyOperations.RevenueIxbrlValidation,
            new { companyId, periodId },
            async _ =>
            {
                var package = await service.ValidateIxbrlAsync(
                    companyId,
                    periodId,
                    AuthenticatedIdentity.AuditUserId(user));
                var status = await service.GetStatusAsync(companyId, periodId);
                return new IdempotencyOperationOutcome<FilingWorkflowService.RevenueFilingStatus>(
                    status.Revenue,
                    "RevenueFilingPackage",
                    package.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });
        return command.Error ?? IdempotencyHttpContract.JsonResult(command.Execution!);
    }

    private static async Task<IResult> ExecuteCommandAsync<T>(
        HttpContext context,
        AccountsDbContext db,
        IdempotencyService? idempotency,
        AuthenticatedUser actor,
        string operation,
        object requestPayload,
        Func<CancellationToken, Task<T>> action,
        string resourceType,
        Func<T, int> resourceId)
    {
        idempotency ??= new IdempotencyService(db);
        var command = await IdempotencyHttpContract.ExecuteAsync(
            context,
            idempotency,
            actor,
            operation,
            requestPayload,
            async cancellationToken =>
            {
                var result = await action(cancellationToken);
                return new IdempotencyOperationOutcome<T>(
                    result,
                    resourceType,
                    resourceId(result).ToString(System.Globalization.CultureInfo.InvariantCulture));
            });
        return command.Error ?? IdempotencyHttpContract.JsonResult(command.Execution!);
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
public record RevenueExternalValidationInput(string ArtifactSha256, string ExternalReference);
public record RevenueFilingStatusInput(FilingStatus Status, string? Reason, string? FilingReference);
public record MarkGeneratedInput(string DocumentType);
public record CroPaymentInput(string? By);
