namespace Accounts.Api.Data;

/// <summary>
/// Canonical inventory of relational tables that must be protected by PostgreSQL row-level
/// security. This is deliberately explicit: adding a mapped table without classifying its tenant
/// ownership path must fail the metadata and live-database completeness gates.
/// </summary>
public static class TenantIsolationPolicyCatalog
{
    public const string ApplicationGroupRole = "accounts_api_rls";
    public const string AdministratorGroupRole = "accounts_migration_rls_admin";
    public const string TenantSettingName = "accounts.tenant_id";
    public const string ApplicationPolicyName = "tenant_isolation_application";
    public const string ApplicationSelectPolicyName = "tenant_isolation_application_select";
    public const string ApplicationInsertPolicyName = "tenant_isolation_application_insert";
    public const string ApplicationUpdatePolicyName = "tenant_isolation_application_update";
    public const string ApplicationDeletePolicyName = "tenant_isolation_application_delete";
    public const string AdministratorPolicyName = "tenant_isolation_administrator";

    public static IReadOnlyList<TenantIsolationPolicyDefinition> Policies { get; } =
    [
        new("tenants", TenantOwnershipPath.TenantPrimaryKey),

        // Tables that retain an authoritative TenantId directly.
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

        // Anonymous rejected-login telemetry may be inserted without a resolved tenant, but it is
        // never readable through that anonymous context. Known-user events remain tenant-scoped.
        new("login_security_events", TenantOwnershipPath.NullableTenantLoginTelemetry),

        // Tables whose authoritative parent is a company.
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

        // Tables whose authoritative parent is an accounting period.
        new("size_classifications", TenantOwnershipPath.Period),
        new("filing_regimes", TenantOwnershipPath.Period),
        new("cro_filing_packages", TenantOwnershipPath.Period),
        new("revenue_filing_packages", TenantOwnershipPath.Period),
        new("charity_filing_packages", TenantOwnershipPath.Period),
        // Imported rows can legitimately be unassigned to a period during review. Their bank
        // account remains the authoritative ownership anchor throughout that state.
        new("imported_transactions", TenantOwnershipPath.BankAccount),
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

        // Deeper or deliberately exceptional ownership paths.
        new("user_company_accesses", TenantOwnershipPath.UserAndCompany),
        new("import_batches", TenantOwnershipPath.BankAccount),
        new("director_loan_movements", TenantOwnershipPath.DirectorLoan),
        new("account_categories", TenantOwnershipPath.NullableCompanyWithGlobalReadOnlyRows)
    ];

    public static IReadOnlySet<string> TableNames { get; } = Policies
        .Select(policy => policy.TableName)
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> ExpectedApplicationPolicyNames(TenantIsolationPolicyDefinition policy) =>
        policy.OwnershipPath is TenantOwnershipPath.NullableCompanyWithGlobalReadOnlyRows
            or TenantOwnershipPath.NullableTenantLoginTelemetry
            ? new HashSet<string>(StringComparer.Ordinal)
            {
                ApplicationSelectPolicyName,
                ApplicationInsertPolicyName,
                ApplicationUpdatePolicyName,
                ApplicationDeletePolicyName
            }
            : new HashSet<string>(StringComparer.Ordinal) { ApplicationPolicyName };
}

public sealed record TenantIsolationPolicyDefinition(
    string TableName,
    TenantOwnershipPath OwnershipPath);

public enum TenantOwnershipPath
{
    TenantPrimaryKey,
    DirectTenantId,
    Company,
    Period,
    BankAccount,
    DirectorLoan,
    UserAndCompany,
    NullableCompanyWithGlobalReadOnlyRows,
    NullableTenantLoginTelemetry
}
