using Accounts.Api.Entities;
using Microsoft.AspNetCore.Http;

namespace Accounts.Api.Services;

public sealed record DomainAuditCoverageEntry(
    string Operation,
    string Area,
    string EntityType,
    string EventCode,
    bool CompanyScoped,
    bool PeriodScoped,
    bool RequiresOldValue,
    bool RequiresNewValue);

/// <summary>
/// Canonical audit contract for material domain writes. This is deliberately narrower than the
/// transport-level ApiWrite events: every row names the business event and the evidence shape that
/// must survive in the tenant/company hash chain.
/// </summary>
public static class DomainAuditCoverage
{
    public static IReadOnlyList<DomainAuditCoverageEntry> Entries { get; } =
    [
        new("company.create", "company", "Company", AuditEventCodes.CompanyCreated, true, false, false, true),
        new("company.update", "company", "Company", AuditEventCodes.CompanyUpdated, true, false, true, true),
        new("company.quarantine", "deletion", "Company", AuditEventCodes.CompanyQuarantined, true, false, true, true),
        new("company.recover", "deletion", "Company", AuditEventCodes.CompanyRecovered, true, false, true, true),
        new("officer.create", "officer", "CompanyOfficer", AuditEventCodes.CompanyOfficerCreated, true, false, false, true),
        new("officer.update", "officer", "CompanyOfficer", AuditEventCodes.CompanyOfficerUpdated, true, false, true, true),
        new("officer.delete", "officer", "CompanyOfficer", AuditEventCodes.CompanyOfficerDeleted, true, false, true, false),
        new("period.create", "period", "AccountingPeriod", AuditEventCodes.AccountingPeriodCreated, true, true, false, true),
        new("period.status", "period", "AccountingPeriod", AuditEventCodes.AccountingPeriodStatusChanged, true, true, true, true),
        new("bank.create", "bank", "BankAccount", AuditEventCodes.BankAccountCreated, true, false, false, true),
        new("bank.update", "bank", "BankAccount", AuditEventCodes.BankAccountUpdated, true, false, true, true),
        new("bank.delete", "bank", "BankAccount", AuditEventCodes.BankAccountDeleted, true, false, true, false),
        new("category.create", "category", "AccountCategory", AuditEventCodes.AccountCategoryCreated, true, false, false, true),
        new("category.seed", "category", "AccountCategory", AuditEventCodes.AccountCategoriesSeeded, true, false, false, true),
        new("rule.create", "rule", "TransactionRule", AuditEventCodes.TransactionRuleCreated, true, false, false, true),
        new("rule.delete", "rule", "TransactionRule", AuditEventCodes.TransactionRuleDeleted, true, false, true, false),
        new("adjustment.approve", "professional-approval", "Adjustment", AuditEventCodes.AdjustmentApproved, true, true, true, true),
        new("filing.artifact.generate", "artifact", "FilingArtifact", AuditEventCodes.FilingArtifactGenerated, true, true, false, true),
        new("filing.cro-document.generate", "artifact", "CroFilingPackage", AuditEventCodes.CroDocumentGenerated, true, true, true, true),
        new("filing.cro-status", "workflow", "CroFilingPackage", AuditEventCodes.CroFilingStatusChanged, true, true, true, true),
        new("filing.revenue-status", "workflow", "RevenueFilingPackage", AuditEventCodes.RevenueFilingStatusChanged, true, true, true, true),
        new("filing.charity-status", "workflow", "CharityFilingPackage", AuditEventCodes.CharityFilingStatusChanged, true, true, true, true)
    ];

    public static object CompanySnapshot(Company company) => new
    {
        company.Id,
        company.LegalName,
        company.TradingName,
        company.CroNumber,
        company.CompanyType,
        company.IncorporationDate,
        company.FinancialYearStartMonth,
        company.AnnualReturnDate,
        company.IsTrading,
        company.IsDormant,
        company.IsCharitableOrganisation,
        company.IsQuarantined
    };

    public static object OfficerSnapshot(CompanyOfficer officer) => new
    {
        officer.Id,
        officer.CompanyId,
        officer.Name,
        officer.Role,
        officer.AppointedDate,
        officer.ResignedDate
    };

    public static object PeriodSnapshot(AccountingPeriod period) => new
    {
        period.Id,
        period.CompanyId,
        period.PeriodStart,
        period.PeriodEnd,
        period.Status,
        period.IsFirstYear,
        period.ApprovalDate,
        period.LockedAt,
        period.LockedBy,
        period.ReopenedAt,
        period.ReopenedBy,
        period.ReopenReason
    };

    public static object BankSnapshot(BankAccount bank) => new
    {
        bank.Id,
        bank.CompanyId,
        bank.Name,
        IbanRecorded = !string.IsNullOrWhiteSpace(bank.Iban),
        bank.Currency,
        bank.OpeningBalance,
        bank.OpeningBalanceDate
    };

    public static object CategorySnapshot(AccountCategory category) => new
    {
        category.Id,
        category.CompanyId,
        category.Code,
        category.Name,
        category.Type,
        category.TaxTreatment,
        category.IsNonTradingIncome,
        category.IsSystem,
        category.ParentId
    };

    public static object RuleSnapshot(TransactionRule rule) => new
    {
        rule.Id,
        rule.CompanyId,
        rule.Pattern,
        rule.CategoryId,
        rule.Priority
    };

    public static Task LogAsync(
        AuditService audit,
        HttpContext context,
        int companyId,
        int? periodId,
        string entityType,
        int entityId,
        string eventCode,
        object? oldValue,
        object? newValue,
        CancellationToken cancellationToken = default)
    {
        var actor = AuthContext.RequireUser(context);
        return audit.LogAsync(
            companyId,
            periodId,
            entityType,
            entityId,
            eventCode,
            oldValue,
            newValue,
            AuthenticatedIdentity.AuditUserId(actor),
            actor.TenantId,
            RequestId(context),
            AuthenticatedIdentity.ReviewerDisplayName(actor),
            cancellationToken: cancellationToken);
    }

    public static string RequestId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId)) return correlationId;
        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(requestId) ? context.TraceIdentifier : requestId;
    }
}
