using System.Text;
using Accounts.Api.Middleware;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Accounts.Tests;

public sealed class InternalEvidenceAuthorizationTests
{
    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Reviewer", true)]
    [InlineData("Accountant", false)]
    [InlineData("Client", false)]
    public void ProductionReadinessRequiresAnExplicitReleaseReviewRole(string role, bool expected)
    {
        var decision = RoleAuthorizationService.Authorize(
            User(role),
            new PathString("/api/system/production-readiness"),
            HttpMethods.Get);

        Assert.Equal(expected, decision.IsAllowed);
        if (!expected)
            Assert.Contains("internal release evidence", decision.DenialReason);
    }

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Client")]
    public async Task MiddlewareReturnsForbiddenWithoutCallingTheReadinessEndpoint(string role)
    {
        var nextCalled = false;
        var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            Response = { Body = responseBody }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/system/production-readiness";
        context.Items[AuthContext.ItemKey] = User(role);
        var middleware = new RoleAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Contains("internal release evidence", Encoding.UTF8.GetString(responseBody.ToArray()));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Reviewer")]
    public async Task MiddlewareAllowsExplicitReleaseReviewRoles(string role)
    {
        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/system/production-readiness";
        context.Items[AuthContext.ItemKey] = User(role);
        var middleware = new RoleAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static AuthenticatedUser User(string role) =>
        new(7, 1, "Internal Evidence Firm", $"{role.ToLowerInvariant()}@example.ie", role, role);
}
