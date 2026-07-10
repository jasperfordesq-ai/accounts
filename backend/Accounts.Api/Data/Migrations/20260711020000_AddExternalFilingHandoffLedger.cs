using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    // Canonically ordered after the tenant-scoped idempotency ledger.
    /// <inheritdoc />
    public partial class AddExternalFilingHandoffLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "filing_authority_engagements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SupersedesAuthorityId = table.Column<long>(type: "bigint", nullable: true),
                    Workflow = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PracticeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MaskedPresenterOrTain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AuthorityScope = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EngagementReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExternalAuthorityReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AuthorityEvidenceArtifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    AuthorityEvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceMediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EvidenceFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ReviewedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewedByRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleaseCandidate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedByRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filing_authority_engagements", x => x.Id);
                    table.CheckConstraint("CK_filing_authority_engagements_sha256", "length(\"AuthorityEvidenceSha256\") = 64 AND length(\"RecordSha256\") = 64");
                    table.CheckConstraint("CK_filing_authority_engagements_status_dates", "(\"Status\" = 'Active' AND \"RevokedAtUtc\" IS NULL) OR (\"Status\" = 'Revoked' AND \"RevokedAtUtc\" IS NOT NULL) OR (\"Status\" = 'Expired' AND \"EffectiveUntilUtc\" IS NOT NULL) OR \"Status\" IN ('Draft', 'Pending')");
                    table.CheckConstraint("CK_filing_authority_engagements_version_chain", "(\"Version\" = 1 AND \"SupersedesAuthorityId\" IS NULL) OR (\"Version\" > 1 AND \"SupersedesAuthorityId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_filing_authority_engagements_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_filing_authority_engagements_filing_authority_engagements_S~",
                        column: x => x.SupersedesAuthorityId,
                        principalTable: "filing_authority_engagements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_filing_authority_engagements_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_filing_handoff_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Workflow = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SupersedesSnapshotRecordId = table.Column<long>(type: "bigint", nullable: true),
                    SupersedesSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedesArtifactSha256 = table.Column<string>(type: "text", nullable: true),
                    AmendmentReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SchemaVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ArtifactBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFingerprintSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorityId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorityEvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QualifiedReviewManifestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReleaseCandidate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DirectSubmissionSupported = table.Column<bool>(type: "boolean", nullable: false),
                    IsCompleteExternalReturn = table.Column<bool>(type: "boolean", nullable: false),
                    ReadyForManualHandoff = table.Column<bool>(type: "boolean", nullable: false),
                    PreparedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PreparedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreparedByRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_filing_handoff_snapshots", x => x.Id);
                    table.CheckConstraint("CK_external_filing_handoff_snapshots_manual_only", "NOT \"DirectSubmissionSupported\" AND NOT \"IsCompleteExternalReturn\"");
                    table.CheckConstraint("CK_external_filing_handoff_snapshots_sha256", "length(\"ArtifactSha256\") = 64 AND length(\"SourceFingerprintSha256\") = 64 AND length(\"AuthorityEvidenceSha256\") = 64 AND length(\"QualifiedReviewManifestSha256\") = 64");
                    table.CheckConstraint("CK_external_filing_handoff_snapshots_version_chain", "(\"Version\" = 1 AND \"SupersedesSnapshotRecordId\" IS NULL AND \"SupersedesSnapshotId\" IS NULL AND \"SupersedesArtifactSha256\" IS NULL AND \"AmendmentReason\" IS NULL) OR (\"Version\" > 1 AND \"SupersedesSnapshotRecordId\" IS NOT NULL AND \"SupersedesSnapshotId\" IS NOT NULL AND length(\"SupersedesArtifactSha256\") = 64 AND length(\"AmendmentReason\") >= 10)");
                    table.ForeignKey(
                        name: "FK_external_filing_handoff_snapshots_accounting_periods_Period~",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_handoff_snapshots_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_handoff_snapshots_external_filing_handoff_s~",
                        column: x => x.SupersedesSnapshotRecordId,
                        principalTable: "external_filing_handoff_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_handoff_snapshots_filing_authority_engageme~",
                        column: x => x.AuthorityId,
                        principalTable: "filing_authority_engagements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_handoff_snapshots_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_filing_outcome_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    SnapshotRecordId = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotArtifactSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExternalOccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CorrectionDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EvidenceArtifact = table.Column<byte[]>(type: "bytea", nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupersedingSnapshotRecordId = table.Column<long>(type: "bigint", nullable: true),
                    SupersedingSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedingSnapshotArtifactSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RecordedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedByRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EventSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_filing_outcome_events", x => x.Id);
                    table.CheckConstraint("CK_external_filing_outcome_events_correction", "(\"Outcome\" = 'CorrectionRequired' AND length(\"Reason\") >= 5 AND \"CorrectionDeadlineUtc\" > \"ExternalOccurredAtUtc\") OR (\"Outcome\" = 'ExternallyRejected' AND length(\"Reason\") >= 5 AND \"CorrectionDeadlineUtc\" IS NULL) OR (\"Outcome\" NOT IN ('CorrectionRequired', 'ExternallyRejected') AND \"CorrectionDeadlineUtc\" IS NULL)");
                    table.CheckConstraint("CK_external_filing_outcome_events_evidence_shape", "(\"Outcome\" = 'ReadyForManualHandoff' AND \"ExternalReference\" IS NULL AND \"ExternalOccurredAtUtc\" IS NULL AND \"EvidenceReference\" IS NULL AND \"EvidenceArtifact\" IS NULL AND \"EvidenceSha256\" IS NULL AND \"CorrectionDeadlineUtc\" IS NULL AND \"SupersedingSnapshotRecordId\" IS NULL AND \"SupersedingSnapshotId\" IS NULL AND \"SupersedingSnapshotArtifactSha256\" IS NULL) OR (\"Outcome\" = 'SupersededByAmendment' AND \"ExternalReference\" IS NULL AND \"ExternalOccurredAtUtc\" IS NULL AND \"EvidenceReference\" IS NULL AND \"EvidenceArtifact\" IS NULL AND \"EvidenceSha256\" IS NULL AND \"CorrectionDeadlineUtc\" IS NULL AND \"SupersedingSnapshotRecordId\" IS NOT NULL AND \"SupersedingSnapshotId\" IS NOT NULL AND \"SupersedingSnapshotArtifactSha256\" IS NOT NULL) OR (\"Outcome\" IN ('ExternallySubmittedRecorded', 'CorrectionRequired', 'ExternallyRejected', 'ExternallyAcceptedRecorded') AND \"ExternalReference\" IS NOT NULL AND \"ExternalOccurredAtUtc\" IS NOT NULL AND \"EvidenceReference\" IS NOT NULL AND \"EvidenceArtifact\" IS NOT NULL AND \"EvidenceSha256\" IS NOT NULL AND \"SupersedingSnapshotRecordId\" IS NULL AND \"SupersedingSnapshotId\" IS NULL AND \"SupersedingSnapshotArtifactSha256\" IS NULL)");
                    table.CheckConstraint("CK_external_filing_outcome_events_hashes", "length(\"SnapshotArtifactSha256\") = 64 AND length(\"EventSha256\") = 64 AND (\"EvidenceSha256\" IS NULL OR length(\"EvidenceSha256\") = 64) AND (\"SupersedingSnapshotArtifactSha256\" IS NULL OR length(\"SupersedingSnapshotArtifactSha256\") = 64)");
                    table.ForeignKey(
                        name: "FK_external_filing_outcome_events_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_outcome_events_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_outcome_events_external_filing_handoff_snap~",
                        column: x => x.SnapshotRecordId,
                        principalTable: "external_filing_handoff_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_outcome_events_external_filing_handoff_sna~1",
                        column: x => x.SupersedingSnapshotRecordId,
                        principalTable: "external_filing_handoff_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_filing_outcome_events_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_handoff_snapshots_AuthorityId",
                table: "external_filing_handoff_snapshots",
                column: "AuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_handoff_snapshots_CompanyId_PeriodId_Workfl~",
                table: "external_filing_handoff_snapshots",
                columns: new[] { "CompanyId", "PeriodId", "Workflow", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_handoff_snapshots_PeriodId",
                table: "external_filing_handoff_snapshots",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_handoff_snapshots_SnapshotId",
                table: "external_filing_handoff_snapshots",
                column: "SnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_handoff_snapshots_SupersedesSnapshotRecordId",
                table: "external_filing_handoff_snapshots",
                column: "SupersedesSnapshotRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_handoff_snapshots_TenantId_CompanyId_Period~",
                table: "external_filing_handoff_snapshots",
                columns: new[] { "TenantId", "CompanyId", "PeriodId", "PreparedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_outcome_events_CompanyId",
                table: "external_filing_outcome_events",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_outcome_events_PeriodId",
                table: "external_filing_outcome_events",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_outcome_events_SnapshotRecordId_Sequence",
                table: "external_filing_outcome_events",
                columns: new[] { "SnapshotRecordId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_outcome_events_SupersedingSnapshotRecordId",
                table: "external_filing_outcome_events",
                column: "SupersedingSnapshotRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_external_filing_outcome_events_TenantId_CompanyId_PeriodId_~",
                table: "external_filing_outcome_events",
                columns: new[] { "TenantId", "CompanyId", "PeriodId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_filing_authority_engagements_CompanyId_Workflow_Version",
                table: "filing_authority_engagements",
                columns: new[] { "CompanyId", "Workflow", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_filing_authority_engagements_SupersedesAuthorityId",
                table: "filing_authority_engagements",
                column: "SupersedesAuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_filing_authority_engagements_TenantId_CompanyId_Workflow_Cr~",
                table: "filing_authority_engagements",
                columns: new[] { "TenantId", "CompanyId", "Workflow", "CreatedAtUtc" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounts_prevent_external_filing_evidence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'External filing handoff evidence is append-only.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_external_filing_handoff_evidence_immutable';
                END;
                $function$;

                CREATE TRIGGER "TR_filing_authority_engagements_immutable"
                    BEFORE UPDATE OR DELETE ON filing_authority_engagements
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_external_filing_evidence_mutation();
                CREATE TRIGGER "TR_external_filing_handoff_snapshots_immutable"
                    BEFORE UPDATE OR DELETE ON external_filing_handoff_snapshots
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_external_filing_evidence_mutation();
                CREATE TRIGGER "TR_external_filing_outcome_events_immutable"
                    BEFORE UPDATE OR DELETE ON external_filing_outcome_events
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_external_filing_evidence_mutation();

                CREATE OR REPLACE FUNCTION accounts_validate_filing_authority_scope()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    company_tenant integer;
                    predecessor filing_authority_engagements%ROWTYPE;
                BEGIN
                    SELECT "TenantId" INTO company_tenant FROM companies WHERE "Id" = NEW."CompanyId";
                    IF company_tenant IS DISTINCT FROM NEW."TenantId" THEN
                        RAISE EXCEPTION 'Filing authority tenant/company scope mismatch.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_filing_authority_engagements_scope';
                    END IF;
                    IF NEW."SupersedesAuthorityId" IS NOT NULL THEN
                        SELECT * INTO predecessor FROM filing_authority_engagements WHERE "Id" = NEW."SupersedesAuthorityId";
                        IF predecessor."TenantId" IS DISTINCT FROM NEW."TenantId"
                           OR predecessor."CompanyId" IS DISTINCT FROM NEW."CompanyId"
                           OR predecessor."Workflow" IS DISTINCT FROM NEW."Workflow"
                           OR predecessor."Version" + 1 IS DISTINCT FROM NEW."Version" THEN
                            RAISE EXCEPTION 'Filing authority predecessor scope or version mismatch.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_filing_authority_engagements_predecessor';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "TR_filing_authority_engagements_scope"
                    BEFORE INSERT ON filing_authority_engagements
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_filing_authority_scope();

                CREATE OR REPLACE FUNCTION accounts_validate_external_filing_snapshot_scope()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    period_company integer;
                    company_tenant integer;
                    authority filing_authority_engagements%ROWTYPE;
                    predecessor external_filing_handoff_snapshots%ROWTYPE;
                BEGIN
                    SELECT "CompanyId" INTO period_company FROM accounting_periods WHERE "Id" = NEW."PeriodId";
                    SELECT "TenantId" INTO company_tenant FROM companies WHERE "Id" = NEW."CompanyId";
                    SELECT * INTO authority FROM filing_authority_engagements WHERE "Id" = NEW."AuthorityId";
                    IF period_company IS DISTINCT FROM NEW."CompanyId"
                       OR company_tenant IS DISTINCT FROM NEW."TenantId"
                       OR authority."TenantId" IS DISTINCT FROM NEW."TenantId"
                       OR authority."CompanyId" IS DISTINCT FROM NEW."CompanyId"
                       OR authority."Workflow" IS DISTINCT FROM NEW."Workflow"
                       OR authority."Status" IS DISTINCT FROM 'Active'
                       OR authority."EffectiveFromUtc" > NEW."PreparedAtUtc"
                       OR (authority."EffectiveUntilUtc" IS NOT NULL AND authority."EffectiveUntilUtc" < NEW."PreparedAtUtc")
                       OR (authority."RevokedAtUtc" IS NOT NULL AND authority."RevokedAtUtc" <= NEW."PreparedAtUtc")
                       OR authority."AuthorityEvidenceSha256" IS DISTINCT FROM NEW."AuthorityEvidenceSha256"
                       OR authority."ReleaseCandidate" IS DISTINCT FROM NEW."ReleaseCandidate" THEN
                        RAISE EXCEPTION 'External filing snapshot tenant, period or current-authority scope mismatch.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_external_filing_handoff_snapshots_scope';
                    END IF;
                    IF NEW."SupersedesSnapshotRecordId" IS NOT NULL THEN
                        SELECT * INTO predecessor FROM external_filing_handoff_snapshots WHERE "Id" = NEW."SupersedesSnapshotRecordId";
                        IF predecessor."TenantId" IS DISTINCT FROM NEW."TenantId"
                           OR predecessor."CompanyId" IS DISTINCT FROM NEW."CompanyId"
                           OR predecessor."PeriodId" IS DISTINCT FROM NEW."PeriodId"
                           OR predecessor."Workflow" IS DISTINCT FROM NEW."Workflow"
                           OR predecessor."Version" + 1 IS DISTINCT FROM NEW."Version"
                           OR predecessor."SnapshotId" IS DISTINCT FROM NEW."SupersedesSnapshotId"
                           OR predecessor."ArtifactSha256" IS DISTINCT FROM NEW."SupersedesArtifactSha256" THEN
                            RAISE EXCEPTION 'External filing snapshot predecessor scope, identity or hash mismatch.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_external_filing_handoff_snapshots_predecessor';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "TR_external_filing_handoff_snapshots_scope"
                    BEFORE INSERT ON external_filing_handoff_snapshots
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_external_filing_snapshot_scope();

                CREATE OR REPLACE FUNCTION accounts_validate_external_filing_outcome_scope()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    snapshot external_filing_handoff_snapshots%ROWTYPE;
                    successor external_filing_handoff_snapshots%ROWTYPE;
                BEGIN
                    SELECT * INTO snapshot FROM external_filing_handoff_snapshots WHERE "Id" = NEW."SnapshotRecordId";
                    IF snapshot."TenantId" IS DISTINCT FROM NEW."TenantId"
                       OR snapshot."CompanyId" IS DISTINCT FROM NEW."CompanyId"
                       OR snapshot."PeriodId" IS DISTINCT FROM NEW."PeriodId"
                       OR snapshot."SnapshotId" IS DISTINCT FROM NEW."SnapshotId"
                       OR snapshot."ArtifactSha256" IS DISTINCT FROM NEW."SnapshotArtifactSha256" THEN
                        RAISE EXCEPTION 'External filing outcome snapshot scope, identity or hash mismatch.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_external_filing_outcome_events_scope';
                    END IF;
                    IF NEW."SupersedingSnapshotRecordId" IS NOT NULL THEN
                        SELECT * INTO successor FROM external_filing_handoff_snapshots WHERE "Id" = NEW."SupersedingSnapshotRecordId";
                        IF successor."TenantId" IS DISTINCT FROM snapshot."TenantId"
                           OR successor."CompanyId" IS DISTINCT FROM snapshot."CompanyId"
                           OR successor."PeriodId" IS DISTINCT FROM snapshot."PeriodId"
                           OR successor."Workflow" IS DISTINCT FROM snapshot."Workflow"
                           OR successor."Version" IS DISTINCT FROM snapshot."Version" + 1
                           OR successor."SnapshotId" IS DISTINCT FROM NEW."SupersedingSnapshotId"
                           OR successor."ArtifactSha256" IS DISTINCT FROM NEW."SupersedingSnapshotArtifactSha256"
                           OR successor."SupersedesSnapshotId" IS DISTINCT FROM snapshot."SnapshotId"
                           OR successor."SupersedesArtifactSha256" IS DISTINCT FROM snapshot."ArtifactSha256" THEN
                            RAISE EXCEPTION 'External filing supersession successor scope, identity or hash mismatch.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_external_filing_outcome_events_successor';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "TR_external_filing_outcome_events_scope"
                    BEFORE INSERT ON external_filing_outcome_events
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_external_filing_outcome_scope();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_external_filing_outcome_events_scope" ON external_filing_outcome_events;
                DROP TRIGGER IF EXISTS "TR_external_filing_outcome_events_immutable" ON external_filing_outcome_events;
                DROP TRIGGER IF EXISTS "TR_external_filing_handoff_snapshots_scope" ON external_filing_handoff_snapshots;
                DROP TRIGGER IF EXISTS "TR_external_filing_handoff_snapshots_immutable" ON external_filing_handoff_snapshots;
                DROP TRIGGER IF EXISTS "TR_filing_authority_engagements_scope" ON filing_authority_engagements;
                DROP TRIGGER IF EXISTS "TR_filing_authority_engagements_immutable" ON filing_authority_engagements;
                DROP FUNCTION IF EXISTS accounts_validate_external_filing_outcome_scope();
                DROP FUNCTION IF EXISTS accounts_validate_external_filing_snapshot_scope();
                DROP FUNCTION IF EXISTS accounts_validate_filing_authority_scope();
                DROP FUNCTION IF EXISTS accounts_prevent_external_filing_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "external_filing_outcome_events");

            migrationBuilder.DropTable(
                name: "external_filing_handoff_snapshots");

            migrationBuilder.DropTable(
                name: "filing_authority_engagements");
        }
    }
}
