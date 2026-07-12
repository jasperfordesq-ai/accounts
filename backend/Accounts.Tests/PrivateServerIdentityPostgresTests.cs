using System.Security.Cryptography;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class PrivateServerIdentityPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const string OwnerPassword = "Correct Horse Battery Staple 91!";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "private_identity_" + Guid.NewGuid().ToString("N");
    private readonly FixedTimeProvider clock = new();
    private DbContextOptions<AccountsDbContext>? options;
    private string? scopedConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var command = admin.CreateCommand())
        {
            command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await command.ExecuteNonQueryAsync();
        }

        scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        }.ConnectionString;
        options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(scopedConnectionString)
            .Options;

        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Lockout_SetBasedUpdateRevokesSessionsAndOutstandingMfaChallenges()
    {
        var session = SessionConfig();
        int userId;
        int originalSessionVersion;
        string cookie;
        string tenantSlug;

        await using (var seed = CreateDb())
        {
            var tenant = NewTenant("lockout");
            tenantSlug = tenant.Slug;
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            var user = NewUser(tenant, "lockout-user@example.invalid", "Client");
            seed.UserAccounts.Add(user);
            await seed.SaveChangesAsync();
            seed.UserMfaChallenges.Add(new UserMfaChallenge
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Purpose = MfaChallengePurposes.Login,
                TokenHash = RandomHash(),
                SessionVersion = user.SessionVersion,
                CreatedAtUtc = clock.UtcNow,
                ExpiresAtUtc = clock.UtcNow.AddMinutes(10)
            });
            await seed.SaveChangesAsync();

            var auth = CreateAuth(seed, session);
            var principal = await auth.GetPrincipalAsync(user.Id)
                ?? throw new InvalidOperationException("Seeded user principal was not available.");
            cookie = auth.CreateSessionCookieValue(
                principal with { CsrfToken = "before-postgres-lockout" },
                clock.GetUtcNow());
            userId = user.Id;
            originalSessionVersion = user.SessionVersion;
        }

        LoginResult lastFailure = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            // A new context mirrors the request-scoped production lifetime and ensures every
            // update observes the row written by the preceding failed login.
            await using var requestDb = CreateDb();
            lastFailure = await CreateAuth(requestDb, session)
                .LoginAsync(tenantSlug, "lockout-user@example.invalid", "Wrong Password Value 91!");
        }

        Assert.False(lastFailure.Succeeded);
        Assert.True(lastFailure.AccountLocked);
        Assert.True(lastFailure.LockoutStarted);
        Assert.Equal(5, lastFailure.FailedLoginCount);

        await using var verify = CreateDb();
        var stored = await verify.UserAccounts.AsNoTracking().SingleAsync(user => user.Id == userId);
        var challenge = await verify.UserMfaChallenges.AsNoTracking().SingleAsync();
        Assert.Equal(originalSessionVersion + 1, stored.SessionVersion);
        Assert.Equal(5, stored.FailedLoginCount);
        Assert.Equal(clock.UtcNow.AddMinutes(15), stored.LockedUntilUtc);
        Assert.Equal(clock.UtcNow, challenge.RevokedAtUtc);
        Assert.Null(await CreateAuth(verify, session).ReadSessionAsync(
            cookie,
            clock.GetUtcNow().AddMinutes(1)));
    }

    [PostgresFact]
    public async Task UserLifecycle_SetBasedUnlockRevokesSessionsAndPersistedMfaChallenges()
    {
        var session = SessionConfig();
        int targetId;
        int originalSessionVersion;
        AuthenticatedUser actor;
        string cookie;

        await using (var seed = CreateDb())
        {
            var tenant = NewTenant("unlock");
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            var owner = NewUser(tenant, "unlock-owner@example.invalid", "Owner");
            var target = NewUser(tenant, "unlock-target@example.invalid", "Client");
            target.FailedLoginCount = 5;
            target.LastFailedLoginAt = clock.UtcNow;
            target.LockedUntilUtc = clock.UtcNow.AddMinutes(15);
            seed.UserAccounts.AddRange(owner, target);
            await seed.SaveChangesAsync();
            seed.UserMfaChallenges.Add(new UserMfaChallenge
            {
                TenantId = tenant.Id,
                UserId = target.Id,
                Purpose = MfaChallengePurposes.Login,
                TokenHash = RandomHash(),
                SessionVersion = target.SessionVersion,
                CreatedAtUtc = clock.UtcNow,
                ExpiresAtUtc = clock.UtcNow.AddMinutes(10)
            });
            await seed.SaveChangesAsync();

            var auth = CreateAuth(seed, session);
            actor = await auth.GetPrincipalAsync(owner.Id)
                ?? throw new InvalidOperationException("Seeded Owner principal was not available.");
            var targetPrincipal = await auth.GetPrincipalAsync(target.Id)
                ?? throw new InvalidOperationException("Seeded target principal was not available.");
            cookie = auth.CreateSessionCookieValue(
                targetPrincipal with { CsrfToken = "before-postgres-unlock" },
                clock.GetUtcNow());
            targetId = target.Id;
            originalSessionVersion = target.SessionVersion;
        }

        await using (var requestDb = CreateDb())
        {
            var lifecycle = new UserLifecycleService(
                requestDb,
                new MfaSecurityService(Options.Create(session)),
                new AuditService(requestDb),
                clock);
            await lifecycle.UnlockAsync(actor, targetId);
        }

        await using var verify = CreateDb();
        var stored = await verify.UserAccounts.AsNoTracking().SingleAsync(user => user.Id == targetId);
        var challenge = await verify.UserMfaChallenges.AsNoTracking().SingleAsync();
        Assert.Equal(originalSessionVersion + 1, stored.SessionVersion);
        Assert.Equal(0, stored.FailedLoginCount);
        Assert.Null(stored.LastFailedLoginAt);
        Assert.Null(stored.LockedUntilUtc);
        Assert.Equal(clock.UtcNow, challenge.RevokedAtUtc);
        Assert.Single(await verify.UserLifecycleEvents.AsNoTracking().ToListAsync());
        Assert.Single(await verify.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Null(await CreateAuth(verify, session).ReadSessionAsync(
            cookie,
            clock.GetUtcNow().AddMinutes(1)));
    }

    [PostgresFact]
    public async Task PrivateInitializerAndOwnerRecovery_AreAtomicAndHonorIdentityTriggers()
    {
        var deployment = new DeploymentConfig
        {
            Mode = DeploymentModeContract.PrivateServer,
            InstallationId = Guid.NewGuid().ToString()
        };
        var initialization = new PrivateInitializationConfig
        {
            TenantName = "Private PostgreSQL Firm",
            TenantSlug = "private-postgres-firm",
            OwnerEmail = "private-owner@example.invalid",
            OwnerDisplayName = "Private PostgreSQL Owner",
            OwnerInitialPassword = OwnerPassword
        };
        var provenance = new FileBackedConfigurationProvenance(
            ["PrivateInitialization:OwnerInitialPassword"]);

        await ExecuteSqlAsync(
            """
            CREATE FUNCTION reject_private_owner_insert()
            RETURNS trigger AS $body$
            BEGIN
                RAISE EXCEPTION 'synthetic private initializer failure';
            END
            $body$ LANGUAGE plpgsql;
            CREATE TRIGGER "TR_test_reject_private_owner_insert"
                BEFORE INSERT ON user_accounts
                FOR EACH ROW EXECUTE FUNCTION reject_private_owner_insert();
            """);

        await using (var failingInitializationDb = CreateDb())
        {
            var service = CreateInitializationService(
                failingInitializationDb,
                provenance,
                deployment,
                initialization);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.InitializeAsync());
        }

        await using (var verifyRollback = CreateDb())
        {
            Assert.Empty(await verifyRollback.Tenants.AsNoTracking().ToListAsync());
            Assert.Empty(await verifyRollback.UserAccounts.AsNoTracking().ToListAsync());
        }

        await ExecuteSqlAsync(
            """
            DROP TRIGGER "TR_test_reject_private_owner_insert" ON user_accounts;
            DROP FUNCTION reject_private_owner_insert();
            """);

        PrivateInitializationResult initialized;
        await using (var initializationDb = CreateDb())
        {
            var service = CreateInitializationService(
                initializationDb,
                provenance,
                deployment,
                initialization);
            initialized = await service.InitializeAsync();
            var repeat = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeAsync());
            Assert.Contains("empty database", repeat.Message, StringComparison.OrdinalIgnoreCase);
        }

        var session = SessionConfig();
        var mfa = new MfaSecurityService(Options.Create(session));
        int originalSessionVersion;
        int otherOwnerId;
        int otherOwnerSessionVersion;
        await using (var seedRecovery = CreateDb())
        {
            var owner = await seedRecovery.UserAccounts.SingleAsync(
                user => user.Id == initialized.OwnerUserId);
            var otherTenant = NewTenant("recovery-other");
            seedRecovery.Tenants.Add(otherTenant);
            await seedRecovery.SaveChangesAsync();
            var otherOwner = NewUser(otherTenant, owner.Email, "Owner");
            seedRecovery.UserAccounts.Add(otherOwner);
            await seedRecovery.SaveChangesAsync();
            otherOwnerId = otherOwner.Id;
            otherOwnerSessionVersion = otherOwner.SessionVersion;
            owner.FailedLoginCount = 5;
            owner.LastFailedLoginAt = clock.UtcNow;
            owner.LockedUntilUtc = clock.UtcNow.AddMinutes(15);
            originalSessionVersion = owner.SessionVersion;
            seedRecovery.UserActionTokens.Add(new UserActionToken
            {
                TenantId = owner.TenantId,
                UserId = owner.Id,
                CreatedByUserId = owner.Id,
                Purpose = UserActionPurposes.PasswordReset,
                TokenHash = mfa.HashOpaqueToken("old-private-reset-token"),
                CreatedAtUtc = clock.UtcNow,
                ExpiresAtUtc = clock.UtcNow.AddHours(1)
            });
            seedRecovery.UserMfaCredentials.Add(new UserMfaCredential
            {
                TenantId = owner.TenantId,
                UserId = owner.Id,
                EncryptedSecret = "synthetic-private-recovery-envelope",
                CreatedAtUtc = clock.UtcNow,
                EnabledAtUtc = clock.UtcNow
            });
            seedRecovery.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCode
            {
                TenantId = owner.TenantId,
                UserId = owner.Id,
                CodeHash = RandomHash(),
                CreatedAtUtc = clock.UtcNow
            });
            seedRecovery.UserMfaChallenges.Add(new UserMfaChallenge
            {
                TenantId = owner.TenantId,
                UserId = owner.Id,
                Purpose = MfaChallengePurposes.Login,
                TokenHash = RandomHash(),
                SessionVersion = owner.SessionVersion,
                CreatedAtUtc = clock.UtcNow,
                ExpiresAtUtc = clock.UtcNow.AddMinutes(10)
            });
            await seedRecovery.SaveChangesAsync();
        }

        await ExecuteSqlAsync(
            """
            CREATE FUNCTION reject_private_recovery_audit()
            RETURNS trigger AS $body$
            BEGIN
                IF NEW."Action" = 'PrivateOwnerRecoveryStarted' THEN
                    RAISE EXCEPTION 'synthetic private recovery audit failure';
                END IF;
                RETURN NEW;
            END
            $body$ LANGUAGE plpgsql;
            CREATE TRIGGER "TR_test_reject_private_recovery_audit"
                BEFORE INSERT ON audit_logs
                FOR EACH ROW EXECUTE FUNCTION reject_private_recovery_audit();
            """);

        var recoveryOptions = RecoveryOptions(
            deployment,
            initialization.TenantSlug,
            initialization.OwnerEmail);
        await using (var failingRecoveryDb = CreateDb())
        {
            var service = CreateRecoveryService(
                failingRecoveryDb,
                deployment,
                recoveryOptions,
                mfa);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.BeginAsync());
        }

        await using (var verifyRecoveryRollback = CreateDb())
        {
            var owner = await verifyRecoveryRollback.UserAccounts.AsNoTracking()
                .SingleAsync(user => user.Id == initialized.OwnerUserId);
            Assert.Equal(originalSessionVersion, owner.SessionVersion);
            Assert.Equal(5, owner.FailedLoginCount);
            Assert.Equal(clock.UtcNow.AddMinutes(15), owner.LockedUntilUtc);
            Assert.Single(await verifyRecoveryRollback.UserActionTokens.AsNoTracking().ToListAsync());
            Assert.Null((await verifyRecoveryRollback.UserActionTokens.AsNoTracking().SingleAsync()).RevokedAtUtc);
            Assert.Single(await verifyRecoveryRollback.UserMfaCredentials.AsNoTracking().ToListAsync());
            Assert.Single(await verifyRecoveryRollback.UserMfaRecoveryCodes.AsNoTracking().ToListAsync());
            Assert.Null((await verifyRecoveryRollback.UserMfaChallenges.AsNoTracking().SingleAsync()).RevokedAtUtc);
            Assert.Empty(await verifyRecoveryRollback.UserLifecycleEvents.AsNoTracking().ToListAsync());
            Assert.Empty(await verifyRecoveryRollback.AuditLogs.AsNoTracking().ToListAsync());
        }

        await ExecuteSqlAsync(
            """
            DROP TRIGGER "TR_test_reject_private_recovery_audit" ON audit_logs;
            DROP FUNCTION reject_private_recovery_audit();
            """);

        PrivateOwnerRecoveryResult recovered;
        await using (var recoveryDb = CreateDb())
        {
            recovered = await CreateRecoveryService(
                recoveryDb,
                deployment,
                recoveryOptions,
                mfa).BeginAsync();
        }

        await using (var verifySuccess = CreateDb())
        {
            var recoveredOwner = await verifySuccess.UserAccounts.AsNoTracking()
                .SingleAsync(user => user.Id == initialized.OwnerUserId);
            var untouchedOtherOwner = await verifySuccess.UserAccounts.AsNoTracking()
                .SingleAsync(user => user.Id == otherOwnerId);
            Assert.Equal(originalSessionVersion + 1, recoveredOwner.SessionVersion);
            Assert.Equal(otherOwnerSessionVersion, untouchedOtherOwner.SessionVersion);
            Assert.False(untouchedOtherOwner.MustChangePassword);
            Assert.Equal(0, recoveredOwner.FailedLoginCount);
            Assert.Null(recoveredOwner.LastFailedLoginAt);
            Assert.Null(recoveredOwner.LockedUntilUtc);
            Assert.True(recoveredOwner.MustChangePassword);
            Assert.Empty(await verifySuccess.UserMfaCredentials.AsNoTracking().ToListAsync());
            Assert.Empty(await verifySuccess.UserMfaRecoveryCodes.AsNoTracking().ToListAsync());
            Assert.Equal(clock.UtcNow, (await verifySuccess.UserMfaChallenges.AsNoTracking().SingleAsync()).RevokedAtUtc);
            var resetTokens = await verifySuccess.UserActionTokens.AsNoTracking()
                .OrderBy(token => token.Id)
                .ToListAsync();
            Assert.Equal(2, resetTokens.Count);
            Assert.Equal(clock.UtcNow, resetTokens[0].RevokedAtUtc);
            Assert.Equal(mfa.HashOpaqueToken(recovered.ResetToken), resetTokens[1].TokenHash);
            Assert.DoesNotContain(recovered.ResetToken, resetTokens[1].TokenHash, StringComparison.Ordinal);
            Assert.Null(resetTokens[1].CreatedByUserId);
            Assert.Equal(IdentityActorKinds.PrivateServerHostOperator, resetTokens[1].CreatedByActorKind);
            var lifecycle = await verifySuccess.UserLifecycleEvents.AsNoTracking().SingleAsync();
            Assert.Null(lifecycle.ActorUserId);
            Assert.Equal(IdentityActorKinds.PrivateServerHostOperator, lifecycle.ActorKind);
            Assert.Equal(UserLifecycleEventTypes.PrivateOwnerRecoveryStarted, lifecycle.EventType);
            Assert.Contains("PrivateServerHostOperator", lifecycle.DetailsJson, StringComparison.Ordinal);
            var audit = await verifySuccess.AuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("operator:private-host", audit.UserId);
        }

        const string recoveredPassword = "Recovered Private Owner Password 92!";
        await using (var resetDb = CreateDb())
        {
            var lifecycle = new UserLifecycleService(
                resetDb,
                mfa,
                new AuditService(resetDb),
                clock,
                passwordSafety: new FixedPasswordSafetyService());
            await lifecycle.CompletePasswordResetAsync(
                new AcceptActionTokenInput(recovered.ResetToken, recoveredPassword));
        }

        await using var verifyResetCompletion = CreateDb();
        var completedOwner = await verifyResetCompletion.UserAccounts.AsNoTracking()
            .SingleAsync(user => user.Id == initialized.OwnerUserId);
        Assert.False(completedOwner.MustChangePassword);
        Assert.Equal(originalSessionVersion + 2, completedOwner.SessionVersion);
        var consumedRecoveryToken = await verifyResetCompletion.UserActionTokens.AsNoTracking()
            .SingleAsync(token => token.TokenHash == mfa.HashOpaqueToken(recovered.ResetToken));
        Assert.Equal(clock.UtcNow, consumedRecoveryToken.ConsumedAtUtc);
        var completedLifecycle = await verifyResetCompletion.UserLifecycleEvents.AsNoTracking()
            .SingleAsync(entry => entry.EventType == UserLifecycleEventTypes.PasswordResetCompleted);
        Assert.Equal(UserLifecycleEventTypes.PasswordResetCompleted, completedLifecycle.EventType);
        Assert.Null(completedLifecycle.ActorUserId);
        Assert.Equal(IdentityActorKinds.PrivateServerHostOperator, completedLifecycle.ActorKind);
        var completionAudit = await verifyResetCompletion.AuditLogs.AsNoTracking()
            .SingleAsync(entry => entry.Action == UserLifecycleEventTypes.PasswordResetCompleted);
        Assert.Equal("operator:private-host", completionAudit.UserId);
    }

    private AccountsDbContext CreateDb() => new(
        options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required."));

    private AuthService CreateAuth(AccountsDbContext db, AuthSessionConfig session) => new(
        db,
        Options.Create(session),
        new TestEnvironment(),
        new Pbkdf2PasswordVerifier(),
        clock);

    private PrivateInitializationService CreateInitializationService(
        AccountsDbContext db,
        FileBackedConfigurationProvenance configuration,
        DeploymentConfig deployment,
        PrivateInitializationConfig initialization) => new(
            db,
            configuration,
            Options.Create(deployment),
            Options.Create(initialization),
            new FixedPasswordSafetyService(),
            clock);

    private PrivateOwnerRecoveryService CreateRecoveryService(
        AccountsDbContext db,
        DeploymentConfig deployment,
        PrivateOwnerRecoveryConfig recovery,
        MfaSecurityService mfa) => new(
            db,
            Options.Create(deployment),
            Options.Create(recovery),
            mfa,
            new AuditService(db),
            clock);

    private static PrivateOwnerRecoveryConfig RecoveryOptions(
        DeploymentConfig deployment,
        string tenantSlug,
        string ownerEmail) => new()
        {
            ConfirmInstallationId = deployment.InstallationId,
            TenantSlug = tenantSlug,
            OwnerEmail = ownerEmail,
            ConfirmOwnerEmail = ownerEmail,
            ConfirmationPhrase = PrivateOwnerRecoveryConfig.RequiredConfirmationPhrase
        };

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(
            scopedConnectionString
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required."));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static Tenant NewTenant(string suffix) => new()
    {
        Name = "Private identity PostgreSQL " + suffix,
        Slug = "private-identity-" + suffix + "-" + Guid.NewGuid().ToString("N")
    };

    private UserAccount NewUser(Tenant tenant, string email, string role)
    {
        var password = PasswordHasher.HashPassword(OwnerPassword);
        return new UserAccount
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = email,
            DisplayName = role + " PostgreSQL User",
            Role = role,
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            PasswordLastChangedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            IsActive = true,
            SessionVersion = 1
        };
    }

    private static AuthSessionConfig SessionConfig() => new()
    {
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        ExpiryMinutes = 60,
        IdleTimeoutMinutes = 30,
        AbsoluteLifetimeMinutes = 480,
        RecentMfaMinutes = 10,
        MfaChallengeMinutes = 5,
        RequirePrivilegedMfa = false
    };

    private static string RandomHash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private sealed class FixedPasswordSafetyService : IPasswordSafetyService
    {
        public Task<PasswordSafetyDecision> CheckAsync(
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasswordSafetyDecision(PasswordSafetyStatus.Accepted));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset FixedNow =
            new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

        public DateTime UtcNow => FixedNow.UtcDateTime;
        public override DateTimeOffset GetUtcNow() => FixedNow;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
