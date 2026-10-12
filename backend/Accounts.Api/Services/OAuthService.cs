using System.Net;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed record OAuthRegisterInput(
    IReadOnlyList<string>? RedirectUris,
    string? ClientName,
    IReadOnlyList<string>? GrantTypes,
    IReadOnlyList<string>? ResponseTypes,
    string? TokenEndpointAuthMethod);

public sealed record OAuthAuthorizeInput(
    string? ClientId,
    string? RedirectUri,
    string? State,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? Resource,
    string? TenantSlug,
    string? Email,
    string? Password,
    string? TotpCode,
    string? RecoveryCode,
    string? AccessLevel,
    string? DataScope);

public sealed record OAuthAuthorizeDescription(
    string ClientName,
    string RedirectUri,
    string RedirectHost,
    string RequestedAccessLevel,
    string RequestedDataScope,
    string Resource);

public sealed record OAuthError(int StatusCode, string Error, string Description, string? Code = null);

public sealed record OAuthResult<T>(T? Value, OAuthError? Error)
{
    public static OAuthResult<T> Ok(T value) => new(value, null);
    public static OAuthResult<T> Fail(int status, string error, string description, string? code = null) => new(default, new OAuthError(status, error, description, code));
}

/// <summary>
/// OAuth 2.1 authorization server for remote MCP clients (Claude.ai, Claude Code, Codex): RFC 8414
/// and RFC 9728 metadata, stateless RFC 7591 dynamic registration of public clients, PKCE S256
/// authorization codes and refresh rotation. Every grant becomes an ordinary connector session, so
/// access levels, the personal-data gate, per-action approval and revocation apply unchanged.
/// </summary>
public sealed class OAuthService
{
    public const string ScopeRead = "fb:read";
    public const string ScopeWrite = "fb:write";
    public const string ScopeAdmin = "fb:admin";
    public const string ScopeDataFull = "fb:data-full";
    public static readonly IReadOnlyList<string> Scopes = [ScopeRead, ScopeWrite, ScopeAdmin, ScopeDataFull];
    public const string CodePrefix = "fbac1";
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    private readonly AccountsDbContext db;
    private readonly ConnectorSessionService connectors;
    private readonly MfaSecurityService mfaSecurity;
    private readonly ConnectorConfig config;
    private readonly TimeProvider timeProvider;
    private readonly DatabaseTenantBootstrapResolver? tenantBootstrap;
    private readonly OAuthClientCodec? clients;

    public OAuthService(
        AccountsDbContext db,
        ConnectorSessionService connectors,
        MfaSecurityService mfaSecurity,
        IOptions<ConnectorConfig> options,
        TimeProvider timeProvider,
        DatabaseTenantBootstrapResolver? tenantBootstrap = null)
    {
        this.db = db;
        this.connectors = connectors;
        this.mfaSecurity = mfaSecurity;
        config = options.Value;
        this.timeProvider = timeProvider;
        this.tenantBootstrap = tenantBootstrap;
        clients = AuthSessionKey.HasStrongKey(config.SigningKey)
            ? new OAuthClientCodec(AuthSessionKey.DecodeRequired(config.SigningKey))
            : null;
    }

