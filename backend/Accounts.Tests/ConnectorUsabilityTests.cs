using System.Text.Json;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// Findings from the first Private Server acceptance run of the connector (2026-10-13): the
/// approval summary must name the record, approvals must last long enough for a person to act,
/// and agent writes outside company routes must be attributed in the audit log.
/// </summary>
public sealed class ConnectorUsabilityTests
{
    [Theory]
    [InlineData("DELETE", "/api/companies/{companyId:int}/periods/{periodId:int}/debtors/{id:int}", "Delete debtor")]
    [InlineData("DELETE", "/api/companies/{companyId:int}/periods/{periodId:int}/inventories/{id:int}", "Delete inventory")]
    [InlineData("DELETE", "/api/companies/{companyId:int}/periods/{periodId:int}/fixed-assets/{id:int}", "Delete fixed asset")]
    [InlineData("DELETE", "/api/companies/{companyId:int}/officers/{officerId:int}", "Delete officer")]
    [InlineData("POST", "/api/companies/{companyId:int}/periods/{periodId:int}/adjustments/generate", "Run adjustments generate")]
    [InlineData("PUT", "/api/companies/{companyId:int}/periods/{periodId:int}/status", "Change status")]
    [InlineData("POST", "/api/admin/users/{id:int}/offboard", "Run admin users offboard")]
    public void DescribeAction_NamesOneRecordInTheSingular(string method, string pattern, string expected)
    {
        Assert.Equal(expected, ConnectorActionSummaryService.DescribeAction(method, pattern));
    }

