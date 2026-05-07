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
        var croOption1 = MoveToNextWorkingDay(ardDate.AddDays(56));
        var croOption2 = MoveToNextWorkingDay(fye.AddMonths(9).AddDays(56));
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

    public static DateOnly MoveToNextWorkingDay(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsIrishPublicHoliday(date))
            date = date.AddDays(1);

        return date;
    }

    public static bool IsIrishPublicHoliday(DateOnly date)
    {
        var year = date.Year;
        var observedFixedHolidays = new HashSet<DateOnly>
        {
            ObservedFixedHoliday(year, 1, 1),
            ObservedFixedHoliday(year, 3, 17),
            ObservedFixedHoliday(year, 12, 25),
            ObservedFixedHoliday(year, 12, 26)
        };

        return observedFixedHolidays.Contains(date)
            || date == FirstMonday(year, 2)
            || date == EasterSunday(year).AddDays(1)
            || date == FirstMonday(year, 5)
            || date == FirstMonday(year, 6)
            || date == FirstMonday(year, 8)
            || date == LastMonday(year, 10);
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
            ? "AUDIT EXEMPTION LOST: Company has filed late more than once in the past 5 years. Statutory audit is mandatory for the next 2 years."
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

    private static DateOnly ObservedFixedHoliday(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => date.AddDays(2),
            DayOfWeek.Sunday => date.AddDays(1),
            _ => date
        };
    }

    private static DateOnly FirstMonday(int year, int month)
    {
        var date = new DateOnly(year, month, 1);
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    private static DateOnly LastMonday(int year, int month)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(-1);
        return date;
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}

public record AuditExemptionJeopardy(int LateFilingCount, bool IsAtRisk, bool HasLostExemption, string? Warning);
