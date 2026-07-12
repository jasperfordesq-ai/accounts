using System.Text;

namespace Accounts.Api.Data;

/// <summary>
/// Produces the PostgreSQL DDL used by the versioned tenant-isolation migration. The migration
/// passes an immutable policy inventory; the live verifier separately compares the resulting
/// database with <see cref="TenantIsolationPolicyCatalog"/> so newly mapped tables fail closed.
/// </summary>
public static class TenantIsolationMigrationSql
{
    // Immutable inventory installed by migration 20260711060000. New tenant-owned tables require
    // a later migration; never append them here or a fresh database would protect a table before
    // the migration that creates it has run.
    public static IReadOnlyList<TenantIsolationPolicyDefinition> Version1PolicyInventory { get; } =
    [
        new("tenants", TenantOwnershipPath.TenantPrimaryKey),
        new("user_accounts", TenantOwnershipPath.DirectTenantId),
        new("user_action_tokens", TenantOwnershipPath.DirectTenantId),
        new("user_mfa_credentials", TenantOwnershipPath.DirectTenantId),
        new("user_mfa_recovery_codes", TenantOwnershipPath.DirectTenantId),
        new("user_mfa_challenges", TenantOwnershipPath.DirectTenantId),
        new("user_lifecycle_events", TenantOwnershipPath.DirectTenantId),
        new("companies", TenantOwnershipPath.DirectTenantId),
        new("filing_authority_engagements", TenantOwnershipPath.DirectTenantId),
        new("external_filing_handoff_snapshots", TenantOwnershipPath.DirectTenantId),
        new("external_filing_outcome_events", TenantOwnershipPath.DirectTenantId),
        new("audit_logs", TenantOwnershipPath.DirectTenantId),
        new("audit_integrity_checkpoints", TenantOwnershipPath.DirectTenantId),
        new("company_quarantine_events", TenantOwnershipPath.DirectTenantId),
        new("company_onboarding_requests", TenantOwnershipPath.DirectTenantId),
        new("idempotency_records", TenantOwnershipPath.DirectTenantId),
        new("privacy_subject_requests", TenantOwnershipPath.DirectTenantId),
        new("privacy_incident_exercises", TenantOwnershipPath.DirectTenantId),
        new("deadline_reminder_outbox", TenantOwnershipPath.DirectTenantId),
        new("platform_job_runs", TenantOwnershipPath.DirectTenantId),
        new("login_security_events", TenantOwnershipPath.NullableTenantLoginTelemetry),
        new("annual_return_date_records", TenantOwnershipPath.Company),
        new("company_officers", TenantOwnershipPath.Company),
        new("accounting_periods", TenantOwnershipPath.Company),
        new("bank_accounts", TenantOwnershipPath.Company),
        new("transaction_rules", TenantOwnershipPath.Company),
        new("fixed_assets", TenantOwnershipPath.Company),
        new("loans", TenantOwnershipPath.Company),
        new("share_capitals", TenantOwnershipPath.Company),
        new("filing_deadlines", TenantOwnershipPath.Company),
        new("filing_histories", TenantOwnershipPath.Company),
        new("charity_infos", TenantOwnershipPath.Company),
        new("size_classifications", TenantOwnershipPath.Period),
        new("filing_regimes", TenantOwnershipPath.Period),
        new("cro_filing_packages", TenantOwnershipPath.Period),
        new("revenue_filing_packages", TenantOwnershipPath.Period),
        new("charity_filing_packages", TenantOwnershipPath.Period),
        new("debtors", TenantOwnershipPath.Period),
        new("creditors", TenantOwnershipPath.Period),
        new("depreciation_entries", TenantOwnershipPath.Period),
        new("capital_allowance_claims", TenantOwnershipPath.Period),
        new("inventories", TenantOwnershipPath.Period),
        new("opening_balances", TenantOwnershipPath.Period),
        new("year_end_review_confirmations", TenantOwnershipPath.Period),
        new("loan_balance_snapshots", TenantOwnershipPath.Period),
        new("director_loans", TenantOwnershipPath.Period),
        new("payroll_summaries", TenantOwnershipPath.Period),
        new("corporation_tax_scope_reviews", TenantOwnershipPath.Period),
        new("corporation_tax_loss_records", TenantOwnershipPath.Period),
        new("corporation_tax_filing_support_reviews", TenantOwnershipPath.Period),
        new("corporation_tax_payment_records", TenantOwnershipPath.Period),
        new("tax_balances", TenantOwnershipPath.Period),
        new("dividends", TenantOwnershipPath.Period),
        new("adjustments", TenantOwnershipPath.Period),
        new("reports", TenantOwnershipPath.Period),
        new("notes_disclosures", TenantOwnershipPath.Period),
        new("post_balance_sheet_events", TenantOwnershipPath.Period),
        new("related_party_transactions", TenantOwnershipPath.Period),
        new("contingent_liabilities", TenantOwnershipPath.Period),
        new("fund_balances", TenantOwnershipPath.Period),
        new("user_company_accesses", TenantOwnershipPath.UserAndCompany),
        new("import_batches", TenantOwnershipPath.BankAccount),
        new("imported_transactions", TenantOwnershipPath.BankAccount),
        new("director_loan_movements", TenantOwnershipPath.DirectorLoan),
        new("account_categories", TenantOwnershipPath.NullableCompanyWithGlobalReadOnlyRows)
    ];

