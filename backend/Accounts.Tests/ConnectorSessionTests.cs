using System.Security.Cryptography;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class ConnectorSessionTests
{
    [Fact]
    public async Task OwnerLogin_WithTotp_IssuesTokensThatAuthenticate()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var result = await fixture.ConnectOwnerAsync("Write", "Full");

        Assert.True(result.Succeeded, result.Error);
        var response = result.Response!;
        Assert.StartsWith("fbc1.", response.AccessToken, StringComparison.Ordinal);
        Assert.StartsWith("fbr1.", response.RefreshToken, StringComparison.Ordinal);
        Assert.Equal("Write", response.Session.AccessLevel);
        Assert.Equal("Full", response.Session.DataScope);
        Assert.Equal(fixture.ConnectorConfig.WriteBudgetPerMinute, response.Session.WriteBudgetPerMinute);
        var row = await fixture.Db.ConnectorSessions.SingleAsync();
        Assert.NotEqual(response.RefreshToken, row.RefreshTokenHash);
        Assert.DoesNotContain(response.RefreshToken, row.RefreshTokenHash, StringComparison.Ordinal);

        var authenticated = await fixture.Connectors.AuthenticateAsync(response.AccessToken);

        Assert.NotNull(authenticated);
        Assert.Equal(fixture.Owner.Id, authenticated!.Value.User.UserId);
        Assert.Equal("", authenticated.Value.User.CsrfToken);
        Assert.Equal("totp", authenticated.Value.User.MfaMethod);
        Assert.Equal(row.FamilyId, authenticated.Value.Session.FamilyId);
    }

    [Fact]
    public async Task PrivilegedLogin_WithoutSecondFactor_IsRefusedAndMintsNothing()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, fixture.Owner.Email, ConnectorTestFixture.OwnerPassword, "Read", "Withheld", null, null, null),
            "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(ConnectorErrorCodes.SecondFactorRequired, result.Code);
        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
    }

    [Fact]
    public async Task PrivilegedLogin_WithWrongTotp_IsRefused()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, fixture.Owner.Email, ConnectorTestFixture.OwnerPassword, "Read", "Withheld", "000000", null, null),
            "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(ConnectorErrorCodes.SecondFactorRejected, result.Code);
        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
    }

    [Fact]
    public async Task WrongPassword_IsRefusedWithoutRevealingRoleOrEnrolment()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, fixture.Owner.Email, "Not The Password 123!", "Admin", "Full", fixture.NextTotp(), null, null),
            "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal(ConnectorErrorCodes.InvalidCredentials, result.Code);
    }

    [Fact]
    public async Task NeverEnrolledPrivilegedUser_MustEnrolInTheBrowserFirst()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var accountant = await fixture.AddUserAsync("accountant@example.ie", "Accountant");

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, accountant.Email, ConnectorTestFixture.OwnerPassword, "Write", "Withheld", "123456", null, null),
            "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(ConnectorErrorCodes.MfaEnrolmentRequired, result.Code);
        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
    }

    [Fact]
    public async Task FullDataScope_RequiresOwner()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var accountant = await fixture.AddUserAsync("accountant@example.ie", "Accountant");
        var secret = await fixture.EnrolAsync(accountant);

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, accountant.Email, ConnectorTestFixture.OwnerPassword, "Write", "Full", fixture.NextTotp(secret), null, null),
            "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(ConnectorErrorCodes.DataScopeForbidden, result.Code);
        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
    }

    [Fact]
    public async Task ClientRole_ConnectsWithoutSecondFactor()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var client = await fixture.AddUserAsync("client@example.ie", "Client");

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, client.Email, ConnectorTestFixture.OwnerPassword, "Read", null, null, null, null),
            "0.1.0");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Withheld", result.Response!.Session.DataScope);
        Assert.Equal("Client", result.Response.Session.Role);
    }

    [Theory]
    [InlineData("Owner", "Withheld")]
    [InlineData("read", "Everything")]
    [InlineData(null, "Withheld")]
    public async Task InvalidPosture_IsRejectedBeforeAnyCredentialWork(string? accessLevel, string? dataScope)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var result = await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, fixture.Owner.Email, ConnectorTestFixture.OwnerPassword, accessLevel, dataScope, fixture.NextTotp(), null, null),
            "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(ConnectorErrorCodes.InvalidArguments, result.Code);
    }

    [Fact]
    public async Task BrowserLogout_LeavesConnectorSessionsWorking()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;

        await fixture.Auth.RevokeSessionAsync(fixture.Owner.Id);

        Assert.NotNull(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
    }

    [Fact]
    public async Task PasswordChange_EndsConnectorSessions()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));

        var changed = await fixture.Auth.ChangePasswordAsync(fixture.Owner.Id, ConnectorTestFixture.OwnerPassword, "A Completely New Passphrase 42!");

        Assert.True(changed.Succeeded, changed.FailureReason);
        Assert.Null(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
        Assert.Null(await fixture.Connectors.RefreshAsync(login.RefreshToken));
    }

    [Fact]
    public async Task OwnerRevokingAUsersSessions_EndsTheirConnectorSessions()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var client = await fixture.AddUserAsync("client@example.ie", "Client");
        var login = (await fixture.Connectors.LoginAsync(
            new ConnectorLoginInput(fixture.Tenant.Slug, client.Email, ConnectorTestFixture.OwnerPassword, "Read", "Withheld", null, null, null),
            "0.1.0")).Response!;
        var owner = await fixture.Auth.GetPrincipalAsync(fixture.Owner.Id) ?? throw new InvalidOperationException();

        await fixture.Lifecycle.RevokeSessionsAsync(owner, client.Id);

        Assert.Null(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
        var row = await fixture.Db.ConnectorSessions.AsNoTracking().SingleAsync();
        Assert.Equal(ConnectorRevocationReasons.SecurityStateRevoked, row.RevocationReason);
    }

    [Fact]
    public async Task DeactivatedUser_CannotUseExistingToken()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var user = await fixture.Db.UserAccounts.SingleAsync(candidate => candidate.Id == fixture.Owner.Id);
        user.IsActive = false;
        await fixture.Db.SaveChangesAsync();

        Assert.Null(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
    }

    [Fact]
    public async Task ExpiredTamperedOrForeignKeyTokens_AreRefused()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var parts = login.AccessToken.Split('.');

        var tampered = $"{parts[0]}.{parts[1]}x.{parts[2]}";
        var foreign = new ConnectorTokenCodec(RandomNumberGenerator.GetBytes(64)).CreateAccessToken(
            new ConnectorAccessPayload(Guid.NewGuid(), Guid.NewGuid(), fixture.Owner.Id, fixture.Tenant.Id, 0, long.MaxValue / 2, 1));

        Assert.Null(await fixture.Connectors.AuthenticateAsync(tampered));
        Assert.Null(await fixture.Connectors.AuthenticateAsync(foreign));
        Assert.Null(await fixture.Connectors.AuthenticateAsync(login.RefreshToken));
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
    }

    [Fact]
    public async Task Refresh_RotatesSingleUseTokens_AndReuseRevokesTheFamily()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        fixture.Clock.Advance(TimeSpan.FromMinutes(20));

        var rotated = await fixture.Connectors.RefreshAsync(login.RefreshToken);

        Assert.NotNull(rotated);
        Assert.NotEqual(login.RefreshToken, rotated!.RefreshToken);
        Assert.Equal(login.Session.FamilyId, rotated.Session.FamilyId);
        Assert.Equal("Admin", rotated.Session.AccessLevel);
        Assert.NotNull(await fixture.Connectors.AuthenticateAsync(rotated.AccessToken));

        // Presenting the spent token again is treated as theft: the whole family ends.
        Assert.Null(await fixture.Connectors.RefreshAsync(login.RefreshToken));
        Assert.Null(await fixture.Connectors.RefreshAsync(rotated.RefreshToken));
        Assert.Null(await fixture.Connectors.AuthenticateAsync(rotated.AccessToken));
        Assert.All(await fixture.Db.ConnectorSessions.AsNoTracking().ToListAsync(), row => Assert.NotNull(row.RevokedAtUtc));
    }

    [Fact]
    public async Task Refresh_StopsAtTheFamilyAbsoluteLifetime()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync(config =>
        {
            config.RefreshIdleDays = 30;
            config.FamilyAbsoluteDays = 40;
        });
        var token = (await fixture.ConnectOwnerAsync()).Response!.RefreshToken;
        fixture.Clock.Advance(TimeSpan.FromDays(25));
        token = (await fixture.Connectors.RefreshAsync(token))!.RefreshToken;
        fixture.Clock.Advance(TimeSpan.FromDays(16));

        Assert.Null(await fixture.Connectors.RefreshAsync(token));
    }

    [Fact]
    public async Task Logout_RevokesOnlyThatFamily()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var first = (await fixture.ConnectOwnerAsync()).Response!;
        var second = (await fixture.ConnectOwnerAsync("Read")).Response!;
        var authenticated = await fixture.Connectors.AuthenticateAsync(first.AccessToken);

        await fixture.Connectors.LogoutAsync(authenticated!.Value.User, authenticated.Value.Session, first.RefreshToken);

        Assert.Null(await fixture.Connectors.AuthenticateAsync(first.AccessToken));
        Assert.NotNull(await fixture.Connectors.AuthenticateAsync(second.AccessToken));
    }

    [Fact]
    public async Task DisabledConnector_RefusesEverything()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync(config => config.Enabled = false);

        var result = await fixture.ConnectOwnerAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.StatusCode);
    }
}
