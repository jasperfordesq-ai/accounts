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
        group.MapPut("/cro-status", async (int companyId, int periodId, FilingStatusInput input, FilingWorkflowService service) =>
        {
            try
            {
                var result = await service.UpdateCroStatusAsync(periodId, input.Status, input.By);
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
                var result = await service.MarkDocumentGeneratedAsync(periodId, input.DocumentType);
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

public record FilingStatusInput(FilingStatus Status, string? By);
public record MarkGeneratedInput(string DocumentType);
