using System.Text.Json;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class ConnectorMiddlewareTests
{
    [Theory]
    [InlineData("Origin", "https://evil.example")]
    [InlineData("Referer", "https://evil.example/page")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Dest", "empty")]
    public async Task BrowserEvidence_IsRefusedBeforeAnyCredentialIsRead(string header, string value)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var token = (await fixture.ConnectOwnerAsync()).Response!.AccessToken;
        var context = fixture.NewHttpContext("GET", "/api/companies", bearer: token);
        context.Request.Headers[header] = value;
        var called = false;

        await new UserSessionMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Contains(ConnectorErrorCodes.BrowserClientRejected, await ConnectorTestFixture.ReadResponseAsync(context));
    }

    [Fact]
    public async Task BrowserEvidence_IsRefusedOnConnectorSignIn()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var context = fixture.NewHttpContext("POST", "/api/auth/connector/login", "{}");
        context.Request.Headers.Origin = "https://evil.example";
        var called = false;

        await new UserSessionMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task BearerTogetherWithSessionCookie_IsRefused()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var token = (await fixture.ConnectOwnerAsync()).Response!.AccessToken;
        var context = fixture.NewHttpContext("GET", "/api/companies", bearer: token);
        context.Request.Headers.Cookie = "accounts_session=anything";
        var called = false;

        await new UserSessionMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("curl/8.0")]
    [InlineData("mcp-connector/latest")]
    public async Task MissingOrMalformedClientHeader_IsRefused(string? client)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var token = (await fixture.ConnectOwnerAsync()).Response!.AccessToken;
        var context = fixture.NewHttpContext("GET", "/api/companies", bearer: token);
        if (client is null) context.Request.Headers.Remove("X-FilingBridge-Client");
        else context.Request.Headers["X-FilingBridge-Client"] = client;
        var called = false;

        await new UserSessionMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task ValidBearer_SetsIdentityAndPosture_WithoutIssuingCookies()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var token = (await fixture.ConnectOwnerAsync("Write")).Response!.AccessToken;
        var context = fixture.NewHttpContext("GET", "/api/companies", bearer: token);
        HttpContext? seen = null;

        await new UserSessionMiddleware(inner => { seen = inner; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.NotNull(seen);
        Assert.Equal(fixture.Owner.Id, AuthContext.GetUser(context)!.UserId);
        Assert.Equal("Write", ConnectorSessionContext.Get(context)!.AccessLevel);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task InvalidBearer_Is401()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var context = fixture.NewHttpContext("GET", "/api/companies", bearer: "fbc1.not.valid");
        var called = false;

        await new UserSessionMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Contains(ConnectorErrorCodes.SessionInvalid, await ConnectorTestFixture.ReadResponseAsync(context));
    }

    [Theory]
    [InlineData("Read", "POST", "/api/companies/1/officers", 403, ConnectorErrorCodes.SessionReadOnly)]
    [InlineData("Read", "GET", "/api/companies/1/officers", 200, null)]
    [InlineData("Read", "POST", "/api/auth/connector/logout", 200, null)]
    [InlineData("Write", "POST", "/api/companies/1/officers", 200, null)]
    [InlineData("Write", "DELETE", "/api/companies/1/officers/3", 403, ConnectorErrorCodes.SessionLevelTooLow)]
    [InlineData("Write", "GET", "/api/admin/users", 403, ConnectorErrorCodes.SessionLevelTooLow)]
    [InlineData("Write", "POST", "/api/companies", 403, ConnectorErrorCodes.SessionLevelTooLow)]
    [InlineData("Read", "GET", "/api/system/production-readiness", 403, ConnectorErrorCodes.SessionLevelTooLow)]
    [InlineData("Admin", "DELETE", "/api/companies/1/officers/3", 200, null)]
    [InlineData("Admin", "POST", "/api/auth/reauthenticate", 403, ConnectorErrorCodes.NotPermitted)]
    [InlineData("Admin", "POST", "/api/auth/password", 403, ConnectorErrorCodes.NotPermitted)]
    [InlineData("Admin", "POST", "/api/auth/mfa/recovery-codes", 403, ConnectorErrorCodes.NotPermitted)]
    public async Task AccessLevel_IsEnforced(string level, string method, string path, int expectedStatus, string? expectedCode)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var (context, called) = await RunPostureAsync(fixture, level, "Full", method, path);

        Assert.Equal(expectedStatus == 200, called());
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        if (expectedCode is not null)
            Assert.Contains(expectedCode, await ConnectorTestFixture.ReadResponseAsync(context));
    }

    [Theory]
    [InlineData("GET", "/api/admin/users", null, true)]
    [InlineData("POST", "/api/privacy/subjects/4/access-export", "{}", true)]
    [InlineData("GET", "/api/companies/1/periods/2/documents/accounts-package", null, true)]
    [InlineData("GET", "/api/companies/1/periods/2/revenue/ixbrl", null, true)]
    [InlineData("POST", "/api/companies/1/officers", "{\"name\":\"A Director\",\"role\":\"Director\",\"address\":\"1 Home Road\"}", true)]
    [InlineData("POST", "/api/companies/1/officers", "{\"name\":\"A Director\",\"role\":\"Director\",\"address\":null}", false)]
    [InlineData("POST", "/api/companies/onboard", "{\"officers\":[{\"name\":\"A\",\"address\":\"1 Home Road\"}]}", true)]
    [InlineData("PUT", "/api/companies/1/periods/2/director-loans/9", "{\"counterpartyName\":\"Someone\"}", true)]
    [InlineData("GET", "/api/companies/1/periods/2/statements/balance-sheet", null, false)]
    public async Task WithheldScope_RefusesPersonalRoutesAndFields(string method, string path, string? body, bool refused)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var (context, called) = await RunPostureAsync(fixture, "Admin", "Withheld", method, path, body);

        Assert.Equal(!refused, called());
        if (refused)
        {
            Assert.Equal(403, context.Response.StatusCode);
            Assert.Contains(ConnectorErrorCodes.DataScopeWithheld, await ConnectorTestFixture.ReadResponseAsync(context));
        }

        // The full scope reaches the same route.
        var (_, fullCalled) = await RunPostureAsync(fixture, "Admin", "Full", method, path, body);
        Assert.True(fullCalled());
    }

    [Fact]
    public async Task WriteBudget_RefusesTheWriteAfterTheLimitWithRetryAfter()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync(config => config.WriteBudgetPerMinute = 5);
        var login = (await fixture.ConnectOwnerAsync("Write")).Response!;
        var authenticated = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var budget = fixture.Services.GetRequiredService<ConnectorWriteBudget>();
        HttpContext? last = null;
        var calls = 0;
        for (var index = 0; index < 6; index++)
        {
            last = fixture.NewHttpContext("POST", "/api/companies/1/periods/2/debtors", "{}");
            last.Items[AuthContext.ItemKey] = authenticated.User;
            last.Items[ConnectorSessionContext.ItemKey] = authenticated.Session;
            await new ConnectorPostureMiddleware(_ => { calls++; return Task.CompletedTask; })
                .InvokeAsync(last, budget, fixture.ScopeFactory, fixture.Clock);
        }

        Assert.Equal(5, calls);
        Assert.Equal(429, last!.Response.StatusCode);
        Assert.True(int.Parse(last.Response.Headers.RetryAfter.ToString()) >= 1);
    }

    [Fact]
    public async Task Activity_IsRecordedForWritesAndRefusals_WithReason()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var (context, _) = await RunPostureAsync(fixture, "Read", "Withheld", "POST", "/api/companies/1/officers", "{}",
            configure: request => request.Headers["X-FilingBridge-Reason"] = "Adding the new director\u0007 from the minutes");

        var row = await fixture.Db.ConnectorActivityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(403, row.StatusCode);
        Assert.Equal("POST", row.Method);
        Assert.Equal(1, row.TargetCompanyId);
        Assert.Equal("Adding the new director from the minutes", row.Reason);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task CookieSessions_AreUntouchedByPosture()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var context = fixture.NewHttpContext("DELETE", "/api/companies/1/officers/3");
        context.Items[AuthContext.ItemKey] = await fixture.Auth.GetPrincipalAsync(fixture.Owner.Id);
        var called = false;

        await new ConnectorPostureMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(context, fixture.Services.GetRequiredService<ConnectorWriteBudget>(), fixture.ScopeFactory, fixture.Clock);

        Assert.True(called);
        Assert.Empty(await fixture.Db.ConnectorActivityEvents.ToListAsync());
    }

    [Fact]
    public async Task Csrf_IsSkippedOnlyForConnectorSessions()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var owner = await fixture.Auth.GetPrincipalAsync(fixture.Owner.Id) with { CsrfToken = "expected-token" };
        var options = Options.Create(new AuthSessionConfig());

        var browser = fixture.NewHttpContext("POST", "/api/companies/1/officers", "{}");
        browser.Items[AuthContext.ItemKey] = owner;
        var browserCalled = false;
        await new CsrfProtectionMiddleware(_ => { browserCalled = true; return Task.CompletedTask; }).InvokeAsync(browser, options);

        var connector = fixture.NewHttpContext("POST", "/api/companies/1/officers", "{}");
        connector.Items[AuthContext.ItemKey] = owner;
        connector.Items[ConnectorSessionContext.ItemKey] = new ConnectorSessionContext(Guid.NewGuid(), Guid.NewGuid(), "Write", "Withheld", "0.1.0");
        var connectorCalled = false;
        await new CsrfProtectionMiddleware(_ => { connectorCalled = true; return Task.CompletedTask; }).InvokeAsync(connector, options);

        Assert.False(browserCalled);
        Assert.Equal(403, browser.Response.StatusCode);
        Assert.True(connectorCalled);
    }

    private static async Task<(HttpContext Context, Func<bool> Called)> RunPostureAsync(
        ConnectorTestFixture fixture,
        string level,
        string scope,
        string method,
        string path,
        string? body = null,
        Action<HttpRequest>? configure = null)
    {
        var owner = await fixture.Auth.GetPrincipalAsync(fixture.Owner.Id) ?? throw new InvalidOperationException();
        var context = fixture.NewHttpContext(method, path, body);
        configure?.Invoke(context.Request);
        context.Items[AuthContext.ItemKey] = owner;
        context.Items[ConnectorSessionContext.ItemKey] = new ConnectorSessionContext(Guid.NewGuid(), Guid.NewGuid(), level, scope, "0.1.0");
        var called = false;
        await new ConnectorPostureMiddleware(inner =>
            {
                called = true;
                inner.Response.StatusCode = 200;
                return Task.CompletedTask;
            })
            .InvokeAsync(context, fixture.Services.GetRequiredService<ConnectorWriteBudget>(), fixture.ScopeFactory, fixture.Clock);
        return (context, () => called);
    }
}
