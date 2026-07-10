using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenIdentitySecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastAcceptedTotpCounter",
                table: "user_mfa_credentials",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_identity_tenant_scope()
                RETURNS trigger AS $body$
                DECLARE
                    payload jsonb;
                    tenant_id integer;
                    subject_id integer;
                    actor_id integer;
                    subject_tenant integer;
                    actor_tenant integer;
                BEGIN
                    payload := to_jsonb(NEW);
                    tenant_id := (payload ->> 'TenantId')::integer;
                    subject_id := CASE
                        WHEN TG_TABLE_NAME = 'user_lifecycle_events' THEN (payload ->> 'TargetUserId')::integer
                        ELSE (payload ->> 'UserId')::integer
                    END;
                    SELECT "TenantId" INTO subject_tenant FROM user_accounts WHERE "Id" = subject_id;
                    IF subject_tenant IS NULL OR subject_tenant <> tenant_id THEN
                        RAISE EXCEPTION 'Identity record subject and tenant scope do not match.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_identity_record_user_tenant';
                    END IF;
                    IF TG_TABLE_NAME = 'user_action_tokens' THEN
                        actor_id := (payload ->> 'CreatedByUserId')::integer;
                    ELSIF TG_TABLE_NAME = 'user_lifecycle_events' THEN
                        actor_id := (payload ->> 'ActorUserId')::integer;
                    ELSE
                        actor_id := NULL;
                    END IF;
                    IF actor_id IS NOT NULL THEN
                        SELECT "TenantId" INTO actor_tenant FROM user_accounts WHERE "Id" = actor_id;
                        IF actor_tenant IS NULL OR actor_tenant <> tenant_id THEN
                            RAISE EXCEPTION 'Identity record actor and tenant scope do not match.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_identity_record_actor_tenant';
                        END IF;
                    END IF;
                    RETURN NEW;
                END
                $body$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION enforce_identity_record_transition()
                RETURNS trigger AS $body$
                BEGIN
                    IF TG_TABLE_NAME = 'user_lifecycle_events' THEN
                        RAISE EXCEPTION 'User lifecycle evidence is immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_user_lifecycle_events_immutable';
                    END IF;
                    IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId"
                       OR NEW."UserId" IS DISTINCT FROM OLD."UserId" THEN
                        RAISE EXCEPTION 'Identity ownership anchors are immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_identity_record_ownership_immutable';
                    END IF;

                    IF TG_TABLE_NAME = 'user_action_tokens' THEN
                        IF NEW."Purpose" IS DISTINCT FROM OLD."Purpose"
                           OR NEW."TokenHash" IS DISTINCT FROM OLD."TokenHash"
                           OR NEW."CreatedAtUtc" IS DISTINCT FROM OLD."CreatedAtUtc"
                           OR NEW."ExpiresAtUtc" IS DISTINCT FROM OLD."ExpiresAtUtc"
                           OR NEW."CreatedByUserId" IS DISTINCT FROM OLD."CreatedByUserId"
                           OR OLD."ConsumedAtUtc" IS NOT NULL AND NEW."ConsumedAtUtc" IS DISTINCT FROM OLD."ConsumedAtUtc"
                           OR OLD."RevokedAtUtc" IS NOT NULL AND NEW."RevokedAtUtc" IS DISTINCT FROM OLD."RevokedAtUtc" THEN
                            RAISE EXCEPTION 'Identity action token evidence transition is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_user_action_tokens_transition';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'user_mfa_credentials' THEN
                        IF NEW."SecretVersion" < OLD."SecretVersion"
                           OR NEW."LastAcceptedTotpCounter" < OLD."LastAcceptedTotpCounter"
                           OR NEW."CreatedAtUtc" IS DISTINCT FROM OLD."CreatedAtUtc"
                           OR OLD."EnabledAtUtc" IS NOT NULL AND NEW."EnabledAtUtc" IS DISTINCT FROM OLD."EnabledAtUtc" THEN
                            RAISE EXCEPTION 'MFA credential evidence transition is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_user_mfa_credentials_transition';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'user_mfa_recovery_codes' THEN
                        IF NEW."CodeHash" IS DISTINCT FROM OLD."CodeHash"
                           OR NEW."CreatedAtUtc" IS DISTINCT FROM OLD."CreatedAtUtc"
                           OR OLD."UsedAtUtc" IS NOT NULL AND NEW."UsedAtUtc" IS DISTINCT FROM OLD."UsedAtUtc" THEN
                            RAISE EXCEPTION 'MFA recovery-code evidence transition is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_user_mfa_recovery_codes_transition';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'user_mfa_challenges' THEN
                        IF NEW."Purpose" IS DISTINCT FROM OLD."Purpose"
                           OR NEW."TokenHash" IS DISTINCT FROM OLD."TokenHash"
                           OR NEW."SessionVersion" IS DISTINCT FROM OLD."SessionVersion"
                           OR NEW."CreatedAtUtc" IS DISTINCT FROM OLD."CreatedAtUtc"
                           OR NEW."ExpiresAtUtc" IS DISTINCT FROM OLD."ExpiresAtUtc"
                           OR NEW."FailedAttempts" < OLD."FailedAttempts"
                           OR OLD."ConsumedAtUtc" IS NOT NULL AND NEW."ConsumedAtUtc" IS DISTINCT FROM OLD."ConsumedAtUtc"
                           OR OLD."RevokedAtUtc" IS NOT NULL AND NEW."RevokedAtUtc" IS DISTINCT FROM OLD."RevokedAtUtc" THEN
                            RAISE EXCEPTION 'MFA challenge evidence transition is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_user_mfa_challenges_transition';
                        END IF;
                    END IF;
                    RETURN NEW;
                END
                $body$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_user_action_tokens_scope"
                    BEFORE INSERT OR UPDATE ON user_action_tokens
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_tenant_scope();
                CREATE TRIGGER "TR_user_mfa_credentials_scope"
                    BEFORE INSERT OR UPDATE ON user_mfa_credentials
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_tenant_scope();
                CREATE TRIGGER "TR_user_mfa_recovery_codes_scope"
                    BEFORE INSERT OR UPDATE ON user_mfa_recovery_codes
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_tenant_scope();
                CREATE TRIGGER "TR_user_mfa_challenges_scope"
                    BEFORE INSERT OR UPDATE ON user_mfa_challenges
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_tenant_scope();
                CREATE TRIGGER "TR_user_lifecycle_events_scope"
                    BEFORE INSERT ON user_lifecycle_events
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_tenant_scope();

                CREATE TRIGGER "TR_user_action_tokens_transition"
                    BEFORE UPDATE ON user_action_tokens
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_record_transition();
                CREATE TRIGGER "TR_user_mfa_credentials_transition"
                    BEFORE UPDATE ON user_mfa_credentials
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_record_transition();
                CREATE TRIGGER "TR_user_mfa_recovery_codes_transition"
                    BEFORE UPDATE ON user_mfa_recovery_codes
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_record_transition();
                CREATE TRIGGER "TR_user_mfa_challenges_transition"
                    BEFORE UPDATE ON user_mfa_challenges
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_record_transition();
                CREATE TRIGGER "TR_user_lifecycle_events_immutable"
                    BEFORE UPDATE OR DELETE ON user_lifecycle_events
                    FOR EACH ROW EXECUTE FUNCTION enforce_identity_record_transition();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_user_action_tokens_scope" ON user_action_tokens;
                DROP TRIGGER IF EXISTS "TR_user_mfa_credentials_scope" ON user_mfa_credentials;
                DROP TRIGGER IF EXISTS "TR_user_mfa_recovery_codes_scope" ON user_mfa_recovery_codes;
                DROP TRIGGER IF EXISTS "TR_user_mfa_challenges_scope" ON user_mfa_challenges;
                DROP TRIGGER IF EXISTS "TR_user_lifecycle_events_scope" ON user_lifecycle_events;
                DROP TRIGGER IF EXISTS "TR_user_action_tokens_transition" ON user_action_tokens;
                DROP TRIGGER IF EXISTS "TR_user_mfa_credentials_transition" ON user_mfa_credentials;
                DROP TRIGGER IF EXISTS "TR_user_mfa_recovery_codes_transition" ON user_mfa_recovery_codes;
                DROP TRIGGER IF EXISTS "TR_user_mfa_challenges_transition" ON user_mfa_challenges;
                DROP TRIGGER IF EXISTS "TR_user_lifecycle_events_immutable" ON user_lifecycle_events;
                DROP FUNCTION IF EXISTS enforce_identity_record_transition();
                DROP FUNCTION IF EXISTS enforce_identity_tenant_scope();
                """);

            migrationBuilder.DropColumn(
                name: "LastAcceptedTotpCounter",
                table: "user_mfa_credentials");
        }
    }
}
