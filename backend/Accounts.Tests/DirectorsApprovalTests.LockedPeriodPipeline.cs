using System.Net;
using System.Net.Http.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// Withdrawing the directors' approval and treating a CRO filing as not delivered are acts on a
/// finalised, locked period. Through the real middleware order (recent MFA, role routing, period
/// ownership, concurrency and the period lock) they reach their handlers, while the role and
/// recent-authentication checks still refuse everyone else.
/// </summary>
public sealed partial class DirectorsApprovalTests
{
    // Verified now, so the recent-MFA gate passes however long the test run has taken.
    private static AuthenticatedUser VerifiedOwner() => new(
        UserId: 7, TenantId: 1, TenantName: "Firm", Email: "owner@example.ie",
        DisplayName: "Firm Owner", Role: "Owner")
    {
        MfaMethod = "totp",
        MfaVerifiedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task LockedPeriod_WithdrawalThroughTheMiddleware_ReachesTheHandler()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        await LockAsync(db, p.Id);

        await using var host = await LockedPeriodHost.StartAsync(db, VerifiedOwner() with { AllowedCompanyIds = new HashSet<int> { p.CompanyId } });
        var response = await host.Client.PostAsJsonAsync(FilingPath(p.CompanyId, p.Id, "directors-approval-withdrawal"), Withdrawal());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("Revoked", (await response.Content.ReadFromJsonAsync<DirectorsApprovalHistoryItem>())!.Kind);
        Assert.Single(await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.Kind == DirectorsApprovalEventKind.Revoked).ToListAsync());
    }

    [Fact]
    public async Task LockedPeriod_CroNotDeliveredThroughTheMiddleware_ReachesTheHandler()
    {
        var harness = await ApprovedCroAsync();
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, DateTime.UtcNow.AddDays(-1));
        Assert.NotNull((await harness.Db.AccountingPeriods.AsNoTracking().SingleAsync(x => x.Id == harness.PeriodId)).LockedAt);
        var tenantId = (await harness.Db.Companies.SingleAsync(c => c.Id == harness.CompanyId)).TenantId!.Value;

        await using var host = await LockedPeriodHost.StartAsync(harness.Db,
            VerifiedOwner() with { TenantId = tenantId, AllowedCompanyIds = new HashSet<int> { harness.CompanyId } });
        var response = await host.Client.PostAsJsonAsync(FilingPath(harness.CompanyId, harness.PeriodId, "cro-not-delivered"),
            new CroNotDeliveredInput("Treated as not delivered after the CRO deadline."));

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.PackageGenerated, stored.FilingStatus);
        Assert.Null(stored.CroSubmissionReference);
    }

    // The lock exemption never bypasses the checks before it: an Accountant is refused by role routing
    // and an Owner without a recent authenticator verification by the recent-MFA gate, before any row.
    [Theory]
    [InlineData("directors-approval-withdrawal", "Accountant", HttpStatusCode.Forbidden)]
    [InlineData("directors-approval-withdrawal", "Client", HttpStatusCode.Forbidden)]
    [InlineData("directors-approval-withdrawal", "Owner-without-recent-mfa", (HttpStatusCode)428)]
    [InlineData("cro-not-delivered", "Accountant", HttpStatusCode.Forbidden)]
    [InlineData("cro-not-delivered", "Owner-without-recent-mfa", (HttpStatusCode)428)]
    public async Task LockedPeriod_TheExemptActions_KeepTheirRoleAndRecentMfaChecks(string action, string actor, HttpStatusCode expected)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        await LockAsync(db, p.Id);
        var user = (actor == "Owner-without-recent-mfa"
                ? VerifiedOwner() with { MfaVerifiedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) }
                : VerifiedOwner() with { Role = actor })
            with { AllowedCompanyIds = new HashSet<int> { p.CompanyId } };

        await using var host = await LockedPeriodHost.StartAsync(db, user);
        var response = action == "cro-not-delivered"
            ? await host.Client.PostAsJsonAsync(FilingPath(p.CompanyId, p.Id, action), new CroNotDeliveredInput("Treated as not delivered after the CRO deadline."))
            : await host.Client.PostAsJsonAsync(FilingPath(p.CompanyId, p.Id, action), Withdrawal());

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.Kind == DirectorsApprovalEventKind.Revoked).ToListAsync());
    }

    private static async Task LockAsync(AccountsDbContext db, int periodId)
    {
        var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static FilingWorkflowService WorkflowOn(AccountsDbContext db)
    {
        var statements = new FinancialStatementsService(db);
        return new FilingWorkflowService(
            db,
            statements,
            new IxbrlService(db, statements),
            new AuditService(db),
            releaseGate: new FilingReleaseGate(db, FilingGateFixture.Candidate, new AuditService(db)));
    }

    /// <summary>The production middleware order from Program.cs, after authentication, over the seeded store.</summary>
    private sealed class LockedPeriodHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private LockedPeriodHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<LockedPeriodHost> StartAsync(AccountsDbContext seeded, AuthenticatedUser user)
        {
            var options = (DbContextOptions<AccountsDbContext>)seeded.GetService<IDbContextOptions>();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddScoped(_ => new AccountsDbContext(options));
            builder.Services.Configure<AuthSessionConfig>(_ => { });
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(NoApiKey());
            builder.Services.AddScoped<PeriodConcurrencyTokenService>();
            builder.Services.AddScoped(sp => Service(sp.GetRequiredService<AccountsDbContext>()));
            builder.Services.AddScoped(sp => WorkflowOn(sp.GetRequiredService<AccountsDbContext>()));
            builder.Services.AddScoped(sp => new FilingReleaseGate(sp.GetRequiredService<AccountsDbContext>(), FilingGateFixture.Candidate, new AuditService(sp.GetRequiredService<AccountsDbContext>())));
            builder.Services.AddScoped(sp => new AuditService(sp.GetRequiredService<AccountsDbContext>()));
            builder.Services.AddScoped<FilingReadinessProfileService>();
            builder.Services.ConfigureHttpJsonOptions(json =>
            {
                json.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                json.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

            var app = builder.Build();
            app.UseMiddleware<ExceptionMiddleware>();
            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = user;
                await next();
            });
            app.UseMiddleware<RecentAuthenticationMiddleware>();
            app.UseMiddleware<RoleAuthorizationMiddleware>();
            app.UseMiddleware<PeriodOwnershipMiddleware>();
            app.UseMiddleware<PeriodConcurrencyMiddleware>();
            app.UseMiddleware<PeriodLockMiddleware>();
            // The production route groups, so every filing route resolves to its real handler.
            app.MapDirectorsApprovalEndpoints();
            app.MapFilingWorkflowEndpoints();
            await app.StartAsync();
            var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            return new LockedPeriodHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
