using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class DeadlineService(AccountsDbContext db)
{
    /// <summary>
    /// Calculate CRO filing deadline: earlier of (ARD + 56 days) or (FYE + 9 months + 56 days).
    /// Also calculates charity and revenue deadlines if applicable.
    /// </summary>
    public async Task<List<FilingDeadline>> CalculateDeadlinesAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingDeadlines)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new InvalidOperationException($"Period {periodId} not found");

        var company = period.Company;
        var fye = period.PeriodEnd;
        var deadlines = new List<FilingDeadline>();

        // CRO deadline: earlier of (ARD + 56 days) or (FYE + 9 months + 56 days)
        var ardDate = new DateOnly(fye.Year, company.ArdMonth, 1).AddMonths(1).AddDays(-1); // Last day of ARD month
        if (ardDate <= fye) ardDate = ardDate.AddYears(1); // Next occurrence after FYE
        var croOption1 = ardDate.AddDays(56);
        var croOption2 = fye.AddMonths(9).AddDays(56);
        var croDueDate = croOption1 < croOption2 ? croOption1 : croOption2;

        deadlines.Add(await UpsertDeadline(companyId, periodId, DeadlineType.CRO, croDueDate));

        // Revenue CT1 deadline: FYE + 9 months + 23 days (day 23 of 9th month after FYE)
        var revMonth = fye.AddMonths(9);
        var revDueDate = new DateOnly(revMonth.Year, revMonth.Month, 23);
        deadlines.Add(await UpsertDeadline(companyId, periodId, DeadlineType.Revenue, revDueDate));

        // Charity deadline: FYE + 10 months (only if charitable organisation)
        if (company.IsCharitableOrganisation)
        {
            var charityDueDate = fye.AddMonths(10);
            deadlines.Add(await UpsertDeadline(companyId, periodId, DeadlineType.Charity, charityDueDate));
        }

        await db.SaveChangesAsync();
        return deadlines;
    }

    /// <summary>
    /// Get the next upcoming deadline for a company across all periods.
    /// </summary>
    public async Task<FilingDeadline?> GetUpcomingDeadlineAsync(int companyId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.FilingDeadlines
            .Where(d => d.CompanyId == companyId && d.FiledDate == null)
            .OrderBy(d => d.DueDate)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get all deadlines for a company.
    /// </summary>
    public async Task<List<FilingDeadline>> GetDeadlinesAsync(int companyId)
    {
        return await db.FilingDeadlines
            .Where(d => d.CompanyId == companyId)
            .OrderByDescending(d => d.DueDate)
            .ToListAsync();
    }

    /// <summary>
    /// Mark a period as filed and record in filing history.
    /// </summary>
    public async Task<FilingDeadline> MarkFiledAsync(int companyId, int periodId, DeadlineType type, DateOnly filedDate)
    {
        var deadline = await db.FilingDeadlines
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.PeriodId == periodId && d.DeadlineType == type)
            ?? throw new InvalidOperationException("Deadline not found. Calculate deadlines first.");

        deadline.FiledDate = filedDate;
        deadline.IsLate = filedDate > deadline.DueDate;
        deadline.PenaltyAmount = deadline.IsLate ? CalculatePenalty(deadline.DueDate, filedDate) : 0;

        // Record in filing history
        var history = new FilingHistory
        {
            CompanyId = companyId,
            PeriodId = periodId,
            DeadlineType = type,
            DueDate = deadline.DueDate,
            FiledDate = filedDate,
            DaysLate = deadline.IsLate ? filedDate.DayNumber - deadline.DueDate.DayNumber : 0,
            PenaltyAmount = deadline.PenaltyAmount
        };
        db.FilingHistories.Add(history);

        await db.SaveChangesAsync();
        return deadline;
    }

    /// <summary>
    /// CRO late filing penalty: €100 initial + €3/day, max €1,200.
    /// </summary>
    public static decimal CalculatePenalty(DateOnly dueDate, DateOnly filedDate)
    {
        if (filedDate <= dueDate) return 0;
        var daysLate = filedDate.DayNumber - dueDate.DayNumber;
        var penalty = 100m + (daysLate * 3m);
        return Math.Min(penalty, 1200m);
    }

    /// <summary>
    /// Check audit exemption jeopardy: company loses audit exemption if late more than once in 5 years.
    /// Returns (isAtRisk, lateFilingCount, warning).
    /// </summary>
    public async Task<AuditExemptionJeopardy> CheckAuditExemptionJeopardyAsync(int companyId)
    {
        var fiveYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-5));
        var lateFilings = await db.FilingHistories
            .Where(h => h.CompanyId == companyId && h.DaysLate > 0 && h.DueDate >= fiveYearsAgo && h.DeadlineType == DeadlineType.CRO)
            .CountAsync();

        var isAtRisk = lateFilings >= 1;
        var hasLostExemption = lateFilings >= 2;

        var warning = hasLostExemption
            ? "AUDIT EXEMPTION LOST: Company has filed late more than once in the past 5 years. Statutory audit is mandatory for the next 2 years under s.22 Companies (Corporate Governance) Act 2024."
            : isAtRisk
                ? "WARNING: Company has one late CRO filing in the past 5 years. A second late filing will revoke audit exemption for 2 years."
                : null;

        return new AuditExemptionJeopardy(lateFilings, isAtRisk, hasLostExemption, warning);
    }

    private async Task<FilingDeadline> UpsertDeadline(int companyId, int periodId, DeadlineType type, DateOnly dueDate)
    {
        var existing = await db.FilingDeadlines
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.PeriodId == periodId && d.DeadlineType == type);

        if (existing != null)
        {
            existing.DueDate = dueDate;
            return existing;
        }

        var deadline = new FilingDeadline
        {
            CompanyId = companyId,
            PeriodId = periodId,
            DeadlineType = type,
            DueDate = dueDate
        };
        db.FilingDeadlines.Add(deadline);
        return deadline;
    }
}

public record AuditExemptionJeopardy(int LateFilingCount, bool IsAtRisk, bool HasLostExemption, string? Warning);
