using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class MigrationUpgradePostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const string EvidencePathEnvVar = "ACCOUNTS_MIGRATION_EVIDENCE_PATH";
    private const int TenantId = 910001;
    private const int UserId = 910001;
    private const int CompanyId = 910001;
    private const int PeriodId = 910001;
    private const int BankAccountId = 910001;
    private const int DebitCategoryId = 910001;
    private const int CreditCategoryId = 910002;
    private const int TransactionId = 910001;
    private const int AdjustmentId = 910001;
    private const int OpeningBalanceId = 910001;
    private const int CroPackageId = 910001;
    private const int RevenuePackageId = 910001;
    private const int AuditLogId = 910001;
    private const int AuditCheckpointId = 910001;

    [PostgresFact]
    public async Task FreshAndPreviousReleaseSchemas_MigrateWithoutDriftAndPreserveReleaseEvidence()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var repositoryRoot = FindRepositoryRoot();
        var configuration = LoadConfiguration(repositoryRoot);
        AssertToolchainLock(repositoryRoot, configuration);

        var freshSchema = "migration_fresh_" + Guid.NewGuid().ToString("N");
        var upgradeSchema = "migration_upgrade_" + Guid.NewGuid().ToString("N");
        var report = new MigrationGateReport
        {
            Status = "failed",
            GeneratedAtUtc = DateTime.UtcNow,
            ReleaseCandidate = new ReleaseCandidateIdentity(
                Environment.GetEnvironmentVariable("GITHUB_SHA"),
                Environment.GetEnvironmentVariable("ACCOUNTS_GITHUB_ACTIONS_RUN_URL")),
            Toolchain = new ToolchainEvidence(
                configuration.DotnetSdkVersion,
                configuration.EntityFrameworkToolVersion,
                configuration.EntityFrameworkPackageVersion,
                configuration.NpgsqlProviderVersion),
            Database = new DatabaseEvidence(
                configuration.DatabaseEngine,
                configuration.PreviousReleaseMigration,
                configuration.SupportedUpgradeFloorBasis,
                null,
                0,
                null),
            EncryptedRecoveryIntegration = new EncryptedRecoveryIntegration(
                "restore-drill-report.json",
                true,
                "A candidate passes only when this migration gate and the encrypted backup/restore drill are retained in the same CI machine and release evidence packs.")
        };

        try
        {
            await CreateSchemaAsync(baseConnectionString, freshSchema);
            await CreateSchemaAsync(baseConnectionString, upgradeSchema);

            var freshConnectionString = WithSearchPath(baseConnectionString, freshSchema);
            var upgradeConnectionString = WithSearchPath(baseConnectionString, upgradeSchema);
            var freshOptions = CreateOptions(freshConnectionString);
            var upgradeOptions = CreateOptions(upgradeConnectionString);

            FreshDatabaseEvidence freshEvidence;
            IReadOnlyList<string> allMigrations;
            string serverVersion;
            await using (var fresh = new AccountsDbContext(freshOptions))
            {
                allMigrations = fresh.Database.GetMigrations().ToArray();
                Assert.Contains(configuration.PreviousReleaseMigration, allMigrations);
                Assert.True(
                    Array.IndexOf(allMigrations.ToArray(), configuration.PreviousReleaseMigration) < allMigrations.Count - 1,
                    "The configured upgrade floor must precede the current migration target.");

                await fresh.Database.MigrateAsync();
                var applied = (await fresh.Database.GetAppliedMigrationsAsync()).ToArray();
                var pending = (await fresh.Database.GetPendingMigrationsAsync()).ToArray();
                Assert.Equal(allMigrations, applied);
                Assert.Empty(pending);

                var requiredTables = new[]
                {
                    "tenants", "user_accounts", "companies", "accounting_periods",
                    "imported_transactions", "adjustments", "opening_balances",
                    "cro_filing_packages", "revenue_filing_packages",
                    "audit_logs", "audit_integrity_checkpoints"
                };
                var tables = await ReadTableNamesAsync(freshConnectionString);
                Assert.All(requiredTables, table => Assert.Contains(table, tables));
                serverVersion = await ReadServerVersionAsync(freshConnectionString);
                freshEvidence = new FreshDatabaseEvidence(
                    "passed",
                    applied.Length,
                    pending.Length,
                    tables.Count,
                    requiredTables);
            }

            IReadOnlyDictionary<string, PreservationSnapshot> before;
            IReadOnlyDictionary<string, PreservationSnapshot> after;
            string latestMigration;
            int previousMigrationCount;
            await using (var upgrade = new AccountsDbContext(upgradeOptions))
            {
                var migrator = upgrade.GetService<IMigrator>();
                await migrator.MigrateAsync(configuration.PreviousReleaseMigration);
                var previousApplied = (await upgrade.Database.GetAppliedMigrationsAsync()).ToArray();
                Assert.Equal(configuration.PreviousReleaseMigration, previousApplied[^1]);
                previousMigrationCount = previousApplied.Length;

                await SeedRepresentativePreviousReleaseAsync(upgradeConnectionString);
                before = await CapturePreservationSnapshotsAsync(upgradeConnectionString);

                await migrator.MigrateAsync();
                var applied = (await upgrade.Database.GetAppliedMigrationsAsync()).ToArray();
                var pending = (await upgrade.Database.GetPendingMigrationsAsync()).ToArray();
                Assert.Equal(allMigrations, applied);
                Assert.Empty(pending);
                latestMigration = applied[^1];

                after = await CapturePreservationSnapshotsAsync(upgradeConnectionString);
                Assert.Equal(before.Keys.Order(), after.Keys.Order());
                foreach (var name in configuration.RequiredPreservationChecks)
                {
                    Assert.True(before.TryGetValue(name, out var beforeSnapshot), $"Missing pre-upgrade preservation group: {name}");
                    Assert.True(after.TryGetValue(name, out var afterSnapshot), $"Missing post-upgrade preservation group: {name}");
                    Assert.Equal(beforeSnapshot, afterSnapshot);
                }

                var integrity = await new AuditIntegrityService(upgrade).VerifyCompanyAsync(CompanyId);
                Assert.True(integrity.IsValid);
                Assert.Equal(1, integrity.CheckedEntries);
                Assert.Equal(0, integrity.UncheckedLegacyEntries);

                var suppressedTransactions = upgrade.GetService<IMigrationsAssembly>()
                    .Migrations
                    .Values
                    .Select(type => upgrade.GetService<IMigrationsAssembly>().CreateMigration(type, upgrade.Database.ProviderName!))
                    .SelectMany(migration => migration.UpOperations.OfType<SqlOperation>())
                    .Where(operation => operation.SuppressTransaction)
                    .ToArray();
                Assert.Empty(suppressedTransactions);
            }

            var preservationChecks = configuration.RequiredPreservationChecks
                .Select(name => new PreservationCheckEvidence(
                    name,
                    "passed",
                    before[name].RowCount,
                    after[name].RowCount,
                    before[name].CanonicalSha256,
                    after[name].CanonicalSha256))
                .ToArray();

            var historyBeforeFailure = await ReadMigrationHistoryAsync(upgradeConnectionString);
            var dataBeforeFailure = await CapturePreservationSnapshotsAsync(upgradeConnectionString);
            var forcedFailure = await ExecuteForcedMigrationFailureAsync(upgradeConnectionString);
            Assert.True(forcedFailure.FailureObserved);
            Assert.Equal("P0001", forcedFailure.SqlState);
            Assert.False(await TableExistsAsync(upgradeConnectionString, "ops007_partial_migration_marker"));
            var historyAfterFailure = await ReadMigrationHistoryAsync(upgradeConnectionString);
            var dataAfterFailure = await CapturePreservationSnapshotsAsync(upgradeConnectionString);
            Assert.Equal(historyBeforeFailure, historyAfterFailure);
            Assert.Equal(dataBeforeFailure, dataAfterFailure);

            report.Status = "passed";
            report.Database = new DatabaseEvidence(
                configuration.DatabaseEngine,
                configuration.PreviousReleaseMigration,
                configuration.SupportedUpgradeFloorBasis,
                latestMigration,
                allMigrations.Count,
                serverVersion);
            report.FreshDatabase = freshEvidence;
            report.PreviousReleaseUpgrade = new PreviousReleaseUpgradeEvidence(
                "passed",
                configuration.PreviousReleaseMigration,
                latestMigration,
                previousMigrationCount,
                allMigrations.Count,
                preservationChecks,
                true);
            report.FailureRollback = new FailureRollbackEvidence(
                "passed",
                forcedFailure.FailureObserved,
                forcedFailure.SqlState,
                true,
                true,
                true,
                0,
                "PostgreSQL transactional DDL/data rollback; EF migrations contain no transaction-suppressed SQL operations.");
        }
        catch (Exception error)
        {
            report.Failures.Add($"{error.GetType().Name}: {error.Message}");
            throw;
        }
        finally
        {
            report.GeneratedAtUtc = DateTime.UtcNow;
            WriteEvidenceIfRequested(report);
            NpgsqlConnection.ClearAllPools();
            await DropSchemaAsync(baseConnectionString, freshSchema);
            await DropSchemaAsync(baseConnectionString, upgradeSchema);
        }
    }

    [PostgresFact]
    public async Task SystemActorMigration_BackfillsExistingIdentityEvidenceBeforeAddingConstraints()
    {
        const string migrationBeforeActorKinds = "20260711212921_ScopeUserIdentityToTenant";
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var schema = "migration_actor_backfill_" + Guid.NewGuid().ToString("N");
        try
        {
            await CreateSchemaAsync(baseConnectionString, schema);
            var connectionString = WithSearchPath(baseConnectionString, schema);
            await using var db = new AccountsDbContext(CreateOptions(connectionString));
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(migrationBeforeActorKinds);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO tenants ("Id", "Name", "Slug", "IsMainDemoTenant", "CreatedAt", "UpdatedAt")
                    VALUES (920001, 'Actor migration tenant', 'actor-migration-tenant', FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

                    INSERT INTO user_accounts
                        ("Id", "TenantId", "Email", "DisplayName", "Role", "PasswordHash", "PasswordSalt", "PasswordAlgorithm",
                         "PasswordStrengthScore", "IsActive", "MustChangePassword", "PasswordLastChangedAt", "CreatedAt", "UpdatedAt",
                         "FailedLoginCount", "SessionVersion")
                    VALUES
                        (920001, 920001, 'actor-migration@example.ie', 'Actor Migration Owner', 'Owner', repeat('h', 64), repeat('s', 32),
                         'PBKDF2-SHA256', 4, TRUE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0, 1);

                    INSERT INTO user_action_tokens
                        ("TenantId", "UserId", "Purpose", "TokenHash", "CreatedAtUtc", "ExpiresAtUtc", "CreatedByUserId")
                    VALUES
                        (920001, 920001, 'PasswordReset', repeat('a', 64), CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + INTERVAL '1 hour', 920001);

                    INSERT INTO user_lifecycle_events
                        ("TenantId", "TargetUserId", "ActorUserId", "EventType", "DetailsJson", "OccurredAtUtc")
                    VALUES
                        (920001, 920001, 920001, 'PasswordResetStarted', '{}'::jsonb, CURRENT_TIMESTAMP);
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            await migrator.MigrateAsync();
            await using var verify = new NpgsqlConnection(connectionString);
            await verify.OpenAsync();
            await using var command = verify.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT "CreatedByActorKind" || ':' || "CreatedByUserId"::text FROM user_action_tokens WHERE "TenantId" = 920001),
                    (SELECT "ActorKind" || ':' || "ActorUserId"::text FROM user_lifecycle_events WHERE "TenantId" = 920001)
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("User:920001", reader.GetString(0));
            Assert.Equal("User:920001", reader.GetString(1));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropSchemaAsync(baseConnectionString, schema);
        }
    }

    [PostgresFact]
    public async Task HostOperatorConstraintCorrection_UpgradesAppliedPreviewSchemaToCanonicalResetEvent()
    {
        const string actorMigration = "20260711221827_RecordSystemIdentityActors";
        const string correctionMigration = "20260712000457_CorrectPrivateHostLifecycleEventConstraint";
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var schema = "migration_host_reset_constraint_" + Guid.NewGuid().ToString("N");
        try
        {
            await CreateSchemaAsync(baseConnectionString, schema);
            var connectionString = WithSearchPath(baseConnectionString, schema);
            await using var db = new AccountsDbContext(CreateOptions(connectionString));
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(actorMigration);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using (var seed = connection.CreateCommand())
                {
                    seed.CommandText = """
                        INSERT INTO tenants ("Id", "Name", "Slug", "IsMainDemoTenant", "CreatedAt", "UpdatedAt")
                        VALUES (930001, 'Host reset migration tenant', 'host-reset-migration', FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

                        INSERT INTO user_accounts
                            ("Id", "TenantId", "Email", "DisplayName", "Role", "PasswordHash", "PasswordSalt", "PasswordAlgorithm",
                             "PasswordStrengthScore", "IsActive", "MustChangePassword", "PasswordLastChangedAt", "CreatedAt", "UpdatedAt",
                             "FailedLoginCount", "SessionVersion")
                        VALUES
                            (930001, 930001, 'host-reset-migration@example.ie', 'Host Reset Owner', 'Owner', repeat('h', 64), repeat('s', 32),
                             'PBKDF2-SHA256', 4, TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0, 1);
                        """;
                    await seed.ExecuteNonQueryAsync();
                }

                await using var rejected = connection.CreateCommand();
                rejected.CommandText = """
                    INSERT INTO user_lifecycle_events
                        ("TenantId", "TargetUserId", "ActorUserId", "ActorKind", "EventType", "DetailsJson", "OccurredAtUtc")
                    VALUES
                        (930001, 930001, NULL, 'PrivateServerHostOperator', 'UserPasswordResetCompleted', '{}'::jsonb, CURRENT_TIMESTAMP);
                    """;
                var oldConstraintFailure = await Assert.ThrowsAsync<PostgresException>(() => rejected.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, oldConstraintFailure.SqlState);
                Assert.Equal("CK_user_lifecycle_events_actor", oldConstraintFailure.ConstraintName);
            }

            await migrator.MigrateAsync(correctionMigration);

            await using var verify = new NpgsqlConnection(connectionString);
            await verify.OpenAsync();
            await using (var accepted = verify.CreateCommand())
            {
                accepted.CommandText = """
                    INSERT INTO user_lifecycle_events
                        ("TenantId", "TargetUserId", "ActorUserId", "ActorKind", "EventType", "DetailsJson", "OccurredAtUtc")
                    VALUES
                        (930001, 930001, NULL, 'PrivateServerHostOperator', 'UserPasswordResetCompleted', '{}'::jsonb, CURRENT_TIMESTAMP);
                    """;
                Assert.Equal(1, await accepted.ExecuteNonQueryAsync());
            }

            await using var constraint = verify.CreateCommand();
            constraint.CommandText = """
                SELECT pg_get_constraintdef(oid)
                FROM pg_constraint
                WHERE conname = 'CK_user_lifecycle_events_actor'
                  AND conrelid = 'user_lifecycle_events'::regclass;
                """;
            var definition = (string?)await constraint.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Corrected host-operator lifecycle constraint was not found.");
            Assert.Contains("'UserPasswordResetCompleted'", definition, StringComparison.Ordinal);
            Assert.DoesNotContain("'PasswordResetCompleted'", definition, StringComparison.Ordinal);
            Assert.Equal(correctionMigration, (await db.Database.GetAppliedMigrationsAsync()).Last());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropSchemaAsync(baseConnectionString, schema);
        }
    }

    private static DbContextOptions<AccountsDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    private static string WithSearchPath(string connectionString, string schemaName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schemaName,
            IncludeErrorDetail = true
        };
        return builder.ConnectionString;
    }

    private static async Task CreateSchemaAsync(string connectionString, string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropSchemaAsync(string connectionString, string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlySet<string>> ReadTableNamesAsync(string connectionString)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() ORDER BY table_name";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<string> ReadServerVersionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW server_version";
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("PostgreSQL did not report its version."));
    }

    private static async Task SeedRepresentativePreviousReleaseAsync(string connectionString)
    {
        var timestamp = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        var audit = new AuditLog
        {
            CompanyId = CompanyId,
            PeriodId = PeriodId,
            TenantId = TenantId,
            EntityType = "AccountingPeriod",
            EntityId = PeriodId,
            Action = "PeriodApproved",
            NewValueJson = "{\"status\":\"approved\"}",
            UserId = "ops007-owner@example.ie",
            ActorDisplayName = "OPS-007 Owner",
            RequestId = "ops007-previous-release",
            Timestamp = timestamp
        };
        var auditHash = AuditLogIntegrity.ComputeHash(audit);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tenants ("Id", "Name", "Slug", "IsMainDemoTenant", "CreatedAt", "UpdatedAt")
            VALUES (910001, 'OPS-007 Practice', 'ops-007-practice', FALSE, TIMESTAMPTZ '2026-06-20T09:00:00Z', TIMESTAMPTZ '2026-06-20T09:00:00Z');

            INSERT INTO user_accounts
                ("Id", "TenantId", "Email", "DisplayName", "Role", "PasswordHash", "PasswordSalt", "PasswordAlgorithm",
                 "PasswordStrengthScore", "IsActive", "MustChangePassword", "PasswordLastChangedAt", "CreatedAt", "UpdatedAt",
                 "FailedLoginCount", "SessionVersion")
            VALUES
                (910001, 910001, 'ops007-owner@example.ie', 'OPS-007 Owner', 'Owner', repeat('h', 64), repeat('s', 32),
                 'PBKDF2-SHA256', 4, TRUE, FALSE, TIMESTAMPTZ '2026-06-20T09:00:00Z', TIMESTAMPTZ '2026-06-20T09:00:00Z',
                 TIMESTAMPTZ '2026-06-20T09:00:00Z', 0, 3);

            INSERT INTO companies
                ("Id", "LegalName", "TradingName", "CroNumber", "TaxReference", "CompanyType", "IncorporationDate",
                 "FinancialYearStartMonth", "ArdMonth", "IsGroupMember", "IsHolding", "IsInvestment", "IsSubsidiary",
                 "IsDormant", "IsTrading", "IsVatRegistered", "IsEmployer", "HasStock", "OwnsAssets", "HasBorrowings",
                 "HasDirectorLoans", "CreatedAt", "UpdatedAt", "IsCharitableOrganisation", "IsCreditInstitution",
                 "IsInsuranceUndertaking", "IsListedSecurities", "IsPensionFund", "TenantId")
            VALUES
                (910001, 'OPS-007 Upgrade Limited', 'OPS Upgrade', '765432', '7654321A', 'Private', DATE '2025-01-01',
                 1, 10, FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE,
                 TIMESTAMPTZ '2026-06-20T09:00:00Z', TIMESTAMPTZ '2026-06-20T09:00:00Z', FALSE, FALSE, FALSE, FALSE, FALSE, 910001);

            INSERT INTO accounting_periods
                ("Id", "CompanyId", "PeriodStart", "PeriodEnd", "Status", "IsFirstYear", "CreatedAt",
                 "GoingConcernConfirmed", "MemberAuditNoticeReceived", "ClosingRetainedEarnings", "ApprovalDate",
                 "AuditorsReportReceived", "AuditorsReportReference")
            VALUES
                (910001, 910001, DATE '2025-01-01', DATE '2025-12-31', 'Open', TRUE, TIMESTAMPTZ '2026-06-20T09:00:00Z',
                 TRUE, FALSE, 3125.50, DATE '2026-06-15', TRUE, 'AUD-OPS007-2025');

            INSERT INTO bank_accounts ("Id", "CompanyId", "Name", "Iban", "Currency", "OpeningBalance", "OpeningBalanceDate")
            VALUES (910001, 910001, 'EUR Current Account', 'IE29AIBK93115212345678', 'EUR', 1000.00, DATE '2025-01-01');

            INSERT INTO account_categories ("Id", "CompanyId", "Code", "Name", "Type", "TaxTreatment", "IsSystem", "ParentId", "IsNonTradingIncome")
            VALUES
                (910001, 910001, '1100', 'Cash at bank', 'Asset', 'Allowable', FALSE, NULL, FALSE),
                (910002, 910001, '2100', 'Trade creditors', 'Liability', 'Allowable', FALSE, NULL, FALSE);

            INSERT INTO imported_transactions
                ("Id", "BankAccountId", "PeriodId", "Date", "Description", "Amount", "Balance", "Reference",
                 "CategoryId", "ConfidenceScore", "IsDuplicate", "ManualOverride")
            VALUES
                (910001, 910001, 910001, DATE '2025-02-03', 'Representative customer receipt', 456.78, 1456.78,
                 'OPS007-TXN-1', 910001, 0.98, FALSE, TRUE);

            INSERT INTO adjustments
                ("Id", "PeriodId", "Description", "DebitCategoryId", "CreditCategoryId", "Amount", "Source", "Reason",
                 "LegalBasis", "ImpactOnProfit", "ImpactOnAssets", "CreatedBy", "ApprovedBy", "ApprovedAt", "IsAuto", "CreatedAt")
            VALUES
                (910001, 910001, 'Representative valid double-entry journal', 910001, 910002, 250.75, 'Manual',
                 'Previous-release retained journal', 'Qualified-accountant review', 0.00, 250.75,
                 'ops007-owner@example.ie', 'OPS-007 Owner', TIMESTAMPTZ '2026-06-21T09:00:00Z', FALSE,
                 TIMESTAMPTZ '2026-06-21T08:00:00Z');

            INSERT INTO opening_balances
                ("Id", "PeriodId", "AccountCategoryId", "Debit", "Credit", "SourceNote", "EnteredBy", "EnteredAt", "Reviewed", "ReviewedBy", "ReviewedAt")
            VALUES
                (910001, 910001, 910001, 1000.00, 0.00, 'Signed prior-year trial balance', 'OPS-007 Owner',
                 TIMESTAMPTZ '2026-06-20T10:00:00Z', TRUE, 'Qualified Accountant', TIMESTAMPTZ '2026-06-21T08:30:00Z');

            INSERT INTO cro_filing_packages
                ("Id", "PeriodId", "GeneratedAt", "PdfPath", "Status", "AccountsPdfGenerated", "ApprovedAt", "ApprovedBy",
                 "CroSubmissionReference", "FilingStatus", "PaymentCompleted", "SignaturePageGenerated", "SubmittedAt",
                 "SubmittedBy", "SignedAt", "SignedByDirector", "SignedBySecretary", "SignedPdfPath")
            VALUES
                (910001, 910001, TIMESTAMPTZ '2026-06-21T10:00:00Z', '/retained/ops007/accounts.pdf', 'Approved', TRUE,
                 TIMESTAMPTZ '2026-06-21T11:00:00Z', 'Qualified Accountant', 'CRO-WORKFLOW-OPS007', 'RecordedExternally',
                 TRUE, TRUE, TIMESTAMPTZ '2026-06-21T12:00:00Z', 'OPS-007 Owner', TIMESTAMPTZ '2026-06-21T11:30:00Z',
                 'Director Example', 'Secretary Example', '/retained/ops007/accounts-signed.pdf');

            INSERT INTO revenue_filing_packages
                ("Id", "PeriodId", "Ct1DataJson", "IxbrlPath", "GeneratedAt", "Status", "ApprovedAt", "ApprovedBy",
                 "Ct1Reference", "FilingStatus", "IxbrlGenerated", "IxbrlValidated", "IxbrlValidationErrors")
            VALUES
                (910001, 910001, '{"taxableProfit":3125.50,"corporationTax":390.69}', '/retained/ops007/accounts.ixbrl',
                 TIMESTAMPTZ '2026-06-21T10:00:00Z', 'Approved', TIMESTAMPTZ '2026-06-21T11:00:00Z',
                 'Qualified Accountant', 'ROS-WORKFLOW-OPS007', 'RecordedExternally', TRUE, TRUE, '[]');

            INSERT INTO audit_logs
                ("Id", "CompanyId", "PeriodId", "EntityType", "EntityId", "Action", "OldValueJson", "NewValueJson",
                 "UserId", "Timestamp", "ActorDisplayName", "IntegrityHash", "PreviousIntegrityHash", "RequestId", "TenantId")
            VALUES
                (910001, 910001, 910001, 'AccountingPeriod', 910001, 'PeriodApproved', NULL, '{"status":"approved"}',
                 'ops007-owner@example.ie', TIMESTAMPTZ '2026-06-21T10:00:00Z', 'OPS-007 Owner', @auditHash, NULL,
                 'ops007-previous-release', 910001);

            INSERT INTO audit_integrity_checkpoints
                ("Id", "CompanyId", "TenantId", "LastAuditLogId", "LastIntegrityHash", "CheckedEntries", "CreatedAtUtc",
                 "CreatedByUserId", "CreatedByDisplayName", "RequestId", "KeyId", "Signature")
            VALUES
                (910001, 910001, 910001, 910001, @auditHash, 1, TIMESTAMPTZ '2026-06-21T10:05:00Z',
                 'ops007-owner@example.ie', 'OPS-007 Owner', 'ops007-checkpoint', 'ops007-test-key', repeat('c', 64));
            """;
        command.Parameters.AddWithValue("auditHash", auditHash);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyDictionary<string, PreservationSnapshot>> CapturePreservationSnapshotsAsync(string connectionString)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant-and-user"] = """
                SELECT json_build_object(
                    'tenant', (SELECT json_build_object('id', "Id", 'name', "Name", 'slug', "Slug", 'demo', "IsMainDemoTenant", 'created', "CreatedAt", 'updated', "UpdatedAt") FROM tenants WHERE "Id" = 910001),
                    'user', (SELECT json_build_object('id', "Id", 'tenantId', "TenantId", 'email', "Email", 'displayName', "DisplayName", 'role', "Role", 'passwordHash', "PasswordHash", 'passwordSalt', "PasswordSalt", 'algorithm', "PasswordAlgorithm", 'strength', "PasswordStrengthScore", 'active', "IsActive", 'mustChange', "MustChangePassword", 'passwordChanged', "PasswordLastChangedAt", 'created', "CreatedAt", 'updated', "UpdatedAt", 'failed', "FailedLoginCount", 'sessionVersion', "SessionVersion") FROM user_accounts WHERE "Id" = 910001)
                )::text
                """,
            ["company-and-accounting-period"] = """
                SELECT json_build_object(
                    'company', (SELECT json_build_object('id', "Id", 'tenantId', "TenantId", 'legalName', "LegalName", 'tradingName', "TradingName", 'cro', "CroNumber", 'tax', "TaxReference", 'type', "CompanyType", 'incorporation', "IncorporationDate", 'fyMonth', "FinancialYearStartMonth", 'trading', "IsTrading", 'vat', "IsVatRegistered", 'employer', "IsEmployer", 'stock', "HasStock", 'assets', "OwnsAssets") FROM companies WHERE "Id" = 910001),
                    'period', (SELECT json_build_object('id', "Id", 'companyId', "CompanyId", 'start', "PeriodStart", 'end', "PeriodEnd", 'status', "Status", 'firstYear', "IsFirstYear", 'goingConcern', "GoingConcernConfirmed", 'retainedEarnings', "ClosingRetainedEarnings", 'approvalDate', "ApprovalDate", 'auditorReceived', "AuditorsReportReceived", 'auditorReference', "AuditorsReportReference") FROM accounting_periods WHERE "Id" = 910001)
                )::text
                """,
            ["financial-rows-and-figures"] = """
                SELECT json_build_object(
                    'bank', (SELECT json_build_object('id', "Id", 'companyId', "CompanyId", 'name', "Name", 'iban', "Iban", 'currency', "Currency", 'opening', "OpeningBalance", 'openingDate', "OpeningBalanceDate") FROM bank_accounts WHERE "Id" = 910001),
                    'debitCategory', (SELECT json_build_object('id', "Id", 'companyId', "CompanyId", 'code', "Code", 'name', "Name", 'type', "Type", 'tax', "TaxTreatment", 'system', "IsSystem") FROM account_categories WHERE "Id" = 910001),
                    'creditCategory', (SELECT json_build_object('id', "Id", 'companyId', "CompanyId", 'code', "Code", 'name', "Name", 'type', "Type", 'tax', "TaxTreatment", 'system', "IsSystem") FROM account_categories WHERE "Id" = 910002),
                    'transaction', (SELECT json_build_object('id', "Id", 'bankId', "BankAccountId", 'periodId', "PeriodId", 'date', "Date", 'description', "Description", 'amount', "Amount", 'balance', "Balance", 'reference', "Reference", 'categoryId', "CategoryId", 'confidence', "ConfidenceScore", 'duplicate', "IsDuplicate", 'override', "ManualOverride") FROM imported_transactions WHERE "Id" = 910001),
                    'adjustment', (SELECT json_build_object('id', "Id", 'periodId', "PeriodId", 'description', "Description", 'debit', "DebitCategoryId", 'credit', "CreditCategoryId", 'amount', "Amount", 'source', "Source", 'reason', "Reason", 'legalBasis', "LegalBasis", 'profit', "ImpactOnProfit", 'assets', "ImpactOnAssets", 'createdBy', "CreatedBy", 'approvedBy', "ApprovedBy", 'approvedAt', "ApprovedAt", 'auto', "IsAuto", 'createdAt', "CreatedAt") FROM adjustments WHERE "Id" = 910001),
                    'openingBalance', (SELECT json_build_object('id', "Id", 'periodId', "PeriodId", 'categoryId', "AccountCategoryId", 'debit', "Debit", 'credit', "Credit", 'source', "SourceNote", 'enteredBy', "EnteredBy", 'enteredAt', "EnteredAt", 'reviewed', "Reviewed", 'reviewedBy', "ReviewedBy", 'reviewedAt', "ReviewedAt") FROM opening_balances WHERE "Id" = 910001)
                )::text
                """,
            ["filing-snapshots-and-artifacts"] = """
                SELECT json_build_object(
                    'cro', (SELECT json_build_object('id', "Id", 'periodId', "PeriodId", 'generatedAt', "GeneratedAt", 'pdfPath', "PdfPath", 'status', "Status", 'pdfGenerated', "AccountsPdfGenerated", 'approvedAt', "ApprovedAt", 'approvedBy', "ApprovedBy", 'reference', "CroSubmissionReference", 'filingStatus', "FilingStatus", 'payment', "PaymentCompleted", 'signaturePage', "SignaturePageGenerated", 'submittedAt', "SubmittedAt", 'submittedBy', "SubmittedBy", 'signedAt', "SignedAt", 'director', "SignedByDirector", 'secretary', "SignedBySecretary", 'signedPath', "SignedPdfPath") FROM cro_filing_packages WHERE "Id" = 910001),
                    'revenue', (SELECT json_build_object('id', "Id", 'periodId', "PeriodId", 'ct1', "Ct1DataJson", 'ixbrlPath', "IxbrlPath", 'generatedAt', "GeneratedAt", 'status', "Status", 'approvedAt', "ApprovedAt", 'approvedBy', "ApprovedBy", 'reference', "Ct1Reference", 'filingStatus', "FilingStatus", 'generated', "IxbrlGenerated", 'validated', "IxbrlValidated", 'errors', "IxbrlValidationErrors") FROM revenue_filing_packages WHERE "Id" = 910001)
                )::text
                """,
            ["audit-chain-and-checkpoints"] = """
                SELECT json_build_object(
                    'audit', (SELECT json_build_object('id', "Id", 'companyId', "CompanyId", 'periodId', "PeriodId", 'tenantId', "TenantId", 'entityType', "EntityType", 'entityId', "EntityId", 'action', "Action", 'old', "OldValueJson", 'new', "NewValueJson", 'userId', "UserId", 'timestamp', "Timestamp", 'actor', "ActorDisplayName", 'hash', "IntegrityHash", 'previousHash', "PreviousIntegrityHash", 'requestId', "RequestId") FROM audit_logs WHERE "Id" = 910001),
                    'checkpoint', (SELECT json_build_object('id', "Id", 'companyId', "CompanyId", 'tenantId', "TenantId", 'lastId', "LastAuditLogId", 'lastHash', "LastIntegrityHash", 'checked', "CheckedEntries", 'created', "CreatedAtUtc", 'userId', "CreatedByUserId", 'displayName', "CreatedByDisplayName", 'requestId', "RequestId", 'keyId', "KeyId", 'signature', "Signature") FROM audit_integrity_checkpoints WHERE "Id" = 910001)
                )::text
                """
        };

        var result = new Dictionary<string, PreservationSnapshot>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var (name, sql) in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var canonical = (string?)(await command.ExecuteScalarAsync())
                ?? throw new InvalidOperationException($"Preservation query returned no value: {name}");
            using (var document = JsonDocument.Parse(canonical))
            {
                Assert.All(document.RootElement.EnumerateObject(), item => Assert.Equal(JsonValueKind.Object, item.Value.ValueKind));
                result[name] = new PreservationSnapshot(document.RootElement.EnumerateObject().Count(), Sha256(canonical));
            }
        }
        return result;
    }

    private static async Task<ForcedFailureResult> ExecuteForcedMigrationFailureAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var partialSchema = connection.CreateCommand())
            {
                partialSchema.Transaction = transaction;
                partialSchema.CommandText = "CREATE TABLE ops007_partial_migration_marker (\"Id\" integer PRIMARY KEY)";
                await partialSchema.ExecuteNonQueryAsync();
            }
            await using (var partialData = connection.CreateCommand())
            {
                partialData.Transaction = transaction;
                partialData.CommandText = "UPDATE companies SET \"LegalName\" = 'PARTIAL MIGRATION MUST ROLLBACK' WHERE \"Id\" = 910001";
                Assert.Equal(1, await partialData.ExecuteNonQueryAsync());
            }
            await using (var fail = connection.CreateCommand())
            {
                fail.Transaction = transaction;
                fail.CommandText = "DO $forced$ BEGIN RAISE EXCEPTION 'OPS-007 forced migration failure' USING ERRCODE = 'P0001'; END $forced$";
                await fail.ExecuteNonQueryAsync();
            }
            throw new InvalidOperationException("The forced migration failure did not fail.");
        }
        catch (PostgresException error)
        {
            await transaction.RollbackAsync();
            return new ForcedFailureResult(true, error.SqlState);
        }
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @tableName)";
        command.Parameters.AddWithValue("tableName", tableName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<string[]> ReadMigrationHistoryAsync(string connectionString)
    {
        var migrations = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) migrations.Add(reader.GetString(0));
        return migrations.ToArray();
    }

    private static MigrationGateConfiguration LoadConfiguration(string root)
    {
        var json = File.ReadAllText(Path.Combine(root, "config", "migration-gate.json"));
        return JsonSerializer.Deserialize<MigrationGateConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("config/migration-gate.json is invalid.");
    }

    private static void AssertToolchainLock(string root, MigrationGateConfiguration configuration)
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        Assert.Equal(configuration.DotnetSdkVersion, globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString());

        using var toolsJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json")));
        Assert.Equal(
            configuration.EntityFrameworkToolVersion,
            toolsJson.RootElement.GetProperty("tools").GetProperty("dotnet-ef").GetProperty("version").GetString());
        Assert.StartsWith(configuration.EntityFrameworkPackageVersion, typeof(DbContext).Assembly.GetName().Version?.ToString());
        Assert.StartsWith(
            configuration.NpgsqlProviderVersion,
            typeof(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder).Assembly.GetName().Version?.ToString());
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var starts = new[]
        {
            Environment.GetEnvironmentVariable("GITHUB_WORKSPACE"),
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(sourceFile),
            AppContext.BaseDirectory
        };
        foreach (var start in starts.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            var directory = new DirectoryInfo(start!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json"))
                    && File.Exists(Path.Combine(directory.FullName, "config", "migration-gate.json")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Could not locate repository root for migration-gate configuration.");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void WriteEvidenceIfRequested(MigrationGateReport report)
    {
        var path = Environment.GetEnvironmentVariable(EvidencePathEnvVar);
        if (string.IsNullOrWhiteSpace(path)) return;
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }

    private sealed record MigrationGateConfiguration(
        int FormatVersion,
        string PreviousReleaseMigration,
        string SupportedUpgradeFloorBasis,
        string DatabaseEngine,
        string DotnetSdkVersion,
        string EntityFrameworkToolVersion,
        string EntityFrameworkPackageVersion,
        string NpgsqlProviderVersion,
        string RollbackPolicy,
        string[] RequiredPreservationChecks,
        string[] RequiredEvidenceFiles);

    private sealed record PreservationSnapshot(int RowCount, string CanonicalSha256);
    private sealed record ForcedFailureResult(bool FailureObserved, string SqlState);
    private sealed record ReleaseCandidateIdentity(string? CommitSha, string? GitHubActionsRunUrl);
    private sealed record ToolchainEvidence(string DotnetSdkVersion, string EntityFrameworkToolVersion, string EntityFrameworkPackageVersion, string NpgsqlProviderVersion);
    private sealed record DatabaseEvidence(string Engine, string PreviousReleaseMigration, string SupportedUpgradeFloorBasis, string? LatestMigration, int MigrationCount, string? ServerVersion);
    private sealed record FreshDatabaseEvidence(string Status, int AppliedMigrationCount, int PendingMigrationCount, int TableCount, IReadOnlyList<string> RequiredTables);
    private sealed record PreservationCheckEvidence(string Name, string Status, int BeforeRowCount, int AfterRowCount, string BeforeSha256, string AfterSha256);
    private sealed record PreviousReleaseUpgradeEvidence(string Status, string SourceMigration, string TargetMigration, int SourceMigrationCount, int TargetMigrationCount, IReadOnlyList<PreservationCheckEvidence> PreservationChecks, bool AuditChainCryptographicallyValid);
    private sealed record FailureRollbackEvidence(string Status, bool FailureObserved, string SqlState, bool PartialSchemaAbsent, bool DataPreserved, bool MigrationHistoryPreserved, int TransactionSuppressedSqlOperationCount, string RecoveryMode);
    private sealed record EncryptedRecoveryIntegration(string RequiredCompanionReport, bool RequiredInSameReleasePack, string Policy);

    private sealed class MigrationGateReport
    {
        public int FormatVersion { get; init; } = 1;
        public string Status { get; set; } = "failed";
        public DateTime GeneratedAtUtc { get; set; }
        public required ReleaseCandidateIdentity ReleaseCandidate { get; init; }
        public required ToolchainEvidence Toolchain { get; init; }
        public required DatabaseEvidence Database { get; set; }
        public FreshDatabaseEvidence? FreshDatabase { get; set; }
        public PreviousReleaseUpgradeEvidence? PreviousReleaseUpgrade { get; set; }
        public FailureRollbackEvidence? FailureRollback { get; set; }
        public required EncryptedRecoveryIntegration EncryptedRecoveryIntegration { get; init; }
        public List<string> Failures { get; } = [];
    }
}
