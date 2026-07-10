using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record CompanyDependentInventory(
    IReadOnlyDictionary<string, long> TableCounts,
    long TotalDependentRows,
    string CanonicalJson,
    string Sha256);

/// <summary>
/// Authoritative inventory of every current DbSet whose rows are rooted at a company or one of its
/// accounting periods. The coverage regression test must be updated whenever a new owned DbSet is
/// introduced; quarantine never relies on a sample "has financial data" probe.
/// </summary>
public sealed class CompanyDependentInventoryService(AccountsDbContext db)
{
    public static readonly IReadOnlyList<string> RequiredTableNames =
    [
        "companies",
        "user_company_accesses",
        "company_officers",
        "accounting_periods",
        "size_classifications",
        "filing_regimes",
        "cro_filing_packages",
        "revenue_filing_packages",
        "charity_filing_packages",
        "filing_authority_engagements",
        "external_filing_handoff_snapshots",
        "external_filing_outcome_events",
        "bank_accounts",
        "import_batches",
        "imported_transactions",
        "transaction_rules",
        "account_categories",
        "debtors",
        "creditors",
        "fixed_assets",
        "depreciation_entries",
        "capital_allowance_claims",
        "inventories",
        "loans",
        "loan_balance_snapshots",
        "director_loans",
        "director_loan_movements",
        "payroll_summaries",
        "corporation_tax_scope_reviews",
        "corporation_tax_loss_records",
        "corporation_tax_filing_support_reviews",
        "corporation_tax_payment_records",
        "tax_balances",
        "dividends",
        "opening_balances",
        "year_end_review_confirmations",
        "adjustments",
        "reports",
        "notes_disclosures",
        "share_capitals",
        "filing_deadlines",
        "filing_histories",
        "deadline_reminder_outbox",
        "post_balance_sheet_events",
        "related_party_transactions",
        "contingent_liabilities",
        "charity_infos",
        "fund_balances",
        "audit_logs",
        "audit_integrity_checkpoints",
        "annual_return_date_records",
        "company_onboarding_requests",
        "company_quarantine_events"
    ];

