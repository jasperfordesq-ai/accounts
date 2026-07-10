using Accounts.Api.Data;
using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public static class CompanyDashboardRows
{
    public static IQueryable<CompanyDashboardRow> ForContext(HttpContext context, AccountsDbContext db) =>
        CompanyListQuery
            .ForContext(context, db.Companies)
            .OrderBy(c => c.LegalName)
            .ThenBy(c => c.Id)
            .Select(c => new CompanyDashboardRow(
                c.Id,
                c.LegalName,
                c.TradingName,
                c.CroNumber,
                c.TaxReference,
                c.CompanyType,
                c.IncorporationDate,
                c.FinancialYearStartMonth,
                c.AnnualReturnDate,
                c.IsGroupMember,
                c.IsHolding,
                c.IsInvestment,
                c.IsSubsidiary,
                c.IsDormant,
                c.IsTrading,
                c.IsVatRegistered,
                c.IsEmployer,
                c.HasStock,
                c.OwnsAssets,
                c.HasBorrowings,
                c.HasDirectorLoans,
                c.IsListedSecurities,
                c.IsCreditInstitution,
                c.IsInsuranceUndertaking,
                c.IsPensionFund,
                c.IsCharitableOrganisation,
                c.CreatedAt,
                c.Periods.Count,
                c.UserAccesses
                    .Where(a =>
                        a.User.IsActive
                        && a.User.TenantId == c.TenantId
                        && a.User.Role == "Reviewer")
                    .OrderBy(a => a.User.DisplayName)
                    .ThenBy(a => a.User.Email)
                    .Select(a => a.User.DisplayName)
                    .FirstOrDefault(),
                c.UserAccesses
                    .Where(a =>
                        a.User.IsActive
                        && a.User.TenantId == c.TenantId
                        && a.User.Role == "Reviewer")
                    .OrderBy(a => a.User.DisplayName)
                    .ThenBy(a => a.User.Email)
                    .Select(a => a.User.Email)
                    .FirstOrDefault(),
                c.Periods
                    .OrderByDescending(p => p.PeriodEnd)
                    .ThenByDescending(p => p.Id)
                    .Select(p => new AccountingPeriodDashboardRow(
                        p.Id,
                        p.CompanyId,
                        p.PeriodStart,
                        p.PeriodEnd,
                        p.Status,
                        p.IsFirstYear,
                        p.MemberAuditNoticeReceived,
                        p.MemberAuditNoticeDate,
                        p.GoingConcernConfirmed,
                        p.GoingConcernNote))
                    .FirstOrDefault()));
}

public sealed record CompanyDashboardRow(
    int Id,
    string LegalName,
    string? TradingName,
    string? CroNumber,
    string? TaxReference,
    CompanyType CompanyType,
    DateOnly IncorporationDate,
    int FinancialYearStartMonth,
    DateOnly? AnnualReturnDate,
    bool IsGroupMember,
    bool IsHolding,
    bool IsInvestment,
    bool IsSubsidiary,
    bool IsDormant,
    bool IsTrading,
    bool IsVatRegistered,
    bool IsEmployer,
    bool HasStock,
    bool OwnsAssets,
    bool HasBorrowings,
    bool HasDirectorLoans,
    bool IsListedSecurities,
    bool IsCreditInstitution,
    bool IsInsuranceUndertaking,
    bool IsPensionFund,
    bool IsCharitableOrganisation,
    DateTime CreatedAt,
    int PeriodCount,
    string? AssignedReviewerName,
    string? AssignedReviewerEmail,
    AccountingPeriodDashboardRow? LatestPeriod);

public sealed record AccountingPeriodDashboardRow(
    int Id,
    int CompanyId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    PeriodStatus Status,
    bool IsFirstYear,
    bool MemberAuditNoticeReceived,
    DateOnly? MemberAuditNoticeDate,
    bool GoingConcernConfirmed,
    string? GoingConcernNote);
