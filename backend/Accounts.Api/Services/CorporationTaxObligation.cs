using System.Linq.Expressions;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

/// <summary>
/// The single rule for a company holding a charitable tax exemption. It gets no automatic
/// corporation-tax provision and no Revenue (CT1) obligations, except for a period whose entered CT
/// liability an Owner or Reviewer has confirmed as due. A confirmation counts only while it was given
/// for the current liability. Every reader (provision, readiness, deadlines, reminders, dashboard,
/// filing workflow and finalisation) goes through these members so they cannot drift apart.
/// </summary>
public static class CorporationTaxObligation
{
    /// <summary>A CT balance whose current liability is confirmed as due.</summary>
    public static readonly Expression<Func<TaxBalance, bool>> ConfirmedDue = balance =>
        balance.TaxType == TaxType.CorporationTax
        && balance.ConfirmedDueAt != null
        && balance.Liability > 0
        && balance.ConfirmedDueLiability == balance.Liability
        && balance.ConfirmedTaxableProfitOutsideExemption != null
        && balance.ConfirmedTaxableProfitOutsideExemption >= 0;

    /// <summary>Whether Revenue (CT1) obligations apply to the period.</summary>
    public static readonly Expression<Func<AccountingPeriod, bool>> RevenueAppliesToPeriod = period =>
        !period.Company.HoldsCharitableTaxExemption
        || period.TaxBalances.AsQueryable().Any(ConfirmedDue);

    /// <summary>Whether a deadline is an applicable obligation (only Revenue deadlines can be suppressed here).</summary>
    public static readonly Expression<Func<FilingDeadline, bool>> DeadlineApplies = deadline =>
        deadline.DeadlineType != DeadlineType.Revenue
        || !deadline.Company.HoldsCharitableTaxExemption
        || deadline.Period.TaxBalances.AsQueryable().Any(ConfirmedDue);

    public static Task<bool> IsConfirmedDueAsync(AccountsDbContext db, int periodId, CancellationToken cancellationToken = default) =>
        db.TaxBalances.Where(balance => balance.PeriodId == periodId).AnyAsync(ConfirmedDue, cancellationToken);

    public static Task<bool> RevenueAppliesAsync(AccountsDbContext db, int periodId, CancellationToken cancellationToken = default) =>
        db.AccountingPeriods.Where(period => period.Id == periodId).AnyAsync(RevenueAppliesToPeriod, cancellationToken);
}
