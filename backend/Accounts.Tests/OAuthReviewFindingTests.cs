using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Accounts.Tests;

/// <summary>Regression tests for the review findings on the OAuth server (PR #88).</summary>
public sealed class OAuthReviewFindingTests
{
    private const string Redirect = "http://127.0.0.1:53682/callback";

    [Fact]
    public async Task AWrongVerifier_DoesNotBurnTheLegitimateExchange()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture);
        var (verifier, challenge) = Pkce();
        var code = await AuthorizeAsync(fixture, clientId, challenge, "Write", "Withheld");

        var attacker = await Exchange(fixture, clientId, code, Pkce().Verifier);
        var legitimate = await Exchange(fixture, clientId, code, verifier);

        Assert.Equal("invalid_grant", attacker.Error!.Error);
        Assert.Null(legitimate.Error);
    }

    [Fact]
    public async Task RefreshTokens_WorkOnlyForTheClientThatReceivedThem()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientA = Register(fixture, "A");
        var clientB = Register(fixture, "B");
        var (verifier, challenge) = Pkce();
        var tokens = (await Exchange(fixture, clientA, await AuthorizeAsync(fixture, clientA, challenge, "Write", "Withheld"), verifier)).Value!;
        var refreshToken = (string)tokens["refresh_token"];

        var asOtherClient = await fixture.OAuth.ExchangeAsync(Form(("grant_type", "refresh_token"), ("refresh_token", refreshToken), ("client_id", clientB)));
        var throughCliRoute = await fixture.Connectors.RefreshAsync(refreshToken);
        var asIssuingClient = await fixture.OAuth.ExchangeAsync(Form(("grant_type", "refresh_token"), ("refresh_token", refreshToken), ("client_id", clientA)));

        Assert.Equal("invalid_grant", asOtherClient.Error!.Error);
        Assert.Null(throughCliRoute);
        Assert.Null(asIssuingClient.Error);
    }

    [Fact]
    public async Task CliSessions_CannotBeRefreshedThroughOAuth()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;
        var clientId = Register(fixture);

        var result = await fixture.OAuth.ExchangeAsync(Form(("grant_type", "refresh_token"), ("refresh_token", login.RefreshToken), ("client_id", clientId)));

        Assert.Equal("invalid_grant", result.Error!.Error);
        Assert.NotNull(await fixture.Connectors.RefreshAsync(login.RefreshToken));
    }

    [Fact]
    public async Task APendingCode_DoesNotSurviveASessionRevocation()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture);
        var (verifier, challenge) = Pkce();
        var code = await AuthorizeAsync(fixture, clientId, challenge, "Write", "Withheld");
        var owner = await fixture.Auth.GetPrincipalAsync(fixture.Owner.Id) ?? throw new InvalidOperationException();
        var other = await fixture.AddUserAsync("second-owner@example.ie", "Owner");
        var secondOwner = await fixture.Auth.GetPrincipalAsync(other.Id) ?? throw new InvalidOperationException();

        await fixture.Lifecycle.RevokeSessionsAsync(secondOwner, owner.UserId);
        var result = await Exchange(fixture, clientId, code, verifier);

        Assert.Equal("invalid_grant", result.Error!.Error);
        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
    }

    [Fact]
    public async Task ADemotedOwner_CannotExchangeAFullScopeCode()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture);
        var (verifier, challenge) = Pkce();
        var code = await AuthorizeAsync(fixture, clientId, challenge, "Write", "Full");
        var user = await fixture.Db.UserAccounts.SingleAsync(account => account.Id == fixture.Owner.Id);
        user.Role = "Accountant"; // demoted without a session-version change (worst case)
        await fixture.Db.SaveChangesAsync();

        var result = await Exchange(fixture, clientId, code, verifier);

        Assert.Equal("invalid_grant", result.Error!.Error);
        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
    }

    [Fact]
    public async Task OversizedRegistrations_AreRefusedInsteadOfReturningUnusableIds()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var longPath = new string('a', 1_900);
        var redirects = Enumerable.Range(0, 3).Select(index => $"http://127.0.0.1:{5000 + index}/{longPath}").ToArray();

        var result = fixture.OAuth.Register(new OAuthRegisterInput(redirects, "Big", null, null, null));

        Assert.Equal("invalid_client_metadata", result.Error!.Error);
        var ok = fixture.OAuth.Register(new OAuthRegisterInput([Redirect], "Small", null, null, null));
        var clientId = (string)((IReadOnlyDictionary<string, object>)ok.Value!)["client_id"];
        Assert.NotNull(fixture.OAuth.Describe(clientId, Redirect, "code", Pkce().Challenge, "S256", null, null).Value);
    }

    [Fact]
    public async Task Retention_RemovesFinishedAuthorizationCodes()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture);
        await AuthorizeAsync(fixture, clientId, Pkce().Challenge, "Read", "Withheld");
        var privacy = new PrivacyGovernanceService(
            fixture.Db,
            Microsoft.Extensions.Options.Options.Create(new Accounts.Api.Rules.PrivacyGovernanceConfig()),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Options.IOptions<Accounts.Api.Rules.AuthSessionConfig>>(fixture.Services),
            fixture.Clock,
            Microsoft.Extensions.Options.Options.Create(fixture.ConnectorConfig));

        Assert.Equal(0, (await privacy.RunRetentionForTenantAsync(fixture.Tenant.Id)).DeletedConnectorRecords);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        var result = await privacy.RunRetentionForTenantAsync(fixture.Tenant.Id);

        Assert.Equal(1, result.DeletedConnectorRecords);
        Assert.Empty(await fixture.Db.OAuthAuthorizationCodes.ToListAsync());
    }

    private static string Register(ConnectorTestFixture fixture, string name = "Client") =>
        (string)((IReadOnlyDictionary<string, object>)fixture.OAuth.Register(new OAuthRegisterInput([Redirect], name, null, null, null)).Value!)["client_id"];

    private static async Task<string> AuthorizeAsync(ConnectorTestFixture fixture, string clientId, string challenge, string level, string scope)
    {
        var result = await fixture.OAuth.AuthorizeAsync(new OAuthAuthorizeInput(
            clientId, Redirect, null, challenge, "S256", null, fixture.Tenant.Slug, fixture.Owner.Email,
            ConnectorTestFixture.OwnerPassword, fixture.NextTotp(), null, level, scope), null);
        Assert.Null(result.Error);
        return Uri.UnescapeDataString(result.Value!.Split("code=")[1].Split('&')[0]);
    }

    private static Task<OAuthResult<IReadOnlyDictionary<string, object>>> Exchange(ConnectorTestFixture fixture, string clientId, string code, string verifier) =>
        fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", Redirect), ("client_id", clientId), ("code_verifier", verifier)));

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    private static IFormCollection Form(params (string Key, string Value)[] values) =>
        new FormCollection(values.ToDictionary(pair => pair.Key, pair => new StringValues(pair.Value)));
}
