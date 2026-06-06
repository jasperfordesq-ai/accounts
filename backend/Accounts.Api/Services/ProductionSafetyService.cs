using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class ProductionSafetyService(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<DatabaseStartupConfig> databaseStartup,
    IOptions<AuthSessionConfig> authSession,
    ApiAccessService apiAccess)
{
    private const int MinimumSigningKeyBytes = 32;
    private const int MinimumSigningKeyDistinctEncodedChars = 8;
    private const int MinimumSigningKeyDistinctBytes = 16;

    public IReadOnlyList<string> Validate()
    {
        if (!environment.IsProduction())
            return [];

        var failures = new List<string>();
        var dbStartup = databaseStartup.Value;
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];

        if (dbStartup.AutoMigrateOnStartup && !dbStartup.AllowStartupMigrationInProduction)
            failures.Add("DatabaseStartup:AutoMigrateOnStartup is enabled in production. Run migrations through a controlled release step or set AllowStartupMigrationInProduction=true deliberately.");

        if (dbStartup.SeedDemoData && !dbStartup.AllowDemoSeedInProduction)
            failures.Add("DatabaseStartup:SeedDemoData is enabled in production. Disable demo seeding before handling client data.");

        if (!dbStartup.AllowDevelopmentDatabasePasswordInProduction && connectionString.Contains("accounts_dev", StringComparison.OrdinalIgnoreCase))
            failures.Add("DefaultConnection appears to use the development database password in production.");

        if (allowedOrigins.Length == 0)
            failures.Add("AllowedOrigins must be explicitly configured in production.");

        if (allowedOrigins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            failures.Add("AllowedOrigins contains localhost in production. Configure the real application origin.");

        var session = authSession.Value;
        if (!HasStrongSigningKey(session.SigningKey))
            failures.Add("AuthSession:SigningKey must be a generated Base64 or Base64Url-encoded secret of at least 32 bytes in production.");

        if (session.ExpiryMinutes is < 15 or > 1440)
            failures.Add("AuthSession:ExpiryMinutes must be between 15 and 1440 minutes in production.");

        if (!session.SecureCookiesInProduction)
            failures.Add("AuthSession:SecureCookiesInProduction must be true in production.");

        failures.AddRange(apiAccess.ValidateConfiguration());

        return failures;
    }

    public void ThrowIfUnsafe()
    {
        var failures = Validate();
        if (failures.Count > 0)
            throw new InvalidOperationException("Unsafe production configuration: " + string.Join(" ", failures));
    }

    private static bool HasStrongSigningKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            return false;

        var trimmed = signingKey.Trim();
        if (!TryDecodeSigningKey(trimmed, out var decoded))
            return false;

        return decoded.Length >= MinimumSigningKeyBytes
            && trimmed.TrimEnd('=').Distinct().Count() >= MinimumSigningKeyDistinctEncodedChars
            && decoded.Distinct().Count() >= MinimumSigningKeyDistinctBytes;
    }

    private static bool TryDecodeSigningKey(string signingKey, out byte[] decoded)
    {
        if (TryDecodeBase64(signingKey, out decoded))
            return true;

        return TryDecodeBase64Url(signingKey, out decoded);
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }

    private static bool TryDecodeBase64Url(string value, out byte[] decoded)
    {
        if (value.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '=')))
        {
            decoded = [];
            return false;
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => ""
        };

        if (normalized.Length % 4 != 0)
        {
            decoded = [];
            return false;
        }

        return TryDecodeBase64(normalized, out decoded);
    }
}