    public async Task<CompanyDependentInventory> CaptureAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["companies"] = await CountAsync<Company>(x => x.Id == companyId, cancellationToken),
            ["user_company_accesses"] = await CountAsync<UserCompanyAccess>(x => x.CompanyId == companyId, cancellationToken),
            ["company_officers"] = await CountAsync<CompanyOfficer>(x => x.CompanyId == companyId, cancellationToken),
            ["accounting_periods"] = await CountAsync<AccountingPeriod>(x => x.CompanyId == companyId, cancellationToken),
            ["size_classifications"] = await CountAsync<SizeClassification>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["filing_regimes"] = await CountAsync<FilingRegime>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["cro_filing_packages"] = await CountAsync<CroFilingPackage>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["revenue_filing_packages"] = await CountAsync<RevenueFilingPackage>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["charity_filing_packages"] = await CountAsync<CharityFilingPackage>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["filing_authority_engagements"] = await CountAsync<FilingAuthorityEngagement>(x => x.CompanyId == companyId, cancellationToken),
            ["external_filing_handoff_snapshots"] = await CountAsync<ExternalFilingHandoffSnapshot>(x => x.CompanyId == companyId, cancellationToken),
            ["external_filing_outcome_events"] = await CountAsync<ExternalFilingOutcomeEvent>(x => x.CompanyId == companyId, cancellationToken),
            ["bank_accounts"] = await CountAsync<BankAccount>(x => x.CompanyId == companyId, cancellationToken),
            ["import_batches"] = await CountAsync<ImportBatch>(x => x.BankAccount.CompanyId == companyId, cancellationToken),
            ["imported_transactions"] = await CountAsync<ImportedTransaction>(x => x.BankAccount.CompanyId == companyId, cancellationToken),
            ["transaction_rules"] = await CountAsync<TransactionRule>(x => x.CompanyId == companyId, cancellationToken),
            ["account_categories"] = await CountAsync<AccountCategory>(x => x.CompanyId == companyId, cancellationToken),
            ["debtors"] = await CountAsync<Debtor>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["creditors"] = await CountAsync<Creditor>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["fixed_assets"] = await CountAsync<FixedAsset>(x => x.CompanyId == companyId, cancellationToken),
            ["depreciation_entries"] = await CountAsync<DepreciationEntry>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["capital_allowance_claims"] = await CountAsync<CapitalAllowanceClaim>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["inventories"] = await CountAsync<Inventory>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["loans"] = await CountAsync<Loan>(x => x.CompanyId == companyId, cancellationToken),
            ["loan_balance_snapshots"] = await CountAsync<LoanBalanceSnapshot>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["director_loans"] = await CountAsync<DirectorLoan>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["director_loan_movements"] = await CountAsync<DirectorLoanMovement>(x => x.DirectorLoan.Period.CompanyId == companyId, cancellationToken),
            ["payroll_summaries"] = await CountAsync<PayrollSummary>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["corporation_tax_scope_reviews"] = await CountAsync<CorporationTaxScopeReview>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["corporation_tax_loss_records"] = await CountAsync<CorporationTaxLossRecord>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["corporation_tax_filing_support_reviews"] = await CountAsync<CorporationTaxFilingSupportReview>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["corporation_tax_payment_records"] = await CountAsync<CorporationTaxPaymentRecord>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["tax_balances"] = await CountAsync<TaxBalance>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["dividends"] = await CountAsync<Dividend>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["opening_balances"] = await CountAsync<OpeningBalance>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["year_end_review_confirmations"] = await CountAsync<YearEndReviewConfirmation>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["adjustments"] = await CountAsync<Adjustment>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["reports"] = await CountAsync<Report>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["notes_disclosures"] = await CountAsync<NotesDisclosure>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["share_capitals"] = await CountAsync<ShareCapital>(x => x.CompanyId == companyId, cancellationToken),
            ["filing_deadlines"] = await CountAsync<FilingDeadline>(x => x.CompanyId == companyId, cancellationToken),
            ["filing_histories"] = await CountAsync<FilingHistory>(x => x.CompanyId == companyId, cancellationToken),
            ["deadline_reminder_outbox"] = await CountAsync<DeadlineReminderOutbox>(x => x.CompanyId == companyId, cancellationToken),
            ["post_balance_sheet_events"] = await CountAsync<PostBalanceSheetEvent>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["related_party_transactions"] = await CountAsync<RelatedPartyTransaction>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["contingent_liabilities"] = await CountAsync<ContingentLiability>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["charity_infos"] = await CountAsync<CharityInfo>(x => x.CompanyId == companyId, cancellationToken),
            ["fund_balances"] = await CountAsync<FundBalance>(x => x.Period.CompanyId == companyId, cancellationToken),
            ["audit_logs"] = await CountAsync<AuditLog>(x => x.CompanyId == companyId, cancellationToken),
            ["audit_integrity_checkpoints"] = await CountAsync<AuditIntegrityCheckpoint>(x => x.CompanyId == companyId, cancellationToken),
            ["annual_return_date_records"] = await CountAsync<AnnualReturnDateRecord>(x => x.CompanyId == companyId, cancellationToken),
            ["company_onboarding_requests"] = await CountAsync<CompanyOnboardingRequest>(x => x.CompanyId == companyId, cancellationToken),
            ["company_quarantine_events"] = await CountAsync<CompanyQuarantineEvent>(x => x.CompanyId == companyId, cancellationToken)
        };

        var missing = RequiredTableNames.Where(name => !counts.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Company inventory coverage is incomplete: {string.Join(", ", missing)}.");

        var canonicalJson = JsonSerializer.Serialize(counts);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
        return new CompanyDependentInventory(
            counts,
            counts.Where(item => item.Key != "companies").Sum(item => item.Value),
            canonicalJson,
            sha256);
    }

    private Task<long> CountAsync<TEntity>(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
        where TEntity : class =>
        db.Set<TEntity>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .LongCountAsync(predicate, cancellationToken);
}

public static class CompanyQuarantineEvidenceIntegrity
{
    public static string ComputeHash(CompanyQuarantineEvent evidence)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            PreviousEvidenceSha256 = evidence.PreviousEvidenceSha256,
            evidence.CompanyId,
            evidence.TenantId,
            evidence.CompanyLegalName,
            evidence.EventType,
            evidence.ActorUserId,
            evidence.ActorDisplayName,
            evidence.ActorRole,
            evidence.Reason,
            evidence.TypedConfirmation,
            evidence.InventorySha256,
            evidence.TotalDependentRows,
            evidence.RequestId,
            OccurredAtUtc = evidence.OccurredAtUtc.ToUniversalTime().ToString("O")
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool IsValid(CompanyQuarantineEvent evidence)
    {
        if (evidence.InventorySha256 is not { Length: 64 }
            || evidence.EvidenceSha256 is not { Length: 64 }
            || !evidence.InventorySha256.All(Uri.IsHexDigit)
            || !evidence.EvidenceSha256.All(Uri.IsHexDigit))
        {
            return false;
        }
        string inventoryHash;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, long>>(evidence.InventoryJson);
            if (parsed is null) return false;
            var canonicalInventory = JsonSerializer.Serialize(
                new SortedDictionary<string, long>(parsed, StringComparer.Ordinal));
            inventoryHash = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalInventory)));
        }
        catch (JsonException)
        {
            return false;
        }
        return string.Equals(inventoryHash, evidence.InventorySha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ComputeHash(evidence), evidence.EvidenceSha256, StringComparison.OrdinalIgnoreCase);
    }
}
