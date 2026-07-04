using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class ProductionSafetyService(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<DatabaseStartupConfig> databaseStartup,
    IOptions<AuthSessionConfig> authSession,
    ApiAccessService apiAccess,
    IOptions<AuditIntegrityConfig>? auditIntegrity = null,
    IOptions<BootstrapOwnerConfig>? bootstrapOwner = null,
    IOptions<MonitoringConfig>? monitoring = null)
{
    public IReadOnlyList<string> Validate()
    {
        if (environment.IsDevelopment())
            return [];

        var failures = new List<string>();
        var dbStartup = databaseStartup.Value;
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        var allowedHosts = configuration["AllowedHosts"];
        var trustForwardedFor = configuration.GetValue<bool>("RateLimits:TrustForwardedFor");
        var trustedProxyHeadersAcknowledged = configuration.GetValue<bool>("TRUST_PROXY_HEADERS");

        if (dbStartup.AutoMigrateOnStartup && !dbStartup.AllowStartupMigrationInProduction)
            failures.Add("DatabaseStartup:AutoMigrateOnStartup is enabled outside development. Run migrations through a controlled release step or set AllowStartupMigrationInProduction=true deliberately.");

        if (dbStartup.SeedDemoData)
            failures.Add("DatabaseStartup:SeedDemoData must be disabled outside development. Demo seeding creates known sample users and data.");

        if (string.IsNullOrWhiteSpace(connectionString))
            failures.Add("DefaultConnection must be explicitly configured outside development.");

        if (!string.IsNullOrWhiteSpace(connectionString)
            && !dbStartup.AllowDevelopmentDatabasePasswordInProduction
            && connectionString.Contains("accounts_dev", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("DefaultConnection appears to use the development database password outside development.");
        }

        // crypto-tls-to-db: outside development the DB connection must require TLS (sslmode
        // require/verify-ca/verify-full), so accounting data is not sent to PostgreSQL in clear text.
        if (!string.IsNullOrWhiteSpace(connectionString) && !dbStartup.AllowInsecureDatabaseConnection
            && !DatabaseConnectionRequiresTls(connectionString))
        {
            failures.Add("DefaultConnection must require TLS outside development (set SSL Mode=Require, VerifyCA or VerifyFull), or set DatabaseStartup:AllowInsecureDatabaseConnection=true deliberately.");
        }

        if (allowedOrigins.Length == 0)
            failures.Add("AllowedOrigins must be explicitly configured outside development.");

        if (allowedOrigins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            failures.Add("AllowedOrigins contains localhost outside development. Configure the real application origin.");

        if (allowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            failures.Add("AllowedOrigins must contain absolute HTTPS origins outside development.");

        var configuredHosts = allowedHosts?
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (configuredHosts.Length == 0)
        {
            failures.Add("AllowedHosts must be explicitly configured outside development.");
        }
        else if (configuredHosts.Any(host => host == "*" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("AllowedHosts must not use wildcard or localhost values outside development.");
        }

        if (trustForwardedFor && !trustedProxyHeadersAcknowledged)
            failures.Add("RateLimits:TrustForwardedFor requires TRUST_PROXY_HEADERS=true outside development so the trusted ingress header contract is explicit before forwarded client IPs are used for rate limiting.");

        var session = authSession.Value;
        if (!AuthSessionKey.HasStrongKey(session.SigningKey))
            failures.Add("AuthSession:SigningKey must be a generated Base64 or Base64Url-encoded secret of at least 32 bytes outside development.");

        if (AuthSessionKey.IsKnownDevelopmentKey(session.SigningKey))
            failures.Add("AuthSession:SigningKey uses the committed development session signing key outside development. Generate a fresh deployment secret.");

        if (session.ExpiryMinutes is < 15 or > 1440)
            failures.Add("AuthSession:ExpiryMinutes must be between 15 and 1440 minutes outside development.");

        if (!session.SecureCookiesInProduction)
            failures.Add("AuthSession:SecureCookiesInProduction must be true outside development.");

        failures.AddRange(ValidateBootstrapOwner(bootstrapOwner?.Value ?? new BootstrapOwnerConfig()));
        failures.AddRange(AuditIntegrityCheckpointService.ValidateConfiguration(
            auditIntegrity?.Value ?? new AuditIntegrityConfig()));
        failures.AddRange(apiAccess.ValidateConfiguration());
        failures.AddRange(ValidateMonitoring(monitoring?.Value ?? new MonitoringConfig()));

        return failures;
    }

    public void ThrowIfUnsafe()
    {
        var failures = Validate();
        if (failures.Count > 0)
            throw new InvalidOperationException("Unsafe production configuration: " + string.Join(" ", failures));
    }

    // crypto-tls-to-db: a connection requires TLS only when its SSL mode is Require / VerifyCA /
    // VerifyFull. A missing or weaker mode (Disable/Allow/Prefer) does not guarantee an encrypted link.
    private static bool DatabaseConnectionRequiresTls(string connectionString)
    {
        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            return builder.SslMode is Npgsql.SslMode.Require or Npgsql.SslMode.VerifyCA or Npgsql.SslMode.VerifyFull;
        }
        catch
        {
            // Unparseable connection string — fail safe (treat as not TLS-required).
            return false;
        }
    }

    private static IReadOnlyList<string> ValidateBootstrapOwner(BootstrapOwnerConfig bootstrap)
    {
        if (!bootstrap.Enabled)
            return [];

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(bootstrap.TenantName))
            failures.Add("BootstrapOwner:TenantName is required when bootstrap owner creation is enabled outside development.");

        if (string.IsNullOrWhiteSpace(bootstrap.TenantSlug))
            failures.Add("BootstrapOwner:TenantSlug is required when bootstrap owner creation is enabled outside development.");

        if (string.IsNullOrWhiteSpace(bootstrap.OwnerEmail))
            failures.Add("BootstrapOwner:OwnerEmail is required when bootstrap owner creation is enabled outside development.");

        if (string.IsNullOrWhiteSpace(bootstrap.OwnerDisplayName))
            failures.Add("BootstrapOwner:OwnerDisplayName is required when bootstrap owner creation is enabled outside development.");

        if (BootstrapOwnerPasswordPolicy.Validate(bootstrap.OwnerInitialPassword) is { } passwordFailure)
            failures.Add($"{passwordFailure} outside development.");

        return failures;
    }

    private static IReadOnlyList<string> ValidateMonitoring(MonitoringConfig monitoring)
    {
        if (!monitoring.RequireInProduction)
            return [];

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(monitoring.ErrorTrackingProvider))
            failures.Add("Monitoring:ErrorTrackingProvider must identify the production error-tracking provider outside development.");

        if (string.IsNullOrWhiteSpace(monitoring.ErrorTrackingDsn))
        {
            failures.Add("Monitoring:ErrorTrackingDsn must be configured outside development so unhandled production errors are routed to operators.");
        }
        else if (!Uri.TryCreate(monitoring.ErrorTrackingDsn, UriKind.Absolute, out var dsn) || dsn.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Monitoring:ErrorTrackingDsn must be an absolute HTTPS DSN outside development.");
        }

        if (!monitoring.StructuredJsonConsole)
            failures.Add("Monitoring:StructuredJsonConsole must be true outside development so logs can be indexed with structured fields.");

        if (!monitoring.IncludeCorrelationId)
            failures.Add("Monitoring:IncludeCorrelationId must be true outside development so errors can be traced from client response to server logs.");

        if (monitoring.TracesSampleRate is < 0 or > 1)
            failures.Add("Monitoring:TracesSampleRate must be between 0 and 1.");

        return failures;
    }
}
