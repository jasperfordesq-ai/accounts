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

    public ApiAccessDecision Authorize(string? presentedKey, PathString path)
    {
        if (!_config.Enabled)
            return ApiAccessDecision.Allowed("Development", null);

        if (string.IsNullOrWhiteSpace(presentedKey))
            return ApiAccessDecision.Denied("Missing API access key.");

        var companyId = ExtractCompanyId(path);
        foreach (var key in _config.Keys)
        {
            if (!Matches(key, presentedKey))
                continue;

            if (companyId.HasValue && key.AllowedCompanyIds.Count > 0 && !key.AllowedCompanyIds.Contains(companyId.Value))
                return ApiAccessDecision.Denied("API key is not authorised for this company.");

            return ApiAccessDecision.Allowed(key.Name, companyId);
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

            if (environment.IsProduction() && !string.IsNullOrWhiteSpace(key.DevelopmentKey))
                failures.Add($"API access key '{key.Name}' uses DevelopmentKey in production. Store only KeyHash in production.");

            if (string.IsNullOrWhiteSpace(key.KeyHash) && string.IsNullOrWhiteSpace(key.DevelopmentKey))
                failures.Add($"API access key '{key.Name}' must provide KeyHash or a development-only DevelopmentKey.");
        }

        return failures;
    }

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
}

public record ApiAccessDecision(bool IsAllowed, string? PrincipalName, int? CompanyId, string? DenialReason)
{
    public static ApiAccessDecision Allowed(string principalName, int? companyId) => new(true, principalName, companyId, null);
    public static ApiAccessDecision Denied(string reason) => new(false, null, null, reason);
}
