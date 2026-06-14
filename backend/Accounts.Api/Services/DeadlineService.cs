using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class DeadlineService(AccountsDbContext db, AuditService? audit = null, TimeProvider? timeProvider = null)
{
    private static readonly TimeZoneInfo IrelandTimeZone = ResolveIrelandTimeZone();

    /// <summary>
    /// Calculate CRO filing deadline: earlier of (ARD + 56 days) or (FYE + 9 months + 56 days).
    /// Also calculates charity and revenue deadlines if applicable.
    /// </summary>
    public async Task<List<FilingDeadline>> CalculateDeadlinesAsync(int companyId, int periodId, string? userId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingDeadlines)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var fye = period.PeriodEnd;
        var deadlines = new List<FilingDeadline>();
        var oldValue = period.FilingDeadlines
            .OrderBy(d => d.DeadlineType)
            .Select(DeadlineAuditSnapshot)
            .ToList();

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
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "FilingDeadlineSet",
                periodId,
                AuditEventCodes.DeadlinesCalculated,
                oldValue,
                deadlines.OrderBy(d => d.DeadlineType).Select(DeadlineAuditSnapshot).ToList(),
                userId);
        }
        return deadlines;
    }

    /// <summary>
    /// Get the next upcoming deadline for a company across all periods.
    /// </summary>
    public async Task<FilingDeadline?> GetUpcomingDeadlineAsync(int companyId)
    {
        var today = CurrentIrelandDate();
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
    public async Task<FilingDeadline> MarkFiledAsync(
        int companyId,
        int periodId,
        DeadlineType type,
        DateOnly filedDate,
        string? userId = null,
        string? filingReference = null)
    {
        var periodBelongsToCompany = await db.AccountingPeriods
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (!periodBelongsToCompany)
            throw new ResourceNotFoundException($"Period {periodId} not found");

        var today = CurrentIrelandDate();
        if (filedDate > today)
            throw new BusinessRuleException("Filed date cannot be in the future.");

        var evidenceReference = type switch
        {
            DeadlineType.CRO => await AssertCroWorkflowAcceptedAsync(companyId, periodId),
            DeadlineType.Revenue => await AssertRevenueFilingEvidenceAsync(periodId, filingReference),
            DeadlineType.Charity => await AssertCharityFilingEvidenceAsync(companyId, periodId, filingReference),
            _ => NormalizeOptionalFilingReference(filingReference)
        };

        var deadline = await db.FilingDeadlines
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.PeriodId == periodId && d.DeadlineType == type)
            ?? throw new BusinessRuleException("Deadline not found. Calculate deadlines first.");
        var oldValue = DeadlineAuditSnapshot(deadline);

        deadline.FiledDate = filedDate;
        deadline.FilingReference = evidenceReference;
        deadline.IsLate = filedDate > deadline.DueDate;
        deadline.PenaltyAmount = deadline.IsLate ? CalculatePenalty(deadline.DueDate, filedDate) : 0;

        // Record one filing-history row per company period and deadline type.
        var history = await db.FilingHistories
            .FirstOrDefaultAsync(h =>
                h.CompanyId == companyId
                && h.PeriodId == periodId
                && h.DeadlineType == type);

        if (history is null)
        {
            history = new FilingHistory
            {
                CompanyId = companyId,
                PeriodId = periodId,
                DeadlineType = type
            };
            db.FilingHistories.Add(history);
        }

        history.DueDate = deadline.DueDate;
        history.FiledDate = filedDate;
        history.FilingReference = evidenceReference;
        history.DaysLate = deadline.IsLate ? filedDate.DayNumber - deadline.DueDate.DayNumber : 0;
        history.PenaltyAmount = deadline.PenaltyAmount;

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "FilingDeadline",
                deadline.Id,
                AuditEventCodes.DeadlineMarkedFiled,
                oldValue,
                DeadlineAuditSnapshot(deadline),
                userId);
        }
        return deadline;
    }

    /// <summary>
    /// CRO late filing penalty: €100 initial + €3/day, max €1,200.
    /// </summary>
    private async Task<string> AssertCroWorkflowAcceptedAsync(int companyId, int periodId)
    {
        var croPackage = await db.AccountingPeriods
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => p.CroFilingPackage == null
                ? null
                : new { p.CroFilingPackage.FilingStatus, p.CroFilingPackage.CroSubmissionReference })
            .SingleAsync();

        if (croPackage?.FilingStatus != FilingStatus.Accepted)
            throw new BusinessRuleException("Mark the CRO filing workflow as accepted before recording the CRO filed date.");

        var trimmedReference = NormalizeOptionalFilingReference(croPackage.CroSubmissionReference);
        if (string.IsNullOrWhiteSpace(trimmedReference))
            throw new BusinessRuleException("CORE submission reference is required before recording the CRO filed date.");

        return trimmedReference;
    }

    private async Task<string> AssertRevenueFilingEvidenceAsync(int periodId, string? filingReference)
    {
        var revenuePackage = await db.RevenueFilingPackages
            .FirstOrDefaultAsync(p => p.PeriodId == periodId);

        if (revenuePackage?.IxbrlGenerated != true
            || !InternalIxbrlChecksPassed(revenuePackage.IxbrlValidationErrors))
        {
            throw new BusinessRuleException("Run and pass internal iXBRL checks before recording the Revenue filed date.");
        }

        var trimmedReference = NormalizeOptionalFilingReference(filingReference);
        if (!string.IsNullOrWhiteSpace(trimmedReference))
            revenuePackage.Ct1Reference = trimmedReference;

        if (string.IsNullOrWhiteSpace(revenuePackage.Ct1Reference))
            throw new BusinessRuleException("Revenue filing reference is required before recording the Revenue filed date.");

        return revenuePackage.Ct1Reference;
    }

    private async Task<string> AssertCharityFilingEvidenceAsync(int companyId, int periodId, string? filingReference)
    {
        var evidence = await db.AccountingPeriods
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new
            {
                p.Company.IsCharitableOrganisation,
                Package = p.CharityFilingPackage == null
                    ? null
                    : new
                    {
                        p.CharityFilingPackage.FilingStatus,
                        p.CharityFilingPackage.AnnualReturnReference
                    }
            })
            .SingleAsync();

        var trimmedReference = NormalizeOptionalFilingReference(filingReference);
        if (!evidence.IsCharitableOrganisation
            || evidence.Package?.FilingStatus != FilingStatus.Accepted
            || string.IsNullOrWhiteSpace(evidence.Package.AnnualReturnReference))
        {
            throw new BusinessRuleException("Charity annual return package must be accepted with a Charities Regulator reference before recording the Charity filed date.");
        }

        if (!string.IsNullOrWhiteSpace(trimmedReference)
            && !string.Equals(trimmedReference, evidence.Package.AnnualReturnReference, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("Charity annual return reference must match the accepted package reference.");
        }

        return evidence.Package.AnnualReturnReference;
    }

    private static bool InternalIxbrlChecksPassed(string? validationErrors) =>
        validationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) == true;

    private static string? NormalizeOptionalFilingReference(string? filingReference)
    {
        var trimmed = filingReference?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;
        if (trimmed.Length > 200)
            throw new BusinessRuleException("Filing reference must be 200 characters or fewer.");
        return trimmed;
    }

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
        var fiveYearsAgo = CurrentIrelandDate().AddYears(-5);
        var latePeriodFilings = await db.FilingHistories
            .Where(h => h.CompanyId == companyId && h.DaysLate > 0 && h.DueDate >= fiveYearsAgo && h.DeadlineType == DeadlineType.CRO)
            .Where(h => h.PeriodId != null)
            .Select(h => h.PeriodId)
            .Distinct()
            .CountAsync();
        var lateLegacyFilings = await db.FilingHistories
            .Where(h => h.CompanyId == companyId && h.DaysLate > 0 && h.DueDate >= fiveYearsAgo && h.DeadlineType == DeadlineType.CRO)
            .Where(h => h.PeriodId == null)
            .Select(h => h.DueDate)
            .Distinct()
            .CountAsync();
        var lateFilings = latePeriodFilings + lateLegacyFilings;

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

    private static object DeadlineAuditSnapshot(FilingDeadline deadline) => new
    {
        deadline.DeadlineType,
        deadline.DueDate,
        deadline.FiledDate,
        deadline.FilingReference,
        deadline.IsLate,
        deadline.PenaltyAmount
    };

    private DateOnly CurrentIrelandDate()
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var irishNow = TimeZoneInfo.ConvertTime(now, IrelandTimeZone);
        return DateOnly.FromDateTime(irishNow.DateTime);
    }

    private static TimeZoneInfo ResolveIrelandTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        }
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
