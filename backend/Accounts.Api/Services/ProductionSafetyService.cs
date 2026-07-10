using Accounts.Api.Rules;
using Accounts.Api.Data;
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
    IOptions<MonitoringConfig>? monitoring = null,
    IOptions<DeadlineDeliveryConfig>? deadlineDelivery = null,
    IOptions<PlatformMetricsConfig>? platformMetrics = null,
    IOptions<DatabaseTenantIsolationConfig>? databaseTenantIsolation = null,
    IOptions<IdentitySecurityConfig>? identitySecurity = null)
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

        if (dbStartup.AllowInsecureDatabaseConnection)
        {
            failures.Add("DatabaseStartup:AllowInsecureDatabaseConnection must be false outside development; production database transport cannot opt out of certificate verification.");
        }

        // crypto-tls-to-db: production must authenticate PostgreSQL against a deployment CA as well
        // as encrypting the connection. Encryption without server identity validation is insufficient.
        if (!string.IsNullOrWhiteSpace(connectionString) && !DatabaseConnectionUsesVerifiedTls(connectionString))
        {
            failures.Add("DefaultConnection must use certificate-verified TLS outside development (SSL Mode=VerifyFull, a Root Certificate path, and Trust Server Certificate=false).");
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

        if (session.IdleTimeoutMinutes is < 5 or > 120)
            failures.Add("AuthSession:IdleTimeoutMinutes must be between 5 and 120 minutes outside development.");

        if (session.AbsoluteLifetimeMinutes is < 30 or > 1440
            || session.AbsoluteLifetimeMinutes < session.IdleTimeoutMinutes)
            failures.Add("AuthSession:AbsoluteLifetimeMinutes must be between 30 and 1440 minutes and at least the idle timeout outside development.");

        if (session.RecentMfaMinutes is < 1 or > 15)
            failures.Add("AuthSession:RecentMfaMinutes must be between 1 and 15 minutes outside development.");

        if (session.MfaChallengeMinutes is < 2 or > 10)
            failures.Add("AuthSession:MfaChallengeMinutes must be between 2 and 10 minutes outside development.");

        if (!session.RequirePrivilegedMfa)
            failures.Add("AuthSession:RequirePrivilegedMfa must be true outside development.");

        if (!session.SecureCookiesInProduction)
            failures.Add("AuthSession:SecureCookiesInProduction must be true outside development.");

        failures.AddRange(ValidateBootstrapOwner(bootstrapOwner?.Value ?? new BootstrapOwnerConfig()));
        failures.AddRange(AuditIntegrityCheckpointService.ValidateConfiguration(
            auditIntegrity?.Value ?? new AuditIntegrityConfig()));
        failures.AddRange(apiAccess.ValidateConfiguration());
        failures.AddRange(ValidateMonitoringConfiguration(monitoring?.Value ?? new MonitoringConfig()));
        failures.AddRange(PlatformOperationsConfigurationValidator.Validate(
            deadlineDelivery?.Value ?? new DeadlineDeliveryConfig(),
            platformMetrics?.Value ?? new PlatformMetricsConfig()));
        var tenantIsolation = databaseTenantIsolation?.Value ?? new DatabaseTenantIsolationConfig();
        if (!tenantIsolation.Required)
            failures.Add("DatabaseTenantIsolation:Required must be true outside development.");
        else if (!DatabaseTenantIsolationConfig.IsValid(tenantIsolation))
            failures.Add("DatabaseTenantIsolation requires the fixed application group, a distinct safe API login role, and a generated context key of at least 32 bytes.");
        failures.AddRange(ValidateIdentitySecurity(identitySecurity?.Value ?? new IdentitySecurityConfig()));

        return failures;
    }

    public void ThrowIfUnsafe()
    {
        var failures = Validate();
        if (failures.Count > 0)
            throw new InvalidOperationException("Unsafe production configuration: " + string.Join(" ", failures));
    }

    public static IReadOnlyList<string> ValidateIdentitySecurity(IdentitySecurityConfig config)
    {
        var failures = new List<string>();
        if (!config.RequireInProduction)
            failures.Add("IdentitySecurity:RequireInProduction must be true outside development.");
        if (!config.BreachedPasswordCheckEnabled || !config.BreachedPasswordFailClosed)
            failures.Add("IdentitySecurity breached-password checking must be enabled and fail closed outside development.");
        if (!Uri.TryCreate(config.PwnedPasswordsRangeBaseUrl, UriKind.Absolute, out var rangeUri)
            || rangeUri.Scheme != Uri.UriSchemeHttps)
            failures.Add("IdentitySecurity:PwnedPasswordsRangeBaseUrl must be an absolute HTTPS endpoint.");
        if (config.PwnedPasswordsTimeoutSeconds is < 1 or > 15
            || config.PwnedPasswordsMaximumResponseBytes is < 100_000 or > 5_000_000)
            failures.Add("IdentitySecurity Pwned Passwords timeout/response bounds are invalid.");
        if (!IdentitySecurityConfig.HasValidProductionKeys(config))
            failures.Add("IdentitySecurity requires a separate HMAC key and a valid versioned MFA encryption key ring with an active key.");
        return failures;
    }

    // crypto-tls-to-db: a connection requires TLS only when its SSL mode is Require / VerifyCA /
    // VerifyFull. A missing or weaker mode (Disable/Allow/Prefer) does not guarantee an encrypted link.
    private static bool DatabaseConnectionUsesVerifiedTls(string connectionString)
    {
        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            return builder.SslMode == Npgsql.SslMode.VerifyFull
                && !string.IsNullOrWhiteSpace(builder.RootCertificate)
                && !System.Text.RegularExpressions.Regex.IsMatch(
                    connectionString,
                    @"(?:^|;)\s*Trust\s+Server\s+Certificate\s*=\s*(?:true|yes|1)\s*(?:;|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        catch
        {
            // Unparseable connection string: fail closed.
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

    public static IReadOnlyList<string> ValidateMonitoringConfiguration(MonitoringConfig monitoring)
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

        if (monitoring.StructuredLogRetentionDays is < 30 or > 3650)
            failures.Add("Monitoring:StructuredLogRetentionDays must be between 30 and 3650 days.");

        if (monitoring.ErrorEventRetentionDays is < 30 or > 3650)
            failures.Add("Monitoring:ErrorEventRetentionDays must be between 30 and 3650 days.");

        if (monitoring.AlertAcknowledgementMinutes is < 1 or > 60)
            failures.Add("Monitoring:AlertAcknowledgementMinutes must be between 1 and 60 minutes.");

        if (monitoring.EscalationMinutes <= monitoring.AlertAcknowledgementMinutes
            || monitoring.EscalationMinutes > 240)
        {
            failures.Add("Monitoring:EscalationMinutes must be greater than the acknowledgement target and no more than 240 minutes.");
        }

        if (string.IsNullOrWhiteSpace(monitoring.OnCallOwner))
            failures.Add("Monitoring:OnCallOwner must name the accountable production on-call owner.");

        if (string.IsNullOrWhiteSpace(monitoring.AlertRoute))
            failures.Add("Monitoring:AlertRoute must identify the configured operator notification route.");

        if (!string.Equals(
                monitoring.IncidentRunbookPath,
                "Docs/operations/monitoring-incident-response.md",
                StringComparison.Ordinal))
        {
            failures.Add("Monitoring:IncidentRunbookPath must reference the controlled monitoring incident runbook.");
        }

        return failures;
    }
}