    [Fact]
    public async Task ApprovalSummary_NamesTheDebtorItsTypeAndAmount_ButNeverItsName()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var period = new AccountingPeriod { CompanyId = fixture.Company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31) };
        fixture.Db.AccountingPeriods.Add(period);
        await fixture.Db.SaveChangesAsync();
        var debtor = new Debtor { PeriodId = period.Id, Name = "Mary Private Individual", Amount = 1234.5m, Type = DebtorType.Trade };
        fixture.Db.Debtors.Add(debtor);
        await fixture.Db.SaveChangesAsync();

        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = fixture.NewHttpContext("DELETE", $"/api/companies/{fixture.Company.Id}/periods/{period.Id}/debtors/{debtor.Id}");
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/companies/{companyId:int}/periods/{periodId:int}/debtors/{id:int}"),
            0,
            EndpointMetadataCollection.Empty,
            "debtor delete"));
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;

        await new ConnectorActionApprovalMiddleware(_ => throw new InvalidOperationException("next must not run"))
            .InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));

        using var body = JsonDocument.Parse(await ConnectorTestFixture.ReadResponseAsync(context));
        var summary = body.RootElement.GetProperty("summary").GetString()!;
        Assert.StartsWith($"Delete debtor #{debtor.Id}, Trade, EUR 1,234.50, company Example Limited (CRO 123456)", summary);
        Assert.Contains("period ending 2025-12-31", summary);
        Assert.DoesNotContain("Mary", summary);
    }

    [Theory]
    [InlineData("/api/companies/4/periods/9/year-end-reviews/directors-report", "'directors-report'")]
    [InlineData("/api/companies/4/periods/9/debtors/12", "#12")]
    [InlineData("/api/auth/connector/sessions/9b2d6a3e-1f4c-4c5e-8d7a-2b3c4d5e6f70", "#9b2d6a3e-1f4c-4c5e-8d7a-2b3c4d5e6f70")]
    [InlineData("/api/companies/4/periods/9/year-end-reviews/%3Cscript%3Ebad", "'scriptbad'")]
    [InlineData("/api/companies/4/periods/9/year-end-reviews/%20%2F", null)]
    public void RecordKey_IsTheFinalRouteValue_NotAnEarlierIdentifier(string path, string? expected)
    {
        Assert.Equal(expected, ConnectorActionSummaryService.RecordKeyOf(path));
    }

    [Fact]
    public async Task ApprovalSummary_ForAReviewSection_NamesTheSection_NotThePeriod()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = fixture.NewHttpContext("PUT", $"/api/companies/{fixture.Company.Id}/periods/987/year-end-reviews/directors-report", "{}");
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/companies/{companyId:int}/periods/{periodId:int}/year-end-reviews/{sectionKey}"),
            0,
            EndpointMetadataCollection.Empty,
            "review update"));
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;

        var summary = await new ConnectorActionSummaryService(fixture.Db).DescribeAsync(context, ConnectorPostureMiddleware.RoutePatternOf(context));

        Assert.StartsWith("Change year end review 'directors-report'", summary);
        Assert.DoesNotContain("#987", summary);
    }

    [Fact]
    public async Task AWorkspaceAuditFailure_DoesNotReplaceTheCompletedAnswer()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = fixture.NewHttpContext("POST", "/api/companies", "{\"legalName\":\"New Co\"}");
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;

        await new AuditTrailMiddleware(async inner =>
        {
            inner.Response.StatusCode = StatusCodes.Status201Created;
            await inner.Response.WriteAsync("{\"id\":99}");
        }).InvokeAsync(context, fixture.Db, null, new FailingScopeFactory());

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("{\"id\":99}", await ConnectorTestFixture.ReadResponseAsync(context));
    }

    [Fact]
    public async Task AWorkspaceAuditFailure_IsReportedThroughTheSanitisingReporter_WithoutThePath()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var reporter = new RecordingErrorReporter();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<IErrorReporter>(reporter)
            .BuildServiceProvider();
        var context = fixture.NewHttpContext("POST", "/api/companies/private-client-name", "{}");
        context.RequestServices = services;
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;

        await new AuditTrailMiddleware(inner =>
        {
            inner.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        }).InvokeAsync(context, fixture.Db, null, new FailingScopeFactory());

        var report = Assert.Single(reporter.Reports);
        Assert.Equal("POST", report.Method);
        Assert.DoesNotContain("private-client-name", report.Path);
    }

    private sealed class RecordingErrorReporter : IErrorReporter
    {
        public List<ErrorReportContext> Reports { get; } = [];

        public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
        {
            Reports.Add(context);
            return "test";
        }
    }

    private sealed class FailingScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() =>
            throw new InvalidOperationException("The audit database is unavailable.");
    }

    [Fact]
    public void Approvals_LastFifteenMinutesByDefault_AndAtMostThirty()
    {
        Assert.Equal(15, new ConnectorConfig().ApprovalMinutes);
        Assert.True(ConnectorConfig.IsValid(new ConnectorConfig { ApprovalMinutes = 30 }));
        Assert.False(ConnectorConfig.IsValid(new ConnectorConfig { ApprovalMinutes = 31 }));
        Assert.False(ConnectorConfig.IsValid(new ConnectorConfig { ApprovalMinutes = 1 }));
    }

    [Theory]
    [InlineData(StatusCodes.Status201Created, "ApiWriteSucceeded")]
    [InlineData(StatusCodes.Status400BadRequest, "ApiWriteRejected")]
    public async Task AgentWritesOutsideCompanyRoutes_AreAttributedAtWorkspaceLevel(int status, string expectedAction)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = fixture.NewHttpContext("POST", "/api/companies", "{\"legalName\":\"New Co\"}");
        context.Request.Headers[ConnectorAudit.ReasonHeader] = "Set up the new client";
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;
        var before = await fixture.Db.AuditLogs.AsNoTracking().CountAsync(entry => entry.EntityType == "ApiRequest");

        var ranNext = false;
        await new AuditTrailMiddleware(inner =>
        {
            ranNext = true;
            inner.Response.StatusCode = status;
            return Task.CompletedTask;
        }).InvokeAsync(context, fixture.Db, fixture.Services.GetService(typeof(AuditService)) as AuditService, fixture.ScopeFactory);

        Assert.True(ranNext);
        var entries = await fixture.Db.AuditLogs.AsNoTracking().Where(entry => entry.EntityType == "ApiRequest").ToListAsync();
        Assert.Equal(before + 1, entries.Count);
        var entry = entries.Single(row => row.Action == expectedAction);
        Assert.Null(entry.CompanyId);
        Assert.Equal(fixture.Tenant.Id, entry.TenantId);
        using var payload = JsonDocument.Parse(entry.NewValueJson!);
        Assert.Equal("/api/companies", payload.RootElement.GetProperty("path").GetString());
        var client = payload.RootElement.GetProperty("client");
        Assert.Equal(ConnectorClientKinds.McpConnector, client.GetProperty("kind").GetString());
        Assert.Equal(session.Session.FamilyId, client.GetProperty("sessionFamilyId").GetGuid());
        Assert.Equal("Set up the new client", client.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task BrowserWritesOutsideCompanyRoutes_AreUnchanged()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var context = fixture.NewHttpContext("POST", "/api/companies", "{\"legalName\":\"New Co\"}");
        var before = await fixture.Db.AuditLogs.AsNoTracking().CountAsync();

        await new AuditTrailMiddleware(inner =>
        {
            inner.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        }).InvokeAsync(context, fixture.Db, null, fixture.ScopeFactory);

        Assert.Equal(before, await fixture.Db.AuditLogs.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task AgentWritesToTheirOwnConnectorRoutes_AreNotDoubleAudited()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = fixture.NewHttpContext("POST", $"/api/auth/connector/sessions/{session.Session.FamilyId}/revoke");
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;
        var before = await fixture.Db.AuditLogs.AsNoTracking().CountAsync(entry => entry.EntityType == "ApiRequest");

        await new AuditTrailMiddleware(inner =>
        {
            inner.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }).InvokeAsync(context, fixture.Db, null, fixture.ScopeFactory);

        Assert.Equal(before, await fixture.Db.AuditLogs.AsNoTracking().CountAsync(entry => entry.EntityType == "ApiRequest"));
    }
}