    public static string BuildInstallSql(IReadOnlyList<TenantIsolationPolicyDefinition> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (policies.Count == 0) throw new ArgumentException("At least one tenant table is required.", nameof(policies));

        var duplicate = policies.GroupBy(policy => policy.TableName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate tenant-isolation table '{duplicate.Key}'.", nameof(policies));

        var sql = new StringBuilder(64_000);
        sql.Append(BaseInstallSql
            .Replace("__APPLICATION_ROLE__", TenantIsolationPolicyCatalog.ApplicationGroupRole, StringComparison.Ordinal)
            .Replace("__ADMINISTRATOR_ROLE__", TenantIsolationPolicyCatalog.AdministratorGroupRole, StringComparison.Ordinal));

        foreach (var policy in policies)
            AppendTablePolicy(sql, policy);

        sql.Append(SequenceGrantSql
            .Replace("__APPLICATION_ROLE__", TenantIsolationPolicyCatalog.ApplicationGroupRole, StringComparison.Ordinal)
            .Replace("__ADMINISTRATOR_ROLE__", TenantIsolationPolicyCatalog.AdministratorGroupRole, StringComparison.Ordinal));
        return sql.ToString();
    }

    public static string BuildRemoveSql(IReadOnlyList<TenantIsolationPolicyDefinition> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var sql = new StringBuilder();
        foreach (var policy in policies.Reverse())
        {
            var table = QuoteIdentifier(policy.TableName);
            foreach (var name in TenantIsolationPolicyCatalog.ExpectedApplicationPolicyNames(policy)
                         .Append(TenantIsolationPolicyCatalog.AdministratorPolicyName))
            {
                sql.Append("DROP POLICY IF EXISTS ").Append(QuoteIdentifier(name))
                    .Append(" ON ").Append(table).AppendLine(";");
            }
            sql.Append("ALTER TABLE ").Append(table).AppendLine(" DISABLE ROW LEVEL SECURITY;");
        }

        sql.AppendLine("DROP FUNCTION IF EXISTS accounts_delete_expired_anonymous_login_events();")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_list_tenant_ids_for_jobs();")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_resolve_mfa_challenge_tenant(text);")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_resolve_action_token_tenant(text, text);")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_email_exists(text);")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_resolve_login_tenant(text, text);")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_resolve_login_tenant(text);")
            .AppendLine("DROP FUNCTION IF EXISTS accounts_current_tenant_id();")
            .AppendLine("DROP TABLE IF EXISTS tenant_rls_context_keys;");
        return sql.ToString();
    }

    private static void AppendTablePolicy(StringBuilder sql, TenantIsolationPolicyDefinition policy)
    {
        var table = QuoteIdentifier(policy.TableName);
        sql.Append("ALTER TABLE ").Append(table).AppendLine(" ENABLE ROW LEVEL SECURITY;")
            .Append("ALTER TABLE ").Append(table).AppendLine(" FORCE ROW LEVEL SECURITY;")
            .Append("REVOKE ALL PRIVILEGES ON TABLE ").Append(table).AppendLine(" FROM PUBLIC;")
            .Append("GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE ").Append(table)
            .Append(" TO ").Append(QuoteIdentifier(TenantIsolationPolicyCatalog.ApplicationGroupRole)).AppendLine(";")
            .Append("GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE ").Append(table)
            .Append(" TO ").Append(QuoteIdentifier(TenantIsolationPolicyCatalog.AdministratorGroupRole)).AppendLine(";");

        if (policy.OwnershipPath is TenantOwnershipPath.NullableCompanyWithGlobalReadOnlyRows)
        {
            var owned = CompanyOwnershipExpression(policy.TableName, "CompanyId");
            var readable = $"({table}.\"CompanyId\" IS NULL OR {owned})";
            var writable = $"({table}.\"CompanyId\" IS NOT NULL AND {owned})";
            AppendSplitApplicationPolicies(sql, table, readable, writable, writable, writable);
        }
        else if (policy.OwnershipPath is TenantOwnershipPath.NullableTenantLoginTelemetry)
        {
            var owned = $"{table}.\"TenantId\" = accounts_current_tenant_id()";
            var insertable = $"({table}.\"TenantId\" IS NULL OR {owned})";
            AppendSplitApplicationPolicies(sql, table, owned, insertable, owned, owned);
        }
        else
        {
            var expression = OwnershipExpression(policy);
            sql.Append("CREATE POLICY ").Append(QuoteIdentifier(TenantIsolationPolicyCatalog.ApplicationPolicyName))
                .Append(" ON ").Append(table)
                .Append(" AS PERMISSIVE FOR ALL TO ")
                .Append(QuoteIdentifier(TenantIsolationPolicyCatalog.ApplicationGroupRole))
                .Append(" USING (").Append(expression).Append(") WITH CHECK (").Append(expression).AppendLine(");");
        }

        sql.Append("CREATE POLICY ").Append(QuoteIdentifier(TenantIsolationPolicyCatalog.AdministratorPolicyName))
            .Append(" ON ").Append(table)
            .Append(" AS PERMISSIVE FOR ALL TO ")
            .Append(QuoteIdentifier(TenantIsolationPolicyCatalog.AdministratorGroupRole))
            .AppendLine(" USING (true) WITH CHECK (true);");
    }

    private static void AppendSplitApplicationPolicies(
        StringBuilder sql,
        string table,
        string selectExpression,
        string insertExpression,
        string updateExpression,
        string deleteExpression)
    {
        AppendPolicy(sql, TenantIsolationPolicyCatalog.ApplicationSelectPolicyName, table, "SELECT", selectExpression, null);
        AppendPolicy(sql, TenantIsolationPolicyCatalog.ApplicationInsertPolicyName, table, "INSERT", null, insertExpression);
        AppendPolicy(sql, TenantIsolationPolicyCatalog.ApplicationUpdatePolicyName, table, "UPDATE", updateExpression, updateExpression);
        AppendPolicy(sql, TenantIsolationPolicyCatalog.ApplicationDeletePolicyName, table, "DELETE", deleteExpression, null);
    }

    private static void AppendPolicy(
        StringBuilder sql,
        string name,
        string table,
        string operation,
        string? usingExpression,
        string? checkExpression)
    {
        sql.Append("CREATE POLICY ").Append(QuoteIdentifier(name)).Append(" ON ").Append(table)
            .Append(" AS PERMISSIVE FOR ").Append(operation).Append(" TO ")
            .Append(QuoteIdentifier(TenantIsolationPolicyCatalog.ApplicationGroupRole));
        if (usingExpression is not null) sql.Append(" USING (").Append(usingExpression).Append(')');
        if (checkExpression is not null) sql.Append(" WITH CHECK (").Append(checkExpression).Append(')');
        sql.AppendLine(";");
    }

    private static string OwnershipExpression(TenantIsolationPolicyDefinition policy) => policy.OwnershipPath switch
    {
        TenantOwnershipPath.TenantPrimaryKey =>
            $"{QuoteIdentifier(policy.TableName)}.\"Id\" = accounts_current_tenant_id()",
        TenantOwnershipPath.DirectTenantId =>
            $"{QuoteIdentifier(policy.TableName)}.\"TenantId\" = accounts_current_tenant_id()",
        TenantOwnershipPath.Company => CompanyOwnershipExpression(policy.TableName, "CompanyId"),
        TenantOwnershipPath.Period => PeriodOwnershipExpression(policy.TableName, "PeriodId"),
        TenantOwnershipPath.BankAccount => BankOwnershipExpression(policy.TableName, "BankAccountId"),
        TenantOwnershipPath.DirectorLoan => DirectorLoanOwnershipExpression(policy.TableName, "DirectorLoanId"),
        TenantOwnershipPath.UserAndCompany => UserAndCompanyOwnershipExpression(policy.TableName),
        _ => throw new InvalidOperationException(
            $"Ownership path '{policy.OwnershipPath}' requires split operation policies.")
    };

    private static string CompanyOwnershipExpression(string tableName, string companyColumn) =>
        $"EXISTS (SELECT 1 FROM \"companies\" AS tenant_company " +
        $"WHERE tenant_company.\"Id\" = {QuoteIdentifier(tableName)}.{QuoteIdentifier(companyColumn)} " +
        "AND tenant_company.\"TenantId\" = accounts_current_tenant_id())";

    private static string PeriodOwnershipExpression(string tableName, string periodColumn) =>
        $"EXISTS (SELECT 1 FROM \"accounting_periods\" AS tenant_period " +
        "JOIN \"companies\" AS tenant_company ON tenant_company.\"Id\" = tenant_period.\"CompanyId\" " +
        $"WHERE tenant_period.\"Id\" = {QuoteIdentifier(tableName)}.{QuoteIdentifier(periodColumn)} " +
        "AND tenant_company.\"TenantId\" = accounts_current_tenant_id())";

    private static string BankOwnershipExpression(string tableName, string bankColumn) =>
        $"EXISTS (SELECT 1 FROM \"bank_accounts\" AS tenant_bank " +
        "JOIN \"companies\" AS tenant_company ON tenant_company.\"Id\" = tenant_bank.\"CompanyId\" " +
        $"WHERE tenant_bank.\"Id\" = {QuoteIdentifier(tableName)}.{QuoteIdentifier(bankColumn)} " +
        "AND tenant_company.\"TenantId\" = accounts_current_tenant_id())";

    private static string DirectorLoanOwnershipExpression(string tableName, string directorLoanColumn) =>
        $"EXISTS (SELECT 1 FROM \"director_loans\" AS tenant_loan " +
        "JOIN \"accounting_periods\" AS tenant_period ON tenant_period.\"Id\" = tenant_loan.\"PeriodId\" " +
        "JOIN \"companies\" AS tenant_company ON tenant_company.\"Id\" = tenant_period.\"CompanyId\" " +
        $"WHERE tenant_loan.\"Id\" = {QuoteIdentifier(tableName)}.{QuoteIdentifier(directorLoanColumn)} " +
        "AND tenant_company.\"TenantId\" = accounts_current_tenant_id())";

    private static string UserAndCompanyOwnershipExpression(string tableName)
    {
        var table = QuoteIdentifier(tableName);
        return "EXISTS (SELECT 1 FROM \"user_accounts\" AS tenant_user " +
            $"WHERE tenant_user.\"Id\" = {table}.\"UserId\" " +
            "AND tenant_user.\"TenantId\" = accounts_current_tenant_id()) " +
            "AND " + CompanyOwnershipExpression(tableName, "CompanyId");
    }

    private static string QuoteIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            throw new ArgumentException("PostgreSQL identifier is invalid.", nameof(value));
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private const string BaseInstallSql = """
        DO $tenant_roles$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '__APPLICATION_ROLE__') THEN
                CREATE ROLE __APPLICATION_ROLE__ NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '__ADMINISTRATOR_ROLE__') THEN
                CREATE ROLE __ADMINISTRATOR_ROLE__ NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
            END IF;
            ALTER ROLE __APPLICATION_ROLE__ NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
            ALTER ROLE __ADMINISTRATOR_ROLE__ NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
            REVOKE __ADMINISTRATOR_ROLE__ FROM __APPLICATION_ROLE__;
            IF NOT pg_has_role(current_user, '__ADMINISTRATOR_ROLE__', 'MEMBER') THEN
                EXECUTE format('GRANT __ADMINISTRATOR_ROLE__ TO %I', current_user);
            END IF;
        END
        $tenant_roles$;

        CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;
        SELECT set_config('search_path', quote_ident(current_schema()) || ',pg_catalog', true);

        CREATE TABLE tenant_rls_context_keys (
            "KeyId" integer PRIMARY KEY,
            "KeyMaterial" bytea NOT NULL CHECK (octet_length("KeyMaterial") >= 32),
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        REVOKE ALL PRIVILEGES ON TABLE tenant_rls_context_keys FROM PUBLIC;
        REVOKE ALL PRIVILEGES ON TABLE tenant_rls_context_keys FROM __APPLICATION_ROLE__;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE tenant_rls_context_keys TO __ADMINISTRATOR_ROLE__;

        CREATE FUNCTION accounts_current_tenant_id()
        RETURNS integer
        LANGUAGE plpgsql
        STABLE
        SECURITY DEFINER
        SET search_path FROM CURRENT
        AS $tenant_context$
        DECLARE
            tenant_text text;
            supplied_signature text;
            expected_signature text;
            signing_key bytea;
            tenant_number bigint;
        BEGIN
            tenant_text := current_setting('accounts.tenant_id', true);
            supplied_signature := lower(current_setting('accounts.tenant_signature', true));
            IF tenant_text IS NULL OR tenant_text !~ '^[1-9][0-9]{0,9}$' THEN
                RETURN NULL;
            END IF;
            tenant_number := tenant_text::bigint;
            IF tenant_number > 2147483647 THEN
                RETURN NULL;
            END IF;
            SELECT "KeyMaterial" INTO signing_key FROM tenant_rls_context_keys WHERE "KeyId" = 1;
            IF signing_key IS NULL THEN
                RETURN NULL;
            END IF;
            expected_signature := encode(
                public.hmac(convert_to(tenant_text || ':' || pg_backend_pid()::text, 'UTF8'), signing_key, 'sha256'),
                'hex');
            IF supplied_signature IS NULL
               OR length(supplied_signature) <> 64
               OR supplied_signature <> expected_signature THEN
                RETURN NULL;
            END IF;
            RETURN tenant_number::integer;
        EXCEPTION WHEN OTHERS THEN
            RETURN NULL;
        END
        $tenant_context$;

        CREATE FUNCTION accounts_resolve_login_tenant(tenant_slug text, lookup_value text)
        RETURNS integer
        LANGUAGE sql
        STABLE
        SECURITY DEFINER
        SET search_path FROM CURRENT
        AS $resolver$
            SELECT login_user."TenantId"
            FROM user_accounts AS login_user
            INNER JOIN tenants AS login_tenant ON login_tenant."Id" = login_user."TenantId"
            WHERE login_tenant."Slug" = lower(btrim(tenant_slug))
              AND login_user."Email" = lower(btrim(lookup_value))
            LIMIT 1
        $resolver$;

        CREATE FUNCTION accounts_resolve_action_token_tenant(lookup_value text, lookup_purpose text)
        RETURNS integer
        LANGUAGE sql
        STABLE
        SECURITY DEFINER
        SET search_path FROM CURRENT
        AS $resolver$
            SELECT "TenantId" FROM user_action_tokens
            WHERE "TokenHash" = lookup_value
              AND "Purpose" = lookup_purpose
              AND "ConsumedAtUtc" IS NULL
              AND "RevokedAtUtc" IS NULL
              AND "ExpiresAtUtc" > CURRENT_TIMESTAMP
            LIMIT 1
        $resolver$;

        CREATE FUNCTION accounts_resolve_mfa_challenge_tenant(lookup_value text)
        RETURNS integer
        LANGUAGE sql
        STABLE
        SECURITY DEFINER
        SET search_path FROM CURRENT
        AS $resolver$
            SELECT "TenantId" FROM user_mfa_challenges
            WHERE "TokenHash" = lookup_value
              AND "ConsumedAtUtc" IS NULL
              AND "RevokedAtUtc" IS NULL
              AND "FailedAttempts" < 5
              AND "ExpiresAtUtc" > CURRENT_TIMESTAMP
            LIMIT 1
        $resolver$;

        CREATE FUNCTION accounts_list_tenant_ids_for_jobs()
        RETURNS SETOF integer
        LANGUAGE sql
        STABLE
        SECURITY DEFINER
        SET search_path FROM CURRENT
        AS $resolver$
            SELECT "Id" FROM tenants ORDER BY "Id"
        $resolver$;

        CREATE FUNCTION accounts_delete_expired_anonymous_login_events()
        RETURNS bigint
        LANGUAGE sql
        VOLATILE
        SECURITY DEFINER
        SET search_path FROM CURRENT
        AS $cleanup$
            WITH deleted AS (
                DELETE FROM login_security_events
                WHERE "TenantId" IS NULL AND "ExpiresAtUtc" <= CURRENT_TIMESTAMP
                RETURNING 1
            )
            SELECT count(*) FROM deleted
        $cleanup$;

        REVOKE ALL ON FUNCTION accounts_current_tenant_id() FROM PUBLIC;
        REVOKE ALL ON FUNCTION accounts_resolve_login_tenant(text, text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION accounts_resolve_action_token_tenant(text, text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION accounts_resolve_mfa_challenge_tenant(text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION accounts_list_tenant_ids_for_jobs() FROM PUBLIC;
        REVOKE ALL ON FUNCTION accounts_delete_expired_anonymous_login_events() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION accounts_current_tenant_id() TO __APPLICATION_ROLE__, __ADMINISTRATOR_ROLE__;
        GRANT EXECUTE ON FUNCTION accounts_resolve_login_tenant(text, text) TO __APPLICATION_ROLE__;
        GRANT EXECUTE ON FUNCTION accounts_resolve_action_token_tenant(text, text) TO __APPLICATION_ROLE__;
        GRANT EXECUTE ON FUNCTION accounts_resolve_mfa_challenge_tenant(text) TO __APPLICATION_ROLE__;
        GRANT EXECUTE ON FUNCTION accounts_list_tenant_ids_for_jobs() TO __APPLICATION_ROLE__;
        GRANT EXECUTE ON FUNCTION accounts_delete_expired_anonymous_login_events() TO __APPLICATION_ROLE__;

        """;

    private const string SequenceGrantSql = """
        DO $sequence_grants$
        DECLARE
            sequence_name text;
        BEGIN
            FOR sequence_name IN
                SELECT sequence_entry.relname
                FROM pg_catalog.pg_class AS sequence_entry
                JOIN pg_catalog.pg_namespace AS namespace_entry ON namespace_entry.oid = sequence_entry.relnamespace
                WHERE namespace_entry.nspname = current_schema()
                  AND sequence_entry.relkind = 'S'
            LOOP
                EXECUTE format(
                    'GRANT USAGE, SELECT ON SEQUENCE %I.%I TO __APPLICATION_ROLE__, __ADMINISTRATOR_ROLE__',
                    current_schema(), sequence_name);
            END LOOP;
        END
        $sequence_grants$;
        """;
}
