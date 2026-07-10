using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

public class DeadlineService(
    AccountsDbContext db,
    AuditService? audit = null,
    TimeProvider? timeProvider = null,
    FilingReleaseGate? releaseGate = null)
{
    public const string CroRuleVersion = "CRO-ANNUAL-RETURN-2026-07-10";
    public const string CroGuidanceUrl = "https://cro.ie/Annual-Return/Filing-an-Annual-Return/";
    public const string CompaniesActSection347Url = "https://www.irishstatutebook.ie/eli/2014/act/38/section/347/enacted/";
    private static readonly TimeZoneInfo IrelandTimeZone = ResolveIrelandTimeZone();
    private FilingReleaseGate ReleaseGate => releaseGate ??= new FilingReleaseGate(db);

    /// <summary>
    /// Calculates and retains the separate CRO ARD, B1 made-up-to date, 56-day delivery date and
    /// section 347(4) financial-statement age limit. Also calculates charity and Revenue deadlines.
    /// </summary>
    public async Task<List<FilingDeadline>> CalculateDeadlinesAsync(int companyId, int periodId, string? userId = null)
    {
        await using var concurrencyLease = await new AccountingConcurrencyCoordinator(db)
            .AcquirePeriodAsync(companyId, periodId);
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

        if (company.AnnualReturnDate is null)
        {
            throw new BusinessRuleException(
                "Confirm the company's exact Annual Return Date against CRO CORE and retain its evidence before calculating filing deadlines.");
        }

        var currentArdRecord = await db.AnnualReturnDateRecords
            .Where(record => record.CompanyId == companyId
                && record.AnnualReturnDate == company.AnnualReturnDate)
            .OrderByDescending(record => record.RecordedAtUtc)
            .FirstOrDefaultAsync();
        var ardDate = ResolveAnnualReturnDateOccurrence(company.AnnualReturnDate.Value, fye);
        var financialStatementsLatestMadeUpToDate = fye.AddMonths(9);
        var returnMadeUpToDate = ardDate <= financialStatementsLatestMadeUpToDate
            ? ardDate
            : financialStatementsLatestMadeUpToDate;
        var croDeliveryDueDate = MoveToNextWorkingDay(returnMadeUpToDate.AddDays(56));
        var croCalculation = DeadlineCalculation.Cro(
            companyId,
            periodId,
            croDeliveryDueDate,
            ardDate,
            currentArdRecord?.Id,
            returnMadeUpToDate,
            financialStatementsLatestMadeUpToDate);

        deadlines.Add(await UpsertDeadline(companyId, periodId, DeadlineType.CRO, croCalculation));

        // ROS CT1/balance deadline: the earlier of nine months after period end and day 23
        // of that month. A period ending early in a month therefore keeps its exact day.
        var revDueDate = CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(fye);
        deadlines.Add(await UpsertDeadline(
            companyId,
            periodId,
            DeadlineType.Revenue,
            DeadlineCalculation.Simple(companyId, periodId, DeadlineType.Revenue, revDueDate)));

        // Charity deadline: FYE + 10 months (only if charitable organisation)
        if (company.IsCharitableOrganisation)
        {
            var charityDueDate = fye.AddMonths(10);
            deadlines.Add(await UpsertDeadline(
                companyId,
                periodId,
                DeadlineType.Charity,
                DeadlineCalculation.Simple(companyId, periodId, DeadlineType.Charity, charityDueDate)));
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
        await concurrencyLease.CommitIfOwnedAsync();
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
    /// Records a reviewed deadline override without overwriting the statutory calculation. The
    /// override is automatically moved to NeedsReview and stops controlling DueDate if its source
    /// calculation later changes.
    /// </summary>
    public async Task<FilingDeadline> RecordManualOverrideAsync(
        int companyId,
        int periodId,
        DeadlineType type,
        DateOnly overrideDueDate,
        string? reason,
        string? evidenceReference,
        string? evidenceSha256,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var normalizedReason = reason?.Trim();
        var normalizedReference = evidenceReference?.Trim();
        var normalizedSha256 = evidenceSha256?.Trim().ToLowerInvariant();
        if (!Enum.IsDefined(type))
            throw new BusinessRuleException("Valid deadline type is required.");
        if (overrideDueDate == default)
            throw new BusinessRuleException("Override due date is required.");
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length is < 20 or > 1000)
            throw new BusinessRuleException("A specific deadline override reason of 20 to 1000 characters is required.");
        if (string.IsNullOrWhiteSpace(normalizedReference) || normalizedReference.Length > 300)
            throw new BusinessRuleException("A retained override evidence reference of 300 characters or fewer is required.");
        if (normalizedSha256 is null || normalizedSha256.Length != 64 || !normalizedSha256.All(Uri.IsHexDigit))
            throw new BusinessRuleException("Deadline override evidence SHA-256 must contain exactly 64 hexadecimal characters.");

        await using var concurrencyLease = await new AccountingConcurrencyCoordinator(db)
            .AcquirePeriodAsync(companyId, periodId, cancellationToken);
        var deadline = await db.FilingDeadlines.FirstOrDefaultAsync(candidate =>
                candidate.CompanyId == companyId
                && candidate.PeriodId == periodId
                && candidate.DeadlineType == type,
            cancellationToken)
            ?? throw new BusinessRuleException("Deadline not found. Calculate deadlines first.");
        if (deadline.FiledDate is not null)
            throw new BusinessRuleException("A filed deadline cannot be overridden.");
        if (string.IsNullOrWhiteSpace(deadline.CalculationFingerprintSha256))
            throw new BusinessRuleException("Recalculate this deadline before recording an override so its statutory basis is retained.");

        var oldValue = DeadlineAuditSnapshot(deadline);
        deadline.ManualOverrideStatus = "Active";
        deadline.ManualOverrideDueDate = overrideDueDate;
        deadline.ManualOverrideReason = normalizedReason;
        deadline.ManualOverrideEvidenceReference = normalizedReference;
        deadline.ManualOverrideEvidenceSha256 = normalizedSha256;
        deadline.ManualOverrideByUserId = AuthenticatedIdentity.AuditUserId(actor);
        deadline.ManualOverrideByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor);
        deadline.ManualOverrideAtUtc = UtcNowMicrosecond();
        deadline.ManualOverrideCalculationFingerprintSha256 = deadline.CalculationFingerprintSha256;
        deadline.DueDate = overrideDueDate;
        deadline.IsLate = false;
        deadline.PenaltyAmount = 0;

        await db.SaveChangesAsync(cancellationToken);
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                nameof(FilingDeadline),
                deadline.Id,
                AuditEventCodes.DeadlineOverrideRecorded,
                oldValue,
                DeadlineAuditSnapshot(deadline),
                AuthenticatedIdentity.AuditUserId(actor),
                actor.TenantId,
                actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(actor),
                cancellationToken: cancellationToken);
        }
        await concurrencyLease.CommitIfOwnedAsync(cancellationToken);
        return deadline;
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

        var releaseWorkflow = type switch
        {
            DeadlineType.CRO => FilingReleaseWorkflow.Cro,
            DeadlineType.Revenue => FilingReleaseWorkflow.Revenue,
            DeadlineType.Charity => FilingReleaseWorkflow.Charity,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        await ReleaseGate.AssertCanRecordFiledAsync(
            companyId,
            periodId,
            releaseWorkflow,
            filingReference,
            userId);

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
        var fixedHolidays = IrishFixedPublicHolidays(year);
        var brigidHoliday = new DateOnly(year, 2, 1).DayOfWeek == DayOfWeek.Friday
            ? new DateOnly(year, 2, 1)
            : FirstMonday(year, 2);

        return fixedHolidays.Contains(date)
            || year >= 2023 && date == brigidHoliday
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

    private async Task<FilingDeadline> UpsertDeadline(
        int companyId,
        int periodId,
        DeadlineType type,
        DeadlineCalculation calculation)
    {
        var existing = db.FilingDeadlines.Local.FirstOrDefault(deadline =>
            deadline.CompanyId == companyId
            && deadline.PeriodId == periodId
            && deadline.DeadlineType == type);
        existing ??= await db.FilingDeadlines.FirstOrDefaultAsync(deadline =>
            deadline.CompanyId == companyId
            && deadline.PeriodId == periodId
            && deadline.DeadlineType == type);
        if (existing is not null)
        {
            ApplyCalculation(existing, calculation);
            return existing;
        }

        if (IsPostgresProvider())
            return await InsertDeadlineAtomicallyAsync(companyId, periodId, type, calculation);

        var created = new FilingDeadline
        {
            CompanyId = companyId,
            PeriodId = periodId,
            DeadlineType = type,
            CreatedAt = DateTime.UtcNow
        };
        ApplyCalculation(created, calculation);
        db.FilingDeadlines.Add(created);
        return created;
    }

    private async Task<FilingDeadline> InsertDeadlineAtomicallyAsync(
        int companyId,
        int periodId,
        DeadlineType type,
        DeadlineCalculation calculation)
    {
        var deadlineType = type.ToString();
        var createdAt = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO filing_deadlines (
                "CompanyId", "PeriodId", "DeadlineType", "CalculatedDueDate", "DueDate",
                "AnnualReturnDate", "AnnualReturnDateRecordId", "ReturnMadeUpToDate",
                "FinancialStatementsLatestMadeUpToDate", "DeliveryDueDate",
                "MadeUpToDateBroughtForwardForAccountsAge", "CalculationRuleVersion",
                "CalculationSourceUrl", "CalculationFingerprintSha256", "CreatedAt", "IsLate", "PenaltyAmount")
            VALUES (
                {companyId}, {periodId}, {deadlineType}, {calculation.CalculatedDueDate}, {calculation.CalculatedDueDate},
                {calculation.AnnualReturnDate}, {calculation.AnnualReturnDateRecordId}, {calculation.ReturnMadeUpToDate},
                {calculation.FinancialStatementsLatestMadeUpToDate}, {calculation.DeliveryDueDate},
                {calculation.MadeUpToDateBroughtForwardForAccountsAge}, {calculation.RuleVersion},
                {calculation.SourceUrl}, {calculation.FingerprintSha256}, {createdAt}, FALSE, 0)
            ON CONFLICT ("CompanyId", "PeriodId", "DeadlineType")
            DO UPDATE SET
                "CalculatedDueDate" = EXCLUDED."CalculatedDueDate",
                "AnnualReturnDate" = EXCLUDED."AnnualReturnDate",
                "AnnualReturnDateRecordId" = EXCLUDED."AnnualReturnDateRecordId",
                "ReturnMadeUpToDate" = EXCLUDED."ReturnMadeUpToDate",
                "FinancialStatementsLatestMadeUpToDate" = EXCLUDED."FinancialStatementsLatestMadeUpToDate",
                "DeliveryDueDate" = EXCLUDED."DeliveryDueDate",
                "MadeUpToDateBroughtForwardForAccountsAge" = EXCLUDED."MadeUpToDateBroughtForwardForAccountsAge",
                "CalculationRuleVersion" = EXCLUDED."CalculationRuleVersion",
                "CalculationSourceUrl" = EXCLUDED."CalculationSourceUrl",
                "CalculationFingerprintSha256" = EXCLUDED."CalculationFingerprintSha256",
                "ManualOverrideStatus" = CASE
                    WHEN filing_deadlines."ManualOverrideStatus" = 'Active'
                         AND filing_deadlines."ManualOverrideCalculationFingerprintSha256" = EXCLUDED."CalculationFingerprintSha256"
                    THEN 'Active'
                    WHEN filing_deadlines."ManualOverrideStatus" IS NOT NULL THEN 'NeedsReview'
                    ELSE NULL
                END,
                "DueDate" = CASE
                    WHEN filing_deadlines."ManualOverrideStatus" = 'Active'
                         AND filing_deadlines."ManualOverrideCalculationFingerprintSha256" = EXCLUDED."CalculationFingerprintSha256"
                    THEN filing_deadlines."ManualOverrideDueDate"
                    ELSE EXCLUDED."CalculatedDueDate"
                END
            """);

        db.ChangeTracker.Clear();
        return await db.FilingDeadlines.SingleAsync(deadline =>
            deadline.CompanyId == companyId
            && deadline.PeriodId == periodId
            && deadline.DeadlineType == type);
    }

    private static void ApplyCalculation(FilingDeadline deadline, DeadlineCalculation calculation)
    {
        var overrideRemainsActive = string.Equals(deadline.ManualOverrideStatus, "Active", StringComparison.Ordinal)
            && string.Equals(
                deadline.ManualOverrideCalculationFingerprintSha256,
                calculation.FingerprintSha256,
                StringComparison.OrdinalIgnoreCase)
            && deadline.ManualOverrideDueDate is not null;
        if (!overrideRemainsActive && deadline.ManualOverrideStatus is not null)
            deadline.ManualOverrideStatus = "NeedsReview";

        deadline.CalculatedDueDate = calculation.CalculatedDueDate;
        deadline.DueDate = overrideRemainsActive
            ? deadline.ManualOverrideDueDate!.Value
            : calculation.CalculatedDueDate;
        deadline.AnnualReturnDate = calculation.AnnualReturnDate;
        deadline.AnnualReturnDateRecordId = calculation.AnnualReturnDateRecordId;
        deadline.ReturnMadeUpToDate = calculation.ReturnMadeUpToDate;
        deadline.FinancialStatementsLatestMadeUpToDate = calculation.FinancialStatementsLatestMadeUpToDate;
        deadline.DeliveryDueDate = calculation.DeliveryDueDate;
        deadline.MadeUpToDateBroughtForwardForAccountsAge = calculation.MadeUpToDateBroughtForwardForAccountsAge;
        deadline.CalculationRuleVersion = calculation.RuleVersion;
        deadline.CalculationSourceUrl = calculation.SourceUrl;
        deadline.CalculationFingerprintSha256 = calculation.FingerprintSha256;
        if (deadline.FiledDate is not null)
        {
            deadline.IsLate = deadline.FiledDate > deadline.DueDate;
            deadline.PenaltyAmount = deadline.IsLate ? CalculatePenalty(deadline.DueDate, deadline.FiledDate.Value) : 0;
        }
    }

    private bool IsPostgresProvider() =>
        string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    private static object DeadlineAuditSnapshot(FilingDeadline deadline) => new
    {
        deadline.DeadlineType,
        deadline.CalculatedDueDate,
        deadline.DueDate,
        deadline.AnnualReturnDate,
        deadline.AnnualReturnDateRecordId,
        deadline.ReturnMadeUpToDate,
        deadline.FinancialStatementsLatestMadeUpToDate,
        deadline.DeliveryDueDate,
        deadline.MadeUpToDateBroughtForwardForAccountsAge,
        deadline.CalculationRuleVersion,
        deadline.CalculationSourceUrl,
        deadline.CalculationFingerprintSha256,
        deadline.ManualOverrideStatus,
        deadline.ManualOverrideDueDate,
        deadline.ManualOverrideReason,
        deadline.ManualOverrideEvidenceReference,
        deadline.ManualOverrideEvidenceSha256,
        deadline.ManualOverrideByUserId,
        deadline.ManualOverrideByDisplayName,
        deadline.ManualOverrideAtUtc,
        deadline.ManualOverrideCalculationFingerprintSha256,
        deadline.FiledDate,
        deadline.FilingReference,
        deadline.IsLate,
        deadline.PenaltyAmount
    };

    public DateOnly CurrentIrelandDate()
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var irishNow = TimeZoneInfo.ConvertTime(now, IrelandTimeZone);
        return DateOnly.FromDateTime(irishNow.DateTime);
    }

    private DateTime UtcNowMicrosecond()
    {
        var utc = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    public static DateOnly ResolveAnnualReturnDateOccurrence(DateOnly exactAnnualReturnDate, DateOnly periodEnd)
    {
        var occurrence = exactAnnualReturnDate;
        while (occurrence < periodEnd)
            occurrence = occurrence.AddYears(1);
        return occurrence;
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

    private static HashSet<DateOnly> IrishFixedPublicHolidays(int year)
    {
        var actual = new[]
        {
            new DateOnly(year, 1, 1),
            new DateOnly(year, 3, 17),
            new DateOnly(year, 12, 25),
            new DateOnly(year, 12, 26)
        };
        var holidays = actual.ToHashSet();
        var occupiedWorkingDays = actual
            .Where(candidate => candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToHashSet();
        foreach (var holiday in actual.Where(candidate =>
                     candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            var observed = holiday.AddDays(1);
            while (observed.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                   || occupiedWorkingDays.Contains(observed))
            {
                observed = observed.AddDays(1);
            }
            occupiedWorkingDays.Add(observed);
            holidays.Add(observed);
        }
        return holidays;
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

internal sealed record DeadlineCalculation(
    DateOnly CalculatedDueDate,
    DateOnly? AnnualReturnDate,
    int? AnnualReturnDateRecordId,
    DateOnly? ReturnMadeUpToDate,
    DateOnly? FinancialStatementsLatestMadeUpToDate,
    DateOnly? DeliveryDueDate,
    bool? MadeUpToDateBroughtForwardForAccountsAge,
    string RuleVersion,
    string? SourceUrl,
    string FingerprintSha256)
{
    public static DeadlineCalculation Cro(
        int companyId,
        int periodId,
        DateOnly calculatedDueDate,
        DateOnly annualReturnDate,
        int? annualReturnDateRecordId,
        DateOnly returnMadeUpToDate,
        DateOnly financialStatementsLatestMadeUpToDate)
    {
        var broughtForward = returnMadeUpToDate < annualReturnDate;
        const string ruleVersion = DeadlineService.CroRuleVersion;
        var canonical = string.Join('|',
            companyId.ToString(CultureInfo.InvariantCulture),
            periodId.ToString(CultureInfo.InvariantCulture),
            DeadlineType.CRO.ToString(),
            calculatedDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            annualReturnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            annualReturnDateRecordId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            returnMadeUpToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            financialStatementsLatestMadeUpToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            broughtForward.ToString(CultureInfo.InvariantCulture),
            ruleVersion,
            DeadlineService.CroGuidanceUrl,
            DeadlineService.CompaniesActSection347Url);
        return new DeadlineCalculation(
            calculatedDueDate,
            annualReturnDate,
            annualReturnDateRecordId,
            returnMadeUpToDate,
            financialStatementsLatestMadeUpToDate,
            calculatedDueDate,
            broughtForward,
            ruleVersion,
            DeadlineService.CroGuidanceUrl,
            Hash(canonical));
    }

    public static DeadlineCalculation Simple(
        int companyId,
        int periodId,
        DeadlineType type,
        DateOnly calculatedDueDate)
    {
        var ruleVersion = type switch
        {
            DeadlineType.Revenue => "REVENUE-CT1-2026-07-10",
            DeadlineType.Charity => "CHARITIES-ANNUAL-RETURN-2026-07-10",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var canonical = string.Join('|',
            companyId.ToString(CultureInfo.InvariantCulture),
            periodId.ToString(CultureInfo.InvariantCulture),
            type.ToString(),
            calculatedDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ruleVersion);
        return new DeadlineCalculation(
            calculatedDueDate,
            null,
            null,
            null,
            null,
            null,
            null,
            ruleVersion,
            null,
            Hash(canonical));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}

public record AuditExemptionJeopardy(int LateFilingCount, bool IsAtRisk, bool HasLostExemption, string? Warning);
