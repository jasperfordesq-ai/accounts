using Accounts.Api.Data;
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
        companyGroup.MapGet("/deadlines", async (int companyId, DeadlineService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var deadlines = await service.GetDeadlinesAsync(companyId);
            return Results.Ok(deadlines);
        });

        // Get upcoming deadline for a company
        companyGroup.MapGet("/deadlines/upcoming", async (int companyId, DeadlineService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var deadline = await service.GetUpcomingDeadlineAsync(companyId);
            return deadline != null ? Results.Ok(deadline) : Results.Ok(new { message = "No upcoming deadlines" });
        });

        // Check audit exemption jeopardy
        companyGroup.MapGet("/deadlines/jeopardy", async (int companyId, DeadlineService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var jeopardy = await service.CheckAuditExemptionJeopardyAsync(companyId);
            return Results.Ok(jeopardy);
        });

        // Calculate deadlines for a period
        periodGroup.MapPost("/deadlines/calculate", async (int companyId, int periodId, DeadlineService service, AccountsDbContext db, ApiAccessService apiAccess, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var denied = EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess);
            if (denied is not null)
                return denied;

            var deadlines = await service.CalculateDeadlinesAsync(companyId, periodId, AuditUserId(context));
            return Results.Ok(deadlines);
        });

        // Mark a period as filed
        periodGroup.MapPost("/mark-filed", async (int companyId, int periodId, MarkFiledInput input, DeadlineService service, AccountsDbContext db, ApiAccessService apiAccess, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var denied = EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess);
            if (denied is not null)
                return denied;

            var validation = ValidateMarkFiledInput(input);
            if (validation is not null)
                return validation;

            var deadline = await service.MarkFiledAsync(
                companyId,
                periodId,
                input.DeadlineType!.Value,
                input.FiledDate!.Value,
                AuditUserId(context),
                input.FilingReference);
            return Results.Ok(deadline);
        });
    }

    private static IResult? ValidateMarkFiledInput(MarkFiledInput input)
    {
        if (input.DeadlineType is null || !Enum.IsDefined(input.DeadlineType.Value))
            return Results.BadRequest(new { error = "Valid deadline type is required." });

        if (input.FiledDate is null || input.FiledDate.Value == default)
            return Results.BadRequest(new { error = "Filed date is required." });

        return null;
    }

    private static string? AuditUserId(HttpContext context)
    {
        var user = AuthContext.GetUser(context);
        return user is null ? null : AuthenticatedIdentity.AuditUserId(user);
    }
}

public record MarkFiledInput(DeadlineType? DeadlineType, DateOnly? FiledDate, string? FilingReference);
