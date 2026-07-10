using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class PrivacyGovernancePostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "privacy_governance_" + Guid.NewGuid().ToString("N");
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
    public async Task Migration_EnforcesPrivacyScopeImmutabilityAndRetentionDeletion()
    {
        var configured = options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var connection = scopedConnectionString ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        int firstTenantId;
        int secondTenantId;
        int firstUserId;
        int secondUserId;
        long loginEventId;
        Guid requestId;
        Guid incidentId;

        await using (var db = new AccountsDbContext(configured))
        {
            var migrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20260711040000_AddPrivacyGovernance", migrations);
            Assert.Contains("20260711040010_AddPrivacyOwnership", migrations);
            var firstTenant = new Tenant { Name = "Privacy PG A", Slug = "privacy-pg-a-" + Guid.NewGuid().ToString("N") };
            var secondTenant = new Tenant { Name = "Privacy PG B", Slug = "privacy-pg-b-" + Guid.NewGuid().ToString("N") };
            db.Tenants.AddRange(firstTenant, secondTenant);
            await db.SaveChangesAsync();
            var firstUser = User(firstTenant.Id, "first@example.invalid");
            var secondUser = User(secondTenant.Id, "second@example.invalid");
            db.UserAccounts.AddRange(firstUser, secondUser);
            await db.SaveChangesAsync();
            firstTenantId = firstTenant.Id;
            secondTenantId = secondTenant.Id;
            firstUserId = firstUser.Id;
            secondUserId = secondUser.Id;

            var now = DateTime.UtcNow;
            var loginEvent = new LoginSecurityEvent
            {
                TenantId = firstTenantId,
                UserId = firstUserId,
                IdentifierFingerprint = new string('a', 64),
                OutcomeCode = "rejected",
                ReasonCode = "invalid-credentials",
                CorrelationId = "privacy-pg-001",
                OccurredAtUtc = now.AddDays(-32),
                ExpiresAtUtc = now.AddDays(-2)
            };
            requestId = Guid.NewGuid();
            var request = new PrivacySubjectRequest
            {
                Id = requestId,
                TenantId = firstTenantId,
                SubjectUserId = firstUserId,
                RequestKind = PrivacyRequestKinds.AccessExport,
                State = PrivacyRequestStates.Completed,
                RequestedByUserId = firstUserId,
                RequestedAtUtc = now,
                DecidedByUserId = firstUserId,
                DecidedAtUtc = now,
                DecisionReason = "Synthetic same-tenant PostgreSQL retention evidence.",
                MetadataExpiresAtUtc = now.AddYears(3)
            };
            incidentId = Guid.NewGuid();
            var incident = new PrivacyIncidentExercise
            {
                Id = incidentId,
                TenantId = firstTenantId,
                ExerciseKind = "synthetic-tabletop",
                ReleaseCandidate = new string('c', 40),
                EnvironmentName = "postgres-test",
                ScenarioSha256 = new string('d', 64),
                DetectedAtUtc = now,
                NotificationRoutedAtUtc = now.AddMinutes(1),
                ContainedAtUtc = now.AddMinutes(2),
                EvidencePreservedAtUtc = now.AddMinutes(3),
                RecoveryVerifiedAtUtc = now.AddMinutes(4),
                ReviewedAtUtc = now.AddMinutes(5),
                ReviewedByUserId = firstUserId,
                NotificationDecision = "No real notification from a synthetic test.",
                EvidenceManifestSha256 = new string('e', 64),
                ReviewDecision = "accepted",
                UsedSyntheticDataOnly = true,
                TenantIsolationVerified = true,
                AuditIntegrityVerified = true,
                FinancialIntegrityVerified = true
            };
            db.AddRange(loginEvent, request, incident);
            await db.SaveChangesAsync();
            loginEventId = loginEvent.Id;
        }

        var crossLogin = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO login_security_events
                ("TenantId", "UserId", "IdentifierFingerprint", "OutcomeCode", "ReasonCode", "OccurredAtUtc", "ExpiresAtUtc")
            VALUES (@tenantId, @userId, repeat('f', 64), 'rejected', 'invalid-credentials', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + INTERVAL '30 days')
            """,
            new NpgsqlParameter("tenantId", firstTenantId),
            new NpgsqlParameter("userId", secondUserId)));
        Assert.Equal("CK_login_security_events_user_tenant", crossLogin.ConstraintName);

        var loginMutation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "UPDATE login_security_events SET \"ReasonCode\" = 'changed' WHERE \"Id\" = @id",
            new NpgsqlParameter("id", loginEventId)));
        Assert.Equal("CK_privacy_evidence_immutable", loginMutation.ConstraintName);

        var crossDecision = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "UPDATE privacy_subject_requests SET \"DecidedByUserId\" = @otherUser WHERE \"Id\" = @id",
            new NpgsqlParameter("otherUser", secondUserId),
            new NpgsqlParameter("id", requestId)));
        Assert.Equal("CK_privacy_subject_requests_user_tenant", crossDecision.ConstraintName);

        var earlyRequestDeletion = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "DELETE FROM privacy_subject_requests WHERE \"Id\" = @id",
            new NpgsqlParameter("id", requestId)));
        Assert.Equal("CK_privacy_subject_requests_retention_delete", earlyRequestDeletion.ConstraintName);

        var incidentMutation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "UPDATE privacy_incident_exercises SET \"ReviewDecision\" = 'changed' WHERE \"Id\" = @id",
            new NpgsqlParameter("id", incidentId)));
        Assert.Equal("CK_privacy_evidence_immutable", incidentMutation.ConstraintName);
        var earlyIncidentDeletion = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "DELETE FROM privacy_incident_exercises WHERE \"Id\" = @id",
            new NpgsqlParameter("id", incidentId)));
        Assert.Equal("CK_privacy_incident_exercises_retention_delete", earlyIncidentDeletion.ConstraintName);

        await using (var db = new AccountsDbContext(configured))
        {
            var service = new PrivacyGovernanceService(
                db,
                Options.Create(new PrivacyGovernanceConfig()),
                Options.Create(new AuthSessionConfig { SigningKey = StrongSigningKey() }),
                TimeProvider.System);
            var result = await service.RunRetentionAsync();
            Assert.Equal(1, result.DeletedLoginSecurityEvents);
            Assert.False(await db.LoginSecurityEvents.IgnoreQueryFilters().AnyAsync(item => item.Id == loginEventId));
            Assert.True(await db.PrivacySubjectRequests.IgnoreQueryFilters().AnyAsync(item => item.Id == requestId));
        }

        Assert.NotEqual(firstTenantId, secondTenantId);
    }

    private static UserAccount User(int tenantId, string email) => new()
    {
        TenantId = tenantId,
        Email = email,
        DisplayName = "Privacy PostgreSQL User",
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

    private static string StrongSigningKey() => Convert.ToBase64String(
        Enumerable.Range(1, 64).Select(value => (byte)value).ToArray());

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
