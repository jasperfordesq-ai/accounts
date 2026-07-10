using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class IdentitySecurityPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "identity_security_" + Guid.NewGuid().ToString("N");
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
        options = new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(scopedConnectionString).Options;
        await using var db = new AccountsDbContext(options);
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
    public async Task Migration_EnforcesIdentityOwnershipAndOneWaySecurityTransitions()
    {
        var configured = options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var connection = scopedConnectionString ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        int firstTenantId;
        int secondTenantId;
        int firstUserId;
        int secondUserId;
        int credentialId;
        int recoveryCodeId;
        int challengeId;
        int tokenId;
        long lifecycleEventId;

        await using (var db = new AccountsDbContext(configured))
        {
            Assert.Contains("20260711051000_HardenIdentitySecurity", await db.Database.GetAppliedMigrationsAsync());
            var firstTenant = new Tenant { Name = "Identity PG A", Slug = "identity-pg-a-" + Guid.NewGuid().ToString("N") };
            var secondTenant = new Tenant { Name = "Identity PG B", Slug = "identity-pg-b-" + Guid.NewGuid().ToString("N") };
            db.Tenants.AddRange(firstTenant, secondTenant);
            await db.SaveChangesAsync();
            var firstUser = User(firstTenant.Id, "identity-first@example.invalid");
            var secondUser = User(secondTenant.Id, "identity-second@example.invalid");
            db.UserAccounts.AddRange(firstUser, secondUser);
            await db.SaveChangesAsync();
            firstTenantId = firstTenant.Id;
            secondTenantId = secondTenant.Id;
            firstUserId = firstUser.Id;
            secondUserId = secondUser.Id;

            var now = DateTime.UtcNow;
            var credential = new UserMfaCredential
            {
                TenantId = firstTenantId,
                UserId = firstUserId,
                EncryptedSecret = "v2.test.synthetic-envelope",
                SecretVersion = 2,
                CreatedAtUtc = now,
                EnabledAtUtc = now,
                LastAcceptedTotpCounter = 100
            };
            var recoveryCode = new UserMfaRecoveryCode
            {
                TenantId = firstTenantId,
                UserId = firstUserId,
                CodeHash = new string('a', 64),
                CreatedAtUtc = now,
                UsedAtUtc = now
            };
            var challenge = new UserMfaChallenge
            {
                TenantId = firstTenantId,
                UserId = firstUserId,
                Purpose = MfaChallengePurposes.Login,
                TokenHash = new string('b', 64),
                SessionVersion = firstUser.SessionVersion,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(5),
                FailedAttempts = 2
            };
            var token = new UserActionToken
            {
                TenantId = firstTenantId,
                UserId = firstUserId,
                Purpose = "PasswordReset",
                TokenHash = new string('c', 64),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(15),
                ConsumedAtUtc = now,
                CreatedByUserId = firstUserId
            };
            var lifecycleEvent = new UserLifecycleEvent
            {
                TenantId = firstTenantId,
                TargetUserId = firstUserId,
                ActorUserId = firstUserId,
                EventType = "SyntheticSecurityEvidence",
                DetailsJson = "{}",
                OccurredAtUtc = now
            };
            db.AddRange(credential, recoveryCode, challenge, token, lifecycleEvent);
            await db.SaveChangesAsync();
            credentialId = credential.Id;
            recoveryCodeId = recoveryCode.Id;
            challengeId = challenge.Id;
            tokenId = token.Id;
            lifecycleEventId = lifecycleEvent.Id;
        }

        var crossTenantCredential = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO user_mfa_credentials
                ("TenantId", "UserId", "EncryptedSecret", "SecretVersion", "CreatedAtUtc", "LastAcceptedTotpCounter")
            VALUES (@tenantId, @userId, 'synthetic-envelope', 2, CURRENT_TIMESTAMP, -1)
            """,
            new NpgsqlParameter("tenantId", firstTenantId),
            new NpgsqlParameter("userId", secondUserId)));
        Assert.Equal("CK_identity_record_user_tenant", crossTenantCredential.ConstraintName);

        var crossTenantActor = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO user_lifecycle_events
                ("TenantId", "TargetUserId", "ActorUserId", "EventType", "DetailsJson", "OccurredAtUtc")
            VALUES (@tenantId, @targetUserId, @actorUserId, 'SyntheticCrossTenantActor', '{}'::jsonb, CURRENT_TIMESTAMP)
            """,
            new NpgsqlParameter("tenantId", firstTenantId),
            new NpgsqlParameter("targetUserId", firstUserId),
            new NpgsqlParameter("actorUserId", secondUserId)));
        Assert.Equal("CK_identity_record_actor_tenant", crossTenantActor.ConstraintName);

        await AssertConstraintAsync(connection,
            "UPDATE user_mfa_credentials SET \"LastAcceptedTotpCounter\" = 99 WHERE \"Id\" = @id",
            "CK_user_mfa_credentials_transition", credentialId);
        await AssertConstraintAsync(connection,
            "UPDATE user_mfa_recovery_codes SET \"UsedAtUtc\" = NULL WHERE \"Id\" = @id",
            "CK_user_mfa_recovery_codes_transition", recoveryCodeId);
        await AssertConstraintAsync(connection,
            "UPDATE user_mfa_challenges SET \"FailedAttempts\" = 1 WHERE \"Id\" = @id",
            "CK_user_mfa_challenges_transition", challengeId);
        await AssertConstraintAsync(connection,
            "UPDATE user_action_tokens SET \"ConsumedAtUtc\" = NULL WHERE \"Id\" = @id",
            "CK_user_action_tokens_transition", tokenId);
        await AssertConstraintAsync(connection,
            "UPDATE user_lifecycle_events SET \"EventType\" = 'Mutated' WHERE \"Id\" = @id",
            "CK_user_lifecycle_events_immutable", lifecycleEventId);
        await AssertConstraintAsync(connection,
            "DELETE FROM user_lifecycle_events WHERE \"Id\" = @id",
            "CK_user_lifecycle_events_immutable", lifecycleEventId);

        Assert.NotEqual(firstTenantId, secondTenantId);
    }

    private static async Task AssertConstraintAsync(string connection, string sql, string expectedConstraint, object id)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, sql, new NpgsqlParameter("id", id)));
        Assert.Equal(expectedConstraint, failure.ConstraintName);
    }

    private static UserAccount User(int tenantId, string email) => new()
    {
        TenantId = tenantId,
        Email = email,
        DisplayName = "Identity PostgreSQL User",
        Role = "Owner",
        PasswordHash = "synthetic-hash",
        PasswordSalt = "synthetic-salt"
    };

    private static async Task ExecuteAsync(string connectionString, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
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
