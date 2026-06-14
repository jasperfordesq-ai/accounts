namespace Accounts.Api.Services;

public static class EndpointRequestAuthorization
{
    public static IResult? AuthorizeCurrentRequest(HttpContext context, ApiAccessService access)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var presentedKey = context.Request.Headers[access.HeaderName].FirstOrDefault();
            var apiDecision = access.Authorize(presentedKey, context.Request.Path, context.Request.Method);
            if (!apiDecision.IsAllowed)
                return Results.Json(new { error = apiDecision.DenialReason }, statusCode: StatusCodes.Status401Unauthorized);

            context.Items["ApiPrincipal"] = apiDecision.PrincipalName;
            context.Items["ApiRole"] = apiDecision.Role;
            context.Items["ApiCompanyId"] = apiDecision.CompanyId;
            context.Items["ApiAllowedCompanyIds"] = apiDecision.AllowedCompanyIds;
        }

        var user = AuthContext.GetUser(context);
        if (user is null)
            return null;

        var roleDecision = RoleAuthorizationService.Authorize(user, context.Request.Path, context.Request.Method);
        return roleDecision.IsAllowed
            ? null
            : Results.Json(new { error = roleDecision.DenialReason }, statusCode: StatusCodes.Status403Forbidden);
    }
}
