using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed record SizeClassificationOverrideRequest(
    CompanySizeClass OverrideClass,
    string Reason,
    byte[] EvidenceArtifact,
    string EvidenceSha256);

/// <summary>
/// Read-only view of whether a period can be classified yet: a later year's size test needs a
/// current decision for every earlier period back to the first year (Companies Act 2014, s.280A).
/// </summary>
public sealed record ClassificationPrerequisites(
    bool Ready,
    bool IsFirstYear,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClassificationBlockingPeriod? FirstBlockingPeriod,
    string Message,
    // The immediately preceding period, when historic figures from its filed accounts may be recorded
    // instead of classifying every earlier period in order.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClassificationBlockingPeriod? HistoricFiguresPeriod = null,
    // Whether figures from this period's own filed accounts may be keyed in on its Classify page.
    bool CanRecordHistoricFigures = false,
    // The threshold election the dependent period uses, to default the historic-figures form.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateOnly? SuggestedThresholdElection = null);

/// <summary>The earliest period in the decision chain that must be classified or re-run first.</summary>
public sealed record ClassificationBlockingPeriod(
    int PeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Reason);

/// <summary>
/// Figures keyed in from accounts filed elsewhere for a preceding period, with the retained PDF
/// evidence and an optional attested effective class (Companies Act 2014, s.280A).
/// </summary>
public sealed record HistoricSizeFiguresRequest(
    decimal Turnover,
    decimal BalanceSheetTotal,
    int AvgEmployees,
    DateOnly? ThresholdElectionEffectiveFrom,
    string SourceReference,
    byte[] EvidenceArtifact,
    string EvidenceSha256,
    CompanySizeClass? AttestedEffectiveClass);

public class SizeClassificationService(
    AccountsDbContext db,
    IOptions<SizeThresholdConfig> thresholds,
    AuditService? audit = null)
{
    private static readonly DateOnly HistoricalScheduleStart = new(2017, 1, 1);

    public const string HistoricAnchorNotATargetMessage =
        "This period's size figures were keyed in from accounts filed elsewhere; they are evidence for the following year's size test only, so this period cannot be classified, overridden or used as a filing basis here.";

    public const string ClassifiableNormallyMessage =
        "This period can be classified from the earlier periods already in FilingBridge; classify it normally instead.";

    public const string RetiredHistoricAnchorMessage =
        "The historic figures recorded for this period were retired; enter this period's own figures to classify it.";

    private const int HistoricRetirementReasonMaxLength = 1000;

    private const string HistoricFiguresOption = "record historic figures from the filed accounts";
    private const int HistoricSourceReferenceMaxLength = 300;
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
        var period = await LoadPeriodForClassificationAsync(companyId, periodId);
        var sc = period.SizeClassification
            ?? throw new BusinessRuleException("Size classification data not yet entered for this period.");
        return await ClassifyLoadedAsync(period, ClassificationAuditSnapshot(sc, null, null, null, null, null), userId);
    }

    /// <summary>
    /// Read-only preflight for the Classify page. Uses the classifier's own decision-chain
    /// evaluation, so it never reports Ready for a period the classifier would reject on the chain.
    /// </summary>
    public async Task<ClassificationPrerequisites> GetPrerequisitesAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        // Whether figures from this period's own filed accounts may be keyed in here instead: only
        // where the period cannot be classified from the retained chain and nothing is replaced.
        var canRecordHere = await CanRecordHistoricFiguresOnAsync(period);
        return (await BuildPrerequisitesAsync(period)) with
        {
            CanRecordHistoricFigures = canRecordHere,
            SuggestedThresholdElection = canRecordHere ? await SuggestedHistoricElectionAsync(period) : null
        };
    }

    /// <summary>
    /// Historic figures are a fallback for genuinely missing history: never for a first year, a
    /// period the retained chain can already classify (or could, once an earlier period's figures
    /// are classified), a holding company, the latest period, or a period with a system decision,
    /// override or lock.
    /// </summary>
    private async Task<bool> CanRecordHistoricFiguresOnAsync(AccountingPeriod period) =>
        CanRecordHistoricFigures(period)
        && !period.Company.IsHolding
        && await db.AccountingPeriods.AsNoTracking().AnyAsync(p => p.CompanyId == period.CompanyId && p.PeriodStart > period.PeriodEnd)
        && await HistoricFiguresChainRefusalAsync(period) is null;

    /// <summary>
    /// Decided structurally, not by whether the chain happens to be current right now: historic
    /// figures may be recorded only when the predecessors' chain first fails because an earlier period
    /// has no figures at all (Missing) or the chain has no first year (FirstYearMissing). Any other
    /// first failure (undecided, stale, invalid, election, override, anchor evidence) means figures
    /// already exist in FilingBridge and must be classified instead. Returns the refusal, or null.
    /// </summary>
    private async Task<string?> HistoricFiguresChainRefusalAsync(AccountingPeriod period)
    {
        if (period.IsFirstYear)
            return ClassifiableNormallyMessage;
        var failures = new List<DecisionChainFailure>();
        foreach (var election in new[] { new DateOnly(2023, 1, 1), new DateOnly(2024, 1, 1) })
        {
            var evaluation = await EvaluateDecisionChainAsync(db, period, election, includeTarget: false);
            if (evaluation.FirstFailure is null)
                return evaluation.Periods.Count > 0 ? ClassifiableNormallyMessage : null;
            failures.Add(evaluation.FirstFailure);
        }
        // Missing figures are independent of the election, so either election finding them first
        // means the history is genuinely missing (the other may stop earlier on an election mismatch).
        if (failures.Any(failure => failure.Kind is DecisionChainFailureKind.Missing or DecisionChainFailureKind.FirstYearMissing))
            return null;
        var earliest = failures.OrderBy(failure => failure.Period.PeriodEnd).ThenBy(failure => failure.Period.Id).First().Period;
        return $"An earlier period ({DescribePeriod(earliest)}) already has figures in FilingBridge; finish classifying it instead of recording historic figures.";
    }

    /// <summary>The election the immediately following (dependent) period uses, else the company's decided one.</summary>
    private async Task<DateOnly?> SuggestedHistoricElectionAsync(AccountingPeriod period)
    {
        var dependent = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.CompanyId == period.CompanyId && p.PeriodStart > period.PeriodEnd)
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.Id)
            .Select(p => new { Election = p.SizeClassification == null ? null : p.SizeClassification.ThresholdElectionEffectiveFrom })
            .FirstOrDefaultAsync();
        if (dependent?.Election is { } dependentElection)
            return dependentElection;
        return await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.CompanyId == period.CompanyId
                && p.PeriodStart >= new DateOnly(2023, 1, 1)
                && p.SizeClassification != null
                && p.SizeClassification.InputBasis == SizeInputBasis.Computed
                && p.SizeClassification.DecisionInputFingerprintSha256 != null)
            .OrderBy(p => p.PeriodStart)
            .Select(p => p.SizeClassification!.ThresholdElectionEffectiveFrom)
            .FirstOrDefaultAsync();
    }

    private async Task<ClassificationPrerequisites> BuildPrerequisitesAsync(AccountingPeriod period)
    {
        var companyId = period.CompanyId;
        if (IsHistoricAnchor(period.SizeClassification))
            return new ClassificationPrerequisites(false, period.IsFirstYear, null, HistoricAnchorNotATargetMessage);

        // The election is chosen on the form when classifying, so the preflight takes it from the
        // retained predecessors that the consistency rule applies to; the classifier still checks
        // the submitted election against them.
        var predecessorElection = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId
                && p.PeriodEnd < period.PeriodStart
                && p.PeriodStart >= new DateOnly(2023, 1, 1)
                && p.SizeClassification != null)
            .OrderBy(p => p.PeriodEnd)
            .ThenBy(p => p.Id)
            .Select(p => new { p.SizeClassification!.ThresholdElectionEffectiveFrom })
            .FirstOrDefaultAsync();
        var election = predecessorElection is null
            ? NormalizeElection(null)
            : predecessorElection.ThresholdElectionEffectiveFrom ?? new DateOnly(2024, 1, 1);

        var evaluation = await EvaluateDecisionChainAsync(db, period, election, includeTarget: false);
        if (evaluation.FirstFailure is { } failure)
        {
            var immediatePrior = evaluation.Periods[^1];
            var blocking = failure.Period;
            var reason = IsHistoricAnchor(blocking.SizeClassification)
                ? "historic figures from the filed accounts are no longer current — record them again"
                : PrerequisiteReason(failure.Kind);
            // Second route (s.280A still applies in full): key in the immediately preceding period's
            // figures from its filed accounts, when that period has no system decision to replace.
            var historicFigures = await CanRecordHistoricFiguresOnAsync(immediatePrior)
                ? new ClassificationBlockingPeriod(immediatePrior.Id, immediatePrior.PeriodStart, immediatePrior.PeriodEnd, HistoricFiguresOption)
                : null;
            string message;
            if (IsHistoricAnchor(blocking.SizeClassification))
            {
                // An anchor is never classified here; it can only be recorded again.
                message = $"Record the historic figures for {DescribePeriod(blocking)} again from its filed accounts ({reason}). {PriorYearRule}.";
                return new ClassificationPrerequisites(
                    false,
                    period.IsFirstYear,
                    new ClassificationBlockingPeriod(blocking.Id, blocking.PeriodStart, blocking.PeriodEnd, reason),
                    message,
                    historicFigures);
            }
            if (historicFigures is null)
            {
                message = $"Classify {DescribePeriod(immediatePrior)} first. {PriorYearRule}";
                if (blocking.Id != immediatePrior.Id)
                    message += $", so earlier periods must be classified in order, starting from {DescribePeriod(blocking)}";
            }
            else
            {
                message = $"Classify {DescribePeriod(blocking)} first, or {HistoricFiguresOption} for {DescribePeriod(immediatePrior)}. {PriorYearRule}";
                if (blocking.Id != immediatePrior.Id)
                    message += $", so without historic figures every period from {DescribePeriod(blocking)} must be classified in order";
            }
            message += ".";
            if (failure.Kind is not DecisionChainFailureKind.Missing and not DecisionChainFailureKind.Undecided)
                message += $" {DescribePeriod(blocking)}: {reason}.";
            return new ClassificationPrerequisites(
                false,
                period.IsFirstYear,
                new ClassificationBlockingPeriod(blocking.Id, blocking.PeriodStart, blocking.PeriodEnd, reason),
                message,
                historicFigures);
        }

        if (period.IsFirstYear)
        {
            return new ClassificationPrerequisites(
                true, true, null, "First financial year: the size test uses this period's figures only.");
        }
        if (evaluation.Periods.Count == 0)
        {
            return new ClassificationPrerequisites(
                false,
                false,
                null,
                $"No earlier period is recorded, but this period is not marked as the first financial year. {PriorYearRule}, so the preceding period must be added and classified first.");
        }
        return new ClassificationPrerequisites(
            true, false, null, "Every earlier period has a current classification, so this period can be classified.");
    }

    private const string PriorYearRule =
        "Each year's size test uses the previous year's figures (Companies Act 2014, s.280A)";

    // Only a current anchor roots the chain and is refused as a target. Retired historic figures are
    // neither: they are evidence only, never a decision (see EvaluateChainLink and ClassifyLoadedAsync).
    private static bool IsHistoricAnchor(SizeClassification? classification) =>
        classification is not null && classification.InputBasis == SizeInputBasis.HistoricAnchor;

    /// <summary>
    /// Historic figures may be keyed in for a period only when that replaces no system decision or
    /// override, the period is open, and its year is within the automated schedules.
    /// </summary>
    private static bool CanRecordHistoricFigures(AccountingPeriod period) =>
        period.PeriodStart >= HistoricalScheduleStart
        && period.Status is not PeriodStatus.Finalised and not PeriodStatus.Filed
        && period.LockedAt is null
        && period.SizeClassification switch
        {
            null => true,
            { InputBasis: not SizeInputBasis.Computed } => true,
            var computed => !computed.HasCurrentDecision && computed.OverrideClass is null
        };

    private static string DescribePeriod(AccountingPeriod period) =>
        $"{period.PeriodStart.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} – {period.PeriodEnd.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

    private static string PrerequisiteReason(DecisionChainFailureKind kind) => kind switch
    {
        DecisionChainFailureKind.Missing or DecisionChainFailureKind.Undecided => "not classified yet",
        DecisionChainFailureKind.Stale => "classification out of date — re-run it",
        DecisionChainFailureKind.InvalidInputs => "saved figures are not valid — correct them and re-run it",
        DecisionChainFailureKind.ElectionMismatch =>
            "uses a different 2024 threshold election from the other years beginning on or after 1 Jan 2023 — re-run it with the same election",
        DecisionChainFailureKind.FirstYearRepeated =>
            "more than one period is marked as the first financial year — correct the period set-up",
        DecisionChainFailureKind.FirstYearMissing =>
            "the earliest period is not marked as the first financial year — correct the period set-up",
        _ => "size override is out of date or lacks retained authority and evidence — review the override"
    };

    /// <summary>
    /// Loads the tracked period graph that <see cref="ClassifyLoadedAsync"/> requires.
    /// </summary>
    public async Task<AccountingPeriod> LoadPeriodForClassificationAsync(int companyId, int periodId) =>
        await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

    /// <summary>
    /// Classifies a period loaded by <see cref="LoadPeriodForClassificationAsync"/>. Pending input
    /// changes on the tracked classification are persisted by the same single SaveChanges as the
    /// decision, so a rejected classification never leaves inputs saved without a decision.
    /// </summary>
    public async Task<ClassificationResult> ClassifyLoadedAsync(
        AccountingPeriod period,
        object? auditOldValue,
        string? userId = null)
    {
        var periodId = period.Id;
        var sc = period.SizeClassification
            ?? throw new BusinessRuleException("Size classification data not yet entered for this period.");
        if (IsHistoricAnchor(sc))
            throw new BusinessRuleException(HistoricAnchorNotATargetMessage);
        // Retired figures are never classified as they stand; the period's own figures must be entered.
        if (sc.InputBasis == SizeInputBasis.RetiredHistoricAnchor)
            throw new BusinessRuleException(RetiredHistoricAnchorMessage);
        ValidateInputs(sc.Turnover, sc.BalanceSheetTotal, sc.AvgEmployees, sc.ThresholdElectionEffectiveFrom);
        EnsureEntityClassificationSupported(period.Company);

        var oldValue = auditOldValue;
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
            var priorEffectiveClass = PriorEffectiveClass(priorSc);
            if (IsHistoricAnchor(priorSc))
            {
                notes.Add(priorSc.HistoricAttestedEffectiveClass is { } attested
                    ? $"Preceding-year figures were keyed in from accounts filed elsewhere ({priorSc.HistoricSourceReference}); the system did not compute them. Attested effective class {attested}, on retained evidence."
                    : $"Preceding-year figures were keyed in from accounts filed elsewhere ({priorSc.HistoricSourceReference}); the system did not compute them. No effective class is attested, so the preceding year is treated as not qualified: the current and preceding years' raw figures must both meet the conditions.");
            }
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
            ? PriorEffectiveClass(priorDecision)
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
        if (IsHistoricAnchor(sc))
            throw new BusinessRuleException(HistoricAnchorNotATargetMessage);
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

    /// <summary>
    /// Records (or replaces) figures keyed in from a preceding period's filed accounts as a
    /// provenance-marked root for the next period's s.280A test. The same raw tests, schedules and
    /// pre-2017 floor apply. It never replaces a system decision or override, never deletes, and
    /// the period it is recorded on is never classified or filed from these figures.
    /// </summary>
    public async Task<SizeClassification> RecordHistoricAnchorAsync(
        int companyId,
        int periodId,
        HistoricSizeFiguresRequest request,
        string authorityRole,
        string attestedBy,
        string? auditUserId = null)
    {
        if (authorityRole is not "Owner" and not "Reviewer")
            throw new BusinessRuleException("Only an Owner or Reviewer may record historic size figures from filed accounts.");
        if (string.IsNullOrWhiteSpace(attestedBy))
            throw new BusinessRuleException("A named person recording the historic figures is required.");
        var sourceReference = request.SourceReference?.Trim();
        if (string.IsNullOrEmpty(sourceReference)
            || sourceReference.Length > HistoricSourceReferenceMaxLength
            || sourceReference.Any(char.IsControl))
        {
            throw new BusinessRuleException("Identify the filed accounts the figures come from (a source reference of up to 300 characters).");
        }
        EnsureHistoricEvidence(request.EvidenceArtifact, request.EvidenceSha256);
        ValidateInputs(request.Turnover, request.BalanceSheetTotal, request.AvgEmployees, request.ThresholdElectionEffectiveFrom);

        await using var transaction = await BeginUnderCompanyLockAsync(companyId);
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        if (period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null)
            throw new BusinessRuleException("This period is finalised or locked; reopen it before recording historic figures.");
        var hasSuccessor = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.CompanyId == companyId && p.PeriodStart > period.PeriodEnd);
        if (!hasSuccessor)
            throw new BusinessRuleException("Historic figures can be recorded only for a period that precedes another accounting period of the company.");

        var sc = period.SizeClassification;
        if (sc is not null && !IsHistoricAnchor(sc))
        {
            if (sc.HasCurrentDecision)
                throw new BusinessRuleException("This period already has a size classification computed by the system; historic figures cannot replace it.");
            if (sc.OverrideClass is not null)
                throw new BusinessRuleException("This period has a size classification override; historic figures cannot replace it.");
        }
        EnsureEntityClassificationSupported(period.Company);
        var election = NormalizeElection(request.ThresholdElectionEffectiveFrom);
        var schedule = SelectSchedule(period.PeriodStart, election);
        // A fallback for genuinely missing history only, never a shortcut past a classifiable chain.
        if (await HistoricFiguresChainRefusalAsync(period) is { } chainRefusal)
            throw new BusinessRuleException(chainRefusal);

        var oldValue = sc is null ? null : HistoricAnchorAuditSnapshot(sc);
        // Replacing an active anchor is refused like retiring it while filed or locked work relies on it.
        if (sc is { InputBasis: SizeInputBasis.HistoricAnchor }
            && await RelyingLockedPeriodsRefusalAsync(period, "replaced") is { } relyingRefusal)
            throw new BusinessRuleException(relyingRefusal);
        // The evidence being replaced is archived first (append-only), so it is never lost.
        if (sc is { InputBasis: SizeInputBasis.HistoricAnchor or SizeInputBasis.RetiredHistoricAnchor })
        {
            var retiredByUserId = sc.HistoricRetiredAt is null
                ? null
                : await db.HistoricAnchorEvidenceRecords
                    .Where(record => record.PeriodId == sc.PeriodId
                        && record.Event == HistoricAnchorEvidenceEvent.Retired
                        && record.RetiredAt == sc.HistoricRetiredAt)
                    .Select(record => record.RetiredByUserId)
                    .FirstOrDefaultAsync();
            ArchiveAnchorEvidence(sc, HistoricAnchorEvidenceEvent.Replaced, attestedBy.Trim(), auditUserId, retiredByUserId);
        }
        if (sc is null)
        {
            sc = new SizeClassification { PeriodId = period.Id };
            db.SizeClassifications.Add(sc);
            period.SizeClassification = sc;
        }

        sc.InputBasis = SizeInputBasis.HistoricAnchor;
        sc.Turnover = request.Turnover;
        sc.BalanceSheetTotal = request.BalanceSheetTotal;
        sc.AvgEmployees = request.AvgEmployees;
        sc.ThresholdElectionEffectiveFrom = election;
        sc.HistoricSourceReference = sourceReference;
        sc.HistoricEvidenceArtifact = request.EvidenceArtifact.ToArray();
        sc.HistoricEvidenceSha256 = request.EvidenceSha256.ToLowerInvariant();
        sc.HistoricAttestedBy = attestedBy.Trim();
        sc.HistoricAttestedRole = authorityRole;
        sc.HistoricAttestedAt = DateTime.UtcNow;
        sc.HistoricAttestedEffectiveClass = request.AttestedEffectiveClass;
        // A new anchor is current: a retirement of earlier figures stays in the audit trail.
        sc.HistoricRetiredAt = null;
        sc.HistoricRetiredBy = null;
        sc.HistoricRetiredReason = null;

        var raw = EvaluateRaw(period, sc, schedule);
        sc.PriorYearClass = null;
        sc.RawCurrentClass = raw.RawClass;
        sc.RawPriorClass = null;
        sc.RawCurrentMicroQualified = raw.Micro;
        sc.RawCurrentSmallQualified = raw.Small;
        sc.RawCurrentMediumQualified = raw.Medium;
        sc.RawPriorMicroQualified = null;
        sc.RawPriorSmallQualified = null;
        sc.RawPriorMediumQualified = null;
        sc.AnnualisedTurnover = raw.AnnualisedTurnover;
        sc.PeriodLengthInYears = raw.PeriodLengthInYears;
        sc.ThresholdScheduleEffectiveFrom = schedule.EffectiveFrom;
        sc.ThresholdScheduleCode = schedule.Code;
        // The class the next year relies on: the attested class, or "not qualified" without one.
        sc.CalculatedClass = PriorEffectiveClass(sc);
        sc.ExclusionFlagsJson = null;
        sc.QualificationNotes = string.Join("\n", new[]
        {
            $"Historic figures keyed in from accounts filed elsewhere ({sourceReference}); the system did not compute them. Recorded by {sc.HistoricAttestedBy} ({authorityRole}) with retained PDF evidence.",
            $"Raw tests on these figures under schedule {schedule.Code} — Micro={raw.Micro}; Small={raw.Small}; Medium={raw.Medium}.",
            sc.HistoricAttestedEffectiveClass is { } attested
                ? $"Attested effective class {attested}: the following year may rely on it for the consecutive-year rule."
                : "No effective class is attested: the following year treats this year as not qualified, so both years' raw figures must meet the conditions.",
            "These figures are evidence for the following year's size test only; this period is not classified or filed from them."
        });
        sc.CalculatedAt = DateTime.UtcNow;
        sc.DecisionInputFingerprintSha256 = ComputeInputFingerprint(period, sc, election);
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
                AuditEventCodes.SizeClassificationHistoricAnchorRecorded,
                oldValue,
                HistoricAnchorAuditSnapshot(sc),
                auditUserId);
        }
        if (transaction is not null)
            await transaction.CommitAsync();
        return sc;
    }

    /// <summary>
    /// Retires a period's historic figures (Owner only). Nothing is deleted: the figures, evidence,
    /// source and attestation stay on the row, marked RetiredHistoricAnchor with who retired them,
    /// when and why. The row no longer roots the next period's s.280A chain (so its successors'
    /// decisions become stale), is never a decision, and the period is classified normally once its
    /// own figures are entered.
    /// </summary>
    public async Task<SizeClassification> RetireHistoricAnchorAsync(
        int companyId,
        int periodId,
        string? reason,
        string authorityRole,
        string retiredBy,
        string? auditUserId = null)
    {
        if (authorityRole is not "Owner")
            throw new BusinessRuleException("Only an Owner may retire historic size figures.");
        if (string.IsNullOrWhiteSpace(retiredBy))
            throw new BusinessRuleException("A named person retiring the historic figures is required.");
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason) || trimmedReason.Length > HistoricRetirementReasonMaxLength)
            throw new BusinessRuleException("Give the reason the historic figures are being retired (up to 1000 characters).");

        await using var transaction = await BeginUnderCompanyLockAsync(companyId);
        var period = await db.AccountingPeriods
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        if (period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null)
            throw new BusinessRuleException("This period is finalised or locked; reopen it before retiring historic figures.");
        var sc = period.SizeClassification;
        if (sc is null || !IsHistoricAnchor(sc))
            throw new BusinessRuleException("This period holds no current historic figures to retire.");
        if (await RelyingLockedPeriodsRefusalAsync(period, "retired") is { } relyingRefusal)
            throw new BusinessRuleException(relyingRefusal);

        var oldValue = HistoricAnchorAuditSnapshot(sc);
        sc.InputBasis = SizeInputBasis.RetiredHistoricAnchor;
        sc.DecisionInputFingerprintSha256 = null;
        sc.HistoricRetiredAt = DateTime.UtcNow;
        sc.HistoricRetiredBy = retiredBy.Trim();
        sc.HistoricRetiredReason = trimmedReason;
        ArchiveAnchorEvidence(sc, HistoricAnchorEvidenceEvent.Retired, sc.HistoricRetiredBy, auditUserId, auditUserId);
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
                AuditEventCodes.SizeClassificationHistoricAnchorRetired,
                oldValue,
                HistoricAnchorAuditSnapshot(sc),
                auditUserId);
        }
        if (transaction is not null)
            await transaction.CommitAsync();
        return sc;
    }

    /// <summary>
    /// Takes the company advisory lock (the one period finalisation and chart adoption take) before the
    /// anchor and the later periods relying on it are read, held through the save: a concurrent
    /// finalisation of a relying period then commits either before (and the change is refused) or
    /// after (and it sees the change). Returns the transaction to commit when this call opened it.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginUnderCompanyLockAsync(int companyId)
    {
        if (!db.Database.IsRelational())
            return null;
        var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;
        try
        {
            await AccountingConcurrencyCoordinator.AcquireCompanyAdvisoryLockAsync(db, companyId, CancellationToken.None);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
            throw;
        }
        return transaction;
    }

    /// <summary>
    /// Never overturn finalised, filed or locked work: the refusal while a later period whose size test
    /// rests on this anchor (every later period up to the next anchor) is finalised, filed or locked.
    /// </summary>
    private async Task<string?> RelyingLockedPeriodsRefusalAsync(AccountingPeriod period, string action)
    {
        var laterPeriods = await db.AccountingPeriods
            .AsNoTracking()
            .Include(p => p.SizeClassification)
            .Where(p => p.CompanyId == period.CompanyId && p.PeriodStart > period.PeriodEnd)
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.Id)
            .ToListAsync();
        var relyingLocked = laterPeriods
            .TakeWhile(p => !IsHistoricAnchor(p.SizeClassification))
            .Where(p => p.Status is PeriodStatus.Finalised or PeriodStatus.Filed || p.LockedAt is not null)
            .ToList();
        return relyingLocked.Count == 0
            ? null
            : $"These historic figures cannot be {action}: {string.Join(", ", relyingLocked.Select(DescribePeriod))} {(relyingLocked.Count == 1 ? "relies" : "rely")} on them for {(relyingLocked.Count == 1 ? "its" : "their")} size test and {(relyingLocked.Count == 1 ? "is" : "are")} finalised, filed or locked. Filed or locked work is never changed silently; reopen that period first if it can be reopened.";
    }

    /// <summary>
    /// Adds an append-only archive record of the row's historic evidence as it stands (figures, PDF,
    /// hash, source, attestation and any retirement) with the principals' user ids, saved with the
    /// caller's SaveChanges.
    /// </summary>
    private void ArchiveAnchorEvidence(
        SizeClassification sc,
        HistoricAnchorEvidenceEvent archiveEvent,
        string archivedBy,
        string? archivedByUserId,
        string? retiredByUserId) =>
        db.HistoricAnchorEvidenceRecords.Add(new HistoricAnchorEvidenceRecord
        {
            PeriodId = sc.PeriodId,
            Event = archiveEvent,
            ArchivedAt = DateTime.UtcNow,
            ArchivedBy = archivedBy,
            ArchivedByUserId = archivedByUserId,
            RetiredByUserId = sc.HistoricRetiredAt is null ? null : retiredByUserId,
            Turnover = sc.Turnover,
            BalanceSheetTotal = sc.BalanceSheetTotal,
            AvgEmployees = sc.AvgEmployees,
            ThresholdElectionEffectiveFrom = sc.ThresholdElectionEffectiveFrom,
            SourceReference = sc.HistoricSourceReference!,
            EvidenceArtifact = sc.HistoricEvidenceArtifact!.ToArray(),
            EvidenceSha256 = sc.HistoricEvidenceSha256!,
            AttestedBy = sc.HistoricAttestedBy!,
            AttestedRole = sc.HistoricAttestedRole!,
            AttestedAt = sc.HistoricAttestedAt!.Value,
            AttestedEffectiveClass = sc.HistoricAttestedEffectiveClass,
            RetiredAt = sc.HistoricRetiredAt,
            RetiredBy = sc.HistoricRetiredBy,
            RetiredReason = sc.HistoricRetiredReason
        });

    /// <summary>
    /// The effective class a following year relies on. A historic anchor counts only by its
    /// attested class; without one it is treated as not qualified (never more lenient).
    /// </summary>
    private static CompanySizeClass PriorEffectiveClass(SizeClassification prior) =>
        IsHistoricAnchor(prior)
            ? prior.HistoricAttestedEffectiveClass ?? CompanySizeClass.Large
            : prior.OverrideClass ?? prior.CalculatedClass;

    public static bool HasCurrentHistoricAnchorEvidence(SizeClassification classification)
    {
        if (!IsHistoricAnchor(classification))
            return true;
        if (classification.HistoricAttestedRole is not "Owner" and not "Reviewer"
            || string.IsNullOrWhiteSpace(classification.HistoricAttestedBy)
            || classification.HistoricAttestedAt is null
            || string.IsNullOrWhiteSpace(classification.HistoricSourceReference)
            || classification.HistoricEvidenceArtifact is not { Length: > 5 } artifact
            || !artifact.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            || classification.HistoricEvidenceSha256 is not { Length: 64 }
            || !classification.HistoricEvidenceSha256.All(Uri.IsHexDigit))
        {
            return false;
        }
        return string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(artifact)),
            classification.HistoricEvidenceSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureHistoricEvidence(byte[]? content, string? expectedHash)
    {
        if (content is null || content.Length == 0)
            throw new BusinessRuleException("A PDF copy of the filed accounts is required as evidence.");
        if (expectedHash is not { Length: 64 } || !expectedHash.All(Uri.IsHexDigit))
            throw new BusinessRuleException("The evidence requires a valid SHA-256 digest.");
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The evidence bytes do not match the supplied SHA-256 digest.");
        if (content.Length <= 5 || !content.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new BusinessRuleException("The evidence must be a PDF copy of the filed accounts.");
    }

    private static object HistoricAnchorAuditSnapshot(SizeClassification sc) => new
    {
        sc.InputBasis,
        sc.Turnover,
        sc.BalanceSheetTotal,
        sc.AvgEmployees,
        sc.ThresholdElectionEffectiveFrom,
        sc.RawCurrentClass,
        sc.CalculatedClass,
        sc.DecisionInputFingerprintSha256,
        sc.HistoricSourceReference,
        sc.HistoricEvidenceSha256,
        sc.HistoricAttestedBy,
        sc.HistoricAttestedRole,
        sc.HistoricAttestedAt,
        sc.HistoricAttestedEffectiveClass,
        sc.HistoricRetiredAt,
        sc.HistoricRetiredBy,
        sc.HistoricRetiredReason
    };

    public static object DecisionAuditSnapshot(SizeClassification sc) =>
        ClassificationAuditSnapshot(sc, null, null, null, null, null);

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
        // A historic anchor is a chain root: it never hashes a predecessor.
        if (IsHistoricAnchor(sc))
            priorPeriod = null;
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
            // Appended only for a non-Computed prior, so every Computed fingerprint is unchanged.
            if (IsHistoricAnchor(prior))
            {
                canonicalParts.AddRange([
                    $"priorInputBasis={prior.InputBasis}",
                    $"priorAttestedEffectiveClass={prior.HistoricAttestedEffectiveClass?.ToString() ?? "none"}"
                ]);
            }
        }
        else
        {
            canonicalParts.Add("priorPeriod=none");
        }
        // Appended only for a non-Computed row, so every Computed fingerprint is unchanged. Free text
        // is JSON-encoded so a value can never forge another canonical line.
        if (IsHistoricAnchor(sc))
        {
            canonicalParts.AddRange([
                "chainRoot=historic-anchor",
                $"inputBasis={sc.InputBasis}",
                $"historicSourceReference={JsonSerializer.Serialize(sc.HistoricSourceReference)}",
                $"historicEvidenceSha256={sc.HistoricEvidenceSha256?.ToLowerInvariant() ?? "none"}",
                $"historicAttestedBy={JsonSerializer.Serialize(sc.HistoricAttestedBy)}",
                $"historicAttestedRole={JsonSerializer.Serialize(sc.HistoricAttestedRole)}",
                $"historicAttestedEffectiveClass={sc.HistoricAttestedEffectiveClass?.ToString() ?? "none"}"
            ]);
        }
        var canonical = string.Join("\n", canonicalParts);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public enum DecisionChainFailureKind
    {
        Missing,
        InvalidInputs,
        ElectionMismatch,
        FirstYearMissing,
        FirstYearRepeated,
        Undecided,
        Stale,
        OverrideNotCurrent,
        HistoricAnchorEvidenceNotCurrent,
        HistoricAnchorTarget
    }

    /// <summary>The earliest period at which the retained decision chain fails, and why.</summary>
    public sealed record DecisionChainFailure(AccountingPeriod Period, DecisionChainFailureKind Kind, string Message);

    /// <summary>
    /// The chain periods in ascending order (the last is the immediately preceding period when the
    /// target is excluded) and the first failure, if any.
    /// </summary>
    public sealed record DecisionChainEvaluation(IReadOnlyList<AccountingPeriod> Periods, DecisionChainFailure? FirstFailure);

    public static async Task EnsureDecisionChainCurrentAsync(
        AccountsDbContext context,
        AccountingPeriod targetPeriod,
        DateOnly companyElection,
        bool includeTarget)
    {
        var evaluation = await EvaluateDecisionChainAsync(context, targetPeriod, companyElection, includeTarget);
        if (evaluation.FirstFailure is { } failure)
            throw new BusinessRuleException(failure.Message);
    }

    /// <summary>
    /// Non-throwing evaluation of the retained statutory decision chain (Companies Act 2014, s.280A).
    /// <see cref="EnsureDecisionChainCurrentAsync"/> throws the returned failure's message unchanged.
    /// </summary>
    public static async Task<DecisionChainEvaluation> EvaluateDecisionChainAsync(
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

        // A historic anchor is never itself a decision target (classification, regime or release).
        if (includeTarget
            && periods.FirstOrDefault(p => p.Id == targetPeriod.Id) is { } target
            && IsHistoricAnchor(target.SizeClassification))
        {
            return new DecisionChainEvaluation(
                periods,
                new DecisionChainFailure(target, DecisionChainFailureKind.HistoricAnchorTarget, HistoricAnchorNotATargetMessage));
        }

        // The chain starts at its latest root: the first year, or the latest historic anchor before
        // the target. Earlier periods are not part of the s.280A comparison any more.
        var rootIndex = periods.FindLastIndex(p => IsHistoricAnchor(p.SizeClassification));
        if (rootIndex > 0)
        {
            // The election rule still binds every decided year beginning on or after 2023-01-01,
            // including those before the root.
            foreach (var earlier in periods.Take(rootIndex))
            {
                if (earlier.SizeClassification is { InputBasis: SizeInputBasis.Computed } decided
                    && !string.IsNullOrWhiteSpace(decided.DecisionInputFingerprintSha256)
                    && earlier.PeriodStart >= new DateOnly(2023, 1, 1)
                    && (decided.ThresholdElectionEffectiveFrom ?? new DateOnly(2024, 1, 1)) != companyElection)
                {
                    return new DecisionChainEvaluation(
                        periods,
                        new DecisionChainFailure(
                            earlier,
                            DecisionChainFailureKind.ElectionMismatch,
                            "The 2024 threshold adjustment election must be applied consistently to every financial year of the company beginning on or after 2023-01-01."));
                }
            }
            periods = periods.GetRange(rootIndex, periods.Count - rootIndex);
        }

        AccountingPeriod? prior = null;
        foreach (var candidate in periods)
        {
            var failure = EvaluateChainLink(candidate, prior, companyElection);
            if (failure is not null)
                return new DecisionChainEvaluation(periods, failure);
            prior = candidate;
        }
        return new DecisionChainEvaluation(periods, null);
    }

    private static DecisionChainFailure? EvaluateChainLink(
        AccountingPeriod candidate,
        AccountingPeriod? prior,
        DateOnly companyElection)
    {
        DecisionChainFailure Fail(DecisionChainFailureKind kind, string message) => new(candidate, kind, message);

        var classification = candidate.SizeClassification;
        if (classification is null)
        {
            return Fail(
                DecisionChainFailureKind.Missing,
                $"The retained statutory decision chain is incomplete at period {candidate.Id}; classify every preceding period first.");
        }
        // Retired historic figures are evidence only: never a decision the chain can rely on.
        if (classification.InputBasis == SizeInputBasis.RetiredHistoricAnchor)
        {
            return Fail(
                DecisionChainFailureKind.Undecided,
                $"The statutory size decision for preceding period {candidate.Id} is stale; re-run it before relying on consecutive-year qualification.");
        }
        try
        {
            ValidateInputs(
                classification.Turnover,
                classification.BalanceSheetTotal,
                classification.AvgEmployees,
                classification.ThresholdElectionEffectiveFrom);
        }
        catch (BusinessRuleException invalid)
        {
            return Fail(DecisionChainFailureKind.InvalidInputs, invalid.Message);
        }
        var candidateElection = classification.ThresholdElectionEffectiveFrom ?? new DateOnly(2024, 1, 1);
        if (candidate.PeriodStart >= new DateOnly(2023, 1, 1) && candidateElection != companyElection)
        {
            return Fail(
                DecisionChainFailureKind.ElectionMismatch,
                "The 2024 threshold adjustment election must be applied consistently to every financial year of the company beginning on or after 2023-01-01.");
        }
        var anchorRoot = prior is null && IsHistoricAnchor(classification);
        if (!anchorRoot && candidate.IsFirstYear != (prior is null))
        {
            return Fail(
                candidate.IsFirstYear ? DecisionChainFailureKind.FirstYearRepeated : DecisionChainFailureKind.FirstYearMissing,
                "The retained statutory decision chain does not have one deterministic first financial year.");
        }
        var expected = ComputeInputFingerprint(candidate, classification, candidateElection, prior);
        if (string.IsNullOrWhiteSpace(classification.DecisionInputFingerprintSha256)
            || !string.Equals(classification.DecisionInputFingerprintSha256, expected, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                string.IsNullOrWhiteSpace(classification.DecisionInputFingerprintSha256)
                    ? DecisionChainFailureKind.Undecided
                    : DecisionChainFailureKind.Stale,
                $"The statutory size decision for preceding period {candidate.Id} is stale; re-run it before relying on consecutive-year qualification.");
        }
        if (classification.OverrideClass is not null && !HasCurrentOverrideEvidence(classification))
        {
            return Fail(
                DecisionChainFailureKind.OverrideNotCurrent,
                $"The statutory classification override for preceding period {candidate.Id} is stale or lacks retained authority and evidence.");
        }
        if (!HasCurrentHistoricAnchorEvidence(classification))
        {
            return Fail(
                DecisionChainFailureKind.HistoricAnchorEvidenceNotCurrent,
                $"The historic figures recorded for preceding period {candidate.Id} lack current retained evidence or authority; record them again before relying on them.");
        }
        return null;
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
