using System.Net;
using System.Security.Cryptography;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class PrivateServerModeTests
{
    private const string OwnerPassword = "Correct Horse Battery Staple 1!";

    [Theory]
    [InlineData("privateServer")]
    [InlineData("Private Server")]
    [InlineData("PRIVATEserver")]
    [InlineData("")]
    [InlineData(null)]
    public void DeploymentModeContract_RequiresExactSupportedValues(string? configuredMode)
    {
        var failures = DeploymentModeContract.Validate(
            configuredMode,
            Environments.Production,
            out var effectiveMode);

        Assert.Contains(failures, failure => failure.Contains("must be exactly", StringComparison.Ordinal));
        Assert.Equal(DeploymentMode.PublicProduction, effectiveMode);
    }

    [Theory]
    [InlineData("PrivateServer", "Development")]
    [InlineData("PrivateServer", "Staging")]
    [InlineData("PublicProduction", "Staging")]
    [InlineData("Development", "Production")]
    public void DeploymentModeContract_RequiresMatchingRuntimeEnvironment(string mode, string environment)
    {
        var failures = DeploymentModeContract.Validate(mode, environment, out _);

        Assert.Contains(failures, failure => failure.Contains("requires ASPNETCORE_ENVIRONMENT", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionSafety_AllowsOnlyBoundedPrivateServerRelaxations()
    {
        var service = CreatePrivateSafetyService();

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ComposeModesAndPrivateOperatorCommands_AreExplicitlyWired()
    {
        var root = RepositoryRoot();
        var development = File.ReadAllText(Path.Combine(root, "compose.yml"));
        var privateServer = File.ReadAllText(Path.Combine(root, "compose.private.yml"));
        var publicProduction = File.ReadAllText(Path.Combine(root, "compose.production.yml"));
        var program = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Program.cs"));

        Assert.Contains("Deployment__Mode: Development", development, StringComparison.Ordinal);
        Assert.Contains("Deployment__Mode: PrivateServer", privateServer, StringComparison.Ordinal);
        Assert.Contains("Deployment__Mode: PublicProduction", publicProduction, StringComparison.Ordinal);
        Assert.Contains("--private-initialize", privateServer, StringComparison.Ordinal);
        Assert.Contains("--private-owner-recovery", privateServer, StringComparison.Ordinal);
        Assert.Contains("PrivateOwnerRecovery__TenantSlug", privateServer, StringComparison.Ordinal);
        Assert.Contains("--private-initialize", program, StringComparison.Ordinal);
        Assert.Contains("--private-owner-recovery", program, StringComparison.Ordinal);
        Assert.Contains("BootstrapOwner__Enabled: \"false\"", privateServer, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateServerRecoveryAndLocalAcceptance_AreExplicitAndReleasePackaged()
    {
        var root = RepositoryRoot();
        var module = File.ReadAllText(Path.Combine(root, "scripts", "PrivateServer", "PrivateServer.psm1"));
        var dispatcher = File.ReadAllText(Path.Combine(root, "scripts", "private-server.ps1"));
        var builder = File.ReadAllText(Path.Combine(root, "scripts", "build-private-server-release.ps1"));
        var verifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-private-server-release.ps1"));
        var guide = File.ReadAllText(Path.Combine(root, "Docs", "deployment", "private-server.md"));
        var readiness = File.ReadAllText(Path.Combine(root, "Docs", "deployment", "LOCAL_WINDOWS_READINESS.md"));

        Assert.Contains("export-recovery-key", module, StringComparison.Ordinal);
        Assert.Contains("recover-host", module, StringComparison.Ordinal);
        Assert.Contains("reboot-check", module, StringComparison.Ordinal);
        Assert.Contains("local-check", module, StringComparison.Ordinal);
        Assert.Contains("RecoveryAuthenticationKeyFile", dispatcher, StringComparison.Ordinal);
        Assert.Contains("Assert-FbBackupAuthenticationWithKey", module, StringComparison.Ordinal);
        Assert.Contains("Read important-table fingerprints from the recovered database", module, StringComparison.Ordinal);
        Assert.Contains("The host has not rebooted since this check was prepared", module, StringComparison.Ordinal);
        Assert.Contains("scripts/smoke-production.ps1", builder, StringComparison.Ordinal);
        Assert.Contains("scripts/smoke-production.ps1", verifier, StringComparison.Ordinal);
        Assert.Contains("The coding path is implemented, but it has not yet passed", guide, StringComparison.Ordinal);
        Assert.Contains("Rule for awarding 1,000/1,000", readiness, StringComparison.Ordinal);
        Assert.Contains("qualified-accountant", readiness, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UbuntuPrivateServerProfile_IsExplicitlyPackagedAndAcceptanceSeparated()
    {
        var root = RepositoryRoot();
        var module = File.ReadAllText(Path.Combine(root, "scripts", "PrivateServer", "PrivateServer.psm1"));
        var launcher = File.ReadAllText(Path.Combine(root, "filingbridge"));
        var hostVerifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-linux-private-host.sh"));
        var builder = File.ReadAllText(Path.Combine(root, "scripts", "build-private-server-release.ps1"));
        var guide = File.ReadAllText(Path.Combine(root, "Docs", "deployment", "private-server-linux.md"));
        var cloud = File.ReadAllText(Path.Combine(root, "Docs", "deployment", "GOOGLE_CLOUD_PRIVATE_SERVER.md"));
        var readiness = File.ReadAllText(Path.Combine(root, "Docs", "deployment", "LINUX_CLOUD_READINESS.md"));

        Assert.Contains("ubuntu-x64", module, StringComparison.Ordinal);
        Assert.Contains("Ubuntu 24.04 LTS x86-64", module, StringComparison.Ordinal);
        Assert.Contains("/proc/sys/kernel/random/boot_id", module, StringComparison.Ordinal);
        Assert.Contains("systemctl", module, StringComparison.Ordinal);
        Assert.Contains("persistently enabled", module, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled-runtime", module, StringComparison.Ordinal);
        Assert.Contains("exec pwsh", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("-NonInteractive", launcher, StringComparison.Ordinal);
        Assert.Contains("docker-boot-enabled", hostVerifier, StringComparison.Ordinal);
        Assert.Contains("grep -qx enabled", hostVerifier, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled-runtime", hostVerifier, StringComparison.Ordinal);
        Assert.Contains("compose_at_least_2_20", hostVerifier, StringComparison.Ordinal);
        Assert.DoesNotContain("^[2-9][0-9]*", hostVerifier, StringComparison.Ordinal);
        Assert.Contains("ContainerMountedSecrets", module, StringComparison.Ordinal);
        Assert.Contains("elseif ($ContainerReadable) { \"644\" }", module, StringComparison.Ordinal);
        Assert.Contains("filingbridge", builder, StringComparison.Ordinal);
        Assert.Contains("private-server-linux.md", builder, StringComparison.Ordinal);
        Assert.Contains("never Funnel", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("e2-standard-2", cloud, StringComparison.Ordinal);
        Assert.Contains("no application ports are exposed publicly", cloud, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Implementation alone receives no live-acceptance marks", readiness, StringComparison.Ordinal);
        Assert.Contains("independent 600/1,000", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSafety_DoesNotApplyPrivateRelaxationsToPublicOrIncorrectlyCasedMode()
    {
        var publicService = CreatePrivateSafetyService(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = DeploymentModeContract.PublicProduction
        });
        var incorrectCase = CreatePrivateSafetyService(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = "privateserver"
        });

        Assert.Contains(publicService.Validate(), failure => failure.Contains("certificate-verified TLS", StringComparison.Ordinal));
        Assert.Contains(publicService.Validate(), failure => failure.Contains("AllowInsecureDatabaseConnection must be false", StringComparison.Ordinal));
        Assert.Contains(incorrectCase.Validate(), failure => failure.Contains("must be exactly", StringComparison.Ordinal));
        Assert.Contains(incorrectCase.Validate(), failure => failure.Contains("certificate-verified TLS", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionSafety_RejectsOutboundDeadlineProviderInPrivateServerMode()
    {
        var service = CreatePrivateSafetyService(
            deadlineDelivery: new DeadlineDeliveryConfig
            {
                RequireInProduction = false,
                Enabled = true,
                ProviderEndpoint = "https://provider.example.invalid/deadlines",
                ProviderToken = new string('x', 48)
            });

        var failures = service.Validate();

        Assert.Contains(failures, failure => failure.Contains("Enabled must be false", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("ProviderEndpoint must be blank", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("ProviderToken must be blank", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://example.ts.net:3500")]
    [InlineData("https://example.com")]
    [InlineData("https://machine.tailnet.ts.net:444")]
    [InlineData("https://machine.tailnet.ts.net/path")]
    public void ProductionSafety_RejectsPrivateOriginsOutsideExactLoopbackOrTailnetContract(string origin)
    {
        var service = CreatePrivateSafetyService(new Dictionary<string, string?>
        {
            ["AllowedOrigins:0"] = origin,
            ["AllowedOrigins:1"] = null
        });

        Assert.Contains(service.Validate(), failure => failure.Contains("Private Server AllowedOrigins", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("*.ts.net")]
    [InlineData("machine.tailnet.ts.net:443")]
    [InlineData("machine.tailnet.ts.net/path")]
    public void ProductionSafety_RejectsWildcardOrNonHostPrivateAllowedHosts(string host)
    {
        var service = CreatePrivateSafetyService(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = $"localhost;127.0.0.1;{host}"
        });

        Assert.Contains(service.Validate(), failure => failure.Contains("Private Server AllowedHosts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrivateInitialization_CreatesOnlyTenantAndOwner_ThenRefusesExistingState()
    {
        await using var db = CreateDb();
        var deployment = new DeploymentConfig
        {
            Mode = DeploymentModeContract.PrivateServer,
            InstallationId = Guid.NewGuid().ToString()
        };
        var service = new PrivateInitializationService(
            db,
            new FileBackedConfigurationProvenance(["PrivateInitialization:OwnerInitialPassword"]),
            Options.Create(deployment),
            Options.Create(new PrivateInitializationConfig
            {
                TenantName = "Example Charity",
                TenantSlug = "example-charity",
                OwnerEmail = "OWNER@example.ie",
                OwnerDisplayName = "Named Owner",
                OwnerInitialPassword = OwnerPassword
            }),
            new FixedPasswordSafetyService(PasswordSafetyStatus.Accepted),
            new FixedTimeProvider());

        var created = await service.InitializeAsync();

        Assert.Equal("example-charity", created.TenantSlug);
        Assert.Equal("owner@example.ie", created.OwnerEmail);
        Assert.Equal(1, await db.Tenants.CountAsync());
        var owner = Assert.Single(await db.UserAccounts.Include(user => user.Tenant).ToListAsync());
        Assert.Equal("Owner", owner.Role);
        Assert.True(owner.IsActive);
        Assert.True(owner.MustChangePassword);
        Assert.Empty(await db.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.True(new Pbkdf2PasswordVerifier().Verify(OwnerPassword, owner));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync());
        Assert.Contains("empty database", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.Tenants.CountAsync());
        Assert.Equal(1, await db.UserAccounts.CountAsync());
        Assert.Empty(await db.Companies.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task PrivateInitialization_RequiresFileBackedOwnerPassword()
    {
        await using var db = CreateDb();
        var service = new PrivateInitializationService(
            db,
            new FileBackedConfigurationProvenance([]),
            Options.Create(new DeploymentConfig { Mode = DeploymentModeContract.PrivateServer }),
            Options.Create(new PrivateInitializationConfig
            {
                TenantName = "Example Charity",
                TenantSlug = "example-charity",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Named Owner",
                OwnerInitialPassword = OwnerPassword
            }),
            new FixedPasswordSafetyService(PasswordSafetyStatus.Accepted),
            new FixedTimeProvider());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync());

        Assert.Contains("_FILE", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Tenants.ToListAsync());
    }

    [Fact]
    public async Task PrivateOwnerRecovery_RevokesAllIdentityStateAndPersistsOnlyHashedResetToken()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc);
        var tenant = new Tenant { Name = "Example Charity", Slug = "example-charity" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var hashed = PasswordHasher.HashPassword(OwnerPassword);
        var owner = new UserAccount
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = "owner@example.ie",
            DisplayName = "Named Owner",
            Role = "Owner",
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            IsActive = true,
            FailedLoginCount = 5,
            LockedUntilUtc = now.AddHours(1),
            PasswordLastChangedAt = now
        };
        db.UserAccounts.Add(owner);
        await db.SaveChangesAsync();
        var otherTenant = new Tenant { Name = "Other Charity", Slug = "other-charity" };
        db.Tenants.Add(otherTenant);
        await db.SaveChangesAsync();
        var otherOwner = new UserAccount
        {
            TenantId = otherTenant.Id,
            Tenant = otherTenant,
            Email = owner.Email,
            DisplayName = "Other Named Owner",
            Role = "Owner",
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            IsActive = true,
            PasswordLastChangedAt = now
        };
        db.UserAccounts.Add(otherOwner);
        await db.SaveChangesAsync();
        var sessionOptions = Options.Create(new AuthSessionConfig
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
        });
        var mfa = new MfaSecurityService(sessionOptions);
        db.UserActionTokens.Add(new UserActionToken
        {
            TenantId = tenant.Id,
            UserId = owner.Id,
            CreatedByUserId = owner.Id,
            Purpose = UserActionPurposes.PasswordReset,
            TokenHash = mfa.HashOpaqueToken("old-token"),
            ExpiresAtUtc = now.AddHours(1)
        });
        db.UserMfaCredentials.Add(new UserMfaCredential
        {
            TenantId = tenant.Id,
            UserId = owner.Id,
            EncryptedSecret = "unused-during-recovery",
            EnabledAtUtc = now
        });
        db.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCode
        {
            TenantId = tenant.Id,
            UserId = owner.Id,
            CodeHash = new string('a', 64)
        });
        db.UserMfaChallenges.Add(new UserMfaChallenge
        {
            TenantId = tenant.Id,
            UserId = owner.Id,
            Purpose = MfaChallengePurposes.Login,
            TokenHash = mfa.HashOpaqueToken("old-challenge"),
            SessionVersion = owner.SessionVersion,
            ExpiresAtUtc = now.AddMinutes(5)
        });
        await db.SaveChangesAsync();
        var installationId = Guid.NewGuid();
        var originalSessionVersion = owner.SessionVersion;
        var service = new PrivateOwnerRecoveryService(
            db,
            Options.Create(new DeploymentConfig
            {
                Mode = DeploymentModeContract.PrivateServer,
                InstallationId = installationId.ToString()
            }),
            Options.Create(new PrivateOwnerRecoveryConfig
            {
                ConfirmInstallationId = installationId.ToString(),
                TenantSlug = tenant.Slug,
                OwnerEmail = owner.Email,
                ConfirmOwnerEmail = owner.Email,
                ConfirmationPhrase = PrivateOwnerRecoveryConfig.RequiredConfirmationPhrase
            }),
            mfa,
            new AuditService(db),
            new FixedTimeProvider());

        var result = await service.BeginAsync();

        Assert.Equal(owner.Id, result.OwnerUserId);
        Assert.Equal(originalSessionVersion + 1, owner.SessionVersion);
        Assert.Equal(1, otherOwner.SessionVersion);
        Assert.False(otherOwner.MustChangePassword);
        Assert.True(owner.MustChangePassword);
        Assert.Equal(0, owner.FailedLoginCount);
        Assert.Null(owner.LockedUntilUtc);
        Assert.Empty(await db.UserMfaCredentials.ToListAsync());
        Assert.Empty(await db.UserMfaRecoveryCodes.ToListAsync());
        Assert.All(await db.UserMfaChallenges.ToListAsync(), challenge => Assert.NotNull(challenge.RevokedAtUtc));
        var tokens = await db.UserActionTokens.OrderBy(token => token.Id).ToListAsync();
        Assert.NotNull(tokens[0].RevokedAtUtc);
        Assert.Equal(mfa.HashOpaqueToken(result.ResetToken), tokens[1].TokenHash);
        Assert.DoesNotContain(result.ResetToken, tokens[1].TokenHash, StringComparison.Ordinal);
        var lifecycle = Assert.Single(await db.UserLifecycleEvents.ToListAsync());
        Assert.Equal(UserLifecycleEventTypes.PrivateOwnerRecoveryStarted, lifecycle.EventType);
        Assert.DoesNotContain(result.ResetToken, lifecycle.DetailsJson, StringComparison.Ordinal);
        var audit = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal(UserLifecycleEventTypes.PrivateOwnerRecoveryStarted, audit.Action);
        Assert.DoesNotContain(result.ResetToken, audit.NewValueJson ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordSafety_FailOpenStillEnforcesLocalDenyList()
    {
        var options = Options.Create(new IdentitySecurityConfig
        {
            BreachedPasswordCheckEnabled = true,
            BreachedPasswordFailClosed = false
        });
        var service = new PwnedPasswordSafetyService(
            new HttpClient(new ThrowingHttpHandler()),
            options);

        Assert.Equal(
            PasswordSafetyStatus.Accepted,
            (await service.CheckAsync("A unique unavailable remote check password 9!")).Status);
        Assert.Equal(
            PasswordSafetyStatus.Breached,
            (await service.CheckAsync("password123!")).Status);
    }

    [Fact]
    public async Task LockedAccountStillPerformsPasswordVerificationWork()
    {
        await using var db = CreateDb();
        var tenant = new Tenant { Name = "Example", Slug = "example" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var hash = PasswordHasher.HashPassword(OwnerPassword);
        db.UserAccounts.Add(new UserAccount
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = "locked@example.ie",
            DisplayName = "Locked User",
            Role = "Client",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            LockedUntilUtc = DateTime.UtcNow.AddHours(1),
            FailedLoginCount = 5
        });
        await db.SaveChangesAsync();
        var verifier = new CountingPasswordVerifier();
        var auth = new AuthService(
            db,
            Options.Create(new AuthSessionConfig
            {
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            }),
            new ModeEnvironment(Environments.Development),
            verifier);

        var result = await auth.LoginAsync(tenant.Slug, "locked@example.ie", "wrong-password");

        Assert.False(result.Succeeded);
        Assert.True(result.AccountLocked);
        Assert.Equal(1, verifier.VerifyCalls);
    }

    private static ProductionSafetyService CreatePrivateSafetyService(
        IReadOnlyDictionary<string, string?>? overrides = null,
        DeadlineDeliveryConfig? deadlineDelivery = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = DeploymentModeContract.PrivateServer,
            ["Deployment:InstallationId"] = Guid.NewGuid().ToString(),
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=accounts;Username=accounts;Password=a-generated-password;SSL Mode=Disable",
            ["AllowedOrigins:0"] = "http://localhost:3500",
            ["AllowedOrigins:1"] = "https://machine.tailnet.ts.net",
            ["AllowedHosts"] = "localhost;127.0.0.1;machine.tailnet.ts.net"
        };
        foreach (var pair in overrides ?? new Dictionary<string, string?>())
        {
            if (pair.Value is null) values.Remove(pair.Key);
            else values[pair.Key] = pair.Value;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = new ModeEnvironment(Environments.Production);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return new ProductionSafetyService(
            environment,
            configuration,
            Options.Create(new DatabaseStartupConfig
            {
                AllowInsecureDatabaseConnection = true
            }),
            Options.Create(new AuthSessionConfig
            {
                SigningKey = key,
                SecureCookiesInProduction = true,
                RequirePrivilegedMfa = true
            }),
            new ApiAccessService(Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                RequireInProduction = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Private frontend",
                        KeyHash = ApiAccessService.HashKey("a-generated-private-frontend-key")
                    }
                ]
            }), environment),
            Options.Create(new AuditIntegrityConfig
            {
                ActiveKeyId = "private-key-1",
                SigningKeys =
                [
                    new AuditIntegritySigningKeyConfig
                    {
                        KeyId = "private-key-1",
                        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
                    }
                ]
            }),
            Options.Create(new BootstrapOwnerConfig { Enabled = false }),
            Options.Create(new MonitoringConfig
            {
                RequireInProduction = false,
                ErrorTrackingProvider = "LocalStructuredLogs",
                ErrorTrackingDsn = "",
                StructuredJsonConsole = true,
                IncludeCorrelationId = true,
                StructuredLogRetentionDays = 30
            }),
            Options.Create(deadlineDelivery ?? new DeadlineDeliveryConfig
            {
                RequireInProduction = false,
                Enabled = false
            }),
            Options.Create(new PlatformMetricsConfig()),
            Options.Create(new DatabaseTenantIsolationConfig
            {
                Required = true,
                ApplicationGroupRole = "accounts_api_rls",
                ApplicationLoginRole = "accounts_api",
                ContextSigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            }),
            Options.Create(new IdentitySecurityConfig
            {
                RequireInProduction = true,
                BreachedPasswordCheckEnabled = true,
                BreachedPasswordFailClosed = false,
                IdentityHmacKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                ActiveMfaEncryptionKeyId = "private-mfa-1",
                MfaEncryptionKeys =
                [
                    new MfaEncryptionKeyConfig
                    {
                        KeyId = "private-mfa-1",
                        EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
                    }
                ]
            }));
    }

    private static AccountsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFilePath) })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var directory = new DirectoryInfo(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yml")))
                directory = directory.Parent;
            if (directory is not null) return directory.FullName;
        }
        throw new InvalidOperationException("Repository root could not be found.");
    }

    private sealed class FixedPasswordSafetyService(PasswordSafetyStatus status) : IPasswordSafetyService
    {
        public Task<PasswordSafetyDecision> CheckAsync(
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasswordSafetyDecision(status));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class CountingPasswordVerifier : IPasswordVerifier
    {
        public int VerifyCalls { get; private set; }

        public bool Verify(string password, UserAccount? user)
        {
            VerifyCalls++;
            return false;
        }
    }

    private sealed class ModeEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
