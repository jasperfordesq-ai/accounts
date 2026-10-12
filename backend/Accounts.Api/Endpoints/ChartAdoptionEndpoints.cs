using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public sealed record ChartAdoptionRefusalResponse(string Error, string Code, ChartAdoptionPlan Plan);

/// <summary>
/// Adopting the charity / CLG chart on an existing company: a read-only dry run that returns the plan and
/// its SHA-256, then the execution of exactly that plan. Owner only (RoleAuthorizationService); execution
/// also needs a recent authenticator verification (RecentAuthenticationMiddleware).
/// The company master-data lock (AccountingWriteGuard.BlockIfCompanyMasterDataLockedAsync) is deliberately
/// not used: it refuses whenever any period is locked, so a company with any finalised year could never
/// adopt. The plan applies finer guards instead: it refuses when an account it changes has an entry in a
/// Finalised, Filed or locked period, or when a filed or submitted CRO pack would go stale, and it requires
/// acknowledgement for every other CRO pack that goes stale.
/// </summary>
public static class ChartAdoptionEndpoints
{
    public static void MapChartAdoptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/categories/chart-adoption").WithTags("Categories");

        group.MapPost("/plan", PlanEndpointAsync)
            .Produces<ChartAdoptionPlan>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", AdoptEndpointAsync)
            .Produces<ChartAdoptionPlan>()
            .Produces<ChartAdoptionRefusalResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);
    }

    public static async Task<IResult> PlanEndpointAsync(
        int companyId,
        ChartAdoptionService service,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        return TypedResults.Ok(await service.PlanAsync(companyId, context.RequestAborted));
    }

    public static async Task<IResult> AdoptEndpointAsync(
        int companyId,
        ChartAdoptionInput input,
        ChartAdoptionService service,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (input.Variant != Entities.ChartOfAccountsVariant.CharityClg)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["variant"] = ["Only the charity / CLG chart of accounts (CharityClg) can be adopted."]
            });
        if (string.IsNullOrWhiteSpace(input.PlanSha256))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["planSha256"] = ["Review the plan first and send its planSha256."]
            });

        var outcome = await service.AdoptAsync(
            companyId,
            input,
            AuthContext.RequireUser(context),
            DomainAuditCoverage.RequestId(context),
            context.RequestAborted);
        return outcome.Refusal switch
        {
            null => (IResult)TypedResults.Ok(outcome.Plan),
            ChartAdoptionRefusal.PlanChanged => TypedResults.Conflict(new ChartAdoptionRefusalResponse(outcome.Message!, "chart_adoption_plan_changed", outcome.Plan)),
            ChartAdoptionRefusal.Blocked => TypedResults.Conflict(new ChartAdoptionRefusalResponse(outcome.Message!, "chart_adoption_blocked", outcome.Plan)),
            _ => TypedResults.Conflict(new ChartAdoptionRefusalResponse(outcome.Message!, "chart_adoption_stale_filing_artifacts_unacknowledged", outcome.Plan))
        };
    }
}
