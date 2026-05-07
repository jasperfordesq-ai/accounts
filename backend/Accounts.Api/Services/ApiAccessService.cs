using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class ApiAccessService(IOptions<ApiAccessConfig> options, IHostEnvironment environment)
{
    private readonly ApiAccessConfig _config = options.Value;

    public bool Enabled => _config.Enabled;
    public string HeaderName => string.IsNullOrWhiteSpace(_config.HeaderName) ? "X-Accounts-Api-Key" : _config.HeaderName;

    public ApiAccessDecision Authorize(string? presentedKey, PathString path, string method = "GET")
    {
        if (!_config.Enabled)
            return ApiAccessDecision.Allowed("Development", ApiAccessRole.Admin, null, null);

        if (string.IsNullOrWhiteSpace(presentedKey))
            return ApiAccessDecision.Denied("Missing API access key.");

        var companyId = ExtractCompanyId(path);
        foreach (var key in _config.Keys)
        {
            if (!Matches(key, presentedKey))
                continue;

            if (!TryParseRole(key.Role, out var role))
                return ApiAccessDecision.Denied("API key has an invalid role configuration.");

            if (companyId.HasValue && key.AllowedCompanyIds.Count > 0 && !key.AllowedCompanyIds.Contains(companyId.Value))
                return ApiAccessDecision.Denied("API key is not authorised for this company.");

            if (!CanUseMethod(role, method))
                return ApiAccessDecision.Denied("API key role is read-only.");

            if (RequiresAdmin(path, method) && role != ApiAccessRole.Admin)
                return ApiAccessDecision.Denied("API key role is not authorised for this administrative action.");

            if (!companyId.HasValue && IsCompanyCollectionWrite(path, method) && key.AllowedCompanyIds.Count > 0)
                return ApiAccessDecision.Denied("Company-scoped API keys cannot create new companies.");

            return ApiAccessDecision.Allowed(key.Name, role, companyId, key.AllowedCompanyIds);
        }

        return ApiAccessDecision.Denied("Invalid API access key.");
    }

    public IReadOnlyList<string> ValidateConfiguration()
    {
        var failures = new List<string>();

        if (_config.RequireInProduction && environment.IsProduction() && !_config.Enabled)
            failures.Add("ApiAccess:Enabled must be true in production.");

        if (!_config.Enabled)
            return failures;

        if (_config.Keys.Count == 0)
            failures.Add("ApiAccess:Keys must contain at least one key when API access control is enabled.");

        foreach (var key in _config.Keys)
        {
            if (string.IsNullOrWhiteSpace(key.Name))
                failures.Add("Every API access key must have a Name.");

            if (!TryParseRole(key.Role, out _))
                failures.Add($"API access key '{key.Name}' has invalid Role '{key.Role}'. Use Reader, Writer, or Admin.");

            if (environment.IsProduction() && !string.IsNullOrWhiteSpace(key.DevelopmentKey))
                failures.Add($"API access key '{key.Name}' uses DevelopmentKey in production. Store only KeyHash in production.");

            if (string.IsNullOrWhiteSpace(key.KeyHash) && string.IsNullOrWhiteSpace(key.DevelopmentKey))
                failures.Add($"API access key '{key.Name}' must provide KeyHash or a development-only DevelopmentKey.");

            if (key.AllowedCompanyIds.Any(id => id <= 0))
                failures.Add($"API access key '{key.Name}' has an invalid AllowedCompanyIds entry.");
        }

        return failures;
    }

    public static IReadOnlyCollection<int>? GetAllowedCompanyIds(HttpContext context) =>
        context.Items["ApiAllowedCompanyIds"] as IReadOnlyCollection<int>;

    public static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(hash);
    }

    private bool Matches(ApiAccessKeyConfig key, string presentedKey)
    {
        if (!string.IsNullOrWhiteSpace(key.KeyHash) && FixedTimeEquals(key.KeyHash, HashKey(presentedKey)))
            return true;

        return !environment.IsProduction()
            && !string.IsNullOrWhiteSpace(key.DevelopmentKey)
            && FixedTimeEquals(key.DevelopmentKey, presentedKey);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected.Trim());
        var actualBytes = Encoding.UTF8.GetBytes(actual.Trim());
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static int? ExtractCompanyId(PathString path)
    {
        var match = Regex.Match(path.Value ?? "", @"/api/companies/(?<id>\d+)(/|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["id"].Value, out var companyId) ? companyId : null;
    }

    private static bool CanUseMethod(ApiAccessRole role, string method) =>
        role != ApiAccessRole.Reader
        || HttpMethods.IsGet(method)
        || HttpMethods.IsHead(method)
        || HttpMethods.IsOptions(method);

    private static bool RequiresAdmin(PathString path, string method) =>
        HttpMethods.IsDelete(method)
        || (IsCompanyCollectionWrite(path, method) && HttpMethods.IsPost(method));

    private static bool IsCompanyCollectionWrite(PathString path, string method) =>
        !HttpMethods.IsGet(method)
        && Regex.IsMatch(path.Value ?? "", @"^/api/companies/?$", RegexOptions.IgnoreCase);

    private static bool TryParseRole(string? role, out ApiAccessRole parsed) =>
        Enum.TryParse(role?.Trim() ?? "Admin", ignoreCase: true, out parsed);
}

public enum ApiAccessRole
{
    Reader,
    Writer,
    Admin
}

public record ApiAccessDecision(
    bool IsAllowed,
    string? PrincipalName,
    ApiAccessRole? Role,
    int? CompanyId,
    IReadOnlyCollection<int>? AllowedCompanyIds,
    string? DenialReason)
{
    public static ApiAccessDecision Allowed(string principalName, ApiAccessRole role, int? companyId, IReadOnlyCollection<int>? allowedCompanyIds) =>
        new(true, principalName, role, companyId, allowedCompanyIds?.ToArray(), null);

    public static ApiAccessDecision Denied(string reason) => new(false, null, null, null, null, reason);
}
