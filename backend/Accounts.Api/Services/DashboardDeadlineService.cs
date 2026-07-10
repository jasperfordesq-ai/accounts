using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public static class DashboardDeadlineStates
{
    public const string NotConfigured = "not-configured";
    public const string NotApplicable = "not-applicable";
    public const string Unavailable = "unavailable";
    public const string Overdue = "overdue";
    public const string DueSoon = "due-soon";
    public const string Scheduled = "scheduled";
    public const string Filed = "filed";

    public static readonly IReadOnlyList<string> All =
    [
        NotConfigured,
        NotApplicable,
        Unavailable,
        Overdue,
        DueSoon,
        Scheduled,
        Filed
    ];
}

public sealed record DashboardDeadlineItem(
    int CompanyId,
    string CompanyName,
    string State,
    FilingDeadline? Deadline,
    string Message);

public sealed record DashboardDeadlineBatch(
    int TotalCompanies,
    int UnavailableCount,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<DashboardDeadlineItem> Items);

/// <summary>
/// Supplies the entire accessible practice deadline state through one HTTP request and two bounded
/// database reads. The response never treats missing, inconsistent, or unavailable evidence as an
/// inferred "not scheduled" state.
/// </summary>
public sealed class DashboardDeadlineService(AccountsDbContext db, DeadlineService deadlineService)
{
    private const int DueSoonDays = 30;

    public async Task<DashboardDeadlineBatch> GetAsync(
        AuthenticatedUser user,
        CancellationToken cancellationToken = default)
    {
        var companyQuery = UserCompanyAccessPolicy.ApplyToQuery(user, db.Companies.AsNoTracking());
        var companies = await companyQuery
            .OrderBy(company => company.LegalName)
            .ThenBy(company => company.Id)
            .Select(company => new CompanyDeadlineScope(
                company.Id,
                company.LegalName,
                company.IncorporationDate,
                company.Periods.Count,
                company.IsCharitableOrganisation))
            .ToListAsync(cancellationToken);

        var companyIds = companies.Select(company => company.Id).ToArray();
        List<FilingDeadline> deadlines = companyIds.Length == 0
            ? []
            : await db.FilingDeadlines
                .AsNoTracking()
                .Where(deadline => companyIds.Contains(deadline.CompanyId))
                .OrderBy(deadline => deadline.CompanyId)
                .ThenBy(deadline => deadline.DueDate)
                .ThenBy(deadline => deadline.Id)
                .ToListAsync(cancellationToken);
        var deadlinesByCompany = deadlines
            .GroupBy(deadline => deadline.CompanyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FilingDeadline>)group.ToList());
        var today = deadlineService.CurrentIrelandDate();

        var items = companies
            .Select(company => BuildItem(
                company,
                deadlinesByCompany.GetValueOrDefault(company.Id) ?? [],
                today))
            .ToList();
        var counts = DashboardDeadlineStates.All.ToDictionary(
            state => state,
            state => items.Count(item => item.State == state),
            StringComparer.Ordinal);

        return new DashboardDeadlineBatch(
            items.Count,
            counts[DashboardDeadlineStates.Unavailable],
            counts,
            items);
    }

    internal static DashboardDeadlineItem BuildItem(
        CompanyDeadlineScope company,
        IReadOnlyList<FilingDeadline> deadlines,
        DateOnly today)
    {
        if (company.PeriodCount == 0)
        {
            return new DashboardDeadlineItem(
                company.Id,
                company.LegalName,
                DashboardDeadlineStates.NotApplicable,
                null,
                "No accounting period exists yet, so no filing deadline is currently applicable.");
        }

        if (deadlines.Count == 0)
        {
            return new DashboardDeadlineItem(
                company.Id,
                company.LegalName,
                DashboardDeadlineStates.NotConfigured,
                null,
                "Filing deadlines have not been calculated for this company.");
        }

        if (deadlines.Any(deadline =>
                deadline.CompanyId != company.Id
                || deadline.DueDate == default
                || deadline.DueDate < company.IncorporationDate
                || !Enum.IsDefined(deadline.DeadlineType)))
        {
            return new DashboardDeadlineItem(
                company.Id,
                company.LegalName,
                DashboardDeadlineStates.Unavailable,
                null,
                "Deadline evidence is inconsistent and cannot be relied on. Review the company deadline configuration.");
        }

        var expectedDeadlineCount = company.PeriodCount * (company.IsCharitableOrganisation ? 3 : 2);
        var fullyConfigured = deadlines.Count == expectedDeadlineCount
            && deadlines.Count(deadline => deadline.DeadlineType == DeadlineType.CRO) == company.PeriodCount
            && deadlines.Count(deadline => deadline.DeadlineType == DeadlineType.Revenue) == company.PeriodCount
            && deadlines.Count(deadline => deadline.DeadlineType == DeadlineType.Charity)
                == (company.IsCharitableOrganisation ? company.PeriodCount : 0);
        if (!fullyConfigured)
        {
            return new DashboardDeadlineItem(
                company.Id,
                company.LegalName,
                DashboardDeadlineStates.NotConfigured,
                null,
                "One or more applicable filing deadlines have not been calculated for the company's accounting periods.");
        }

        var next = deadlines
            .Where(deadline => deadline.FiledDate is null)
            .OrderBy(deadline => deadline.DueDate)
            .ThenBy(deadline => deadline.Id)
            .FirstOrDefault();
        if (next is null)
        {
            var latestFiled = deadlines
                .OrderByDescending(deadline => deadline.FiledDate)
                .ThenByDescending(deadline => deadline.DueDate)
                .ThenByDescending(deadline => deadline.Id)
                .First();
            return new DashboardDeadlineItem(
                company.Id,
                company.LegalName,
                DashboardDeadlineStates.Filed,
                latestFiled,
                "All configured filing deadlines are recorded as filed.");
        }

        var state = next.DueDate < today
            ? DashboardDeadlineStates.Overdue
            : next.DueDate <= today.AddDays(DueSoonDays)
                ? DashboardDeadlineStates.DueSoon
                : DashboardDeadlineStates.Scheduled;
        var message = state switch
        {
            DashboardDeadlineStates.Overdue => "The next unfiled deadline is overdue.",
            DashboardDeadlineStates.DueSoon => $"The next unfiled deadline falls within {DueSoonDays} days.",
            _ => "The next filing deadline is scheduled."
        };
        return new DashboardDeadlineItem(company.Id, company.LegalName, state, next, message);
    }

    internal sealed record CompanyDeadlineScope(
        int Id,
        string LegalName,
        DateOnly IncorporationDate,
        int PeriodCount,
        bool IsCharitableOrganisation);
}
