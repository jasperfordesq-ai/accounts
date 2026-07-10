using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed record SizeClassificationOverrideRequest(
    CompanySizeClass OverrideClass,
    string Reason,
    byte[] EvidenceArtifact,
    string EvidenceSha256);

public class SizeClassificationService(
    AccountsDbContext db,
    IOptions<SizeThresholdConfig> thresholds,
    AuditService? audit = null)
{
    private static readonly DateOnly HistoricalScheduleStart = new(2017, 1, 1);
    private readonly SizeThresholdConfig _config = thresholds.Value;

    public record ClassificationResult(
        CompanySizeClass CalculatedClass,
        string QualificationNotes,
        bool CanUseMicro,
        bool CanFileAbridged,
        bool AuditExempt,
        List<string> AvailableRegimes,
        bool IsIneligibleEntity = false,
        string? IneligibleReason = null,
        CompanySizeClass RawCurrentClass = CompanySizeClass.Large,
        CompanySizeClass? RawPriorClass = null,
        decimal AnnualisedTurnover = 0,
        decimal PeriodLengthInYears = 1,
        string? ThresholdScheduleCode = null,
        DateOnly? ThresholdScheduleEffectiveFrom = null);

    private sealed record ThresholdSchedule(
        string Code,
        DateOnly EffectiveFrom,
        ThresholdSet Micro,
        ThresholdSet Small,
        ThresholdSet Medium);

    private sealed record RawThresholdDecision(
        CompanySizeClass RawClass,
        bool Micro,
        bool Small,
        bool Medium,
        decimal AnnualisedTurnover,
        decimal PeriodLengthInYears);

    public async Task<ClassificationResult> ClassifyAsync(int companyId, int periodId, string? userId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var sc = period.SizeClassification
            ?? throw new BusinessRuleException("Size classification data not yet entered for this period.");
        ValidateInputs(sc.Turnover, sc.BalanceSheetTotal, sc.AvgEmployees, sc.ThresholdElectionEffectiveFrom);
        EnsureEntityClassificationSupported(period.Company);

        var oldValue = ClassificationAuditSnapshot(sc, null, null, null, null, null);
        var election = NormalizeElection(sc.ThresholdElectionEffectiveFrom);
        var schedule = SelectSchedule(period.PeriodStart, election);
        var currentRaw = EvaluateRaw(period, sc, schedule);
        var priorPeriod = await LoadPriorPeriodAsync(period);
        if (!period.IsFirstYear && priorPeriod?.SizeClassification is null)
        {
            throw new BusinessRuleException(
                "A subsequent-year classification requires the immediately preceding period's raw turnover, balance-sheet and employee figures.");
        }

        await EnsureDecisionChainCurrentAsync(db, period, election, includeTarget: false);

        RawThresholdDecision? priorRaw = null;
        if (priorPeriod?.SizeClassification is { } priorInputs)
        {
            ValidateInputs(priorInputs.Turnover, priorInputs.BalanceSheetTotal, priorInputs.AvgEmployees, election);
            priorRaw = EvaluateRaw(priorPeriod, priorInputs, SelectSchedule(priorPeriod.PeriodStart, election));
        }

        var company = period.Company;
        var ineligibleReasons = GetIneligibleReasons(company);
        var isIneligible = ineligibleReasons.Count > 0;
        var microExclusionReasons = GetMicroExclusionReasons(company);
        var microExcluded = isIneligible || microExclusionReasons.Count > 0;
        var notes = new List<string>();

        bool qualifiesSmall;
        bool qualifiesMicro;
        bool qualifiesMedium;
        if (isIneligible)
        {
            qualifiesMicro = false;
            qualifiesSmall = false;
            qualifiesMedium = false;
        }
        else if (period.IsFirstYear)
        {
            qualifiesSmall = currentRaw.Small;
            qualifiesMicro = !microExcluded && qualifiesSmall && currentRaw.Micro;
            qualifiesMedium = currentRaw.Medium;
            notes.Add("First financial year — statutory qualification is based on the current-year raw threshold tests only.");
        }
        else
        {
            var priorSc = priorPeriod!.SizeClassification!;
            var priorEffectiveClass = priorSc.OverrideClass ?? priorSc.CalculatedClass;
            qualifiesSmall = QualifiesInSubsequentYear(
                currentRaw.Small,
                priorRaw!.Small,
                priorEffectiveClass <= CompanySizeClass.Small);
            qualifiesMicro = !microExcluded
                && qualifiesSmall
                && QualifiesInSubsequentYear(
                    currentRaw.Micro,
                    priorRaw.Micro,
                    priorEffectiveClass == CompanySizeClass.Micro);
            qualifiesMedium = QualifiesInSubsequentYear(
                currentRaw.Medium,
                priorRaw.Medium,
                priorEffectiveClass <= CompanySizeClass.Medium);
            notes.Add(
                $"Subsequent-year statutory test — current raw {currentRaw.RawClass}; prior raw {priorRaw.RawClass}; prior effective {priorEffectiveClass}.");
        }

        var calculatedClass = isIneligible
            ? CompanySizeClass.Large
            : qualifiesMicro
                ? CompanySizeClass.Micro
                : qualifiesSmall
                    ? CompanySizeClass.Small
                    : qualifiesMedium
                        ? CompanySizeClass.Medium
                        : CompanySizeClass.Large;

        var ineligibleReason = isIneligible
            ? $"Ineligible entity: {string.Join(", ", ineligibleReasons)}."
            : null;
        if (isIneligible)
        {
            notes.Add($"INELIGIBLE ENTITY — {ineligibleReason}");
            notes.Add("Micro, small and medium regimes are unavailable; full reporting is required.");
        }
        else if (microExclusionReasons.Count > 0)
        {
            notes.Add($"Micro regime excluded: {string.Join(", ", microExclusionReasons)}.");
        }

        var canUseMicro = calculatedClass == CompanySizeClass.Micro && !microExcluded;
        var canFileAbridged = !isIneligible && calculatedClass <= CompanySizeClass.Small;
        var regimes = AvailableRegimes(calculatedClass, canUseMicro, canFileAbridged, isIneligible);
        var auditExempt = !isIneligible && calculatedClass <= CompanySizeClass.Small && !company.IsGroupMember;

        notes.Add(
            $"Threshold schedule {schedule.Code} (effective {schedule.EffectiveFrom:yyyy-MM-dd}); " +
            $"period length {currentRaw.PeriodLengthInYears:0.####} years; reported turnover €{sc.Turnover:N2}; " +
            $"annualised turnover €{currentRaw.AnnualisedTurnover:N2}.");
        notes.Add(
            $"Raw tests — Micro={currentRaw.Micro}; Small={currentRaw.Small}; Medium={currentRaw.Medium}. Effective classification: {calculatedClass}.");
        notes.Add(auditExempt
            ? "Audit exemption is potentially available under s.360, subject to all other statutory conditions."
            : "Audit exemption is not available from the size/entity test alone.");

        var fingerprint = ComputeInputFingerprint(period, sc, election, priorPeriod);
        var overrideBecameStale = sc.OverrideClass is not null
            && !string.Equals(sc.OverrideInputFingerprintSha256, fingerprint, StringComparison.OrdinalIgnoreCase);
        var decisionChanged = !string.Equals(sc.DecisionInputFingerprintSha256, fingerprint, StringComparison.OrdinalIgnoreCase)
            || sc.CalculatedClass != calculatedClass;

        sc.PriorYearClass = priorPeriod?.SizeClassification is { } priorDecision
            ? priorDecision.OverrideClass ?? priorDecision.CalculatedClass
            : null;
        sc.RawCurrentClass = currentRaw.RawClass;
        sc.RawPriorClass = priorRaw?.RawClass;
        sc.RawCurrentMicroQualified = currentRaw.Micro;
        sc.RawCurrentSmallQualified = currentRaw.Small;
        sc.RawCurrentMediumQualified = currentRaw.Medium;
        sc.RawPriorMicroQualified = priorRaw?.Micro;
        sc.RawPriorSmallQualified = priorRaw?.Small;
        sc.RawPriorMediumQualified = priorRaw?.Medium;
        sc.AnnualisedTurnover = currentRaw.AnnualisedTurnover;
        sc.PeriodLengthInYears = currentRaw.PeriodLengthInYears;
        sc.ThresholdElectionEffectiveFrom = election;
        sc.ThresholdScheduleEffectiveFrom = schedule.EffectiveFrom;
        sc.ThresholdScheduleCode = schedule.Code;
        sc.DecisionInputFingerprintSha256 = fingerprint;
        sc.CalculatedClass = calculatedClass;
        sc.ExclusionFlagsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            Ineligible = ineligibleReasons,
            Micro = microExclusionReasons,
            GroupAssessmentRequired = company.IsHolding
        });
        sc.QualificationNotes = string.Join("\n", notes);
        sc.CalculatedAt = DateTime.UtcNow;
        if (overrideBecameStale || sc.OverrideClass is { } overrideClass && overrideClass < calculatedClass)
            sc.OverrideRequiresRereview = true;

        if (period.FilingRegime is not null && (decisionChanged || sc.OverrideRequiresRereview))
            db.FilingRegimes.Remove(period.FilingRegime);

        await db.SaveChangesAsync();

        var result = new ClassificationResult(
            calculatedClass,
            sc.QualificationNotes,
            canUseMicro,
            canFileAbridged,
            auditExempt,
            regimes,
            isIneligible,
            ineligibleReason,
            currentRaw.RawClass,
            priorRaw?.RawClass,
            currentRaw.AnnualisedTurnover,
            currentRaw.PeriodLengthInYears,
            schedule.Code,
            schedule.EffectiveFrom);

        if (audit is not null)
        {
            await audit.LogAsync(
                company.Id,
                periodId,
                "SizeClassification",
                sc.Id,
                AuditEventCodes.SizeClassificationRun,
                oldValue,
                ClassificationAuditSnapshot(sc, result.CanUseMicro, result.CanFileAbridged, result.AuditExempt, result.AvailableRegimes, result.IsIneligibleEntity),
                userId);
        }

        return result;
    }

    public async Task<SizeClassification> ApplyOverrideAsync(
        int companyId,
        int periodId,
        SizeClassificationOverrideRequest request,
        string authorityRole,
        string approvedBy,
        string? auditUserId = null)
    {
        if (authorityRole is not "Owner" and not "Reviewer")
            throw new BusinessRuleException("Only an Owner or Reviewer may approve a statutory classification override.");
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new BusinessRuleException("A named override approver is required.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 20)
            throw new BusinessRuleException("A specific override reason of at least 20 characters is required.");
        EnsureRetainedEvidence(request.EvidenceArtifact, request.EvidenceSha256);

        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var sc = period.SizeClassification
            ?? throw new BusinessRuleException("Complete the statutory size classification before applying an override.");
        var election = NormalizeElection(sc.ThresholdElectionEffectiveFrom);
        await EnsureDecisionChainCurrentAsync(db, period, election, includeTarget: false);
        var priorPeriod = await LoadPriorPeriodAsync(period);
        var fingerprint = ComputeInputFingerprint(period, sc, election, priorPeriod);
        if (string.IsNullOrWhiteSpace(sc.DecisionInputFingerprintSha256)
            || !string.Equals(sc.DecisionInputFingerprintSha256, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("Re-run the statutory classification against the current inputs before applying an override.");
        }
        if (request.OverrideClass < sc.CalculatedClass)
            throw new BusinessRuleException("An override cannot claim a smaller statutory class than the calculated decision.");
        if (period.FilingRegime is not null
            && !FilingRegimeService.IsElectionCompatible(request.OverrideClass, period.FilingRegime.ElectedRegime))
        {
            throw new BusinessRuleException("The requested override is incompatible with the retained downstream filing-regime election.");
        }

        var oldValue = ClassificationAuditSnapshot(sc, null, null, null, null, null);
        sc.OverrideClass = request.OverrideClass;
        sc.OverrideReason = request.Reason.Trim();
        sc.OverrideAuthorityRole = authorityRole;
        sc.OverrideApprovedBy = approvedBy.Trim();
        sc.OverrideApprovedAt = DateTime.UtcNow;
        sc.OverrideEvidenceArtifact = request.EvidenceArtifact.ToArray();
        sc.OverrideEvidenceSha256 = request.EvidenceSha256.ToLowerInvariant();
        sc.OverrideInputFingerprintSha256 = fingerprint;
        sc.OverrideRequiresRereview = false;
        if (period.FilingRegime is not null)
            db.FilingRegimes.Remove(period.FilingRegime);
        await db.SaveChangesAsync();

        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "SizeClassification",
                sc.Id,
                AuditEventCodes.SizeClassificationOverrideApplied,
                oldValue,
                ClassificationAuditSnapshot(sc, null, null, null, null, null),
                auditUserId ?? approvedBy);
        }
        return sc;
    }

    public static void ValidateInputs(
        decimal turnover,
        decimal balanceSheetTotal,
        int averageEmployees,
        DateOnly? thresholdElectionEffectiveFrom)
    {
        if (turnover < 0 || balanceSheetTotal < 0 || averageEmployees < 0)
            throw new BusinessRuleException("Turnover, balance-sheet total and average employees must be non-negative.");
        if (thresholdElectionEffectiveFrom is not null
            && thresholdElectionEffectiveFrom != new DateOnly(2023, 1, 1)
            && thresholdElectionEffectiveFrom != new DateOnly(2024, 1, 1))
        {
            throw new BusinessRuleException("The 2024 threshold adjustment election must apply from either 2023-01-01 or 2024-01-01.");
        }
    }

    private async Task<AccountingPeriod?> LoadPriorPeriodAsync(AccountingPeriod period) =>
        await db.AccountingPeriods
            .AsNoTracking()
            .Include(p => p.SizeClassification)
            .Where(p => p.CompanyId == period.CompanyId && p.PeriodEnd < period.PeriodStart)
            .OrderByDescending(p => p.PeriodEnd)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();

    private ThresholdSchedule SelectSchedule(DateOnly periodStart, DateOnly election)
    {
        if (periodStart < HistoricalScheduleStart)
        {
            throw new BusinessRuleException(
                "Automated statutory size classification is supported only for financial years beginning on or after 2017-01-01; earlier-year transition elections require retained professional review.");
        }
        var currentEffective = ParseConfiguredDate(_config.EffectiveFrom, "SizeThresholds:EffectiveFrom");
        var earlyEffective = ParseConfiguredDate(_config.EarlyElectionEffectiveFrom, "SizeThresholds:EarlyElectionEffectiveFrom");
        var useCurrent = periodStart >= currentEffective || (election == earlyEffective && periodStart >= earlyEffective);
        return useCurrent
            ? new ThresholdSchedule("SI-301-2024", election == earlyEffective ? earlyEffective : currentEffective, _config.Micro, _config.Small, _config.Medium)
            : new ThresholdSchedule("CA-2014-2017-HISTORICAL", HistoricalScheduleStart, _config.HistoricalMicro, _config.HistoricalSmall, _config.HistoricalMedium);
    }

    private DateOnly NormalizeElection(DateOnly? election)
    {
        var effective = ParseConfiguredDate(_config.EffectiveFrom, "SizeThresholds:EffectiveFrom");
        var early = ParseConfiguredDate(_config.EarlyElectionEffectiveFrom, "SizeThresholds:EarlyElectionEffectiveFrom");
        var normalized = election ?? effective;
        if (normalized != effective && normalized != early)
            throw new BusinessRuleException($"Threshold election must be {early:yyyy-MM-dd} or {effective:yyyy-MM-dd}.");
        return normalized;
    }

    private static DateOnly ParseConfiguredDate(string value, string name) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{name} must be an ISO date (yyyy-MM-dd).");

    private static RawThresholdDecision EvaluateRaw(
        AccountingPeriod period,
        SizeClassification sc,
        ThresholdSchedule schedule)
    {
        var years = PeriodLengthInYears(period.PeriodStart, period.PeriodEnd);
        var annualisedTurnover = decimal.Round(sc.Turnover / years, 2, MidpointRounding.AwayFromZero);
        var micro = MeetsTwoOfThree(sc.Turnover, sc.BalanceSheetTotal, sc.AvgEmployees, schedule.Micro, years);
        var small = MeetsTwoOfThree(sc.Turnover, sc.BalanceSheetTotal, sc.AvgEmployees, schedule.Small, years);
        var medium = MeetsTwoOfThree(sc.Turnover, sc.BalanceSheetTotal, sc.AvgEmployees, schedule.Medium, years);
        var rawClass = micro ? CompanySizeClass.Micro : small ? CompanySizeClass.Small : medium ? CompanySizeClass.Medium : CompanySizeClass.Large;
        return new RawThresholdDecision(rawClass, micro, small, medium, annualisedTurnover, years);
    }

    private static decimal PeriodLengthInYears(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new BusinessRuleException("Period end must not precede period start for statutory classification.");
        var exclusiveEnd = end.AddDays(1);
        var months = (exclusiveEnd.Year - start.Year) * 12 + exclusiveEnd.Month - start.Month;
        if (exclusiveEnd.Day == start.Day && months > 0)
            return months / 12m;
        var inclusiveDays = end.DayNumber - start.DayNumber + 1;
        return decimal.Divide(inclusiveDays, 365.2425m);
    }

    private static bool MeetsTwoOfThree(
        decimal turnover,
        decimal balanceSheet,
        int employees,
        ThresholdSet thresholds,
        decimal periodLengthInYears)
    {
        var met = 0;
        // The legislation adjusts the turnover threshold itself. Compare against that
        // exact amount so display rounding cannot change a statutory boundary result.
        if (turnover <= thresholds.Turnover * periodLengthInYears) met++;
        if (balanceSheet <= thresholds.BalanceSheet) met++;
        if (employees <= thresholds.Employees) met++;
        return met >= 2;
    }

    public static bool QualifiesInSubsequentYear(bool currentRaw, bool priorRaw, bool priorQualified) =>
        currentRaw && priorRaw || priorQualified && (currentRaw || priorRaw);

    private static void EnsureEntityClassificationSupported(Company company)
    {
        if (company.IsHolding)
        {
            throw new BusinessRuleException(
                "Holding-company size must be determined from retained net/gross group figures; entity-only inputs cannot establish a small or medium group.");
        }
    }

    private static List<string> GetIneligibleReasons(Company company)
    {
        var reasons = new List<string>();
        if (company.CompanyType == CompanyType.PublicLimitedCompany) reasons.Add("public limited company");
        if (company.IsListedSecurities) reasons.Add("securities admitted to a regulated market");
        if (company.IsCreditInstitution) reasons.Add("credit institution");
        if (company.IsInsuranceUndertaking) reasons.Add("insurance undertaking");
        if (company.IsPensionFund) reasons.Add("pension/investment fund");
        if (company.IsFifthScheduleEntity) reasons.Add("Fifth Schedule entity");
        if (company.IsOtherIneligibleEntity) reasons.Add("otherwise designated public-interest/ineligible entity");
        return reasons;
    }

    private static List<string> GetMicroExclusionReasons(Company company)
    {
        var reasons = new List<string>();
        if (company.IsInvestment) reasons.Add("investment undertaking");
        if (company.IsFinancialHoldingUndertaking) reasons.Add("financial holding undertaking");
        if (company.PreparesGroupFinancialStatements) reasons.Add("holding company preparing group financial statements");
        if (company.IncludedInHigherConsolidatedFinancialStatements) reasons.Add("subsidiary included in higher consolidated financial statements");
        // Legacy flags remain conservative until the company profile is migrated to the precise flags.
        if (company.IsSubsidiary && !company.IncludedInHigherConsolidatedFinancialStatements) reasons.Add("subsidiary consolidation status requires review");
        return reasons;
    }

    private static List<string> AvailableRegimes(
        CompanySizeClass sizeClass,
        bool canUseMicro,
        bool canFileAbridged,
        bool isIneligible)
    {
        if (isIneligible) return ["Full"];
        var regimes = new List<string>();
        if (canUseMicro) regimes.Add("Micro (FRS 105)");
        if (sizeClass <= CompanySizeClass.Small)
        {
            regimes.Add("Small — Full");
            if (canFileAbridged) regimes.Add("Small — Abridged");
            regimes.Add("Full");
        }
        else if (sizeClass == CompanySizeClass.Medium)
        {
            regimes.Add("Medium");
            regimes.Add("Full");
        }
        else
        {
            regimes.Add("Full");
        }
        return regimes;
    }

    public static string ComputeInputFingerprint(
        AccountingPeriod period,
        SizeClassification sc,
        DateOnly election,
        AccountingPeriod? priorPeriod = null)
    {
        var company = period.Company;
        var canonicalParts = new List<string>
        {
            $"companyId={period.CompanyId}",
            $"periodId={period.Id}",
            $"periodStart={period.PeriodStart:yyyy-MM-dd}",
            $"periodEnd={period.PeriodEnd:yyyy-MM-dd}",
            $"firstYear={period.IsFirstYear}",
            $"turnover={sc.Turnover.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"balanceSheet={sc.BalanceSheetTotal.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"employees={sc.AvgEmployees}",
            $"thresholdElection={election:yyyy-MM-dd}",
            $"companyType={company.CompanyType}",
            $"holding={company.IsHolding}",
            $"investment={company.IsInvestment}",
            $"subsidiary={company.IsSubsidiary}",
            $"groupMember={company.IsGroupMember}",
            $"listed={company.IsListedSecurities}",
            $"creditInstitution={company.IsCreditInstitution}",
            $"insurance={company.IsInsuranceUndertaking}",
            $"pensionFund={company.IsPensionFund}",
            $"fifthSchedule={company.IsFifthScheduleEntity}",
            $"otherIneligible={company.IsOtherIneligibleEntity}",
            $"financialHolding={company.IsFinancialHoldingUndertaking}",
            $"preparesGroup={company.PreparesGroupFinancialStatements}",
            $"includedInHigherConsolidation={company.IncludedInHigherConsolidatedFinancialStatements}"
        };
        if (priorPeriod?.SizeClassification is { } prior)
        {
            canonicalParts.AddRange([
                $"priorPeriodId={priorPeriod.Id}",
                $"priorPeriodStart={priorPeriod.PeriodStart:yyyy-MM-dd}",
                $"priorPeriodEnd={priorPeriod.PeriodEnd:yyyy-MM-dd}",
                $"priorFirstYear={priorPeriod.IsFirstYear}",
                $"priorTurnover={prior.Turnover.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"priorBalanceSheet={prior.BalanceSheetTotal.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"priorEmployees={prior.AvgEmployees}",
                $"priorCalculatedClass={prior.CalculatedClass}",
                $"priorOverrideClass={prior.OverrideClass?.ToString() ?? "none"}",
                $"priorDecisionFingerprint={prior.DecisionInputFingerprintSha256 ?? "none"}"
            ]);
        }
        else
        {
            canonicalParts.Add("priorPeriod=none");
        }
        var canonical = string.Join("\n", canonicalParts);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static async Task EnsureDecisionChainCurrentAsync(
        AccountsDbContext context,
        AccountingPeriod targetPeriod,
        DateOnly companyElection,
        bool includeTarget)
    {
        ValidateInputs(0, 0, 0, companyElection);
        var periods = await context.AccountingPeriods
            .AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Where(p => p.CompanyId == targetPeriod.CompanyId
                && (p.PeriodEnd < targetPeriod.PeriodStart || includeTarget && p.Id == targetPeriod.Id))
            .OrderBy(p => p.PeriodEnd)
            .ThenBy(p => p.Id)
            .ToListAsync();

        AccountingPeriod? prior = null;
        foreach (var candidate in periods)
        {
            var classification = candidate.SizeClassification
                ?? throw new BusinessRuleException(
                    $"The retained statutory decision chain is incomplete at period {candidate.Id}; classify every preceding period first.");
            ValidateInputs(
                classification.Turnover,
                classification.BalanceSheetTotal,
                classification.AvgEmployees,
                classification.ThresholdElectionEffectiveFrom);
            var candidateElection = classification.ThresholdElectionEffectiveFrom ?? new DateOnly(2024, 1, 1);
            if (candidate.PeriodStart >= new DateOnly(2023, 1, 1) && candidateElection != companyElection)
            {
                throw new BusinessRuleException(
                    "The 2024 threshold adjustment election must be applied consistently to every financial year of the company beginning on or after 2023-01-01.");
            }
            if (candidate.IsFirstYear != (prior is null))
            {
                throw new BusinessRuleException(
                    "The retained statutory decision chain does not have one deterministic first financial year.");
            }
            var expected = ComputeInputFingerprint(candidate, classification, candidateElection, prior);
            if (string.IsNullOrWhiteSpace(classification.DecisionInputFingerprintSha256)
                || !string.Equals(classification.DecisionInputFingerprintSha256, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException(
                    $"The statutory size decision for preceding period {candidate.Id} is stale; re-run it before relying on consecutive-year qualification.");
            }
            if (classification.OverrideClass is not null && !HasCurrentOverrideEvidence(classification))
            {
                throw new BusinessRuleException(
                    $"The statutory classification override for preceding period {candidate.Id} is stale or lacks retained authority and evidence.");
            }
            prior = candidate;
        }
    }

    public static bool HasCurrentOverrideEvidence(SizeClassification classification)
    {
        if (classification.OverrideClass is null)
            return true;
        if (classification.OverrideRequiresRereview
            || classification.OverrideAuthorityRole is not "Owner" and not "Reviewer"
            || string.IsNullOrWhiteSpace(classification.OverrideApprovedBy)
            || classification.OverrideApprovedAt is null
            || string.IsNullOrWhiteSpace(classification.OverrideReason)
            || classification.OverrideEvidenceArtifact is not { Length: > 0 }
            || classification.OverrideEvidenceSha256 is not { Length: 64 }
            || !classification.OverrideEvidenceSha256.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(classification.OverrideInputFingerprintSha256)
            || !string.Equals(
                classification.OverrideInputFingerprintSha256,
                classification.DecisionInputFingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(classification.OverrideEvidenceArtifact)),
            classification.OverrideEvidenceSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureRetainedEvidence(byte[] content, string expectedHash)
    {
        if (content is null || content.Length == 0)
            throw new BusinessRuleException("Retained override evidence is required.");
        if (expectedHash is not { Length: 64 } || !expectedHash.All(Uri.IsHexDigit))
            throw new BusinessRuleException("Override evidence requires a valid SHA-256 digest.");
        var actual = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Override evidence bytes do not match the supplied SHA-256 digest.");
    }

    private static object ClassificationAuditSnapshot(
        SizeClassification sc,
        bool? canUseMicro,
        bool? canFileAbridged,
        bool? auditExempt,
        List<string>? availableRegimes,
        bool? isIneligibleEntity) => new
    {
        sc.Turnover,
        sc.BalanceSheetTotal,
        sc.AvgEmployees,
        sc.AnnualisedTurnover,
        sc.PeriodLengthInYears,
        sc.RawCurrentClass,
        sc.RawPriorClass,
        sc.PriorYearClass,
        sc.CalculatedClass,
        sc.ThresholdElectionEffectiveFrom,
        sc.ThresholdScheduleEffectiveFrom,
        sc.ThresholdScheduleCode,
        sc.DecisionInputFingerprintSha256,
        sc.OverrideClass,
        sc.OverrideReason,
        sc.OverrideAuthorityRole,
        sc.OverrideApprovedBy,
        sc.OverrideApprovedAt,
        sc.OverrideEvidenceSha256,
        sc.OverrideInputFingerprintSha256,
        sc.OverrideRequiresRereview,
        sc.CalculatedAt,
        sc.QualificationNotes,
        CanUseMicro = canUseMicro,
        CanFileAbridged = canFileAbridged,
        AuditExempt = auditExempt,
        AvailableRegimes = availableRegimes,
        IsIneligibleEntity = isIneligibleEntity
    };
}
