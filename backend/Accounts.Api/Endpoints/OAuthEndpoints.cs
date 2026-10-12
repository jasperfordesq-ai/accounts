using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

/// <summary>
/// OAuth 2.1 endpoints for remote MCP clients. Reached through the frontend proxy; the consent page
/// itself (<c>/oauth/authorize</c>) and the well-known documents are served by the Next.js app.
/// </summary>
public static class OAuthEndpoints
{
    public const string RegisterRateLimitPolicy = "oauth-register";
    public const string AuthorizeRateLimitPolicy = "oauth-authorize";
    public const string TokenRateLimitPolicy = "oauth-token";

    public static void MapOAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/oauth").WithTags("OAuth");

        group.MapGet("/metadata/authorization-server", (OAuthService oauth) =>
            oauth.AuthorizationServerMetadata() is { } metadata
                ? Results.Json(metadata)
                : Results.NotFound())
            .Produces<Dictionary<string, object>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/metadata/protected-resource", (OAuthService oauth) =>
            oauth.ProtectedResourceMetadata() is { } metadata
                ? Results.Json(metadata)
                : Results.NotFound())
            .Produces<Dictionary<string, object>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/register", (OAuthRegisterRequest request, OAuthService oauth) =>
        {
            var result = oauth.Register(new OAuthRegisterInput(
                request.redirect_uris, request.client_name, request.grant_types, request.response_types, request.token_endpoint_auth_method));
            return result.Error is { } error
                ? Results.Json(new { error = error.Error, error_description = error.Description }, statusCode: error.StatusCode)
                : Results.Json(result.Value, statusCode: StatusCodes.Status201Created);
        })
            .Produces<Dictionary<string, object>>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireRateLimiting(RegisterRateLimitPolicy);

        group.MapGet("/authorize/describe", (
            string? client_id,
            string? redirect_uri,
            string? response_type,
            string? code_challenge,
            string? code_challenge_method,
            string? scope,
            string? resource,
            OAuthService oauth) =>
        {
            var result = oauth.Describe(client_id, redirect_uri, response_type, code_challenge, code_challenge_method, scope, resource);
            return result.Error is { } error
                ? Results.Json(new { error = error.Error, error_description = error.Description, code = error.Code }, statusCode: error.StatusCode)
                : Results.Ok(result.Value);
        })
            .Produces<OAuthAuthorizeDescription>()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/authorize", async (OAuthAuthorizeInput input, OAuthService oauth, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await oauth.AuthorizeAsync(input, context.Request.Headers.Origin.FirstOrDefault(), cancellationToken);
            return result.Error is { } error
                ? Results.Json(new { error = error.Description, code = error.Code ?? error.Error }, statusCode: error.StatusCode)
                : Results.Ok(new OAuthAuthorizeResponse(result.Value!));
        })
            .Produces<OAuthAuthorizeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireRateLimiting(AuthorizeRateLimitPolicy);

        group.MapPost("/token", async (HttpContext context, OAuthService oauth, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (!context.Request.HasFormContentType)
                return Results.Json(new { error = "invalid_request", error_description = "Use application/x-www-form-urlencoded." }, statusCode: StatusCodes.Status400BadRequest);
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var result = await oauth.ExchangeAsync(form, cancellationToken);
            return result.Error is { } error
                ? Results.Json(new { error = error.Error, error_description = error.Description }, statusCode: error.StatusCode)
                : Results.Json(result.Value);
        })
            .Accepts<OAuthTokenForm>("application/x-www-form-urlencoded")
            .Produces<Dictionary<string, object>>()
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .RequireRateLimiting(TokenRateLimitPolicy);

        group.MapPost("/revoke", async (HttpContext context, OAuthService oauth, CancellationToken cancellationToken) =>
        {
            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(cancellationToken);
                await oauth.RevokeAsync(form["token"].ToString(), cancellationToken);
            }
            return Results.Ok();
        })
            .Accepts<OAuthRevokeForm>("application/x-www-form-urlencoded")
            .Produces(StatusCodes.Status200OK)
            .DisableAntiforgery()
            .RequireRateLimiting(TokenRateLimitPolicy);
    }
}

#pragma warning disable IDE1006 // OAuth wire names are snake_case by specification.
public sealed record OAuthRegisterRequest(
    string[]? redirect_uris,
    string? client_name,
    string[]? grant_types,
    string[]? response_types,
    string? token_endpoint_auth_method);

public sealed record OAuthTokenForm(
    string grant_type,
    string? code,
    string? redirect_uri,
    string? client_id,
    string? code_verifier,
    string? refresh_token,
    string? resource);

public sealed record OAuthRevokeForm(string token);
#pragma warning restore IDE1006

public sealed record OAuthAuthorizeResponse(string RedirectTo);