    public string? Issuer =>
        connectors.IsAvailable && clients is not null && Uri.TryCreate(config.PublicBaseUrl?.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;

    public string? Resource => Issuer is { } issuer ? issuer + "/mcp" : null;

    public IReadOnlyDictionary<string, object>? AuthorizationServerMetadata()
    {
        if (Issuer is not { } issuer) return null;
        return new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = issuer + "/oauth/authorize",
            ["token_endpoint"] = issuer + "/api/oauth/token",
            ["registration_endpoint"] = issuer + "/api/oauth/register",
            ["revocation_endpoint"] = issuer + "/api/oauth/revoke",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["revocation_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = Scopes,
            ["service_documentation"] = "https://github.com/jasperfordesq-ai/accounts/blob/main/Docs/architecture/mcp-connector.md"
        };
    }

    public IReadOnlyDictionary<string, object>? ProtectedResourceMetadata()
    {
        if (Issuer is not { } issuer) return null;
        return new Dictionary<string, object>
        {
            ["resource"] = issuer + "/mcp",
            ["authorization_servers"] = new[] { issuer },
            ["scopes_supported"] = Scopes,
            ["bearer_methods_supported"] = new[] { "header" },
            ["resource_name"] = "FilingBridge"
        };
    }

    public OAuthResult<object> Register(OAuthRegisterInput input)
    {
        if (Issuer is null || clients is null)
            return OAuthResult<object>.Fail(404, "invalid_request", "OAuth is not enabled on this server.");
        if (input.RedirectUris is not { Count: > 0 and <= 10 })
            return OAuthResult<object>.Fail(400, "invalid_redirect_uri", "Provide between 1 and 10 redirect_uris.");
        foreach (var redirect in input.RedirectUris)
        {
            if (!IsAllowedRedirect(redirect, out var why))
                return OAuthResult<object>.Fail(400, "invalid_redirect_uri", why);
        }
        if (input.TokenEndpointAuthMethod is { Length: > 0 } method && method != "none")
            return OAuthResult<object>.Fail(400, "invalid_client_metadata", "Only public clients (token_endpoint_auth_method \"none\") are supported.");
        if (input.GrantTypes is { Count: > 0 } grants && grants.Any(grant => grant is not ("authorization_code" or "refresh_token")))
            return OAuthResult<object>.Fail(400, "invalid_client_metadata", "Only authorization_code and refresh_token grants are supported.");
        if (input.ResponseTypes is { Count: > 0 } responses && responses.Any(response => response != "code"))
            return OAuthResult<object>.Fail(400, "invalid_client_metadata", "Only the code response type is supported.");

        var name = Clean(input.ClientName, 80) ?? "MCP client";
        var issuedAt = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var clientId = clients.Encode(new OAuthClientRegistration(name, input.RedirectUris.Select(uri => uri.Trim()).ToArray(), issuedAt));
        return OAuthResult<object>.Ok(new Dictionary<string, object>
        {
            ["client_id"] = clientId,
            ["client_id_issued_at"] = issuedAt,
            ["client_name"] = name,
            ["redirect_uris"] = input.RedirectUris.Select(uri => uri.Trim()).ToArray(),
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none"
        });
    }

    public OAuthResult<OAuthAuthorizeDescription> Describe(
        string? clientId,
        string? redirectUri,
        string? responseType,
        string? codeChallenge,
        string? codeChallengeMethod,
        string? scope,
        string? resource)
    {
        if (Issuer is null || clients is null)
            return OAuthResult<OAuthAuthorizeDescription>.Fail(404, "invalid_request", "OAuth is not enabled on this server.");
        var client = clients.Decode(clientId);
        if (client is null)
            return OAuthResult<OAuthAuthorizeDescription>.Fail(400, "invalid_client", "This application is not registered with FilingBridge.", "INVALID_CLIENT");
        if (string.IsNullOrWhiteSpace(redirectUri) || !client.RedirectUris.Contains(redirectUri.Trim(), StringComparer.Ordinal))
            return OAuthResult<OAuthAuthorizeDescription>.Fail(400, "invalid_request", "The redirect address does not match the application's registration.", "INVALID_CLIENT");
        if (!IsAllowedRedirect(redirectUri, out var why))
            return OAuthResult<OAuthAuthorizeDescription>.Fail(400, "invalid_request", why, "INVALID_CLIENT");
        if (responseType is not null && responseType != "code")
            return OAuthResult<OAuthAuthorizeDescription>.Fail(400, "unsupported_response_type", "Only response_type=code is supported.");
        if (codeChallengeMethod != "S256" || !IsValidChallenge(codeChallenge))
            return OAuthResult<OAuthAuthorizeDescription>.Fail(400, "invalid_request", "A PKCE S256 code_challenge is required.");
        var expectedResource = Resource!;
        if (!string.IsNullOrWhiteSpace(resource) && !SameResource(resource, expectedResource))
            return OAuthResult<OAuthAuthorizeDescription>.Fail(400, "invalid_target", $"This server only issues access to {expectedResource}.");

        var (level, dataScope) = ParseScope(scope);
        return OAuthResult<OAuthAuthorizeDescription>.Ok(new OAuthAuthorizeDescription(
            client.ClientName,
            redirectUri.Trim(),
            new Uri(redirectUri.Trim()).Authority,
            level,
            dataScope,
            expectedResource));
    }

    public async Task<OAuthResult<string>> AuthorizeAsync(
        OAuthAuthorizeInput input,
        string? originHeader,
        CancellationToken cancellationToken = default)
    {
        var issuer = Issuer;
        if (issuer is null)
            return OAuthResult<string>.Fail(404, "invalid_request", "OAuth is not enabled on this server.");
        if (!string.IsNullOrWhiteSpace(originHeader)
            && !string.Equals(originHeader.Trim().TrimEnd('/'), issuer, StringComparison.OrdinalIgnoreCase))
            return OAuthResult<string>.Fail(403, "access_denied", "Sign-in must come from the FilingBridge consent page.", "CROSS_SITE_REJECTED");

        var description = Describe(input.ClientId, input.RedirectUri, "code", input.CodeChallenge, input.CodeChallengeMethod, null, input.Resource);
        if (description.Error is { } invalid)
            return OAuthResult<string>.Fail(invalid.StatusCode, invalid.Error, invalid.Description, invalid.Code ?? "INVALID_ARGUMENTS");

        var verified = await connectors.VerifyCredentialsAsync(new ConnectorLoginInput(
            input.TenantSlug, input.Email, input.Password, input.AccessLevel, input.DataScope,
            input.TotpCode, input.RecoveryCode, description.Value!.ClientName), cancellationToken);
        if (verified.Principal is null)
        {
            var refusal = verified.Refusal!;
            return OAuthResult<string>.Fail(refusal.StatusCode, "access_denied", refusal.Error ?? "Sign-in failed.", refusal.Code);
        }

        var principal = verified.Principal;
        var now = UtcNow();
        var rawCode = CreateCode(principal.TenantId);
        db.OAuthAuthorizationCodes.Add(new OAuthAuthorizationCode
        {
            Id = Guid.NewGuid(),
            TenantId = principal.TenantId,
            UserId = principal.UserId,
            CodeHash = HashCode(rawCode),
            ClientIdHash = OAuthClientCodec.Hash(input.ClientId!.Trim()),
            ClientName = description.Value.ClientName,
            RedirectUri = description.Value.RedirectUri,
            CodeChallenge = input.CodeChallenge!.Trim(),
            Resource = description.Value.Resource,
            AccessLevel = verified.AccessLevel!,
            DataScope = verified.DataScope!,
            PasswordLastChangedAtTicks = principal.PasswordLastChangedAt.Ticks,
            MfaVerifiedAtUtc = principal.MfaVerifiedAtUtc?.UtcDateTime,
            MfaMethod = principal.MfaMethod,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(CodeLifetime)
        });
        await db.SaveChangesAsync(cancellationToken);

        var query = new List<string> { "code=" + Uri.EscapeDataString(rawCode) };
        if (!string.IsNullOrEmpty(input.State)) query.Add("state=" + Uri.EscapeDataString(input.State));
        query.Add("iss=" + Uri.EscapeDataString(issuer));
        var separator = description.Value.RedirectUri.Contains('?') ? "&" : "?";
        return OAuthResult<string>.Ok(description.Value.RedirectUri + separator + string.Join("&", query));
    }

    public async Task<OAuthResult<IReadOnlyDictionary<string, object>>> ExchangeAsync(
        IFormCollection form,
        CancellationToken cancellationToken = default)
    {
        if (Issuer is null || clients is null)
            return TokenFail(400, "invalid_request", "OAuth is not enabled on this server.");
        var grantType = form["grant_type"].ToString();
        var clientId = form["client_id"].ToString().Trim();
        if (clients.Decode(clientId) is null)
            return TokenFail(401, "invalid_client", "Unknown client_id.");

        if (grantType == "refresh_token")
        {
            var refreshed = await connectors.RefreshAsync(form["refresh_token"].ToString(), cancellationToken);
            return refreshed is null
                ? TokenFail(400, "invalid_grant", "The refresh token is invalid, expired or already used.")
                : OAuthResult<IReadOnlyDictionary<string, object>>.Ok(TokenResponse(refreshed));
        }

        if (grantType != "authorization_code")
            return TokenFail(400, "unsupported_grant_type", "Use authorization_code or refresh_token.");

        var rawCode = form["code"].ToString();
        var tenantId = ReadCodeTenant(rawCode);
        if (tenantId is null) return TokenFail(400, "invalid_grant", "The authorization code is invalid.");
        tenantBootstrap?.UseVerifiedSessionTenant(tenantId.Value);
        var codeHash = HashCode(rawCode);
        var now = UtcNow();
        var grant = await db.OAuthAuthorizationCodes.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CodeHash == codeHash && candidate.TenantId == tenantId, cancellationToken);
        if (grant is null || !await TryConsumeAsync(grant.Id, now, cancellationToken))
            return TokenFail(400, "invalid_grant", "The authorization code is invalid, expired or already used.");

        if (!string.Equals(grant.ClientIdHash, OAuthClientCodec.Hash(clientId), StringComparison.Ordinal)
            || !string.Equals(grant.RedirectUri, form["redirect_uri"].ToString().Trim(), StringComparison.Ordinal)
            || !PkceMatches(form["code_verifier"].ToString(), grant.CodeChallenge)
            || (!string.IsNullOrWhiteSpace(form["resource"]) && !SameResource(form["resource"].ToString(), grant.Resource)))
            return TokenFail(400, "invalid_grant", "The authorization code does not match this client, redirect address or code verifier.");

        var principal = await connectors.ReloadPrincipalAsync(
            grant.UserId, grant.TenantId, grant.PasswordLastChangedAtTicks, grant.MfaVerifiedAtUtc, grant.MfaMethod, cancellationToken);
        if (principal is null)
            return TokenFail(400, "invalid_grant", "The account changed after sign-in. Sign in again.");

        var issued = await connectors.IssueAsync(principal, grant.AccessLevel, grant.DataScope, grant.ClientName, "oauth", cancellationToken);
        return OAuthResult<IReadOnlyDictionary<string, object>>.Ok(TokenResponse(issued));
    }

