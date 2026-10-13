using System.Security.Cryptography;
using System.Text.Json;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

/// <summary>Regression tests for the review findings on the connector (PR #87).</summary>
public sealed class ConnectorReviewFindingTests
{
    [Fact]
    public async Task RefreshRacingADisconnect_DoesNotLeaveTheSuccessorActive()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var current = await fixture.Db.ConnectorSessions.AsNoTracking().SingleAsync();
        // A concurrent disconnect revoked the family while this refresh was between its writes:
        // model that as another family row already revoked for a reason other than rotation.
        fixture.Db.ConnectorSessions.Add(new ConnectorSession
        {
            Id = Guid.NewGuid(),
            TenantId = current.TenantId,
            UserId = current.UserId,
            FamilyId = current.FamilyId,
            FamilyCreatedAtUtc = current.FamilyCreatedAtUtc,
            AccessLevel = current.AccessLevel,
            DataScope = current.DataScope,
            RefreshTokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            PasswordLastChangedAtTicks = current.PasswordLastChangedAtTicks,
            CreatedAtUtc = current.CreatedAtUtc,
            ExpiresAtUtc = current.ExpiresAtUtc,
            RevokedAtUtc = current.CreatedAtUtc,
            RevocationReason = ConnectorRevocationReasons.Logout
        });
        await fixture.Db.SaveChangesAsync();

        var refreshed = await fixture.Connectors.RefreshAsync(login.RefreshToken);

        Assert.Null(refreshed);
        Assert.All(await fixture.Db.ConnectorSessions.AsNoTracking().Where(row => row.FamilyId == current.FamilyId).ToListAsync(),
            row => Assert.NotNull(row.RevokedAtUtc));
    }

    [Fact]
    public async Task RotatingTheConnectorKey_InvalidatesRefreshTokens_EvenWithTheSameIdentityKeys()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        using var scope = fixture.ScopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        // Same database and identity/session keys; only the connector key changed (as on restore).
        var rotated = new ConnectorSessionService(
            provider.GetRequiredService<Accounts.Api.Data.AccountsDbContext>(),
            provider.GetRequiredService<AuthService>(),
            provider.GetRequiredService<IdentityAccessService>(),
            provider.GetRequiredService<MfaSecurityService>(),
            provider.GetRequiredService<AuditService>(),
            Options.Create(new ConnectorConfig { Enabled = true, SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }),
            fixture.Clock);

        Assert.Null(await rotated.RefreshAsync(login.RefreshToken));
        Assert.Null(await rotated.AuthenticateAsync(login.AccessToken));
        Assert.NotNull(await fixture.Connectors.RefreshAsync(login.RefreshToken));
    }

    [Theory]
    [InlineData("POST", "/api/companies/1/periods/2/documents/cro-filing-pack")]
    [InlineData("POST", "/api/companies/1/periods/2/documents/signature-page")]
    [InlineData("GET", "/api/companies/1/periods/2/documents/signature-page/final")]
    public void DocumentGeneration_IsWithheldWhateverTheMethod(string method, string path)
    {
        Assert.True(PersonalDataRouteCatalog.IsWithheldRoute(path, method));
    }

    [Fact]
    public async Task ApprovalSummary_ShowsTheRequestedChange()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = (await fixture.Connectors.AuthenticateAsync(login.AccessToken))!.Value;
        var context = fixture.NewHttpContext("PUT", "/api/admin/users/7/role", "{\"role\":\"Reviewer\",\"note\":\"Private free text\"}");
        context.Items[AuthContext.ItemKey] = session.User;
        context.Items[ConnectorSessionContext.ItemKey] = session.Session;

        await new ConnectorActionApprovalMiddleware(_ => throw new InvalidOperationException("next must not run"))
            .InvokeAsync(context, fixture.Connectors, fixture.ScopeFactory, Options.Create(fixture.ConnectorConfig));

        using var body = JsonDocument.Parse(await ConnectorTestFixture.ReadResponseAsync(context));
        var summary = body.RootElement.GetProperty("summary").GetString()!;
        Assert.Contains("requested: role=Reviewer", summary);
        Assert.DoesNotContain("Private free text", summary);
    }

    [Fact]
    public async Task Retention_RemovesOldAgentActivityAndEndedSessions()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync(config => config.ActivityRetentionDays = 30);
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var session = await fixture.Db.ConnectorSessions.AsNoTracking().SingleAsync();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        foreach (var age in new[] { 400, 5 })
        {
            fixture.Db.ConnectorActivityEvents.Add(new ConnectorActivityEvent
            {
                TenantId = session.TenantId,
                UserId = session.UserId,
                SessionId = session.Id,
                SessionFamilyId = session.FamilyId,
                AccessLevel = session.AccessLevel,
                DataScope = session.DataScope,
                Method = "POST",
                RoutePattern = "/api/companies/{companyId}/officers",
                StatusCode = 201,
                OccurredAtUtc = now.AddDays(-age)
            });
        }
        await fixture.Db.SaveChangesAsync();
        var privacy = new PrivacyGovernanceService(
            fixture.Db,
            Options.Create(new PrivacyGovernanceConfig()),
            fixture.Services.GetRequiredService<IOptions<AuthSessionConfig>>(),
            fixture.Clock,
            Options.Create(fixture.ConnectorConfig));

        var result = await privacy.RunRetentionForTenantAsync(fixture.Tenant.Id);

        Assert.Equal(1, result.DeletedConnectorRecords);
        var remaining = await fixture.Db.ConnectorActivityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(now.AddDays(-5), remaining.OccurredAtUtc);
        Assert.NotNull(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
    }
}
