using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Accounts.Api.Endpoints;

public static class CompanyOnboardingEndpoint
{
    public static async Task<IResult> CreateAsync(
        CompanyOnboardingInput input,
        HttpContext context,
        ApiAccessService apiAccess,
        CompanyOnboardingService service)
    {
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        if (!string.Equals(user.Role, "Owner", StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var idempotencyKey = context.Request.Headers[IdempotencyHttpContract.RequestHeader].ToString();
        var errors = CompanyOnboardingValidation.Validate(input, idempotencyKey);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        try
        {
            var result = await service.CreateAsync(
                input,
                idempotencyKey,
                user,
                context.RequestAborted);
            context.Response.Headers[IdempotencyHttpContract.ReplayedHeader] = result.WasReplay ? "true" : "false";
            context.Response.Headers[IdempotencyHttpContract.RecordIdHeader] = result.IdempotencyRecordId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Response.Headers[IdempotencyHttpContract.OperationHeader] = IdempotencyOperations.CompanyOnboard;
            context.Response.Headers[IdempotencyHttpContract.ExpiresAtHeader] = result.ExpiresAtUtc.ToUniversalTime().ToString("O");
            return result.HttpStatusCode == StatusCodes.Status201Created
                ? Results.Created($"/api/companies/{result.Outcome.CompanyId}", result.Outcome)
                : Results.Json(result.Outcome, statusCode: result.HttpStatusCode);
        }
        catch (CompanyOnboardingValidationException ex)
        {
            return Results.ValidationProblem(ex.Errors);
        }
        catch (CompanyOnboardingIdempotencyConflictException ex)
        {
            return Results.Conflict(new
            {
                error = ex.Message,
                code = "onboarding_idempotency_conflict"
            });
        }
    }
}
