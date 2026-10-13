using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public sealed record ConnectorApprovalGrantResponse(bool Ok, Guid ApprovalId, string Summary, DateTime ExpiresAtUtc);

public sealed record ConnectorActivityRow(
    long Id,
    Guid SessionFamilyId,
    string AccessLevel,
    string DataScope,
    string Method,
    string RoutePattern,
    string? ResourceId,
    int? CompanyId,
    int StatusCode,
    string? Reason,
    DateTime OccurredAtUtc);

public sealed record ConnectorActivityPage(IReadOnlyList<ConnectorActivityRow> Data, int Page, int PageSize, bool HasMore);

/// <summary>
/// Sign-in, rotation, posture, approval and session management for the MCP connector. Sign-in and
/// refresh return tokens in the body and never set a cookie. Approval routes also accept a browser
/// session so a person can grant a pending approval from the web app.
/// </summary>
public static class ConnectorAuthEndpoints
{
    public const string LoginRateLimitPolicy = "connector-login";
    public const string RefreshRateLimitPolicy = "connector-refresh";
    public const string ApproveRateLimitPolicy = "connector-approve";

    public static void MapConnectorAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/connector").WithTags("Connector");

        group.MapPost("/login", async (ConnectorLoginInput input, ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await connectors.LoginAsync(input, ConnectorClientGuard.ClientVersion(context.Request), cancellationToken);
            return result.Succeeded
                ? Results.Ok(result.Response)
                : Results.Json(new { error = result.Error, code = result.Code }, statusCode: result.StatusCode);
        })
            .Produces<ConnectorLoginResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireRateLimiting(LoginRateLimitPolicy);

        group.MapPost("/refresh", async (ConnectorRefreshInput input, ConnectorSessionService connectors, CancellationToken cancellationToken) =>
        {
            var refreshed = await connectors.RefreshAsync(input.RefreshToken, cancellationToken);
            return refreshed is null
                ? Results.Json(new { error = "The connector session has ended. Run filingbridge-mcp connect again.", code = ConnectorErrorCodes.SessionInvalid }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(refreshed);
        })
            .Produces<ConnectorLoginResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(RefreshRateLimitPolicy);

        group.MapPost("/logout", async (ConnectorLogoutInput? input, ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
        {
            await connectors.LogoutAsync(
                AuthContext.GetUser(context),
                ConnectorSessionContext.Get(context),
                input?.RefreshToken,
                cancellationToken);
            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .RequireRateLimiting(RefreshRateLimitPolicy);

        group.MapGet("/session", (ConnectorSessionService connectors, HttpContext context) =>
        {
            var connector = ConnectorSessionContext.Get(context);
            if (connector is null)
                return Results.Json(new { error = "This route describes connector sessions only.", code = ConnectorErrorCodes.SessionInvalid }, statusCode: StatusCodes.Status404NotFound);
            return Results.Ok(connectors.DescribePosture(AuthContext.RequireUser(context), connector, connector.AccessTokenExpiresAtUtc));
        })
            .Produces<ConnectorSessionPosture>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/approve", async (ConnectorApproveInput input, ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
        {
            var grant = await connectors.GrantApprovalAsync(AuthContext.RequireUser(context), input.ApprovalId, input.Password, input.TotpCode, cancellationToken);
            return grant is null
                ? Results.Json(new { error = "The approval was not granted. Check the password and authenticator code, and that the approval has not expired.", code = ConnectorErrorCodes.ApprovalRefused }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(new ConnectorApprovalGrantResponse(true, grant.ApprovalId, grant.Summary, grant.ExpiresAtUtc));
        })
            .Produces<ConnectorApprovalGrantResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(ApproveRateLimitPolicy);

        group.MapGet("/approvals", async (ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await connectors.ListPendingApprovalsAsync(AuthContext.RequireUser(context), cancellationToken)))
            .Produces<IReadOnlyList<ConnectorApprovalView>>();

        group.MapGet("/approvals/{approvalId:guid}", async (Guid approvalId, ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
        {
            var view = await connectors.DescribeApprovalAsync(AuthContext.RequireUser(context), approvalId, cancellationToken);
            return view is null
                ? Results.Json(new { error = "No such approval for this user.", code = ConnectorErrorCodes.ApprovalNotFound }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(view);
        })
            .Produces<ConnectorApprovalView>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/sessions", async (int? userId, ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
        {
            var actor = AuthContext.RequireUser(context);
            var target = ResolveTargetUser(actor, userId);
            return target is null
                ? Results.Json(new { error = "Only an Owner can list another user's connected agents." }, statusCode: StatusCodes.Status403Forbidden)
                : Results.Ok(await connectors.ListSessionsAsync(target.Value, cancellationToken));
        })
            .Produces<IReadOnlyList<ConnectorSessionSummary>>()
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/sessions/{familyId:guid}/revoke", async (Guid familyId, int? userId, ConnectorSessionService connectors, HttpContext context, CancellationToken cancellationToken) =>
        {
            var actor = AuthContext.RequireUser(context);
            var target = ResolveTargetUser(actor, userId);
            if (target is null)
                return Results.Json(new { error = "Only an Owner can disconnect another user's agent." }, statusCode: StatusCodes.Status403Forbidden);
            return await connectors.RevokeFamilyAsync(actor, target.Value, familyId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/activity", async (int? userId, int? page, int? pageSize, AccountsDbContext db, HttpContext context, CancellationToken cancellationToken) =>
        {
            var actor = AuthContext.RequireUser(context);
            var target = ResolveTargetUser(actor, userId);
            if (target is null)
                return Results.Json(new { error = "Only an Owner can read another user's agent activity." }, statusCode: StatusCodes.Status403Forbidden);
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 100);
            var rows = await db.ConnectorActivityEvents.AsNoTracking()
                .Where(activity => activity.UserId == target.Value && activity.TenantId == actor.TenantId)
                .OrderByDescending(activity => activity.OccurredAtUtc)
                .ThenByDescending(activity => activity.Id)
                .Skip((currentPage - 1) * size)
                .Take(size + 1)
                .Select(activity => new ConnectorActivityRow(
                    activity.Id,
                    activity.SessionFamilyId,
                    activity.AccessLevel,
                    activity.DataScope,
                    activity.Method,
                    activity.RoutePattern,
                    activity.ResourceId,
                    activity.TargetCompanyId,
                    activity.StatusCode,
                    activity.Reason,
                    activity.OccurredAtUtc))
                .ToListAsync(cancellationToken);
            return Results.Ok(new ConnectorActivityPage(rows.Take(size).ToList(), currentPage, size, rows.Count > size));
        })
            .Produces<ConnectorActivityPage>()
            .Produces(StatusCodes.Status403Forbidden);
    }

    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", async (string? q, string? types, int? companyId, int? periodId, int? limit, SearchService search, HttpContext context, CancellationToken cancellationToken) =>
        {
            var typeList = string.IsNullOrWhiteSpace(types)
                ? null
                : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(await search.SearchAsync(context, q, typeList, companyId, periodId, limit ?? 20, cancellationToken));
        })
            .WithTags("Search")
            .Produces<SearchResponse>();
    }

    private static int? ResolveTargetUser(AuthenticatedUser actor, int? requestedUserId)
    {
        if (requestedUserId is null || requestedUserId == actor.UserId) return actor.UserId;
        return actor.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase) ? requestedUserId : null;
    }
}
