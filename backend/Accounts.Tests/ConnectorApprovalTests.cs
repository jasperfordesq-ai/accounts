using System.Text;
using System.Text.Json;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class ConnectorApprovalTests
{
    [Fact]
    public void Digest_IgnoresKeyOrderWhitespaceAndQuery_ButNotValuesOrArrayOrder()
    {
        var family = Guid.NewGuid();
        string Digest(string method, string path, string body) =>
            ConnectorActionDigest.Compute(family, method, path, Encoding.UTF8.GetBytes(body));

        var baseline = Digest("POST", "/api/companies/1/periods/2/status", "{\"a\":1,\"b\":[1,2]}");
        Assert.Equal(baseline, Digest("post", "/API/companies/1/periods/2/status/", "{ \"b\": [1, 2], \"a\": 1 }"));
        Assert.NotEqual(baseline, Digest("POST", "/api/companies/1/periods/2/status", "{\"a\":2,\"b\":[1,2]}"));
        Assert.NotEqual(baseline, Digest("POST", "/api/companies/1/periods/2/status", "{\"a\":1,\"b\":[2,1]}"));
        Assert.NotEqual(baseline, Digest("PUT", "/api/companies/1/periods/2/status", "{\"a\":1,\"b\":[1,2]}"));
        Assert.NotEqual(baseline, ConnectorActionDigest.Compute(Guid.NewGuid(), "POST", "/api/companies/1/periods/2/status", Encoding.UTF8.GetBytes("{\"a\":1,\"b\":[1,2]}")));
        Assert.Equal(Digest("DELETE", "/api/companies/1/officers/3", ""), Digest("DELETE", "/api/companies/1/officers/3", "{}"));
    }

    [Fact]
    public void EveryHumanConfirmationRoute_IsAdminLevel()
    {
        var samples = new (string Method, string Path)[]
        {
            ("DELETE", "/api/companies/1/officers/3"),
            ("PUT", "/api/companies/1/periods/2/status"),
            ("POST", "/api/companies/1/periods/2/adjustments/5/approve"),
            ("POST", "/api/companies/1/periods/2/mark-filed"),
            ("POST", "/api/privacy/retention/run"),
            ("POST", "/api/admin/users/4/offboard"),
            ("POST", "/api/companies/1/recover"),
            ("POST", "/api/companies/1/categories/chart-adoption"),
            ("POST", "/api/companies/1/periods/2/external-filing-handoff/cro/snapshots"),
            ("POST", "/api/operations/deadline-reminders/run")
        };
        foreach (var (method, path) in samples)
        {
            Assert.True(PrivilegedRouteCatalog.RequiresHumanConfirmation(path, method), path);
            Assert.True(PrivilegedRouteCatalog.IsAdminLevel(path, method), path);
            Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(path, method), path);
        }
        Assert.False(PrivilegedRouteCatalog.RequiresHumanConfirmation("/api/companies/1/officers", "POST"));
        // Regression: the grant route ends in "/approve" but must never need an approval itself.
        Assert.False(PrivilegedRouteCatalog.RequiresHumanConfirmation("/api/auth/connector/approve", "POST"));
        Assert.False(PrivilegedRouteCatalog.RequiresHumanConfirmation("/api/auth/connector/sessions/5e1d/revoke", "POST"));
        Assert.False(PrivilegedRouteCatalog.IsAdminLevel("/api/auth/connector/approve", "POST"));
        Assert.False(PrivilegedRouteCatalog.IsAdminLevel("/api/companies/1/periods/2/debtors", "POST"));
    }

    [Fact]
    public async Task ConnectorPrivilegedWrite_Gets428_AndNextIsNotInvoked()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var session = await ConnectAsync(fixture);
        var context = Request(fixture, session, "DELETE", $"/api/companies/{fixture.Company.Id}/officers/3");
        var called = false;

        await Middleware(() => called = true).InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));

        Assert.False(called);
        Assert.Equal(428, context.Response.StatusCode);
        using var body = JsonDocument.Parse(await ConnectorTestFixture.ReadResponseAsync(context));
        Assert.Equal(ConnectorErrorCodes.ApprovalRequired, body.RootElement.GetProperty("code").GetString());
        var approvalId = body.RootElement.GetProperty("approvalId").GetGuid();
        Assert.Equal($"filingbridge-mcp approve {approvalId}", body.RootElement.GetProperty("command").GetString());
        Assert.Equal($"https://filingbridge.example/settings/approvals?id={approvalId}", body.RootElement.GetProperty("approvalsUrl").GetString());
        Assert.Contains("Example Limited", body.RootElement.GetProperty("summary").GetString());
        var stored = await fixture.Db.ConnectorActionApprovals.AsNoTracking().SingleAsync();
        Assert.Equal(approvalId, stored.Id);
        Assert.Null(stored.ApprovedAtUtc);
    }

    [Fact]
    public async Task AskingTwice_ReturnsTheSameApproval()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var session = await ConnectAsync(fixture);

        var first = await AskAsync(fixture, session, "DELETE", $"/api/companies/{fixture.Company.Id}/officers/3");
        var second = await AskAsync(fixture, session, "DELETE", $"/api/companies/{fixture.Company.Id}/officers/3");

        Assert.Equal(first, second);
        Assert.Single(await fixture.Db.ConnectorActionApprovals.ToListAsync());
    }

    [Fact]
    public async Task GrantedApproval_IsSpentExactlyOnce_ForTheIdenticalRequestOnly()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var session = await ConnectAsync(fixture);
        var path = $"/api/companies/{fixture.Company.Id}/periods/7/status";
        const string body = "{\"status\":\"Locked\"}";
        var approvalId = await AskAsync(fixture, session, "PUT", path, body);

        // Not yet granted.
        Assert.False(await RetryAsync(fixture, session, approvalId, "PUT", path, body));

        var grant = await fixture.Connectors.GrantApprovalAsync(session.User, approvalId, ConnectorTestFixture.OwnerPassword, fixture.NextTotp());
        Assert.NotNull(grant);

        // A different body is a different action.
        Assert.False(await RetryAsync(fixture, session, approvalId, "PUT", path, "{\"status\":\"Draft\"}"));
        // Another session family cannot spend it.
        var otherSession = await ConnectAsync(fixture);
        Assert.False(await RetryAsync(fixture, otherSession, approvalId, "PUT", path, body));

        var context = Request(fixture, session, "PUT", path, body);
        context.Request.Headers["X-FilingBridge-Approval"] = approvalId.ToString();
        var called = false;
        await Middleware(() => called = true).InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));
        Assert.True(called);
        Assert.Equal(approvalId, context.Items[ConnectorActionApprovalMiddleware.SpentItemKey]);

        // The recent-MFA gate accepts the spent approval and nothing else.
        var gateCalled = false;
        await new RecentAuthenticationMiddleware(_ => { gateCalled = true; return Task.CompletedTask; })
            .InvokeAsync(context, Options.Create(new AuthSessionConfig { RequirePrivilegedMfa = true }), fixture.Clock);
        Assert.True(gateCalled);

        Assert.False(await RetryAsync(fixture, session, approvalId, "PUT", path, body));
    }

    [Fact]
    public async Task RecentMfaGate_RefusesConnectorWithoutSpentApproval_EvenWithFreshTotp()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var session = await ConnectAsync(fixture);
        var context = Request(fixture, session, "DELETE", $"/api/companies/{fixture.Company.Id}/officers/3");
        Assert.NotNull(session.User.MfaVerifiedAtUtc);
        var called = false;

        await new RecentAuthenticationMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(context, Options.Create(new AuthSessionConfig { RequirePrivilegedMfa = true, RecentMfaMinutes = 10 }), fixture.Clock);

        Assert.False(called);
        Assert.Equal(428, context.Response.StatusCode);
    }

    [Fact]
    public async Task Grant_RefusesWrongPasswordWrongTotpForeignOrExpiredApprovals_Identically()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var session = await ConnectAsync(fixture);
        var approvalId = await AskAsync(fixture, session, "DELETE", $"/api/companies/{fixture.Company.Id}/officers/3");

        Assert.Null(await fixture.Connectors.GrantApprovalAsync(session.User, approvalId, "Wrong password value 1!", fixture.NextTotp()));
        Assert.Null(await fixture.Connectors.GrantApprovalAsync(session.User, approvalId, ConnectorTestFixture.OwnerPassword, "000000"));
        Assert.Null(await fixture.Connectors.GrantApprovalAsync(session.User, Guid.NewGuid(), ConnectorTestFixture.OwnerPassword, fixture.NextTotp()));
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await fixture.Connectors.GrantApprovalAsync(session.User, approvalId, ConnectorTestFixture.OwnerPassword, fixture.NextTotp()));
        Assert.Null((await fixture.Db.ConnectorActionApprovals.AsNoTracking().SingleAsync()).ApprovedAtUtc);
    }

    [Fact]
    public async Task RoleForbiddenAction_MintsNoApproval()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var reviewer = await fixture.AddUserAsync("reviewer@example.ie", "Reviewer");
        var secret = await fixture.EnrolAsync(reviewer);
        var login = (await fixture.Connectors.LoginAsync(
            new Accounts.Api.Services.ConnectorLoginInput(fixture.Tenant.Slug, reviewer.Email, ConnectorTestFixture.OwnerPassword, "Admin", "Withheld", fixture.NextTotp(secret), null, null),
            "0.1.0")).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = Request(fixture, session, "POST", "/api/admin/users/9/offboard", "{}");
        var called = false;

        await Middleware(() => called = true).InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));

        Assert.True(called); // passed on so the role gate answers 403
        Assert.Empty(await fixture.Db.ConnectorActionApprovals.ToListAsync());
    }

    [Fact]
    public async Task Summary_NeverIncludesAddressesOrEmails()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        fixture.Db.CompanyOfficers.Add(new Accounts.Api.Entities.CompanyOfficer
        {
            CompanyId = fixture.Company.Id,
            Name = "Aoife Director",
            Role = Accounts.Api.Entities.OfficerRole.Director,
            Address = "12 Private Lane, Galway"
        });
        await fixture.Db.SaveChangesAsync();
        var officerId = (await fixture.Db.CompanyOfficers.SingleAsync()).Id;
        var session = await ConnectAsync(fixture);

        var approvalId = await AskAsync(fixture, session, "DELETE", $"/api/companies/{fixture.Company.Id}/officers/{officerId}");

        var summary = (await fixture.Db.ConnectorActionApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId)).Summary;
        Assert.Contains("Aoife Director", summary);
        Assert.DoesNotContain("Private Lane", summary);
        Assert.DoesNotContain("1 Main Street", summary);
        Assert.DoesNotContain("@", summary);
    }

    private static ConnectorActionApprovalMiddleware Middleware(Action onNext) =>
        new(_ => { onNext(); return Task.CompletedTask; });

    private static async Task<(AuthenticatedUser User, ConnectorSessionContext Session)> ConnectAsync(ConnectorTestFixture fixture)
    {
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        return (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
    }

    private static DefaultHttpContext Request(
        ConnectorTestFixture fixture,
        (AuthenticatedUser User, ConnectorSessionContext Session) session,
        string method,
        string path,
        string? body = null)
    {
        var context = fixture.NewHttpContext(method, path, body);
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;
        return context;
    }

    private static async Task<Guid> AskAsync(
        ConnectorTestFixture fixture,
        (AuthenticatedUser User, ConnectorSessionContext Session) session,
        string method,
        string path,
        string? body = null)
    {
        var context = Request(fixture, session, method, path, body);
        await Middleware(() => throw new InvalidOperationException("next must not run"))
            .InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));
        Assert.Equal(428, context.Response.StatusCode);
        using var document = JsonDocument.Parse(await ConnectorTestFixture.ReadResponseAsync(context));
        return document.RootElement.GetProperty("approvalId").GetGuid();
    }

    private static async Task<bool> RetryAsync(
        ConnectorTestFixture fixture,
        (AuthenticatedUser User, ConnectorSessionContext Session) session,
        Guid approvalId,
        string method,
        string path,
        string body)
    {
        var context = Request(fixture, session, method, path, body);
        context.Request.Headers["X-FilingBridge-Approval"] = approvalId.ToString();
        var called = false;
        await Middleware(() => called = true).InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));
        return called;
    }
}
