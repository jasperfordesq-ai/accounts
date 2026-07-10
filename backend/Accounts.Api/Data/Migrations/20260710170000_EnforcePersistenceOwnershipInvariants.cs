using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710170000_EnforcePersistenceOwnershipInvariants")]
public partial class EnforcePersistenceOwnershipInvariants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Refuse to bless pre-existing cross-owner rows. The ordinary parent references on periods,
        // banks, import batches and filing packages are already protected by foreign keys; these are
        // the relationships whose consistency spans more than one independent foreign key.
        migrationBuilder.Sql("""
            DO $ownership_preflight$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM account_categories AS category
                    WHERE category."CompanyId" IS NULL AND NOT category."IsSystem"
                ) THEN
                    RAISE EXCEPTION 'AccountCategory ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_account_categories_global_requires_system';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM account_categories AS category
                    JOIN account_categories AS parent ON parent."Id" = category."ParentId"
                    WHERE category."ParentId" IS NOT NULL
                      AND NOT (
                          (category."CompanyId" IS NULL
                           AND parent."CompanyId" IS NULL
                           AND parent."IsSystem")
                          OR
                          (category."CompanyId" IS NOT NULL
                           AND (parent."CompanyId" = category."CompanyId"
                                OR (parent."CompanyId" IS NULL AND parent."IsSystem")))
                      )
                ) THEN
                    RAISE EXCEPTION 'AccountCategory ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_account_categories_parent_ownership';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM transaction_rules AS rule
                    JOIN account_categories AS category ON category."Id" = rule."CategoryId"
                    WHERE NOT (
                        category."CompanyId" = rule."CompanyId"
                        OR (category."CompanyId" IS NULL AND category."IsSystem")
                    )
                ) THEN
                    RAISE EXCEPTION 'TransactionRule ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_transaction_rules_category_ownership';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM imported_transactions AS transaction
                    JOIN bank_accounts AS bank ON bank."Id" = transaction."BankAccountId"
                    LEFT JOIN accounting_periods AS period ON period."Id" = transaction."PeriodId"
                    LEFT JOIN import_batches AS batch ON batch."Id" = transaction."ImportBatchId"
                    LEFT JOIN account_categories AS category ON category."Id" = transaction."CategoryId"
                    WHERE (transaction."PeriodId" IS NOT NULL
                           AND period."CompanyId" IS DISTINCT FROM bank."CompanyId")
                       OR (transaction."ImportBatchId" IS NOT NULL
                           AND batch."BankAccountId" IS DISTINCT FROM transaction."BankAccountId")
                       OR (transaction."CategoryId" IS NOT NULL
                           AND NOT (
                               category."CompanyId" = bank."CompanyId"
                               OR (category."CompanyId" IS NULL AND category."IsSystem")
                           ))
                ) THEN
                    RAISE EXCEPTION 'ImportedTransaction ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_ownership';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM filing_deadlines AS deadline
                    JOIN accounting_periods AS period ON period."Id" = deadline."PeriodId"
                    WHERE period."CompanyId" IS DISTINCT FROM deadline."CompanyId"
                ) THEN
                    RAISE EXCEPTION 'FilingDeadline ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_filing_deadlines_period_ownership';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM filing_histories AS history
                    JOIN accounting_periods AS period ON period."Id" = history."PeriodId"
                    WHERE history."PeriodId" IS NOT NULL
                      AND period."CompanyId" IS DISTINCT FROM history."CompanyId"
                ) THEN
                    RAISE EXCEPTION 'FilingHistory ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_filing_histories_period_ownership';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM audit_logs AS audit
                    JOIN companies AS company ON company."Id" = audit."CompanyId"
                    LEFT JOIN accounting_periods AS period ON period."Id" = audit."PeriodId"
                    WHERE (audit."TenantId" IS NOT NULL
                           AND audit."TenantId" IS DISTINCT FROM company."TenantId")
                       OR (audit."PeriodId" IS NOT NULL
                           AND period."CompanyId" IS DISTINCT FROM audit."CompanyId")
                ) THEN
                    RAISE EXCEPTION 'AuditLog ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_logs_scope_ownership';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM audit_integrity_checkpoints AS checkpoint
                    JOIN companies AS company ON company."Id" = checkpoint."CompanyId"
                    JOIN audit_logs AS audit ON audit."Id" = checkpoint."LastAuditLogId"
                    WHERE (checkpoint."TenantId" IS NOT NULL
                           AND checkpoint."TenantId" IS DISTINCT FROM company."TenantId")
                       OR audit."CompanyId" IS DISTINCT FROM checkpoint."CompanyId"
                       OR audit."IntegrityHash" IS DISTINCT FROM checkpoint."LastIntegrityHash"
                ) THEN
                    RAISE EXCEPTION 'AuditIntegrityCheckpoint ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_integrity_checkpoints_anchor';
                END IF;
            END
            $ownership_preflight$;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "CK_account_categories_global_requires_system",
            table: "account_categories",
            sql: "\"CompanyId\" IS NOT NULL OR \"IsSystem\"");

        migrationBuilder.Sql("""
            CREATE FUNCTION accounts_prevent_ownership_reassignment()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                ownership_column text;
            BEGIN
                FOREACH ownership_column IN ARRAY TG_ARGV
                LOOP
                    IF (to_jsonb(NEW) -> ownership_column)
                        IS DISTINCT FROM (to_jsonb(OLD) -> ownership_column) THEN
                        RAISE EXCEPTION '% ownership relationship is immutable.', TG_TABLE_NAME
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_ownership_anchor_immutable';
                    END IF;
                END LOOP;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_prevent_company_tenant_reassignment()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF OLD."TenantId" IS NOT NULL
                   AND NEW."TenantId" IS DISTINCT FROM OLD."TenantId" THEN
                    RAISE EXCEPTION 'Company ownership relationship is immutable.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_companies_tenant_immutable';
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_validate_account_category_ownership()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                parent_company_id integer;
                parent_is_system boolean;
            BEGIN
                IF NEW."CompanyId" IS NULL AND NOT NEW."IsSystem" THEN
                    RAISE EXCEPTION 'AccountCategory ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_account_categories_global_requires_system';
                END IF;

                IF NEW."ParentId" IS NOT NULL THEN
                    SELECT parent."CompanyId", parent."IsSystem"
                    INTO parent_company_id, parent_is_system
                    FROM account_categories AS parent
                    WHERE parent."Id" = NEW."ParentId";

                    IF NOT FOUND OR NOT (
                        (NEW."CompanyId" IS NULL
                         AND parent_company_id IS NULL
                         AND parent_is_system)
                        OR
                        (NEW."CompanyId" IS NOT NULL
                         AND (parent_company_id = NEW."CompanyId"
                              OR (parent_company_id IS NULL AND parent_is_system)))
                    ) THEN
                        RAISE EXCEPTION 'AccountCategory ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_account_categories_parent_ownership';
                    END IF;
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_validate_transaction_rule_ownership()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                category_company_id integer;
                category_is_system boolean;
            BEGIN
                SELECT category."CompanyId", category."IsSystem"
                INTO category_company_id, category_is_system
                FROM account_categories AS category
                WHERE category."Id" = NEW."CategoryId";

                IF NOT FOUND OR NOT (
                    category_company_id = NEW."CompanyId"
                    OR (category_company_id IS NULL AND category_is_system)
                ) THEN
                    RAISE EXCEPTION 'TransactionRule ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_transaction_rules_category_ownership';
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_validate_imported_transaction_ownership()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                bank_company_id integer;
                related_company_id integer;
                related_bank_id integer;
                category_company_id integer;
                category_is_system boolean;
            BEGIN
                SELECT bank."CompanyId"
                INTO bank_company_id
                FROM bank_accounts AS bank
                WHERE bank."Id" = NEW."BankAccountId";
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'ImportedTransaction ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_ownership';
                END IF;

                IF NEW."PeriodId" IS NOT NULL THEN
                    SELECT period."CompanyId"
                    INTO related_company_id
                    FROM accounting_periods AS period
                    WHERE period."Id" = NEW."PeriodId";
                    IF NOT FOUND OR related_company_id IS DISTINCT FROM bank_company_id THEN
                        RAISE EXCEPTION 'ImportedTransaction ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_period_ownership';
                    END IF;
                END IF;

                IF NEW."ImportBatchId" IS NOT NULL THEN
                    SELECT batch."BankAccountId"
                    INTO related_bank_id
                    FROM import_batches AS batch
                    WHERE batch."Id" = NEW."ImportBatchId";
                    IF NOT FOUND OR related_bank_id IS DISTINCT FROM NEW."BankAccountId" THEN
                        RAISE EXCEPTION 'ImportedTransaction ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_batch_ownership';
                    END IF;
                END IF;

                IF NEW."CategoryId" IS NOT NULL THEN
                    SELECT category."CompanyId", category."IsSystem"
                    INTO category_company_id, category_is_system
                    FROM account_categories AS category
                    WHERE category."Id" = NEW."CategoryId";
                    IF NOT FOUND OR NOT (
                        category_company_id = bank_company_id
                        OR (category_company_id IS NULL AND category_is_system)
                    ) THEN
                        RAISE EXCEPTION 'ImportedTransaction ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_category_ownership';
                    END IF;
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_validate_filing_record_ownership()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                period_company_id integer;
            BEGIN
                IF NEW."PeriodId" IS NULL THEN
                    RETURN NEW;
                END IF;

                SELECT period."CompanyId"
                INTO period_company_id
                FROM accounting_periods AS period
                WHERE period."Id" = NEW."PeriodId";
                IF NOT FOUND OR period_company_id IS DISTINCT FROM NEW."CompanyId" THEN
                    RAISE EXCEPTION '% ownership relationship is invalid.', TG_TABLE_NAME
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_filing_record_period_ownership';
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_validate_audit_log_ownership()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                company_tenant_id integer;
                period_company_id integer;
            BEGIN
                IF NEW."CompanyId" IS NULL THEN
                    IF NEW."PeriodId" IS NOT NULL THEN
                        RAISE EXCEPTION 'AuditLog ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_logs_scope_ownership';
                    END IF;
                    RETURN NEW;
                END IF;

                SELECT company."TenantId"
                INTO company_tenant_id
                FROM companies AS company
                WHERE company."Id" = NEW."CompanyId";
                IF NOT FOUND OR (
                    NEW."TenantId" IS NOT NULL
                    AND NEW."TenantId" IS DISTINCT FROM company_tenant_id
                ) THEN
                    RAISE EXCEPTION 'AuditLog ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_logs_scope_ownership';
                END IF;

                IF NEW."PeriodId" IS NOT NULL THEN
                    SELECT period."CompanyId"
                    INTO period_company_id
                    FROM accounting_periods AS period
                    WHERE period."Id" = NEW."PeriodId";
                    IF NOT FOUND OR period_company_id IS DISTINCT FROM NEW."CompanyId" THEN
                        RAISE EXCEPTION 'AuditLog ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_logs_scope_ownership';
                    END IF;
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE FUNCTION accounts_validate_audit_checkpoint_ownership()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                company_tenant_id integer;
                audit_company_id integer;
                audit_integrity_hash text;
            BEGIN
                SELECT company."TenantId"
                INTO company_tenant_id
                FROM companies AS company
                WHERE company."Id" = NEW."CompanyId";
                IF NOT FOUND OR (
                    NEW."TenantId" IS NOT NULL
                    AND NEW."TenantId" IS DISTINCT FROM company_tenant_id
                ) THEN
                    RAISE EXCEPTION 'AuditIntegrityCheckpoint ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_integrity_checkpoints_anchor';
                END IF;

                SELECT audit."CompanyId", audit."IntegrityHash"
                INTO audit_company_id, audit_integrity_hash
                FROM audit_logs AS audit
                WHERE audit."Id" = NEW."LastAuditLogId";
                IF NOT FOUND
                   OR audit_company_id IS DISTINCT FROM NEW."CompanyId"
                   OR audit_integrity_hash IS DISTINCT FROM NEW."LastIntegrityHash" THEN
                    RAISE EXCEPTION 'AuditIntegrityCheckpoint ownership relationship is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_audit_integrity_checkpoints_anchor';
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE TRIGGER "TR_companies_tenant_immutable"
                BEFORE UPDATE OF "TenantId" ON companies
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_company_tenant_reassignment();
            CREATE TRIGGER "TR_accounting_periods_company_immutable"
                BEFORE UPDATE OF "CompanyId" ON accounting_periods
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('CompanyId');
            CREATE TRIGGER "TR_bank_accounts_company_immutable"
                BEFORE UPDATE OF "CompanyId" ON bank_accounts
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('CompanyId');
            CREATE TRIGGER "TR_import_batches_bank_immutable"
                BEFORE UPDATE OF "BankAccountId" ON import_batches
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('BankAccountId');
            CREATE TRIGGER "TR_account_categories_company_immutable"
                BEFORE UPDATE OF "CompanyId" ON account_categories
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('CompanyId');
            CREATE TRIGGER "TR_imported_transactions_bank_immutable"
                BEFORE UPDATE OF "BankAccountId" ON imported_transactions
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('BankAccountId');
            CREATE TRIGGER "TR_transaction_rules_company_immutable"
                BEFORE UPDATE OF "CompanyId" ON transaction_rules
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('CompanyId');
            CREATE TRIGGER "TR_cro_filing_packages_period_immutable"
                BEFORE UPDATE OF "PeriodId" ON cro_filing_packages
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('PeriodId');
            CREATE TRIGGER "TR_revenue_filing_packages_period_immutable"
                BEFORE UPDATE OF "PeriodId" ON revenue_filing_packages
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('PeriodId');
            CREATE TRIGGER "TR_charity_filing_packages_period_immutable"
                BEFORE UPDATE OF "PeriodId" ON charity_filing_packages
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('PeriodId');
            CREATE TRIGGER "TR_audit_logs_scope_immutable"
                BEFORE UPDATE OF "CompanyId", "PeriodId", "TenantId" ON audit_logs
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('CompanyId', 'PeriodId', 'TenantId');
            CREATE TRIGGER "TR_audit_integrity_checkpoints_scope_immutable"
                BEFORE UPDATE OF "CompanyId", "TenantId", "LastAuditLogId", "LastIntegrityHash" ON audit_integrity_checkpoints
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_ownership_reassignment('CompanyId', 'TenantId', 'LastAuditLogId', 'LastIntegrityHash');

            CREATE TRIGGER "TR_account_categories_ownership"
                BEFORE INSERT OR UPDATE OF "CompanyId", "ParentId", "IsSystem" ON account_categories
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_account_category_ownership();
            CREATE TRIGGER "TR_transaction_rules_ownership"
                BEFORE INSERT OR UPDATE OF "CompanyId", "CategoryId" ON transaction_rules
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_transaction_rule_ownership();
            CREATE TRIGGER "TR_imported_transactions_ownership"
                BEFORE INSERT OR UPDATE OF "BankAccountId", "PeriodId", "ImportBatchId", "CategoryId" ON imported_transactions
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_imported_transaction_ownership();
            CREATE TRIGGER "TR_filing_deadlines_ownership"
                BEFORE INSERT OR UPDATE OF "CompanyId", "PeriodId" ON filing_deadlines
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_filing_record_ownership();
            CREATE TRIGGER "TR_filing_histories_ownership"
                BEFORE INSERT OR UPDATE OF "CompanyId", "PeriodId" ON filing_histories
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_filing_record_ownership();
            CREATE TRIGGER "TR_audit_logs_ownership"
                BEFORE INSERT OR UPDATE OF "CompanyId", "PeriodId", "TenantId" ON audit_logs
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_audit_log_ownership();
            CREATE TRIGGER "TR_audit_integrity_checkpoints_ownership"
                BEFORE INSERT OR UPDATE OF "CompanyId", "TenantId", "LastAuditLogId", "LastIntegrityHash" ON audit_integrity_checkpoints
                FOR EACH ROW EXECUTE FUNCTION accounts_validate_audit_checkpoint_ownership();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_audit_integrity_checkpoints_ownership" ON audit_integrity_checkpoints;
            DROP TRIGGER IF EXISTS "TR_audit_logs_ownership" ON audit_logs;
            DROP TRIGGER IF EXISTS "TR_filing_histories_ownership" ON filing_histories;
            DROP TRIGGER IF EXISTS "TR_filing_deadlines_ownership" ON filing_deadlines;
            DROP TRIGGER IF EXISTS "TR_imported_transactions_ownership" ON imported_transactions;
            DROP TRIGGER IF EXISTS "TR_transaction_rules_ownership" ON transaction_rules;
            DROP TRIGGER IF EXISTS "TR_account_categories_ownership" ON account_categories;

            DROP TRIGGER IF EXISTS "TR_audit_integrity_checkpoints_scope_immutable" ON audit_integrity_checkpoints;
            DROP TRIGGER IF EXISTS "TR_audit_logs_scope_immutable" ON audit_logs;
            DROP TRIGGER IF EXISTS "TR_charity_filing_packages_period_immutable" ON charity_filing_packages;
            DROP TRIGGER IF EXISTS "TR_revenue_filing_packages_period_immutable" ON revenue_filing_packages;
            DROP TRIGGER IF EXISTS "TR_cro_filing_packages_period_immutable" ON cro_filing_packages;
            DROP TRIGGER IF EXISTS "TR_transaction_rules_company_immutable" ON transaction_rules;
            DROP TRIGGER IF EXISTS "TR_imported_transactions_bank_immutable" ON imported_transactions;
            DROP TRIGGER IF EXISTS "TR_account_categories_company_immutable" ON account_categories;
            DROP TRIGGER IF EXISTS "TR_import_batches_bank_immutable" ON import_batches;
            DROP TRIGGER IF EXISTS "TR_bank_accounts_company_immutable" ON bank_accounts;
            DROP TRIGGER IF EXISTS "TR_accounting_periods_company_immutable" ON accounting_periods;
            DROP TRIGGER IF EXISTS "TR_companies_tenant_immutable" ON companies;

            DROP FUNCTION IF EXISTS accounts_validate_audit_checkpoint_ownership();
            DROP FUNCTION IF EXISTS accounts_validate_audit_log_ownership();
            DROP FUNCTION IF EXISTS accounts_validate_filing_record_ownership();
            DROP FUNCTION IF EXISTS accounts_validate_imported_transaction_ownership();
            DROP FUNCTION IF EXISTS accounts_validate_transaction_rule_ownership();
            DROP FUNCTION IF EXISTS accounts_validate_account_category_ownership();
            DROP FUNCTION IF EXISTS accounts_prevent_company_tenant_reassignment();
            DROP FUNCTION IF EXISTS accounts_prevent_ownership_reassignment();
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_account_categories_global_requires_system",
            table: "account_categories");
    }
}
