using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710180000_EnforcePeriodChronology")]
public partial class EnforcePeriodChronology : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $chronology_preflight$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM accounting_periods AS period
                    JOIN companies AS company ON company."Id" = period."CompanyId"
                    WHERE period."PeriodStart" < company."IncorporationDate"
                       OR period."PeriodEnd" < period."PeriodStart"
                       OR period."PeriodEnd" > (period."PeriodStart" + INTERVAL '18 months' - INTERVAL '1 day')::date
                       OR (period."IsFirstYear" AND period."PeriodStart" <> company."IncorporationDate")
                ) THEN
                    RAISE EXCEPTION 'Existing accounting-period chronology is invalid.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_chronology';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM accounting_periods AS left_period
                    JOIN accounting_periods AS right_period
                      ON right_period."CompanyId" = left_period."CompanyId"
                     AND right_period."Id" > left_period."Id"
                     AND right_period."PeriodStart" <= left_period."PeriodEnd"
                     AND right_period."PeriodEnd" >= left_period."PeriodStart"
                ) THEN
                    RAISE EXCEPTION 'Existing accounting periods overlap.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_no_overlap';
                END IF;

                IF EXISTS (
                    SELECT period."CompanyId"
                    FROM accounting_periods AS period
                    WHERE period."IsFirstYear"
                    GROUP BY period."CompanyId"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'A company has more than one first-year accounting period.'
                        USING ERRCODE = '23514', CONSTRAINT = 'UX_accounting_periods_one_first_year';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM companies AS company
                    WHERE EXISTS (
                        SELECT 1
                        FROM accounting_periods AS period
                        WHERE period."CompanyId" = company."Id"
                    )
                    AND (
                        SELECT COUNT(*)
                        FROM accounting_periods AS period
                        WHERE period."CompanyId" = company."Id"
                          AND period."IsFirstYear"
                    ) <> 1
                ) THEN
                    RAISE EXCEPTION 'Existing accounting-period history must contain exactly one first year.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_first_year_required';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM (
                        SELECT
                            period."CompanyId",
                            period."PeriodStart",
                            period."IsFirstYear",
                            ROW_NUMBER() OVER (
                                PARTITION BY period."CompanyId"
                                ORDER BY period."PeriodStart", period."PeriodEnd", period."Id"
                            ) AS chronology_position,
                            LAG(period."PeriodEnd") OVER (
                                PARTITION BY period."CompanyId"
                                ORDER BY period."PeriodStart", period."PeriodEnd", period."Id"
                            ) AS previous_end
                        FROM accounting_periods AS period
                    ) AS chronology
                    WHERE (chronology.chronology_position = 1 AND NOT chronology."IsFirstYear")
                       OR (chronology.chronology_position > 1 AND chronology."IsFirstYear")
                       OR (chronology.previous_end IS NOT NULL
                           AND chronology."PeriodStart" <> chronology.previous_end + 1)
                ) THEN
                    RAISE EXCEPTION 'Existing accounting-period history has an invalid first year or unexplained gap.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_contiguous';
                END IF;
            END
            $chronology_preflight$;

            ALTER TABLE accounting_periods
                ADD CONSTRAINT "CK_accounting_periods_date_order"
                CHECK ("PeriodEnd" >= "PeriodStart");

            ALTER TABLE accounting_periods
                ADD CONSTRAINT "CK_accounting_periods_maximum_length"
                CHECK ("PeriodEnd" <= ("PeriodStart" + INTERVAL '18 months' - INTERVAL '1 day')::date);

            CREATE UNIQUE INDEX "UX_accounting_periods_one_first_year"
                ON accounting_periods ("CompanyId")
                WHERE "IsFirstYear";

            CREATE FUNCTION accounts_validate_period_chronology()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                incorporation_date date;
                previous_end date;
                next_start date;
                other_period_count integer;
                other_first_year_count integer;
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    PERFORM pg_advisory_xact_lock(OLD."CompanyId"::bigint);
                    IF EXISTS (SELECT 1 FROM companies WHERE "Id" = OLD."CompanyId")
                       AND EXISTS (
                           SELECT 1
                           FROM accounting_periods AS period
                           WHERE period."CompanyId" = OLD."CompanyId"
                             AND period."Id" <> OLD."Id"
                       ) THEN
                        RAISE EXCEPTION 'An accounting period cannot be deleted while later or earlier periods remain.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_delete_history';
                    END IF;
                    RETURN OLD;
                END IF;

                IF TG_OP = 'UPDATE' THEN
                    IF NEW."CompanyId" <> OLD."CompanyId" THEN
                        RAISE EXCEPTION 'An accounting period cannot be moved to another company.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_company_immutable';
                    END IF;
                END IF;

                PERFORM pg_advisory_xact_lock(NEW."CompanyId"::bigint);

                SELECT company."IncorporationDate"
                INTO incorporation_date
                FROM companies AS company
                WHERE company."Id" = NEW."CompanyId";

                IF incorporation_date IS NULL THEN
                    RAISE EXCEPTION 'Accounting period company does not exist.'
                        USING ERRCODE = '23503', CONSTRAINT = 'FK_accounting_periods_companies_CompanyId';
                END IF;

                IF NEW."IsFirstYear" AND EXISTS (
                    SELECT 1
                    FROM accounting_periods AS period
                    WHERE period."CompanyId" = NEW."CompanyId"
                      AND period."Id" <> COALESCE(NEW."Id", 0)
                      AND period."IsFirstYear"
                ) THEN
                    RAISE EXCEPTION 'A company can have only one first-year accounting period.'
                        USING ERRCODE = '23505', CONSTRAINT = 'UX_accounting_periods_one_first_year';
                END IF;

                IF NEW."PeriodStart" < incorporation_date THEN
                    RAISE EXCEPTION 'Accounting period cannot begin before incorporation.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_after_incorporation';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM accounting_periods AS period
                    WHERE period."CompanyId" = NEW."CompanyId"
                      AND period."Id" <> COALESCE(NEW."Id", 0)
                      AND NEW."PeriodStart" <= period."PeriodEnd"
                      AND NEW."PeriodEnd" >= period."PeriodStart"
                ) THEN
                    RAISE EXCEPTION 'Accounting periods cannot overlap.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_no_overlap';
                END IF;

                SELECT COUNT(*), COUNT(*) FILTER (WHERE period."IsFirstYear")
                INTO other_period_count, other_first_year_count
                FROM accounting_periods AS period
                WHERE period."CompanyId" = NEW."CompanyId"
                  AND period."Id" <> COALESCE(NEW."Id", 0);

                IF NEW."IsFirstYear" THEN
                    IF NEW."PeriodStart" <> incorporation_date THEN
                        RAISE EXCEPTION 'First-year accounting period must begin on incorporation.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_first_year_start';
                    END IF;
                    IF other_first_year_count > 0 THEN
                        RAISE EXCEPTION 'A company can have only one first-year accounting period.'
                            USING ERRCODE = '23505', CONSTRAINT = 'UX_accounting_periods_one_first_year';
                    END IF;
                ELSIF other_period_count = 0 OR other_first_year_count = 0 THEN
                    RAISE EXCEPTION 'The first accounting period must be marked as first year.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_first_year_required';
                END IF;

                SELECT MAX(period."PeriodEnd")
                INTO previous_end
                FROM accounting_periods AS period
                WHERE period."CompanyId" = NEW."CompanyId"
                  AND period."Id" <> COALESCE(NEW."Id", 0)
                  AND period."PeriodEnd" < NEW."PeriodStart";

                SELECT MIN(period."PeriodStart")
                INTO next_start
                FROM accounting_periods AS period
                WHERE period."CompanyId" = NEW."CompanyId"
                  AND period."Id" <> COALESCE(NEW."Id", 0)
                  AND period."PeriodStart" > NEW."PeriodEnd";

                IF previous_end IS NULL THEN
                    IF NOT NEW."IsFirstYear" THEN
                        RAISE EXCEPTION 'Earliest accounting period must be first year.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_first_year_required';
                    END IF;
                ELSE
                    IF NEW."IsFirstYear" OR NEW."PeriodStart" <> previous_end + 1 THEN
                        RAISE EXCEPTION 'Accounting-period chronology contains an unexplained gap.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_contiguous';
                    END IF;
                END IF;

                IF next_start IS NOT NULL AND NEW."PeriodEnd" <> next_start - 1 THEN
                    RAISE EXCEPTION 'Accounting-period chronology contains an unexplained gap.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_accounting_periods_contiguous';
                END IF;

                RETURN NEW;
            END
            $function$;

            CREATE TRIGGER "TR_accounting_periods_chronology"
                BEFORE INSERT OR DELETE OR UPDATE OF "CompanyId", "PeriodStart", "PeriodEnd", "IsFirstYear"
                ON accounting_periods
                FOR EACH ROW
                EXECUTE FUNCTION accounts_validate_period_chronology();

            CREATE FUNCTION accounts_validate_company_incorporation_change()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                PERFORM pg_advisory_xact_lock(NEW."Id"::bigint);
                IF NEW."IncorporationDate" IS DISTINCT FROM OLD."IncorporationDate"
                   AND EXISTS (
                       SELECT 1
                       FROM accounting_periods AS period
                       WHERE period."CompanyId" = NEW."Id"
                         AND (period."PeriodStart" < NEW."IncorporationDate"
                              OR (period."IsFirstYear" AND period."PeriodStart" <> NEW."IncorporationDate"))
                   ) THEN
                    RAISE EXCEPTION 'Incorporation-date change would invalidate accounting-period chronology.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_companies_incorporation_period_chronology';
                END IF;
                RETURN NEW;
            END
            $function$;

            CREATE TRIGGER "TR_companies_incorporation_period_chronology"
                BEFORE UPDATE OF "IncorporationDate"
                ON companies
                FOR EACH ROW
                EXECUTE FUNCTION accounts_validate_company_incorporation_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_companies_incorporation_period_chronology" ON companies;
            DROP FUNCTION IF EXISTS accounts_validate_company_incorporation_change();
            DROP TRIGGER IF EXISTS "TR_accounting_periods_chronology" ON accounting_periods;
            DROP FUNCTION IF EXISTS accounts_validate_period_chronology();
            DROP INDEX IF EXISTS "UX_accounting_periods_one_first_year";
            ALTER TABLE accounting_periods DROP CONSTRAINT IF EXISTS "CK_accounting_periods_maximum_length";
            ALTER TABLE accounting_periods DROP CONSTRAINT IF EXISTS "CK_accounting_periods_date_order";
            """);
    }
}
