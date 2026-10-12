using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Accounts.Tests;

public sealed class OAuthTests
{
    private const string LoopbackRedirect = "http://127.0.0.1:53682/callback";
    private const string ClaudeRedirect = "https://claude.ai/api/mcp/auth_callback";

    [Fact]
    public async Task Metadata_DescribesTheAuthorizationServerAndTheMcpResource()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var server = fixture.OAuth.AuthorizationServerMetadata()!;
        var resource = fixture.OAuth.ProtectedResourceMetadata()!;

        Assert.Equal("https://filingbridge.example", server["issuer"]);
        Assert.Equal("https://filingbridge.example/oauth/authorize", server["authorization_endpoint"]);
        Assert.Equal("https://filingbridge.example/api/oauth/token", server["token_endpoint"]);
        Assert.Equal("https://filingbridge.example/api/oauth/register", server["registration_endpoint"]);
        Assert.Equal(new[] { "S256" }, server["code_challenge_methods_supported"]);
        Assert.Equal(new[] { "none" }, server["token_endpoint_auth_methods_supported"]);
        Assert.Equal("https://filingbridge.example/mcp", resource["resource"]);
        Assert.Equal(new[] { "https://filingbridge.example" }, resource["authorization_servers"]);
    }

    [Fact]
    public async Task Metadata_IsAbsentWithoutAPublicOrigin()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync(config => config.PublicBaseUrl = null);

        Assert.Null(fixture.OAuth.AuthorizationServerMetadata());
        Assert.NotNull(fixture.OAuth.Register(new OAuthRegisterInput([LoopbackRedirect], "x", null, null, null)).Error);
    }

    [Theory]
    [InlineData(LoopbackRedirect, true)]
    [InlineData("http://localhost:3334/oauth/callback", true)]
    [InlineData("http://[::1]:9000/cb", true)]
    [InlineData(ClaudeRedirect, true)]
    [InlineData("https://claude.com/api/mcp/auth_callback", true)]
    [InlineData("https://evil.example/callback", false)]
    [InlineData("http://claude.ai/api/mcp/auth_callback", false)]
    [InlineData("https://claude.ai.evil.example/cb", false)]
    [InlineData("http://127.0.0.1:53682/callback#fragment", false)]
    [InlineData("https://user:pass@claude.ai/cb", false)]
    [InlineData("/relative", false)]
    [InlineData("javascript:alert(1)", false)]
    public async Task Registration_AllowsOnlyLoopbackAndConfiguredHttpsHosts(string redirect, bool allowed)
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        var result = fixture.OAuth.Register(new OAuthRegisterInput([redirect], "Claude", null, null, null));

        Assert.Equal(allowed, result.Error is null);
        if (!allowed) Assert.Equal("invalid_redirect_uri", result.Error!.Error);
    }

    [Fact]
    public async Task Registration_RefusesConfidentialClientsAndOtherGrants()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();

        Assert.Equal("invalid_client_metadata", fixture.OAuth.Register(new OAuthRegisterInput([LoopbackRedirect], "x", null, null, "client_secret_basic")).Error!.Error);
        Assert.Equal("invalid_client_metadata", fixture.OAuth.Register(new OAuthRegisterInput([LoopbackRedirect], "x", ["client_credentials"], null, null)).Error!.Error);
        Assert.Equal("invalid_client_metadata", fixture.OAuth.Register(new OAuthRegisterInput([LoopbackRedirect], "x", null, ["token"], null)).Error!.Error);
    }

    [Fact]
    public async Task ClientIds_CannotBeForgedOrEdited()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, LoopbackRedirect);
        var parts = clientId.Split('.');
        var widened = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"clientName\":\"x\",\"redirectUris\":[\"https://evil.example/cb\"],\"issuedAt\":1}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var (_, challenge) = Pkce();

        var forged = fixture.OAuth.Describe($"{parts[0]}.{widened}.{parts[2]}", "https://evil.example/cb", "code", challenge, "S256", null, null);
        var fromOtherServer = await ConnectorTestFixture.CreateAsync();
        var foreign = fromOtherServer.OAuth.Describe(clientId, LoopbackRedirect, "code", challenge, "S256", null, null);
        await fromOtherServer.DisposeAsync();

        Assert.Equal("invalid_client", forged.Error!.Error);
        Assert.Equal("invalid_client", foreign.Error!.Error);
    }

    [Fact]
    public async Task Describe_ValidatesRedirectPkceAndResource_AndReadsScope()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, ClaudeRedirect, "Claude");
        var (_, challenge) = Pkce();

        var ok = fixture.OAuth.Describe(clientId, ClaudeRedirect, "code", challenge, "S256", "fb:read fb:admin fb:data-full", "https://filingbridge.example/mcp/");
        Assert.Null(ok.Error);
        Assert.Equal("Claude", ok.Value!.ClientName);
        Assert.Equal("claude.ai", ok.Value.RedirectHost);
        Assert.Equal("Admin", ok.Value.RequestedAccessLevel);
        Assert.Equal("Full", ok.Value.RequestedDataScope);

        Assert.Equal("Write", fixture.OAuth.Describe(clientId, ClaudeRedirect, "code", challenge, "S256", null, null).Value!.RequestedAccessLevel);
        Assert.NotNull(fixture.OAuth.Describe(clientId, "https://claude.ai/other", "code", challenge, "S256", null, null).Error);
        Assert.NotNull(fixture.OAuth.Describe(clientId, ClaudeRedirect, "code", challenge, "plain", null, null).Error);
        Assert.NotNull(fixture.OAuth.Describe(clientId, ClaudeRedirect, "code", "short", "S256", null, null).Error);
        Assert.Equal("invalid_target", fixture.OAuth.Describe(clientId, ClaudeRedirect, "code", challenge, "S256", null, "https://other.example/mcp").Error!.Error);
        Assert.Equal("unsupported_response_type", fixture.OAuth.Describe(clientId, ClaudeRedirect, "token", challenge, "S256", null, null).Error!.Error);
    }

    [Fact]
    public async Task FullFlow_IssuesAConnectorSessionWithTheChosenPosture()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, ClaudeRedirect, "Claude");
        var (verifier, challenge) = Pkce();

        var redirect = await AuthorizeAsync(fixture, clientId, ClaudeRedirect, challenge, "Admin", "Full", state: "xyz");
        Assert.StartsWith(ClaudeRedirect + "?code=fbac1.", redirect, StringComparison.Ordinal);
        Assert.Contains("&state=xyz", redirect);
        Assert.Contains("&iss=https%3A%2F%2Ffilingbridge.example", redirect);
        var code = Uri.UnescapeDataString(redirect.Split("code=")[1].Split('&')[0]);
        Assert.DoesNotContain(code, (await fixture.Db.OAuthAuthorizationCodes.SingleAsync()).CodeHash);

        var token = await fixture.OAuth.ExchangeAsync(Form(
            ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", ClaudeRedirect),
            ("client_id", clientId), ("code_verifier", verifier), ("resource", "https://filingbridge.example/mcp")));

        Assert.Null(token.Error);
        Assert.Equal("Bearer", token.Value!["token_type"]);
        Assert.Equal("fb:admin fb:data-full", token.Value["scope"]);
        var accessToken = (string)token.Value["access_token"];
        var authenticated = await fixture.Connectors.AuthenticateAsync(accessToken);
        Assert.NotNull(authenticated);
        Assert.Equal("Admin", authenticated!.Value.Session.AccessLevel);
        Assert.Equal("Full", authenticated.Value.Session.DataScope);
        Assert.Equal("totp", authenticated.Value.User.MfaMethod);
        var session = await fixture.Db.ConnectorSessions.AsNoTracking().SingleAsync();
        Assert.Equal("Claude", session.DeviceLabel);

        // A code is single use.
        var replay = await fixture.OAuth.ExchangeAsync(Form(
            ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", ClaudeRedirect),
            ("client_id", clientId), ("code_verifier", verifier)));
        Assert.Equal("invalid_grant", replay.Error!.Error);
    }

    [Fact]
    public async Task Exchange_RefusesWrongVerifierRedirectClientOrExpiredCode()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, LoopbackRedirect);
        var otherClient = Register(fixture, LoopbackRedirect, "Other");

        async Task<string> FreshCode()
        {
            var (_, challengeForCode) = Pkce(out var verifierForCode);
            lastVerifier = verifierForCode;
            var redirect = await AuthorizeAsync(fixture, clientId, LoopbackRedirect, challengeForCode, "Read", "Withheld");
            return Uri.UnescapeDataString(redirect.Split("code=")[1].Split('&')[0]);
        }

        var code = await FreshCode();
        Assert.Equal("invalid_grant", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", LoopbackRedirect), ("client_id", clientId), ("code_verifier", Pkce().Verifier)))).Error!.Error);

        code = await FreshCode();
        Assert.Equal("invalid_grant", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", "http://127.0.0.1:1/other"), ("client_id", clientId), ("code_verifier", lastVerifier)))).Error!.Error);

        code = await FreshCode();
        Assert.Equal("invalid_grant", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", LoopbackRedirect), ("client_id", otherClient), ("code_verifier", lastVerifier)))).Error!.Error);

        code = await FreshCode();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("invalid_grant", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", LoopbackRedirect), ("client_id", clientId), ("code_verifier", lastVerifier)))).Error!.Error);

        Assert.Empty(await fixture.Db.ConnectorSessions.ToListAsync());
        Assert.Equal("invalid_client", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("client_id", "nope")))).Error!.Error);
        Assert.Equal("unsupported_grant_type", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "password"), ("client_id", clientId)))).Error!.Error);
    }

    [Fact]
    public async Task PasswordChangeBetweenConsentAndExchange_InvalidatesTheCode()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, LoopbackRedirect);
        var (verifier, challenge) = Pkce();
        var redirect = await AuthorizeAsync(fixture, clientId, LoopbackRedirect, challenge, "Write", "Withheld");
        var code = Uri.UnescapeDataString(redirect.Split("code=")[1].Split('&')[0]);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True((await fixture.Auth.ChangePasswordAsync(fixture.Owner.Id, ConnectorTestFixture.OwnerPassword, "A Completely New Passphrase 42!")).Succeeded);

        var token = await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", LoopbackRedirect), ("client_id", clientId), ("code_verifier", verifier)));

        Assert.Equal("invalid_grant", token.Error!.Error);
    }

    [Fact]
    public async Task Authorize_RefusesCrossSiteOriginsAndNonOwnerFullScope()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, LoopbackRedirect);
        var (_, challenge) = Pkce();

        var crossSite = await fixture.OAuth.AuthorizeAsync(Input(fixture, clientId, LoopbackRedirect, challenge, "Read", "Withheld", fixture.Owner.Email, fixture.NextTotp()), "https://evil.example");
        Assert.Equal("CROSS_SITE_REJECTED", crossSite.Error!.Code);

        var sameSite = await fixture.OAuth.AuthorizeAsync(Input(fixture, clientId, LoopbackRedirect, challenge, "Read", "Withheld", fixture.Owner.Email, fixture.NextTotp()), "https://filingbridge.example");
        Assert.Null(sameSite.Error);

        var accountant = await fixture.AddUserAsync("accountant@example.ie", "Accountant");
        var secret = await fixture.EnrolAsync(accountant);
        var full = await fixture.OAuth.AuthorizeAsync(Input(fixture, clientId, LoopbackRedirect, challenge, "Write", "Full", accountant.Email, fixture.NextTotp(secret)), null);
        Assert.Equal(ConnectorErrorCodes.DataScopeForbidden, full.Error!.Code);

        var wrongPassword = await fixture.OAuth.AuthorizeAsync(Input(fixture, clientId, LoopbackRedirect, challenge, "Read", "Withheld", fixture.Owner.Email, fixture.NextTotp()) with { Password = "nope nope nope 1!" }, null);
        Assert.Equal(ConnectorErrorCodes.InvalidCredentials, wrongPassword.Error!.Code);
        Assert.Single(await fixture.Db.OAuthAuthorizationCodes.ToListAsync());
    }

    [Fact]
    public async Task RefreshGrant_RotatesAndRevocationEndsTheAgent()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var clientId = Register(fixture, LoopbackRedirect);
        var (verifier, challenge) = Pkce();
        var redirect = await AuthorizeAsync(fixture, clientId, LoopbackRedirect, challenge, "Write", "Withheld");
        var code = Uri.UnescapeDataString(redirect.Split("code=")[1].Split('&')[0]);
        var first = (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "authorization_code"), ("code", code),
            ("redirect_uri", LoopbackRedirect), ("client_id", clientId), ("code_verifier", verifier)))).Value!;

        var refreshed = await fixture.OAuth.ExchangeAsync(Form(("grant_type", "refresh_token"),
            ("refresh_token", (string)first["refresh_token"]), ("client_id", clientId)));
        Assert.Null(refreshed.Error);
        Assert.NotEqual(first["refresh_token"], refreshed.Value!["refresh_token"]);

        await fixture.OAuth.RevokeAsync((string)refreshed.Value["refresh_token"]);
        Assert.Null(await fixture.Connectors.AuthenticateAsync((string)refreshed.Value["access_token"]));
        Assert.Equal("invalid_grant", (await fixture.OAuth.ExchangeAsync(Form(("grant_type", "refresh_token"),
            ("refresh_token", (string)refreshed.Value["refresh_token"]), ("client_id", clientId)))).Error!.Error);
    }

    [Fact]
    public async Task RevokingAnAccessToken_EndsTheAgent()
    {
        await using var fixture = await ConnectorTestFixture.CreateAsync();
        var login = (await fixture.ConnectOwnerAsync()).Response!;

        await fixture.OAuth.RevokeAsync(login.AccessToken);

        Assert.Null(await fixture.Connectors.AuthenticateAsync(login.AccessToken));
        Assert.Null(await fixture.Connectors.RefreshAsync(login.RefreshToken));
    }

    [Theory]
    [InlineData("fb:read", "Read", "Withheld")]
    [InlineData("fb:write", "Write", "Withheld")]
    [InlineData("fb:read fb:admin", "Admin", "Withheld")]
    [InlineData("fb:data-full", "Write", "Full")]
    [InlineData("openid profile", "Write", "Withheld")]
    [InlineData(null, "Write", "Withheld")]
    public void ScopeParsing_PicksTheHighestLevel(string? scope, string level, string data)
    {
        Assert.Equal((level, data), OAuthService.ParseScope(scope));
    }

    [Fact]
    public void Pkce_AcceptsOnlyTheMatchingVerifier()
    {
        var (verifier, challenge) = Pkce();
        Assert.True(OAuthService.PkceMatches(verifier, challenge));
        Assert.False(OAuthService.PkceMatches(verifier + "x", challenge));
        Assert.False(OAuthService.PkceMatches("short", challenge));
        Assert.False(OAuthService.PkceMatches(null, challenge));
    }

    [Theory]
    [InlineData("POST", "/api/companies", "{\"legalName\":\"X\",\"registeredOfficeAddress1\":\"1 Road\"}", true)]
    [InlineData("PUT", "/api/companies/4", "{\"registeredOfficeEircode\":\"H91 X2Y3\"}", true)]
    [InlineData("PUT", "/api/companies/4", "{\"legalName\":\"X\",\"registeredOfficeCity\":null}", false)]
    [InlineData("POST", "/api/companies/onboard", "{\"company\":{\"registeredOfficeCity\":\"Galway\"}}", true)]
    public void RegisteredOfficeWrites_AreWithheldServerSide(string method, string path, string body, bool withheld)
    {
        var fields = PersonalDataRouteCatalog.WithheldBodyFields(path, method);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var setsWithheld = Walk(document.RootElement).Any(pair => fields.Contains(pair.Name) && pair.Value.ValueKind is not System.Text.Json.JsonValueKind.Null);
        Assert.Equal(withheld, setsWithheld);

        static IEnumerable<(string Name, System.Text.Json.JsonElement Value)> Walk(System.Text.Json.JsonElement element)
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Object) yield break;
            foreach (var property in element.EnumerateObject())
            {
                yield return (property.Name, property.Value);
                foreach (var nested in Walk(property.Value)) yield return nested;
            }
        }
    }

    private static string lastVerifier = "";

    private static string Register(ConnectorTestFixture fixture, string redirect, string name = "Test client") =>
        (string)((IReadOnlyDictionary<string, object>)fixture.OAuth.Register(new OAuthRegisterInput([redirect], name, null, null, "none")).Value!)["client_id"];

    private static OAuthAuthorizeInput Input(
        ConnectorTestFixture fixture,
        string clientId,
        string redirect,
        string challenge,
        string level,
        string scope,
        string email,
        string totp,
        string? state = null) =>
        new(clientId, redirect, state, challenge, "S256", null, fixture.Tenant.Slug, email, ConnectorTestFixture.OwnerPassword, totp, null, level, scope);

    private static async Task<string> AuthorizeAsync(
        ConnectorTestFixture fixture,
        string clientId,
        string redirect,
        string challenge,
        string level,
        string scope,
        string? state = null)
    {
        var result = await fixture.OAuth.AuthorizeAsync(Input(fixture, clientId, redirect, challenge, level, scope, fixture.Owner.Email, fixture.NextTotp(), state), null);
        Assert.Null(result.Error);
        return result.Value!;
    }

    private static (string Verifier, string Challenge) Pkce() => Pkce(out _);

    private static (string Verifier, string Challenge) Pkce(out string verifier)
    {
        verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    private static IFormCollection Form(params (string Key, string Value)[] values) =>
        new FormCollection(values.ToDictionary(pair => pair.Key, pair => new StringValues(pair.Value)));
}
