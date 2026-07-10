using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class ExternalFilingHandoffPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const string MigrationId = "20260711020000_AddExternalFilingHandoffLedger";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "external_filing_handoff_" + Guid.NewGuid().ToString("N");
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
        scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
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
    public async Task Migration_EnforcesAppendOnlyScopeExactHashesAndAmendmentChains()
    {
        var configured = options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var connection = scopedConnectionString ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        long firstAuthorityId;
        long firstSnapshotRecordId;
        Guid firstSnapshotId;
        string firstArtifactSha;
        long amendmentRecordId;
        Guid amendmentSnapshotId;
        string amendmentArtifactSha;
        int firstTenantId;
        int firstCompanyId;
        int firstPeriodId;
        int secondCompanyId;
        int secondPeriodId;

        await using (var db = new AccountsDbContext(configured))
        {
            Assert.Contains(MigrationId, await db.Database.GetAppliedMigrationsAsync());
            var first = Company("FILE001 PostgreSQL Firm A", "file001-pg-a");
            var second = Company("FILE001 PostgreSQL Firm B", "file001-pg-b");
            db.AddRange(first.Tenant, second.Tenant);
            await db.SaveChangesAsync();
            db.AddRange(first.Company, second.Company);
            await db.SaveChangesAsync();
            var firstPeriod = Period(first.Company.Id);
            var secondPeriod = Period(second.Company.Id);
            db.AddRange(firstPeriod, secondPeriod);
            await db.SaveChangesAsync();

            var firstAuthority = Authority(first.Tenant.Id, first.Company.Id, "AUTHORITY-A");
            var secondAuthority = Authority(second.Tenant.Id, second.Company.Id, "AUTHORITY-B");
            db.AddRange(firstAuthority, secondAuthority);
            await db.SaveChangesAsync();

            var initialBuild = ExternalFilingHandoffArtifactBuilder.BuildInitial(Request(
                first.Tenant.Id,
                first.Company.Id,
                firstPeriod.Id,
                AuthorityProjection(firstAuthority),
                Guid.NewGuid()));
            Assert.True(initialBuild.Document.ReadyForManualHandoff, string.Join(" | ", initialBuild.Document.BlockingIssues));
            var initial = Snapshot(initialBuild, firstAuthority.Id);
            db.Add(initial);
            await db.SaveChangesAsync();

            var amendedBuild = ExternalFilingHandoffArtifactBuilder.BuildAmendment(
                Request(first.Tenant.Id, first.Company.Id, firstPeriod.Id, AuthorityProjection(firstAuthority), Guid.NewGuid()),
                initialBuild,
                "Correct the retained shareholder evidence after the external review.");
            var amended = Snapshot(amendedBuild, firstAuthority.Id);
            amended.SupersedesSnapshotRecordId = initial.Id;
            db.Add(amended);
            await db.SaveChangesAsync();

            firstTenantId = first.Tenant.Id;
            firstCompanyId = first.Company.Id;
            firstPeriodId = firstPeriod.Id;
            secondCompanyId = second.Company.Id;
            secondPeriodId = secondPeriod.Id;
            firstAuthorityId = firstAuthority.Id;
            firstSnapshotRecordId = initial.Id;
            firstSnapshotId = initial.SnapshotId;
            firstArtifactSha = initial.ArtifactSha256;
            amendmentRecordId = amended.Id;
            amendmentSnapshotId = amended.SnapshotId;
            amendmentArtifactSha = amended.ArtifactSha256;
        }

        await using (var tenantDb = TenantContext(configured, firstTenantId, firstCompanyId))
        {
            Assert.Single(await tenantDb.FilingAuthorityEngagements.ToListAsync());
            Assert.Equal(2, await tenantDb.ExternalFilingHandoffSnapshots.CountAsync());
            Assert.Equal(2, await tenantDb.FilingAuthorityEngagements.IgnoreQueryFilters().CountAsync());
            var retained = await tenantDb.ExternalFilingHandoffSnapshots.SingleAsync(item => item.Id == firstSnapshotRecordId);
            Assert.Equal(firstArtifactSha, Convert.ToHexStringLower(SHA256.HashData(retained.ArtifactBytes)));
            var parsed = ExternalFilingHandoffArtifactBuilder.ParseRetainedArtifact(retained.ArtifactBytes, retained.ArtifactSha256);
            Assert.Equal(firstSnapshotId, parsed.SnapshotId);
            Assert.False(parsed.DirectSubmissionSupported);
            Assert.False(parsed.IsCompleteExternalReturn);
        }

        var authorityMutation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "UPDATE filing_authority_engagements SET \"AuthorityScope\" = 'MUTATION MUST FAIL' WHERE \"Id\" = @id",
            new NpgsqlParameter("id", firstAuthorityId)));
        Assert.Equal("CK_external_filing_handoff_evidence_immutable", authorityMutation.ConstraintName);

        var snapshotMutation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "DELETE FROM external_filing_handoff_snapshots WHERE \"Id\" = @id",
            new NpgsqlParameter("id", firstSnapshotRecordId)));
        Assert.Equal("CK_external_filing_handoff_evidence_immutable", snapshotMutation.ConstraintName);

        var crossScope = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO external_filing_handoff_snapshots
                ("SnapshotId", "TenantId", "CompanyId", "PeriodId", "Workflow", "Version",
                 "SupersedesSnapshotRecordId", "SupersedesSnapshotId", "SupersedesArtifactSha256", "AmendmentReason",
                 "SchemaVersion", "ArtifactBytes", "ArtifactSha256", "SourceFingerprintSha256", "AuthorityId",
                 "AuthorityEvidenceSha256", "QualifiedReviewManifestSha256", "ReleaseCandidate",
                 "DirectSubmissionSupported", "IsCompleteExternalReturn", "ReadyForManualHandoff",
                 "PreparedByUserId", "PreparedByDisplayName", "PreparedByRole", "PreparedAtUtc")
            SELECT @newId, @tenantId, @otherCompanyId, @otherPeriodId, "Workflow", 1,
                   NULL, NULL, NULL, NULL, "SchemaVersion", "ArtifactBytes", "ArtifactSha256", "SourceFingerprintSha256",
                   "AuthorityId", "AuthorityEvidenceSha256", "QualifiedReviewManifestSha256", "ReleaseCandidate",
                   FALSE, FALSE, "ReadyForManualHandoff", "PreparedByUserId", "PreparedByDisplayName", "PreparedByRole", "PreparedAtUtc"
            FROM external_filing_handoff_snapshots WHERE "Id" = @sourceId
            """,
            new NpgsqlParameter("newId", Guid.NewGuid()),
            new NpgsqlParameter("tenantId", firstTenantId),
            new NpgsqlParameter("otherCompanyId", secondCompanyId),
            new NpgsqlParameter("otherPeriodId", secondPeriodId),
            new NpgsqlParameter("sourceId", firstSnapshotRecordId)));
        Assert.Equal("CK_external_filing_handoff_snapshots_scope", crossScope.ConstraintName);

        var brokenVersion = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO external_filing_handoff_snapshots
                ("SnapshotId", "TenantId", "CompanyId", "PeriodId", "Workflow", "Version",
                 "SupersedesSnapshotRecordId", "SupersedesSnapshotId", "SupersedesArtifactSha256", "AmendmentReason",
                 "SchemaVersion", "ArtifactBytes", "ArtifactSha256", "SourceFingerprintSha256", "AuthorityId",
                 "AuthorityEvidenceSha256", "QualifiedReviewManifestSha256", "ReleaseCandidate",
                 "DirectSubmissionSupported", "IsCompleteExternalReturn", "ReadyForManualHandoff",
                 "PreparedByUserId", "PreparedByDisplayName", "PreparedByRole", "PreparedAtUtc")
            SELECT @newId, "TenantId", "CompanyId", "PeriodId", "Workflow", 3,
                   @sourceId, "SnapshotId", "ArtifactSha256", 'Invalid skipped version in amendment chain',
                   "SchemaVersion", "ArtifactBytes", "ArtifactSha256", "SourceFingerprintSha256", "AuthorityId",
                   "AuthorityEvidenceSha256", "QualifiedReviewManifestSha256", "ReleaseCandidate",
                   FALSE, FALSE, "ReadyForManualHandoff", "PreparedByUserId", "PreparedByDisplayName", "PreparedByRole", "PreparedAtUtc"
            FROM external_filing_handoff_snapshots WHERE "Id" = @sourceId
            """,
            new NpgsqlParameter("newId", Guid.NewGuid()),
            new NpgsqlParameter("sourceId", firstSnapshotRecordId)));
        Assert.Equal("CK_external_filing_handoff_snapshots_predecessor", brokenVersion.ConstraintName);

        var badSuccessor = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO external_filing_outcome_events
                ("TenantId", "CompanyId", "PeriodId", "SnapshotRecordId", "SnapshotId", "SnapshotArtifactSha256",
                 "Sequence", "Outcome", "ExternalReference", "ExternalOccurredAtUtc", "Reason", "CorrectionDeadlineUtc",
                 "EvidenceReference", "EvidenceArtifact", "EvidenceSha256", "SupersedingSnapshotRecordId",
                 "SupersedingSnapshotId", "SupersedingSnapshotArtifactSha256", "RecordedByUserId",
                 "RecordedByDisplayName", "RecordedByRole", "RecordedAtUtc", "EventSha256")
            VALUES
                (@tenantId, @companyId, @periodId, @snapshotRecordId, @snapshotId, @snapshotSha,
                 1, 'SupersededByAmendment', NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                 @successorRecordId, @successorId, @wrongSuccessorSha, 'reviewer@example.invalid',
                 'PostgreSQL Reviewer', 'Reviewer', CURRENT_TIMESTAMP, repeat('e', 64))
            """,
            new NpgsqlParameter("tenantId", firstTenantId),
            new NpgsqlParameter("companyId", firstCompanyId),
            new NpgsqlParameter("periodId", firstPeriodId),
            new NpgsqlParameter("snapshotRecordId", firstSnapshotRecordId),
            new NpgsqlParameter("snapshotId", firstSnapshotId),
            new NpgsqlParameter("snapshotSha", firstArtifactSha),
            new NpgsqlParameter("successorRecordId", amendmentRecordId),
            new NpgsqlParameter("successorId", amendmentSnapshotId),
            new NpgsqlParameter("wrongSuccessorSha", new string('f', 64))));
        Assert.Equal("CK_external_filing_outcome_events_successor", badSuccessor.ConstraintName);

        Assert.NotEqual(firstArtifactSha, amendmentArtifactSha);
    }

    private static (Tenant Tenant, Company Company) Company(string name, string slug) {
        var tenant = new Tenant { Name = name, Slug = $"{slug}-{Guid.NewGuid():N}" };
        return (tenant, new Company
        {
            Tenant = tenant,
            LegalName = name + " Limited",
            CroNumber = Guid.NewGuid().ToString("N")[..8],
            TaxReference = Guid.NewGuid().ToString("N")[..8],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2026, 9, 30),
            IsTrading = true
        });
    }

    private static AccountingPeriod Period(int companyId) => new()
    {
        CompanyId = companyId,
        PeriodStart = new DateOnly(2025, 1, 1),
        PeriodEnd = new DateOnly(2025, 12, 31),
        IsFirstYear = true
    };

    private static FilingAuthorityEngagement Authority(int tenantId, int companyId, string reference)
    {
        var bytes = Encoding.UTF8.GetBytes(reference + " exact authority evidence");
        var now = DateTime.UtcNow.AddMinutes(-1);
        return new FilingAuthorityEngagement
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Version = 1,
            Workflow = ExternalFilingWorkflow.CroB1.ToString(),
            Kind = ExternalFilingAuthorityKind.CroElectronicFilingAgent.ToString(),
            Status = ExternalFilingAuthorityStatus.Active.ToString(),
            LegalName = "PostgreSQL Presenter Limited",
            MaskedPresenterOrTain = "EFA-****42",
            AuthorityScope = "Prepare the retained B1 manual handoff.",
            EngagementReference = reference + "-ENGAGEMENT",
            ExternalAuthorityReference = reference + "-B77",
            EffectiveFromUtc = now.AddDays(-1),
            EffectiveUntilUtc = now.AddDays(30),
            AuthorityEvidenceArtifact = bytes,
            AuthorityEvidenceSha256 = Sha(bytes),
            EvidenceMediaType = "application/pdf",
            EvidenceFileName = "authority.pdf",
            ReviewedByUserId = "reviewer@example.invalid",
            ReviewedByDisplayName = "PostgreSQL Reviewer",
            ReviewedByRole = "Reviewer",
            ReviewedAtUtc = now,
            ReleaseCandidate = "file001-postgres-candidate",
            RecordSha256 = new string('c', 64),
            CreatedByUserId = "reviewer@example.invalid",
            CreatedByDisplayName = "PostgreSQL Reviewer",
            CreatedByRole = "Reviewer",
            CreatedAtUtc = now
        };
    }

    private static ExternalFilingAuthoritySnapshot AuthorityProjection(FilingAuthorityEngagement item) => new(
        item.Id, item.TenantId, item.CompanyId, ExternalFilingWorkflow.CroB1,
        ExternalFilingAuthorityKind.CroElectronicFilingAgent, ExternalFilingAuthorityStatus.Active,
        item.LegalName, null, item.MaskedPresenterOrTain, item.AuthorityScope, item.EngagementReference,
        item.ExternalAuthorityReference, item.EffectiveFromUtc, item.EffectiveUntilUtc, null,
        item.AuthorityEvidenceSha256, item.EvidenceMediaType, item.EvidenceFileName,
        new ExternalFilingActor(item.ReviewedByUserId, item.ReviewedByDisplayName, item.ReviewedByRole),
        item.ReviewedAtUtc, item.ReleaseCandidate);

    private static ExternalFilingHandoffBuildRequest Request(int tenantId, int companyId, int periodId, ExternalFilingAuthoritySnapshot authority, Guid snapshotId)
    {
        var actor = new ExternalFilingActor("accountant@example.invalid", "PostgreSQL Accountant", "Accountant");
        var fields = ExternalFilingHandoffArtifactBuilder.RequiredCroFieldCodes.Select(code =>
            new ExternalHandoffField(
                code,
                "B1 retained field",
                code,
                code == "b1.officers.protected-identity-entry" ? "Confirmed protected entry" : "Confirmed",
                ExternalHandoffFieldStatus.Complete,
                "RETAINED-SOURCE-" + code,
                null,
                code == "b1.officers.protected-identity-entry")).ToArray();
        var cro = new B1ManualHandoffFacts(
            "765432", "PostgreSQL Fixture Limited", "Private", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30),
            "RetainExistingAnnualReturnDate", new ExternalHandoffAddress("1 Main Street", null, "Dublin", null, null, null, null),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), true, false, true, "AUDIT-EXEMPT-01", "EUR",
            false, 0m, "POLITICAL-EVIDENCE-01", "Director", "Secretary",
            [new B1OfficerHandoff(1, "Aoife", "Director", "Director", new DateOnly(2020, 1, 1), null,
                new ExternalHandoffAddress("1 Main Street", null, "Dublin", null, null, null, null),
                "Protected CORE entry", "CORE-IDENTITY-01", new string('a', 64), null, "DIRECTORSHIPS-01", true)],
            [new B1ShareClassHandoff("Ordinary", "EUR", 1m, 100, 100m, 100m, 0m)],
            [new B1ShareholderHandoff("MEMBER-01", "Fixture Member", new ExternalHandoffAddress("2 Main Street", null, "Dublin", null, null, null, null), "Ordinary", "EUR", 100m, 100m, "100 Ordinary shares", "REGISTER-01")],
            [], new string('1', 64), new string('2', 64), null);
        return new ExternalFilingHandoffBuildRequest(
            snapshotId, tenantId, companyId, periodId, ExternalFilingWorkflow.CroB1,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), DateTime.UtcNow, actor, authority,
            new string('3', 64), authority.ReleaseCandidate, cro, null, fields,
            [new ExternalFilingAttachment("accounts-pdf", "accounts.pdf", "application/pdf", 100, new string('1', 64), "RETAINED-ACCOUNTS"),
             new ExternalFilingAttachment("signature-page", "signature.pdf", "application/pdf", 50, new string('2', 64), "RETAINED-SIGNATURE")]);
    }

    private static ExternalFilingHandoffSnapshot Snapshot(ExternalFilingHandoffBuild build, long authorityId) => new()
    {
        SnapshotId = build.Document.SnapshotId,
        TenantId = build.Document.TenantId,
        CompanyId = build.Document.CompanyId,
        PeriodId = build.Document.PeriodId,
        Workflow = build.Document.Workflow.ToString(),
        Version = build.Document.Version,
        SupersedesSnapshotId = build.Document.SupersedesSnapshotId,
        SupersedesArtifactSha256 = build.Document.SupersedesArtifactSha256,
        AmendmentReason = build.Document.AmendmentReason,
        SchemaVersion = build.Document.SchemaVersion,
        ArtifactBytes = build.ArtifactBytes,
        ArtifactSha256 = build.ArtifactSha256,
        SourceFingerprintSha256 = build.Document.SourceFingerprintSha256,
        AuthorityId = authorityId,
        AuthorityEvidenceSha256 = build.Document.Authority.AuthorityEvidenceSha256,
        QualifiedReviewManifestSha256 = build.Document.QualifiedReviewManifestSha256,
        ReleaseCandidate = build.Document.ReleaseCandidate,
        DirectSubmissionSupported = false,
        IsCompleteExternalReturn = false,
        ReadyForManualHandoff = build.Document.ReadyForManualHandoff,
        PreparedByUserId = build.Document.PreparedBy.UserId,
        PreparedByDisplayName = build.Document.PreparedBy.DisplayName,
        PreparedByRole = build.Document.PreparedBy.Role,
        PreparedAtUtc = build.Document.PreparedAtUtc
    };

    private static AccountsDbContext TenantContext(DbContextOptions<AccountsDbContext> configured, int tenantId, int companyId)
    {
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(991, tenantId, "FILE001 PostgreSQL Firm", "pg@example.invalid", "PostgreSQL Accountant", "Accountant", new HashSet<int> { companyId });
        return new AccountsDbContext(configured, new HttpContextAccessor { HttpContext = context });
    }

    private static async Task<int> ExecuteAsync(string connectionString, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
