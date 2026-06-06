using Accounts.Api.Entities;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class FilingWorkflowEndpoints
{
    public static void MapFilingWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/filing").WithTags("Filing Workflow");

        // Get filing workflow status
        group.MapGet("/status", async (int companyId, int periodId, FilingWorkflowService service) =>
        {
            try
            {
                var status = await service.GetStatusAsync(companyId, periodId);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Update CRO filing status
        group.MapPut("/cro-status", async (int companyId, int periodId, FilingStatusInput input, FilingWorkflowService service, HttpContext context) =>
        {
            try
            {
                var user = AuthContext.RequireUser(context);
                var result = await service.UpdateCroStatusAsync(
                    companyId,
                    periodId,
                    input.Status,
                    AuthenticatedIdentity.ReviewerDisplayName(user),
                    input.Reason,
                    input.SubmissionReference);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Confirm CORE payment for the annual return
        group.MapPost("/cro-payment", async (int companyId, int periodId, CroPaymentInput input, FilingWorkflowService service, HttpContext context) =>
        {
            try
            {
                var user = AuthContext.RequireUser(context);
                var result = await service.ConfirmCroPaymentAsync(
                    companyId,
                    periodId,
                    AuthenticatedIdentity.ReviewerDisplayName(user));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Mark document generated
        group.MapPost("/mark-generated", async (int companyId, int periodId, MarkGeneratedInput input, FilingWorkflowService service) =>
        {
            try
            {
                var result = await service.MarkDocumentGeneratedAsync(companyId, periodId, input.DocumentType);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Validate iXBRL
        group.MapPost("/validate-ixbrl", async (int companyId, int periodId, FilingWorkflowService service) =>
        {
            try
            {
                var result = await service.ValidateIxbrlAsync(periodId);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record FilingStatusInput(FilingStatus Status, string? By, string? Reason, string? SubmissionReference);
public record MarkGeneratedInput(string DocumentType);
public record CroPaymentInput(string? By);