    public async Task RevokeAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var trimmed = token.Trim();
        if (trimmed.StartsWith(ConnectorTokenCodec.AccessPrefix + ".", StringComparison.Ordinal))
            await connectors.RevokeByAccessTokenAsync(trimmed, cancellationToken);
        else if (trimmed.StartsWith(ConnectorTokenCodec.RefreshPrefix + ".", StringComparison.Ordinal))
            await connectors.LogoutAsync(null, null, trimmed, cancellationToken);
    }

    public bool IsAllowedRedirect(string? redirectUri, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(redirectUri) || redirectUri.Length > 2000
            || !Uri.TryCreate(redirectUri.Trim(), UriKind.Absolute, out var uri))
        {
            reason = "Redirect addresses must be absolute URLs.";
            return false;
        }
        if (!string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "Redirect addresses may not contain a fragment or credentials.";
            return false;
        }
        if (uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri.Host))
            return true;
        if (uri.Scheme == Uri.UriSchemeHttps
            && config.OAuthRedirectHosts.Any(host => string.Equals(host.Trim(), uri.Host, StringComparison.OrdinalIgnoreCase)))
            return true;
        reason = $"Redirects to {uri.Host} are not allowed. Allowed: loopback (http) and {string.Join(", ", config.OAuthRedirectHosts.Distinct(StringComparer.OrdinalIgnoreCase))} (https).";
        return false;
    }

    public static (string AccessLevel, string DataScope) ParseScope(string? scope)
    {
        var tokens = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var level = tokens.Contains(ScopeAdmin) ? ConnectorAccessLevels.Admin
            : tokens.Contains(ScopeWrite) ? ConnectorAccessLevels.Write
            : tokens.Contains(ScopeRead) ? ConnectorAccessLevels.Read
            : ConnectorAccessLevels.Write;
        var data = tokens.Contains(ScopeDataFull) ? ConnectorDataScopes.Full : ConnectorDataScopes.Withheld;
        return (level, data);
    }

    public static string ScopeFor(string accessLevel, string dataScope)
    {
        var level = accessLevel switch
        {
            ConnectorAccessLevels.Admin => ScopeAdmin,
            ConnectorAccessLevels.Read => ScopeRead,
            _ => ScopeWrite
        };
        return dataScope == ConnectorDataScopes.Full ? $"{level} {ScopeDataFull}" : level;
    }

    public static bool PkceMatches(string? verifier, string challenge)
    {
        if (string.IsNullOrWhiteSpace(verifier) || verifier.Length is < 43 or > 128
            || verifier.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~')))
            return false;
        var computed = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static bool IsValidChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static bool SameResource(string presented, string expected) =>
        string.Equals(presented.Trim().TrimEnd('/'), expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TryConsumeAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.OAuthAuthorizationCodes
                .Where(code => code.Id == id && code.ConsumedAtUtc == null && code.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.ConsumedAtUtc, now), cancellationToken);
            return updated == 1;
        }

        var tracked = await db.OAuthAuthorizationCodes.SingleOrDefaultAsync(
            code => code.Id == id && code.ConsumedAtUtc == null && code.ExpiresAtUtc > now, cancellationToken);
        if (tracked is null) return false;
        tracked.ConsumedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IReadOnlyDictionary<string, object> TokenResponse(ConnectorLoginResponse response)
    {
        var expiresIn = (long)Math.Max(1, (response.AccessTokenExpiresAtUtc - DateTime.UtcNow).TotalSeconds);
        return new Dictionary<string, object>
        {
            ["access_token"] = response.AccessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = expiresIn,
            ["refresh_token"] = response.RefreshToken,
            ["scope"] = ScopeFor(response.Session.AccessLevel, response.Session.DataScope)
        };
    }

    private static OAuthResult<IReadOnlyDictionary<string, object>> TokenFail(int status, string error, string description) =>
        OAuthResult<IReadOnlyDictionary<string, object>>.Fail(status, error, description);

    private static string CreateCode(int tenantId) =>
        $"{CodePrefix}.{Convert.ToBase64String(Encoding.ASCII.GetBytes(tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture))).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    private static int? ReadCodeTenant(string? code) =>
        ConnectorTokenCodec.ReadRefreshTokenTenant(code is null || !code.StartsWith(CodePrefix + ".", StringComparison.Ordinal)
            ? null
            : ConnectorTokenCodec.RefreshPrefix + code[CodePrefix.Length..]);

    private string HashCode(string raw) => mfaSecurity.HashOpaqueToken("oauth-code:" + raw.Trim());

    private static string? Clean(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length == 0) return null;
        return cleaned.Length > length ? cleaned[..length] : cleaned;
    }

    private DateTime UtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }
}
