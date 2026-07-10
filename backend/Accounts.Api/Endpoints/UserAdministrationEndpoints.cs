using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class UserAdministrationEndpoints
{
    public static void MapUserAdministrationEndpoints(this WebApplication app)
    {
        var users = app.MapGroup("/api/admin/users").WithTags("User administration");

        users.MapGet("/", async (UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.ListAsync(AuthContext.RequireUser(context), cancellationToken)))
            .Produces<IReadOnlyList<UserAdministrationSummary>>();

        users.MapPost("/invite", async (InviteUserInput input, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Created("/api/admin/users", await lifecycle.InviteAsync(AuthContext.RequireUser(context), input, cancellationToken)))
            .Produces<UserProvisioningResult>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        users.MapPost("/", async (CreateUserInput input, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Created("/api/admin/users", await lifecycle.CreateAsync(AuthContext.RequireUser(context), input, cancellationToken)))
            .Produces<UserAdministrationSummary>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        users.MapPut("/{userId:int}/active", async (int userId, SetUserActiveInput input, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.SetActiveAsync(AuthContext.RequireUser(context), userId, input.Active, cancellationToken)))
            .Produces<UserAdministrationSummary>();

        users.MapPost("/{userId:int}/unlock", async (int userId, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.UnlockAsync(AuthContext.RequireUser(context), userId, cancellationToken)))
            .Produces<UserAdministrationSummary>();

        users.MapPost("/{userId:int}/password-reset", async (int userId, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.BeginPasswordResetAsync(AuthContext.RequireUser(context), userId, cancellationToken)))
            .Produces<UserProvisioningResult>();

        users.MapPut("/{userId:int}/role", async (int userId, SetUserRoleInput input, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.ChangeRoleAsync(AuthContext.RequireUser(context), userId, input.Role, cancellationToken)))
            .Produces<UserAdministrationSummary>();

        users.MapPut("/{userId:int}/companies", async (int userId, SetUserCompaniesInput input, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.SetCompanyAssignmentsAsync(AuthContext.RequireUser(context), userId, input.CompanyIds, cancellationToken)))
            .Produces<UserAdministrationSummary>();

        users.MapPost("/{userId:int}/revoke-sessions", async (int userId, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.RevokeSessionsAsync(AuthContext.RequireUser(context), userId, cancellationToken)))
            .Produces<UserAdministrationSummary>();

        users.MapPost("/{userId:int}/offboard", async (int userId, UserLifecycleService lifecycle, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await lifecycle.OffboardAsync(AuthContext.RequireUser(context), userId, cancellationToken)))
            .Produces<UserAdministrationSummary>();
    }
}

public sealed record SetUserActiveInput(bool Active);
public sealed record SetUserRoleInput(string? Role);
public sealed record SetUserCompaniesInput(IReadOnlyList<int>? CompanyIds);
