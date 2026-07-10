using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadlineDeliveryAndPlatformMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deadline_reminder_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    FilingDeadlineId = table.Column<int>(type: "integer", nullable: false),
                    DeadlineType = table.Column<string>(type: "text", nullable: false),
                    ReminderKind = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ObservedDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ObservedCalculationFingerprintSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeduplicationKeySha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ProviderDeliveryReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deadline_reminder_outbox", x => x.Id);
                    table.CheckConstraint("CK_deadline_reminder_outbox_attempts", "\"AttemptCount\" >= 0 AND \"Revision\" > 0");
                    table.CheckConstraint("CK_deadline_reminder_outbox_chronology", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\" AND \"NextAttemptAtUtc\" >= \"CreatedAtUtc\" AND (\"LastAttemptAtUtc\" IS NULL OR \"LastAttemptAtUtc\" >= \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_deadline_reminder_outbox_hashes", "length(\"DeduplicationKeySha256\") = 64 AND (\"ObservedCalculationFingerprintSha256\" IS NULL OR length(\"ObservedCalculationFingerprintSha256\") = 64)");
                    table.CheckConstraint("CK_deadline_reminder_outbox_provider_reference", "\"ProviderDeliveryReference\" IS NULL OR \"State\" = 'Delivered'");
                    table.CheckConstraint("CK_deadline_reminder_outbox_terminal_state", "((\"State\" = 'Delivered') = (\"DeliveredAtUtc\" IS NOT NULL)) AND ((\"State\" IN ('Cancelled', 'Superseded')) = (\"CancelledAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_deadline_reminder_outbox_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deadline_reminder_outbox_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deadline_reminder_outbox_filing_deadlines_FilingDeadlineId",
                        column: x => x.FilingDeadlineId,
                        principalTable: "filing_deadlines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deadline_reminder_outbox_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_job_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    JobKind = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ScheduledSlotUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExaminedCount = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedCount = table.Column<int>(type: "integer", nullable: false),
                    DeliveredCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    CancelledCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_job_runs", x => x.Id);
                    table.CheckConstraint("CK_platform_job_runs_chronology", "\"StartedAtUtc\" >= \"ScheduledSlotUtc\" AND (\"CompletedAtUtc\" IS NULL OR \"CompletedAtUtc\" >= \"StartedAtUtc\")");
                    table.CheckConstraint("CK_platform_job_runs_completion", "(\"Status\" = 'Running' AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" <> 'Running' AND \"CompletedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_platform_job_runs_counts", "\"ExaminedCount\" >= 0 AND \"EnqueuedCount\" >= 0 AND \"DeliveredCount\" >= 0 AND \"FailedCount\" >= 0 AND \"CancelledCount\" >= 0");
                    table.CheckConstraint("CK_platform_job_runs_evidence", "\"EvidenceSha256\" IS NULL OR length(\"EvidenceSha256\") = 64");
                    table.ForeignKey(
                        name: "FK_platform_job_runs_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deadline_reminder_outbox_CompanyId",
                table: "deadline_reminder_outbox",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_deadline_reminder_outbox_FilingDeadlineId",
                table: "deadline_reminder_outbox",
                column: "FilingDeadlineId");

            migrationBuilder.CreateIndex(
                name: "IX_deadline_reminder_outbox_PeriodId",
                table: "deadline_reminder_outbox",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_deadline_reminder_outbox_TenantId_CompanyId_ObservedDueDate",
                table: "deadline_reminder_outbox",
                columns: new[] { "TenantId", "CompanyId", "ObservedDueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_deadline_reminder_outbox_TenantId_DeduplicationKeySha256",
                table: "deadline_reminder_outbox",
                columns: new[] { "TenantId", "DeduplicationKeySha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deadline_reminder_outbox_TenantId_State_NextAttemptAtUtc",
                table: "deadline_reminder_outbox",
                columns: new[] { "TenantId", "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_job_runs_TenantId_JobKind_ScheduledSlotUtc",
                table: "platform_job_runs",
                columns: new[] { "TenantId", "JobKind", "ScheduledSlotUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_job_runs_TenantId_Status_StartedAtUtc",
                table: "platform_job_runs",
                columns: new[] { "TenantId", "Status", "StartedAtUtc" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_deadline_reminder_outbox_integrity()
                RETURNS trigger AS $body$
                DECLARE
                    company_tenant integer;
                    period_company integer;
                    deadline_company integer;
                    deadline_period integer;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Deadline reminder evidence cannot be deleted.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_deadline_reminder_outbox_immutable';
                    END IF;

                    SELECT "TenantId" INTO company_tenant FROM companies WHERE "Id" = NEW."CompanyId";
                    SELECT "CompanyId" INTO period_company FROM accounting_periods WHERE "Id" = NEW."PeriodId";
                    SELECT "CompanyId", "PeriodId" INTO deadline_company, deadline_period
                        FROM filing_deadlines WHERE "Id" = NEW."FilingDeadlineId";
                    IF company_tenant IS NULL
                       OR company_tenant <> NEW."TenantId"
                       OR period_company IS NULL
                       OR period_company <> NEW."CompanyId"
                       OR deadline_company IS NULL
                       OR deadline_company <> NEW."CompanyId"
                       OR deadline_period IS NULL
                       OR deadline_period <> NEW."PeriodId" THEN
                        RAISE EXCEPTION 'Deadline reminder ownership scope is inconsistent.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_deadline_reminder_outbox_scope';
                    END IF;

                    IF TG_OP = 'UPDATE' THEN
                        IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId"
                           OR NEW."CompanyId" IS DISTINCT FROM OLD."CompanyId"
                           OR NEW."PeriodId" IS DISTINCT FROM OLD."PeriodId"
                           OR NEW."FilingDeadlineId" IS DISTINCT FROM OLD."FilingDeadlineId"
                           OR NEW."DeadlineType" IS DISTINCT FROM OLD."DeadlineType"
                           OR NEW."ReminderKind" IS DISTINCT FROM OLD."ReminderKind"
                           OR NEW."ObservedDueDate" IS DISTINCT FROM OLD."ObservedDueDate"
                           OR NEW."ObservedCalculationFingerprintSha256" IS DISTINCT FROM OLD."ObservedCalculationFingerprintSha256"
                           OR NEW."DeduplicationKeySha256" IS DISTINCT FROM OLD."DeduplicationKeySha256"
                           OR NEW."CreatedAtUtc" IS DISTINCT FROM OLD."CreatedAtUtc"
                           OR NEW."Revision" <> OLD."Revision" + 1 THEN
                            RAISE EXCEPTION 'Deadline reminder ownership and intent evidence are immutable.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_deadline_reminder_outbox_immutable';
                        END IF;
                        IF OLD."State" IN ('Delivered', 'Cancelled', 'Superseded') THEN
                            RAISE EXCEPTION 'A terminal deadline reminder cannot be changed.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_deadline_reminder_outbox_transition';
                        END IF;
                        IF NOT (
                            NEW."State" = OLD."State"
                            OR OLD."State" IN ('Pending', 'RetryScheduled')
                               AND NEW."State" IN ('Delivering', 'Cancelled', 'Superseded')
                            OR OLD."State" = 'Delivering'
                               AND NEW."State" IN ('Delivered', 'RetryScheduled', 'Cancelled', 'Superseded')) THEN
                            RAISE EXCEPTION 'Deadline reminder state transition is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_deadline_reminder_outbox_transition';
                        END IF;
                    END IF;
                    RETURN NEW;
                END
                $body$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_deadline_reminder_outbox_integrity"
                    BEFORE INSERT OR UPDATE OR DELETE ON deadline_reminder_outbox
                    FOR EACH ROW EXECUTE FUNCTION enforce_deadline_reminder_outbox_integrity();

                CREATE OR REPLACE FUNCTION enforce_platform_job_run_integrity()
                RETURNS trigger AS $body$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Platform job evidence cannot be deleted.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_platform_job_runs_immutable';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId"
                           OR NEW."JobKind" IS DISTINCT FROM OLD."JobKind"
                           OR NEW."Trigger" IS DISTINCT FROM OLD."Trigger"
                           OR NEW."ScheduledSlotUtc" IS DISTINCT FROM OLD."ScheduledSlotUtc"
                           OR NEW."StartedAtUtc" IS DISTINCT FROM OLD."StartedAtUtc"
                           OR NEW."CreatedAtUtc" IS DISTINCT FROM OLD."CreatedAtUtc" THEN
                            RAISE EXCEPTION 'Platform job identity evidence is immutable.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_platform_job_runs_immutable';
                        END IF;
                        IF OLD."Status" <> 'Running'
                           OR NEW."Status" NOT IN ('Succeeded', 'PartiallySucceeded', 'Failed') THEN
                            RAISE EXCEPTION 'Platform job completion transition is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_platform_job_runs_transition';
                        END IF;
                    END IF;
                    RETURN NEW;
                END
                $body$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_platform_job_runs_integrity"
                    BEFORE UPDATE OR DELETE ON platform_job_runs
                    FOR EACH ROW EXECUTE FUNCTION enforce_platform_job_run_integrity();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_deadline_reminder_outbox_integrity" ON deadline_reminder_outbox;
                DROP TRIGGER IF EXISTS "TR_platform_job_runs_integrity" ON platform_job_runs;
                DROP FUNCTION IF EXISTS enforce_deadline_reminder_outbox_integrity();
                DROP FUNCTION IF EXISTS enforce_platform_job_run_integrity();
                """);

            migrationBuilder.DropTable(
                name: "deadline_reminder_outbox");

            migrationBuilder.DropTable(
                name: "platform_job_runs");
        }
    }
}
