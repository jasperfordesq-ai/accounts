using System.Security.Cryptography;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

// Item 2(b) on PostgreSQL: the AddSizeClassificationInputBasis migration back-fills every existing
// classification as Computed, the CHECK constraint refuses an anchor without its evidence and
// attestation, and a recorded anchor's fingerprint survives the database round trip.
public sealed class SizeClassificationHistoricAnchorPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const string MigrationBeforeInputBasis = "20261011211514_AddExplicitAccountCategoryClassification";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = $"historic_anchor_{Guid.NewGuid():N}";
    private string? scopedConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var create = admin.CreateCommand();
        create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
        await create.ExecuteNonQueryAsync();
        scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Migration_BackfillsExistingRowsAsComputed_AndConstrainsAnchors()
    {
        int periodId;
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            periodId = (await SeedPeriodsAsync(db)).Earlier.Id;
            // Re-apply the new migration over a retained classification row created before it.
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeInputBasis);
        }

        await ExecuteAsync($"""
            INSERT INTO size_classifications
                ("PeriodId", "Turnover", "BalanceSheetTotal", "AvgEmployees", "CalculatedClass", "RawCurrentClass",
                 "RawCurrentMicroQualified", "RawCurrentSmallQualified", "RawCurrentMediumQualified",
                 "AnnualisedTurnover", "PeriodLengthInYears", "OverrideRequiresRereview", "CalculatedAt",
                 "DecisionInputFingerprintSha256")
            VALUES ({periodId}, 1000.00, 500.00, 1, 'Micro', 'Micro', TRUE, TRUE, TRUE, 1000.00, 1, FALSE, CURRENT_TIMESTAMP, repeat('d', 64));
            """);

        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var retained = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == periodId);
            Assert.Equal(SizeInputBasis.Computed, retained.InputBasis);
            Assert.Null(retained.HistoricEvidenceSha256);
            Assert.Equal(new string('d', 64), retained.DecisionInputFingerprintSha256);
        }

        var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"""UPDATE size_classifications SET "InputBasis" = 'HistoricAnchor' WHERE "PeriodId" = {periodId}"""));
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        Assert.Equal("CK_size_classifications_historic_anchor_evidence", violation.ConstraintName);

        var unknownBasis = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"""UPDATE size_classifications SET "InputBasis" = 'Keyed' WHERE "PeriodId" = {periodId}"""));
        Assert.Equal(PostgresErrorCodes.CheckViolation, unknownBasis.SqlState);
    }

    [PostgresFact]
    public async Task RecordedAnchor_SurvivesTheRoundTrip_AndRootsTheSuccessorsChain()
    {
        AccountingPeriod earlier;
        AccountingPeriod later;
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            (earlier, later) = await SeedPeriodsAsync(db);
            var evidence = "%PDF-1.4\n% fictitious filed accounts\n%%EOF\n"u8.ToArray();
            await Service(db).RecordHistoricAnchorAsync(
                earlier.CompanyId,
                earlier.Id,
                new HistoricSizeFiguresRequest(
                    1_000_000m,
                    500_000m,
                    5,
                    null,
                    "Fictitious members' accounts, filed with the CRO",
                    evidence,
                    Convert.ToHexStringLower(SHA256.HashData(evidence)),
                    CompanySizeClass.Small),
                "Reviewer",
                "Example Reviewer",
                "user:9");
        }

        await using (var db = CreateDb())
        {
            db.SizeClassifications.Add(new SizeClassification
            {
                PeriodId = later.Id,
                Turnover = 20_000_000m,
                BalanceSheetTotal = 10_000_000m,
                AvgEmployees = 60
            });
            await db.SaveChangesAsync();
            var result = await Service(db).ClassifyAsync(later.CompanyId, later.Id);
            Assert.Equal(CompanySizeClass.Small, result.CalculatedClass);
        }

        await using (var db = CreateDb())
        {
            var anchor = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == earlier.Id);
            Assert.Equal(SizeInputBasis.HistoricAnchor, anchor.InputBasis);
            Assert.Equal(CompanySizeClass.Small, anchor.HistoricAttestedEffectiveClass);
            Assert.NotNull(anchor.HistoricAttestedAt);
            var target = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == later.Id);
            await SizeClassificationService.EnsureDecisionChainCurrentAsync(db, target, new DateOnly(2024, 1, 1), includeTarget: true);
        }
    }

    // Batch B2: a retired anchor keeps its evidence (enforced by the database), records who retired it,
    // when and why, and no longer roots the successor's chain.
    [PostgresFact]
    public async Task RetiredAnchor_KeepsItsEvidenceUnderTheConstraints_AndNoLongerRootsTheChain()
    {
        AccountingPeriod earlier;
        AccountingPeriod later;
        string evidenceSha;
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            (earlier, later) = await SeedPeriodsAsync(db);
            var evidence = "%PDF-1.4\n% fictitious filed accounts\n%%EOF\n"u8.ToArray();
            evidenceSha = Convert.ToHexStringLower(SHA256.HashData(evidence));
            await Service(db).RecordHistoricAnchorAsync(
                earlier.CompanyId,
                earlier.Id,
                new HistoricSizeFiguresRequest(1_000_000m, 500_000m, 5, null, "Fictitious members' accounts, filed with the CRO", evidence, evidenceSha, null),
                "Owner",
                "Example Owner",
                "user:9");
            await Service(db).RetireHistoricAnchorAsync(
                earlier.CompanyId,
                earlier.Id,
                "Fictitious reason: the figures came from the wrong year",
                "Owner",
                "Example Owner",
                "user:9");
        }

        await using (var db = CreateDb())
        {
            var retired = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == earlier.Id);
            Assert.Equal(SizeInputBasis.RetiredHistoricAnchor, retired.InputBasis);
            Assert.Equal(evidenceSha, retired.HistoricEvidenceSha256);
            Assert.NotNull(retired.HistoricEvidenceArtifact);
            Assert.NotNull(retired.HistoricRetiredAt);
            Assert.Equal("Example Owner", retired.HistoricRetiredBy);
            Assert.Null(retired.DecisionInputFingerprintSha256);
            var target = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == later.Id);
            var evaluation = await SizeClassificationService.EvaluateDecisionChainAsync(db, target, new DateOnly(2024, 1, 1), includeTarget: false);
            Assert.NotNull(evaluation.FirstFailure);
            Assert.NotEqual(earlier.Id, evaluation.Periods[0].Id);
        }

        foreach (var (statement, constraint) in new[]
        {
            ("\"HistoricRetiredReason\" = NULL", "CK_size_classifications_historic_anchor_retirement"),
            ("\"HistoricEvidenceArtifact\" = NULL", "CK_size_classifications_historic_anchor_retirement"),
            ("\"HistoricRetiredAt\" = NULL", "CK_size_classifications_historic_anchor_retirement"),
            ("\"InputBasis\" = 'HistoricAnchor'", "CK_size_classifications_historic_anchor_retirement")
        })
        {
            var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                $"""UPDATE size_classifications SET {statement} WHERE "PeriodId" = {earlier.Id}"""));
            Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
            Assert.Equal(constraint, violation.ConstraintName);
        }

        // A plain computed row cannot claim to be retired without the retirement record.
        var unrecorded = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"""UPDATE size_classifications SET "InputBasis" = 'RetiredHistoricAnchor', "HistoricRetiredAt" = NULL, "HistoricRetiredBy" = NULL, "HistoricRetiredReason" = NULL WHERE "PeriodId" = {earlier.Id}"""));
        Assert.Equal(PostgresErrorCodes.CheckViolation, unrecorded.SqlState);

        // Fix round: the retired evidence is archived append-only, under forced row-level security.
        await using (var db = CreateDb())
        {
            var archived = await db.HistoricAnchorEvidenceRecords.AsNoTracking().SingleAsync(r => r.PeriodId == earlier.Id);
            Assert.Equal(HistoricAnchorEvidenceEvent.Retired, archived.Event);
            Assert.Equal(evidenceSha, archived.EvidenceSha256);
            Assert.Equal("%PDF-1.4\n% fictitious filed accounts\n%%EOF\n"u8.ToArray(), archived.EvidenceArtifact);
        }
        foreach (var statement in new[]
        {
            $"""UPDATE historic_anchor_evidence_records SET "RetiredReason" = 'Rewritten' WHERE "PeriodId" = {earlier.Id}""",
            $"""DELETE FROM historic_anchor_evidence_records WHERE "PeriodId" = {earlier.Id}"""
        })
        {
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(statement));
            Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
            Assert.Equal("CK_historic_anchor_evidence_records_immutable", immutable.ConstraintName);
        }
        await using (var connection = new NpgsqlConnection(scopedConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'historic_anchor_evidence_records'::regclass",
                connection);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        }
    }

    // Fix round 2: rolling back the archive migration never drops archived evidence silently.
    [PostgresFact]
    public async Task ArchiveMigrationDown_IsRefusedWhileArchivedEvidenceExists()
    {
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            var (earlier, _) = await SeedPeriodsAsync(db);
            var evidence = "%PDF-1.4\n% fictitious filed accounts\n%%EOF\n"u8.ToArray();
            await Service(db).RecordHistoricAnchorAsync(
                earlier.CompanyId,
                earlier.Id,
                new HistoricSizeFiguresRequest(1_000_000m, 500_000m, 5, null, "Fictitious members' accounts, filed with the CRO", evidence, Convert.ToHexStringLower(SHA256.HashData(evidence)), null),
                "Owner",
                "Example Owner",
                "user:9");
            await Service(db).RetireHistoricAnchorAsync(earlier.CompanyId, earlier.Id, "Fictitious reason", "Owner", "Example Owner", "user:9");
        }

        await using (var db = CreateDb())
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(
                () => db.GetService<IMigrator>().MigrateAsync(MigrationBeforeEvidenceArchive));
            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
            Assert.Contains("archived historic size evidence", refused.MessageText, StringComparison.Ordinal);
        }

        await using (var db = CreateDb())
        {
            // The archive migration is still applied (later migrations may have been rolled back first).
            Assert.DoesNotContain(await db.Database.GetPendingMigrationsAsync(), migration => migration.EndsWith("_AddHistoricAnchorEvidenceArchive", StringComparison.Ordinal));
            await db.Database.MigrateAsync();
            Assert.Single(await db.HistoricAnchorEvidenceRecords.AsNoTracking().ToListAsync());
        }
    }

    // PR #83 review: retiring or replacing the anchor while another request finalises a later period
    // that relies on it. Retirement and replacement take the company lock (the one finalisation takes)
    // before reading the anchor and its dependent periods and hold it through the save, so whichever
    // commits second sees the other: here finalisation holds the lock first, so the anchor is refused.
    [PostgresTheory]
    [InlineData("retired")]
    [InlineData("replaced")]
    public async Task AnchorChange_WaitsForAConcurrentFinalisationOfARelyingPeriod_AndIsThenRefused(string action)
    {
        AccountingPeriod earlier;
        AccountingPeriod later;
        var evidence = "%PDF-1.4\n% fictitious filed accounts\n%%EOF\n"u8.ToArray();
        HistoricSizeFiguresRequest Request(decimal turnover) => new(
            turnover, 500_000m, 5, null, "Fictitious members' accounts, filed with the CRO",
            evidence, Convert.ToHexStringLower(SHA256.HashData(evidence)), CompanySizeClass.Small);
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            (earlier, later) = await SeedPeriodsAsync(db);
            await Service(db).RecordHistoricAnchorAsync(earlier.CompanyId, earlier.Id, Request(1_000_000m), "Owner", "Example Owner", "user:9");
        }

        // Finalisation of the later period: the company and period advisory locks and the FOR UPDATE
        // row lock, held in its transaction until it commits.
        await using var finaliser = CreateDb(protectWrites: true);
        var lease = await new AccountingConcurrencyCoordinator(finaliser).AcquirePeriodAsync(later.CompanyId, later.Id);
        var finalising = await finaliser.AccountingPeriods.SingleAsync(p => p.Id == later.Id);
        finalising.Status = PeriodStatus.Finalised;
        finalising.LockedAt = DateTime.UtcNow;
        await finaliser.SaveChangesAsync();

        var anchorApplication = $"historic-anchor-{action}-{Guid.NewGuid():N}";
        var change = Task.Run(async () =>
        {
            await using var db = CreateDb(protectWrites: true, applicationName: anchorApplication);
            if (action == "retired")
                await Service(db).RetireHistoricAnchorAsync(earlier.CompanyId, earlier.Id, "Fictitious reason: wrong year", "Owner", "Example Owner", "user:9");
            else
                await Service(db).RecordHistoricAnchorAsync(earlier.CompanyId, earlier.Id, Request(1_100_000m), "Owner", "Example Owner", "user:9");
        });

        Assert.True(await WaitForAdvisoryLockWaitAsync(anchorApplication), "the anchor change should wait for the finalisation's lock");
        await lease.CommitIfOwnedAsync();
        await lease.DisposeAsync();

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => change.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.StartsWith($"These historic figures cannot be {action}:", refused.Message, StringComparison.Ordinal);
        await using (var db = CreateDb())
        {
            var anchor = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == earlier.Id);
            Assert.Equal(SizeInputBasis.HistoricAnchor, anchor.InputBasis);
            Assert.Equal(1_000_000m, anchor.Turnover);
            Assert.Empty(await db.HistoricAnchorEvidenceRecords.AsNoTracking().ToListAsync());
            Assert.Equal(PeriodStatus.Finalised, (await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == later.Id)).Status);
        }
    }

    private async Task<bool> WaitForAdvisoryLockWaitAsync(string applicationName)
    {
        await using var observer = new NpgsqlConnection(scopedConnectionString);
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 400; attempt++)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_locks AS lock
                    INNER JOIN pg_stat_activity AS activity ON activity.pid = lock.pid
                    WHERE activity.application_name = @applicationName
                      AND lock.locktype = 'advisory'
                      AND NOT lock.granted)
                """, observer);
            command.Parameters.AddWithValue("applicationName", applicationName);
            if (await command.ExecuteScalarAsync() is true) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private const string MigrationBeforeEvidenceArchive = "20261012055305_AddCorporationTaxDueConfirmedLiability";

    private async Task<(AccountingPeriod Earlier, AccountingPeriod Later)> SeedPeriodsAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Historic Anchor Tenant", Slug = $"historic-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Historic Anchor CLG",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2023, 1, 1)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        // The chronology trigger needs a first year from incorporation and contiguous periods; the first
        // year stays unclassified, so the successor's chain can only be rooted at the anchor.
        AccountingPeriod Add(DateOnly start, DateOnly end, bool firstYear)
        {
            var period = new AccountingPeriod { CompanyId = company.Id, PeriodStart = start, PeriodEnd = end, IsFirstYear = firstYear };
            db.AccountingPeriods.Add(period);
            return period;
        }
        Add(new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31), firstYear: true);
        await db.SaveChangesAsync();
        var earlier = Add(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), firstYear: false);
        await db.SaveChangesAsync();
        var later = Add(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), firstYear: false);
        await db.SaveChangesAsync();
        return (earlier, later);
    }

    private static SizeClassificationService Service(AccountsDbContext db) =>
        new(db, Options.Create(new SizeThresholdConfig()), new AuditService(db));

    private AccountsDbContext CreateDb(bool protectWrites = false, string? applicationName = null)
    {
        var connectionString = applicationName is null
            ? scopedConnectionString
            : new NpgsqlConnectionStringBuilder(scopedConnectionString) { ApplicationName = applicationName }.ConnectionString;
        var options = new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(connectionString);
        // The production save interceptor: company and period advisory locks at save time.
        if (protectWrites)
            options.AddInterceptors(new AccountingConcurrencyInterceptor());
        return new AccountsDbContext(options.Options);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(scopedConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
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

    private sealed class PostgresTheoryAttribute : TheoryAttribute
    {
        public PostgresTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
