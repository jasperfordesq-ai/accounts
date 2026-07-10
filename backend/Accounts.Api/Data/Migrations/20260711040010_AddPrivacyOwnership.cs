using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    // Adds database ownership and scope controls after the privacy tables exist.
    /// <inheritdoc />
    public partial class AddPrivacyOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_privacy_subject_requests_DecidedByUserId",
                table: "privacy_subject_requests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_subject_requests_RequestedByUserId",
                table: "privacy_subject_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_subject_requests_SubjectUserId",
                table: "privacy_subject_requests",
                column: "SubjectUserId");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_incident_exercises_ReviewedByUserId",
                table: "privacy_incident_exercises",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_login_security_events_UserId",
                table: "login_security_events",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_login_security_events_tenants_TenantId",
                table: "login_security_events",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_login_security_events_user_accounts_UserId",
                table: "login_security_events",
                column: "UserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_incident_exercises_tenants_TenantId",
                table: "privacy_incident_exercises",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_incident_exercises_user_accounts_ReviewedByUserId",
                table: "privacy_incident_exercises",
                column: "ReviewedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_subject_requests_tenants_TenantId",
                table: "privacy_subject_requests",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_subject_requests_user_accounts_DecidedByUserId",
                table: "privacy_subject_requests",
                column: "DecidedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_subject_requests_user_accounts_RequestedByUserId",
                table: "privacy_subject_requests",
                column: "RequestedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_subject_requests_user_accounts_SubjectUserId",
                table: "privacy_subject_requests",
                column: "SubjectUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounts_validate_login_security_event_scope()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    user_tenant integer;
                BEGIN
                    IF (NEW."TenantId" IS NULL) IS DISTINCT FROM (NEW."UserId" IS NULL) THEN
                        RAISE EXCEPTION 'Login security event tenant and user scope must both be known or both be absent.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_login_security_events_scope_shape';
                    END IF;
                    IF NEW."UserId" IS NOT NULL THEN
                        SELECT "TenantId" INTO user_tenant FROM user_accounts WHERE "Id" = NEW."UserId";
                        IF user_tenant IS DISTINCT FROM NEW."TenantId" THEN
                            RAISE EXCEPTION 'Login security event user belongs to another tenant.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_login_security_events_user_tenant';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION accounts_validate_privacy_subject_request_scope()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    subject_tenant integer;
                    requester_tenant integer;
                    decider_tenant integer;
                BEGIN
                    SELECT "TenantId" INTO subject_tenant FROM user_accounts WHERE "Id" = NEW."SubjectUserId";
                    SELECT "TenantId" INTO requester_tenant FROM user_accounts WHERE "Id" = NEW."RequestedByUserId";
                    IF NEW."DecidedByUserId" IS NOT NULL THEN
                        SELECT "TenantId" INTO decider_tenant FROM user_accounts WHERE "Id" = NEW."DecidedByUserId";
                    END IF;
                    IF subject_tenant IS DISTINCT FROM NEW."TenantId"
                       OR requester_tenant IS DISTINCT FROM NEW."TenantId"
                       OR (NEW."DecidedByUserId" IS NOT NULL AND decider_tenant IS DISTINCT FROM NEW."TenantId") THEN
                        RAISE EXCEPTION 'Privacy subject request actor or subject belongs to another tenant.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_privacy_subject_requests_user_tenant';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION accounts_validate_privacy_incident_scope()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    reviewer_tenant integer;
                BEGIN
                    SELECT "TenantId" INTO reviewer_tenant FROM user_accounts WHERE "Id" = NEW."ReviewedByUserId";
                    IF reviewer_tenant IS DISTINCT FROM NEW."TenantId" THEN
                        RAISE EXCEPTION 'Privacy incident reviewer belongs to another tenant.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_privacy_incident_exercises_reviewer_tenant';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION accounts_prevent_privacy_evidence_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'Privacy security and incident evidence is immutable.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_privacy_evidence_immutable';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION accounts_guard_privacy_request_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF OLD."MetadataExpiresAtUtc" > CURRENT_TIMESTAMP
                       OR (OLD."StatutoryRetainUntilUtc" IS NOT NULL AND OLD."StatutoryRetainUntilUtc" > CURRENT_TIMESTAMP) THEN
                        RAISE EXCEPTION 'Privacy request metadata is still within its approved retention period.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_privacy_subject_requests_retention_delete';
                    END IF;
                    RETURN OLD;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION accounts_guard_privacy_incident_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF OLD."ReviewedAtUtc" + INTERVAL '6 years' > CURRENT_TIMESTAMP THEN
                        RAISE EXCEPTION 'Privacy incident evidence is still within its approved retention period.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_privacy_incident_exercises_retention_delete';
                    END IF;
                    RETURN OLD;
                END;
                $function$;

                CREATE TRIGGER "TR_login_security_events_scope"
                    BEFORE INSERT ON login_security_events
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_login_security_event_scope();
                CREATE TRIGGER "TR_privacy_subject_requests_scope"
                    BEFORE INSERT OR UPDATE OF "TenantId", "SubjectUserId", "RequestedByUserId", "DecidedByUserId" ON privacy_subject_requests
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_privacy_subject_request_scope();
                CREATE TRIGGER "TR_privacy_incident_exercises_scope"
                    BEFORE INSERT ON privacy_incident_exercises
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_privacy_incident_scope();

                CREATE TRIGGER "TR_login_security_events_immutable"
                    BEFORE UPDATE ON login_security_events
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_privacy_evidence_update();
                CREATE TRIGGER "TR_privacy_incident_exercises_immutable"
                    BEFORE UPDATE ON privacy_incident_exercises
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_privacy_evidence_update();
                CREATE TRIGGER "TR_privacy_subject_requests_ownership_immutable"
                    BEFORE UPDATE OF "TenantId", "SubjectUserId", "RequestKind", "RequestedByUserId", "RequestedAtUtc" ON privacy_subject_requests
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('TenantId', 'SubjectUserId', 'RequestKind', 'RequestedByUserId', 'RequestedAtUtc');
                CREATE TRIGGER "TR_privacy_subject_requests_retention_delete"
                    BEFORE DELETE ON privacy_subject_requests
                    FOR EACH ROW EXECUTE FUNCTION accounts_guard_privacy_request_delete();
                CREATE TRIGGER "TR_privacy_incident_exercises_retention_delete"
                    BEFORE DELETE ON privacy_incident_exercises
                    FOR EACH ROW EXECUTE FUNCTION accounts_guard_privacy_incident_delete();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_privacy_incident_exercises_retention_delete" ON privacy_incident_exercises;
                DROP TRIGGER IF EXISTS "TR_privacy_subject_requests_retention_delete" ON privacy_subject_requests;
                DROP TRIGGER IF EXISTS "TR_privacy_subject_requests_ownership_immutable" ON privacy_subject_requests;
                DROP TRIGGER IF EXISTS "TR_privacy_incident_exercises_immutable" ON privacy_incident_exercises;
                DROP TRIGGER IF EXISTS "TR_login_security_events_immutable" ON login_security_events;
                DROP TRIGGER IF EXISTS "TR_privacy_incident_exercises_scope" ON privacy_incident_exercises;
                DROP TRIGGER IF EXISTS "TR_privacy_subject_requests_scope" ON privacy_subject_requests;
                DROP TRIGGER IF EXISTS "TR_login_security_events_scope" ON login_security_events;
                DROP FUNCTION IF EXISTS accounts_guard_privacy_incident_delete();
                DROP FUNCTION IF EXISTS accounts_guard_privacy_request_delete();
                DROP FUNCTION IF EXISTS accounts_prevent_privacy_evidence_update();
                DROP FUNCTION IF EXISTS accounts_validate_privacy_incident_scope();
                DROP FUNCTION IF EXISTS accounts_validate_privacy_subject_request_scope();
                DROP FUNCTION IF EXISTS accounts_validate_login_security_event_scope();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_login_security_events_tenants_TenantId",
                table: "login_security_events");

            migrationBuilder.DropForeignKey(
                name: "FK_login_security_events_user_accounts_UserId",
                table: "login_security_events");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_incident_exercises_tenants_TenantId",
                table: "privacy_incident_exercises");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_incident_exercises_user_accounts_ReviewedByUserId",
                table: "privacy_incident_exercises");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_subject_requests_tenants_TenantId",
                table: "privacy_subject_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_subject_requests_user_accounts_DecidedByUserId",
                table: "privacy_subject_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_subject_requests_user_accounts_RequestedByUserId",
                table: "privacy_subject_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_subject_requests_user_accounts_SubjectUserId",
                table: "privacy_subject_requests");

            migrationBuilder.DropIndex(
                name: "IX_privacy_subject_requests_DecidedByUserId",
                table: "privacy_subject_requests");

            migrationBuilder.DropIndex(
                name: "IX_privacy_subject_requests_RequestedByUserId",
                table: "privacy_subject_requests");

            migrationBuilder.DropIndex(
                name: "IX_privacy_subject_requests_SubjectUserId",
                table: "privacy_subject_requests");

            migrationBuilder.DropIndex(
                name: "IX_privacy_incident_exercises_ReviewedByUserId",
                table: "privacy_incident_exercises");

            migrationBuilder.DropIndex(
                name: "IX_login_security_events_UserId",
                table: "login_security_events");
        }
    }
}
