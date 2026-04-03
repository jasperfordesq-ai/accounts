using Accounts.Api.Entities;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class DeadlineEndpoints
{
    public static void MapDeadlineEndpoints(this WebApplication app)
    {
        var companyGroup = app.MapGroup("/api/companies/{companyId:int}").WithTags("Deadlines");
        var periodGroup = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}").WithTags("Deadlines");

        // Get all deadlines for a company
        companyGroup.MapGet("/deadlines", async (int companyId, DeadlineService service) =>
        {
            var deadlines = await service.GetDeadlinesAsync(companyId);
            return Results.Ok(deadlines);
        });

        // Get upcoming deadline for a company
        companyGroup.MapGet("/deadlines/upcoming", async (int companyId, DeadlineService service) =>
        {
            var deadline = await service.GetUpcomingDeadlineAsync(companyId);
            return deadline != null ? Results.Ok(deadline) : Results.Ok(new { message = "No upcoming deadlines" });
        });

        // Check audit exemption jeopardy
        companyGroup.MapGet("/deadlines/jeopardy", async (int companyId, DeadlineService service) =>
        {
            var jeopardy = await service.CheckAuditExemptionJeopardyAsync(companyId);
            return Results.Ok(jeopardy);
        });

        // Calculate deadlines for a period
        periodGroup.MapPost("/deadlines/calculate", async (int companyId, int periodId, DeadlineService service) =>
        {
            try
            {
                var deadlines = await service.CalculateDeadlinesAsync(companyId, periodId);
                return Results.Ok(deadlines);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Mark a period as filed
        periodGroup.MapPost("/mark-filed", async (int companyId, int periodId, MarkFiledInput input, DeadlineService service) =>
        {
            try
            {
                var deadline = await service.MarkFiledAsync(companyId, periodId, input.DeadlineType, input.FiledDate);
                return Results.Ok(deadline);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record MarkFiledInput(DeadlineType DeadlineType, DateOnly FiledDate);
