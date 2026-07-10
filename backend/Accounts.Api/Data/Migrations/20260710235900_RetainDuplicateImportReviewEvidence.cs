using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetainDuplicateImportReviewEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE bank_accounts SET \"Currency\" = upper(trim(\"Currency\"));");

            migrationBuilder.AddCheckConstraint(
                name: "CK_bank_accounts_currency_code",
                table: "bank_accounts",
                sql: "\"Currency\" ~ '^[A-Z]{3}$'");

            migrationBuilder.DropForeignKey(
                name: "FK_import_batches_bank_accounts_BankAccountId",
                table: "import_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_imported_transactions_accounting_periods_PeriodId",
                table: "imported_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_imported_transactions_bank_accounts_BankAccountId",
                table: "imported_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_imported_transactions_import_batches_ImportBatchId",
                table: "imported_transactions");

            migrationBuilder.DropIndex(
                name: "IX_imported_transactions_PeriodId",
                table: "imported_transactions");

            migrationBuilder.DropIndex(
                name: "IX_import_batches_BankAccountId",
                table: "import_batches");

            migrationBuilder.AddColumn<string>(
                name: "DuplicateCandidateKind",
                table: "imported_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateCandidateReasonsJson",
                table: "imported_transactions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DuplicateConfidence",
                table: "imported_transactions",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DuplicateDecisionAtUtc",
                table: "imported_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateDecisionByDisplayName",
                table: "imported_transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateDecisionByUserId",
                table: "imported_transactions",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateDecisionReason",
                table: "imported_transactions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateDecisionVersion",
                table: "imported_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateMatchedSourceRowSha256",
                table: "imported_transactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateMatchedTransactionId",
                table: "imported_transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateReviewStatus",
                table: "imported_transactions",
                type: "text",
                nullable: false,
                defaultValue: "NotCandidate");

            migrationBuilder.AddColumn<string>(
                name: "SourceRowJson",
                table: "imported_transactions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRowNumber",
                table: "imported_transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRowSha256",
                table: "imported_transactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceFileBytes",
                table: "import_batches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportWarningsJson",
                table: "import_batches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileSha256",
                table: "import_batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceHeaderJson",
                table: "import_batches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_import_batches_bank_accounts_BankAccountId",
                table: "import_batches",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_imported_transactions_accounting_periods_PeriodId",
                table: "imported_transactions",
                column: "PeriodId",
                principalTable: "accounting_periods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_imported_transactions_bank_accounts_BankAccountId",
                table: "imported_transactions",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_imported_transactions_import_batches_ImportBatchId",
                table: "imported_transactions",
                column: "ImportBatchId",
                principalTable: "import_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION accounts_validate_duplicate_match_ownership()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    matched_bank_id integer;
                    matched_period_id integer;
                BEGIN
                    IF NEW."DuplicateMatchedTransactionId" IS NULL THEN
                        IF NEW."DuplicateMatchedSourceRowSha256" IS NULL THEN
                            RETURN NEW;
                        END IF;
                        PERFORM 1
                        FROM imported_transactions AS matched
                        WHERE matched."BankAccountId" = NEW."BankAccountId"
                          AND matched."PeriodId" IS NOT DISTINCT FROM NEW."PeriodId"
                          AND matched."SourceRowSha256" = NEW."DuplicateMatchedSourceRowSha256"
                          AND matched."Id" <> NEW."Id";
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'Duplicate candidate hash match ownership relationship is invalid.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_duplicate_match_ownership';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF NEW."DuplicateMatchedTransactionId" = NEW."Id" THEN
                        RAISE EXCEPTION 'A duplicate candidate cannot match itself.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_duplicate_match_ownership';
                    END IF;

                    SELECT matched."BankAccountId", matched."PeriodId"
                    INTO matched_bank_id, matched_period_id
                    FROM imported_transactions AS matched
                    WHERE matched."Id" = NEW."DuplicateMatchedTransactionId";
                    IF NOT FOUND
                        OR matched_bank_id IS DISTINCT FROM NEW."BankAccountId"
                        OR matched_period_id IS DISTINCT FROM NEW."PeriodId" THEN
                        RAISE EXCEPTION 'Duplicate candidate match ownership relationship is invalid.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_duplicate_match_ownership';
                    END IF;
                    RETURN NEW;
                END
                $function$;

                CREATE TRIGGER "TR_imported_transactions_duplicate_match_ownership"
                    BEFORE INSERT OR UPDATE OF "DuplicateMatchedTransactionId" ON imported_transactions
                    FOR EACH ROW EXECUTE FUNCTION accounts_validate_duplicate_match_ownership();

                CREATE FUNCTION accounts_prevent_import_batch_source_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD."SourceFileSha256" IS NOT NULL THEN
                            RAISE EXCEPTION 'Retained import batch evidence cannot be deleted.'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_import_batches_source_immutable';
                        END IF;
                        RETURN OLD;
                    END IF;

                    IF OLD."SourceFileSha256" IS NULL THEN
                        IF NEW."SourceFileSha256" IS NULL THEN
                            RETURN NEW;
                        END IF;
                        RAISE EXCEPTION 'Legacy import batches cannot be retrofitted with unverified source evidence.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_import_batches_source_immutable';
                    END IF;

                    IF OLD."BankAccountId" IS DISTINCT FROM NEW."BankAccountId"
                        OR OLD."Filename" IS DISTINCT FROM NEW."Filename"
                        OR OLD."ImportedAt" IS DISTINCT FROM NEW."ImportedAt"
                        OR OLD."RowCount" IS DISTINCT FROM NEW."RowCount"
                        OR OLD."MatchedCount" IS DISTINCT FROM NEW."MatchedCount"
                        OR OLD."SourceFileSha256" IS DISTINCT FROM NEW."SourceFileSha256"
                        OR OLD."SourceFileBytes" IS DISTINCT FROM NEW."SourceFileBytes"
                        OR OLD."SourceHeaderJson" IS DISTINCT FROM NEW."SourceHeaderJson"
                        OR OLD."ImportWarningsJson" IS DISTINCT FROM NEW."ImportWarningsJson" THEN
                        RAISE EXCEPTION 'Import batch source, processing and warning evidence is immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_import_batches_source_immutable';
                    END IF;
                    RETURN NEW;
                END
                $function$;

                CREATE TRIGGER "TR_import_batches_source_immutable"
                    BEFORE UPDATE OR DELETE ON import_batches
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_import_batch_source_change();

                CREATE FUNCTION accounts_prevent_imported_transaction_source_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF (TG_OP = 'DELETE'
                        OR OLD."BankAccountId" IS DISTINCT FROM NEW."BankAccountId"
                        OR OLD."PeriodId" IS DISTINCT FROM NEW."PeriodId")
                        AND EXISTS (
                            SELECT 1
                            FROM imported_transactions AS candidate
                            WHERE candidate."DuplicateMatchedTransactionId" = OLD."Id"
                        ) THEN
                        RAISE EXCEPTION 'A transaction retained as duplicate-match evidence cannot be deleted or reassigned.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_duplicate_match_reference';
                    END IF;
                    IF OLD."SourceRowSha256" IS NULL THEN
                        IF TG_OP = 'DELETE' THEN
                            RETURN OLD;
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Retained imported source rows cannot be deleted.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_source_immutable';
                    END IF;
                    IF OLD."BankAccountId" IS DISTINCT FROM NEW."BankAccountId"
                        OR OLD."PeriodId" IS DISTINCT FROM NEW."PeriodId"
                        OR OLD."ImportBatchId" IS DISTINCT FROM NEW."ImportBatchId"
                        OR OLD."Date" IS DISTINCT FROM NEW."Date"
                        OR OLD."Description" IS DISTINCT FROM NEW."Description"
                        OR OLD."Amount" IS DISTINCT FROM NEW."Amount"
                        OR OLD."Balance" IS DISTINCT FROM NEW."Balance"
                        OR OLD."Reference" IS DISTINCT FROM NEW."Reference"
                        OR OLD."SourceRowNumber" IS DISTINCT FROM NEW."SourceRowNumber"
                        OR OLD."SourceRowSha256" IS DISTINCT FROM NEW."SourceRowSha256"
                        OR OLD."SourceRowJson" IS DISTINCT FROM NEW."SourceRowJson"
                        OR OLD."DuplicateCandidateKind" IS DISTINCT FROM NEW."DuplicateCandidateKind"
                        OR OLD."DuplicateConfidence" IS DISTINCT FROM NEW."DuplicateConfidence"
                        OR OLD."DuplicateCandidateReasonsJson" IS DISTINCT FROM NEW."DuplicateCandidateReasonsJson"
                        OR OLD."DuplicateMatchedTransactionId" IS DISTINCT FROM NEW."DuplicateMatchedTransactionId"
                        OR OLD."DuplicateMatchedSourceRowSha256" IS DISTINCT FROM NEW."DuplicateMatchedSourceRowSha256" THEN
                        RAISE EXCEPTION 'Imported transaction source facts and duplicate evidence are immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_imported_transactions_source_immutable';
                    END IF;
                    RETURN NEW;
                END
                $function$;

                CREATE TRIGGER "TR_imported_transactions_source_immutable"
                    BEFORE UPDATE OR DELETE ON imported_transactions
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_imported_transaction_source_change();

                CREATE FUNCTION accounts_prevent_bank_import_identity_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF (OLD."Name" IS DISTINCT FROM NEW."Name"
                        OR OLD."Iban" IS DISTINCT FROM NEW."Iban"
                        OR OLD."Currency" IS DISTINCT FROM NEW."Currency")
                        AND (EXISTS (SELECT 1 FROM import_batches WHERE "BankAccountId" = OLD."Id")
                            OR EXISTS (SELECT 1 FROM imported_transactions WHERE "BankAccountId" = OLD."Id")) THEN
                        RAISE EXCEPTION 'Bank identity and currency are immutable after import evidence is retained.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_bank_accounts_import_identity_immutable';
                    END IF;
                    RETURN NEW;
                END
                $function$;

                CREATE TRIGGER "TR_bank_accounts_import_identity_immutable"
                    BEFORE UPDATE OF "Name", "Iban", "Currency" ON bank_accounts
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_bank_import_identity_change();
                """);

            // Any legacy flag had no reviewer identity, reason or immutable source-match evidence.
            // Re-include draft/review rows. Preserve the figures of already locked/finalised accounts
            // while visibly quarantining their old exclusion for review after the period is reopened.
            migrationBuilder.Sql("""
                UPDATE imported_transactions AS transaction
                SET "DuplicateReviewStatus" = 'LegacyLockedUnverified',
                    "DuplicateCandidateKind" = 'LegacyUnverified',
                    "DuplicateConfidence" = 0,
                    "DuplicateCandidateReasonsJson" = '["Legacy duplicate flag had no retained reviewer evidence. Its exclusion is temporarily preserved to avoid changing locked or finalised accounts; reopen the period and record an explicit decision."]'::jsonb
                FROM accounting_periods AS period
                WHERE transaction."IsDuplicate" = TRUE
                  AND transaction."PeriodId" = period."Id"
                  AND (period."Status" IN ('Finalised', 'Filed') OR period."LockedAt" IS NOT NULL);

                UPDATE imported_transactions
                SET "DuplicateReviewStatus" = 'Pending',
                    "DuplicateCandidateKind" = 'LegacyUnverified',
                    "DuplicateConfidence" = 0,
                    "DuplicateCandidateReasonsJson" = '["Legacy duplicate flag had no retained reviewer evidence and requires re-review."]'::jsonb,
                    "IsDuplicate" = FALSE
                WHERE "IsDuplicate" = TRUE
                  AND "DuplicateReviewStatus" = 'NotCandidate';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_imported_transactions_BankAccountId_SourceRowSha256",
                table: "imported_transactions",
                columns: new[] { "BankAccountId", "SourceRowSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_imported_transactions_PeriodId_DuplicateReviewStatus",
                table: "imported_transactions",
                columns: new[] { "PeriodId", "DuplicateReviewStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_imported_transactions_duplicate_candidate",
                table: "imported_transactions",
                sql: "(\"DuplicateReviewStatus\" = 'NotCandidate' AND \"DuplicateCandidateKind\" IS NULL AND \"DuplicateConfidence\" IS NULL AND \"DuplicateCandidateReasonsJson\" IS NULL AND \"DuplicateMatchedTransactionId\" IS NULL AND \"DuplicateMatchedSourceRowSha256\" IS NULL) OR (\"DuplicateReviewStatus\" IN ('Pending', 'LegacyLockedUnverified', 'Retained', 'Discarded') AND \"DuplicateCandidateKind\" IS NOT NULL AND \"DuplicateConfidence\" BETWEEN 0 AND 1 AND \"DuplicateCandidateReasonsJson\" IS NOT NULL AND (\"DuplicateCandidateKind\" = 'LegacyUnverified' OR (\"SourceRowNumber\" > 0 AND \"SourceRowSha256\" IS NOT NULL AND \"SourceRowJson\" IS NOT NULL AND (\"DuplicateMatchedTransactionId\" > 0 OR \"DuplicateMatchedSourceRowSha256\" IS NOT NULL))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_imported_transactions_duplicate_decision",
                table: "imported_transactions",
                sql: "(\"DuplicateDecisionVersion\" = 0 AND \"DuplicateReviewStatus\" IN ('NotCandidate', 'Pending', 'LegacyLockedUnverified') AND \"DuplicateDecisionByUserId\" IS NULL AND \"DuplicateDecisionByDisplayName\" IS NULL AND \"DuplicateDecisionAtUtc\" IS NULL AND \"DuplicateDecisionReason\" IS NULL) OR (\"DuplicateDecisionVersion\" > 0 AND \"DuplicateReviewStatus\" IN ('Pending', 'Retained', 'Discarded') AND \"DuplicateDecisionByUserId\" IS NOT NULL AND \"DuplicateDecisionByDisplayName\" IS NOT NULL AND \"DuplicateDecisionAtUtc\" IS NOT NULL AND char_length(\"DuplicateDecisionReason\") BETWEEN 20 AND 1000)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_imported_transactions_duplicate_hashes",
                table: "imported_transactions",
                sql: "(\"SourceRowSha256\" IS NULL OR \"SourceRowSha256\" ~ '^[0-9a-f]{64}$') AND (\"DuplicateMatchedSourceRowSha256\" IS NULL OR \"DuplicateMatchedSourceRowSha256\" ~ '^[0-9a-f]{64}$') AND (\"DuplicateMatchedTransactionId\" IS NULL OR \"DuplicateMatchedTransactionId\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_imported_transactions_duplicate_ledger_state",
                table: "imported_transactions",
                sql: "\"IsDuplicate\" = (\"DuplicateReviewStatus\" IN ('Discarded', 'LegacyLockedUnverified'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_imported_transactions_source_evidence",
                table: "imported_transactions",
                sql: "(\"SourceRowNumber\" IS NULL AND \"SourceRowSha256\" IS NULL AND \"SourceRowJson\" IS NULL) OR (\"SourceRowNumber\" > 0 AND \"SourceRowSha256\" ~ '^[0-9a-f]{64}$' AND jsonb_typeof(\"SourceRowJson\") = 'array' AND \"ImportBatchId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_BankAccountId_SourceFileSha256",
                table: "import_batches",
                columns: new[] { "BankAccountId", "SourceFileSha256" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_import_batches_source_evidence",
                table: "import_batches",
                sql: "(\"SourceFileSha256\" IS NULL AND \"SourceFileBytes\" IS NULL AND \"SourceHeaderJson\" IS NULL AND \"ImportWarningsJson\" IS NULL) OR (\"SourceFileSha256\" ~ '^[0-9a-f]{64}$' AND \"SourceFileBytes\" > 0 AND jsonb_typeof(\"SourceHeaderJson\") = 'array' AND jsonb_typeof(\"ImportWarningsJson\") = 'array')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_bank_accounts_currency_code",
                table: "bank_accounts");

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_bank_accounts_import_identity_immutable" ON bank_accounts;
                DROP FUNCTION IF EXISTS accounts_prevent_bank_import_identity_change();
                DROP TRIGGER IF EXISTS "TR_imported_transactions_source_immutable" ON imported_transactions;
                DROP FUNCTION IF EXISTS accounts_prevent_imported_transaction_source_change();
                DROP TRIGGER IF EXISTS "TR_import_batches_source_immutable" ON import_batches;
                DROP FUNCTION IF EXISTS accounts_prevent_import_batch_source_change();
                DROP TRIGGER IF EXISTS "TR_imported_transactions_duplicate_match_ownership" ON imported_transactions;
                DROP FUNCTION IF EXISTS accounts_validate_duplicate_match_ownership();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_import_batches_bank_accounts_BankAccountId",
                table: "import_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_imported_transactions_accounting_periods_PeriodId",
                table: "imported_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_imported_transactions_bank_accounts_BankAccountId",
                table: "imported_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_imported_transactions_import_batches_ImportBatchId",
                table: "imported_transactions");

            migrationBuilder.DropIndex(
                name: "IX_imported_transactions_BankAccountId_SourceRowSha256",
                table: "imported_transactions");

            migrationBuilder.DropIndex(
                name: "IX_imported_transactions_PeriodId_DuplicateReviewStatus",
                table: "imported_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_imported_transactions_duplicate_candidate",
                table: "imported_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_imported_transactions_duplicate_decision",
                table: "imported_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_imported_transactions_duplicate_hashes",
                table: "imported_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_imported_transactions_duplicate_ledger_state",
                table: "imported_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_imported_transactions_source_evidence",
                table: "imported_transactions");

            migrationBuilder.DropIndex(
                name: "IX_import_batches_BankAccountId_SourceFileSha256",
                table: "import_batches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_import_batches_source_evidence",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "DuplicateCandidateKind",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateCandidateReasonsJson",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateConfidence",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateDecisionAtUtc",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateDecisionByDisplayName",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateDecisionByUserId",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateDecisionReason",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateDecisionVersion",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateMatchedSourceRowSha256",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateMatchedTransactionId",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateReviewStatus",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "SourceRowJson",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "SourceRowNumber",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "SourceRowSha256",
                table: "imported_transactions");

            migrationBuilder.DropColumn(
                name: "ImportWarningsJson",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "SourceFileBytes",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "SourceFileSha256",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "SourceHeaderJson",
                table: "import_batches");

            migrationBuilder.CreateIndex(
                name: "IX_imported_transactions_PeriodId",
                table: "imported_transactions",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_BankAccountId",
                table: "import_batches",
                column: "BankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_import_batches_bank_accounts_BankAccountId",
                table: "import_batches",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_imported_transactions_accounting_periods_PeriodId",
                table: "imported_transactions",
                column: "PeriodId",
                principalTable: "accounting_periods",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_imported_transactions_bank_accounts_BankAccountId",
                table: "imported_transactions",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_imported_transactions_import_batches_ImportBatchId",
                table: "imported_transactions",
                column: "ImportBatchId",
                principalTable: "import_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
