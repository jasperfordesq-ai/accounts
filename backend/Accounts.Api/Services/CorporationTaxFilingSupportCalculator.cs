using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

/// <summary>
/// Deterministic working-paper calculations for Corporation Tax filing and preliminary-tax support.
/// This calculator does not create a CT1, submit to ROS, or replace a Revenue assessment or
/// qualified-accountant review.
/// </summary>
public static class CorporationTaxFilingSupportCalculator
{
    public const string OutputKind = "corporation-tax-filing-support-not-ct1-return";
    public const decimal DailyLatePaymentInterestRate = 0.000219m;
    public const decimal LargeCompanyThreshold = 200_000m;

    public enum PaymentKind
    {
        PreliminaryFirst,
        PreliminarySecondOrSingle,
        Balance,
        InterestOrSurcharge,
        Other
    }

    public enum CompanyPaymentClass
    {
        Unresolved,
        StartUpExempt,
        Small,
        Large
    }

    public record Payment(
        DateOnly PaymentDate,
        decimal Amount,
        PaymentKind Kind,
        string EvidenceReference,
        string? ExternalPaymentReference = null);

    public record Facts(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        bool IsFirstAccountingPeriod,
        decimal CurrentCorporationTax,
        decimal CurrentSection239IncomeTax,
        decimal? PriorCorporationTaxExcludingSurchargeAndSection239,
        decimal? PriorSection239IncomeTax,
        DateOnly? PriorPeriodStart,
        DateOnly? PriorPeriodEnd,
        bool PriorLiabilityEvidenceComplete,
        bool ReviewEvidenceRetained,
        IReadOnlyList<Payment> Payments,
        DateOnly? FiledDate,
        DateOnly AsOfDate,
        bool CurrentTaxChargeSupported,
        IReadOnlyList<string> CurrentTaxBlockers,
        bool HasInterestLimitationRule,
        bool UsesNotionalGroupPaymentAllocation,
        bool HasDirtOrOtherWithholdingCredits,
        bool HasOtherPreliminaryTaxAdjustments,
        bool HasMandatoryElectronicFilingExemption,
        decimal CapitalAllowancesClaimed,
        decimal TradingLossReliefUsed);

    public record SafeHarbourBasis(string Code, string Label, decimal Amount, bool Available);

    public record DueItem(
        string Code,
        string Label,
        DateOnly DueDate,
        decimal CumulativeTaxRequired,
        decimal PaidByDueDate,
        decimal ShortfallAtDueDate,
        string Basis);

    public record InterestSegment(
        DateOnly DueDate,
        DateOnly ThroughDate,
        decimal Principal,
        int InclusiveDays,
        decimal Interest,
        string Basis);

    public record LateFilingExposure(
        bool IsLate,
        DateOnly ReturnDueDate,
        DateOnly ExposureDate,
        int DaysLate,
        decimal Rate,
        decimal Cap,
        decimal EstimatedSurcharge,
        bool ReliefRestrictionExposure,
        string Detail);

    public record Result(
        string OutputKind,
        bool IsCompleteCt1Return,
        bool DirectRosSubmissionSupported,
        CompanyPaymentClass CompanyClass,
        string CompanyClassLabel,
        bool IsShortAccountingPeriod,
        decimal CurrentTotalTaxForPaymentSupport,
        decimal? AnnualisedPriorCorporationTax,
        DateOnly PreliminaryFirstDueDate,
        DateOnly PreliminarySecondOrSingleDueDate,
        DateOnly ReturnAndBalanceDueDate,
        IReadOnlyList<SafeHarbourBasis> SafeHarbourBases,
        decimal PreliminaryTaxSafeHarbourAmount,
        bool? SafeHarbourMet,
        IReadOnlyList<DueItem> DueItems,
        decimal PreliminaryTaxPaymentsRecorded,
        decimal TaxPaymentsRecorded,
        decimal EstimatedLatePaymentInterest,
        IReadOnlyList<InterestSegment> InterestSegments,
        LateFilingExposure LateFiling,
        bool ManualReviewRequired,
        bool FilingSupportReady,
        IReadOnlyList<string> BlockingReasons,
        IReadOnlyList<string> Warnings,
        string CalculationSha256);

    public static Result Calculate(Facts facts)
    {
        var blockers = new List<string>();
        var warnings = new List<string>
        {
            "Working-paper estimate only: Revenue may calculate interest, surcharges and relief restrictions differently after review or intervention."
        };

        if (facts.PeriodEnd < facts.PeriodStart)
            blockers.Add("Accounting-period end precedes its start.");
        if (facts.AsOfDate < facts.PeriodStart)
            blockers.Add("The tracker assessment date cannot precede the accounting period.");
        if (facts.FiledDate is not null && facts.FiledDate > facts.AsOfDate)
            blockers.Add("The recorded CT1 filing date is after the tracker assessment date.");
        if (facts.FiledDate is not null && facts.FiledDate < facts.PeriodEnd)
            blockers.Add("The recorded CT1 filing date cannot precede the accounting-period end.");
        var twelveMonthEnd = facts.PeriodStart.AddMonths(12).AddDays(-1);
        if (facts.PeriodEnd > twelveMonthEnd)
            blockers.Add("A Corporation Tax accounting period cannot be represented as one support worksheet when it exceeds 12 months; split-period professional review is required.");
        if (facts.CurrentCorporationTax < 0 || facts.CurrentSection239IncomeTax < 0)
            blockers.Add("Current Corporation Tax and section 239 Income Tax inputs cannot be negative.");
        if (!facts.CurrentTaxChargeSupported)
            blockers.AddRange(facts.CurrentTaxBlockers.Select(reason => $"Underlying tax support is blocked: {reason}"));
        if (!facts.ReviewEvidenceRetained)
            blockers.Add("No retained preliminary-tax basis review exists for this accounting period.");
        if (facts.HasInterestLimitationRule)
            blockers.Add("Interest Limitation Rule preliminary-tax top-up treatment is outside the automated scope.");
        if (facts.UsesNotionalGroupPaymentAllocation)
            blockers.Add("Notional allocation of group preliminary-tax payments requires Revenue approval and manual handoff.");
        if (facts.HasDirtOrOtherWithholdingCredits)
            blockers.Add("DIRT or other withholding-tax set-offs require a manually evidenced preliminary-tax calculation.");
        if (facts.HasOtherPreliminaryTaxAdjustments)
            blockers.Add("Other preliminary-tax adjustments or special payment rules are outside the automated scope.");
        if (facts.HasMandatoryElectronicFilingExemption)
            blockers.Add("This calculator supports ROS electronic due dates only; a mandatory e-filing exemption requires manual 21-day deadline review.");

        var payments = facts.Payments
            .OrderBy(payment => payment.PaymentDate)
            .ThenBy(payment => payment.Kind)
            .ToList();
        foreach (var payment in payments)
        {
            if (payment.Amount <= 0)
                blockers.Add("Every Corporation Tax payment record must have a positive amount.");
            if ((payment.EvidenceReference?.Trim().Length ?? 0) < 20)
                blockers.Add($"Payment dated {payment.PaymentDate:yyyy-MM-dd} lacks a retained evidence reference of at least 20 characters.");
            if (payment.PaymentDate > facts.AsOfDate)
                blockers.Add($"Payment dated {payment.PaymentDate:yyyy-MM-dd} is after the tracker assessment date and cannot be treated as paid.");
            if (payment.PaymentDate < facts.PeriodStart)
                blockers.Add($"Payment dated {payment.PaymentDate:yyyy-MM-dd} precedes the accounting period and requires manual allocation review.");
            if (payment.Kind == PaymentKind.Other)
                blockers.Add($"Payment dated {payment.PaymentDate:yyyy-MM-dd} has an unclassified Corporation Tax purpose.");
        }

        var currentCorporationTax = Math.Max(0m, facts.CurrentCorporationTax);
        var currentSection239 = Math.Max(0m, facts.CurrentSection239IncomeTax);
        var currentTotal = currentCorporationTax + currentSection239;
        var isShort = facts.PeriodEnd < twelveMonthEnd;
        var prelimFirstDue = PreliminaryFirstDueDate(facts.PeriodStart);
        var prelimSecondDue = PreliminarySecondOrSingleDueDate(facts.PeriodEnd);
        var returnDue = ReturnAndBalanceDueDate(facts.PeriodEnd);

        decimal? annualisedPriorCt = null;
        decimal? priorTotalForSafeHarbour = null;
        CompanyPaymentClass companyClass;
        if (facts.IsFirstAccountingPeriod)
        {
            companyClass = currentTotal < LargeCompanyThreshold
                ? CompanyPaymentClass.StartUpExempt
                : CompanyPaymentClass.Large;
            if (currentTotal == LargeCompanyThreshold)
            {
                blockers.Add("Revenue guidance is inconsistent at the exact EUR 200,000 start-up boundary; obtain professional confirmation of the preliminary-tax treatment.");
            }
        }
        else if (facts.PriorCorporationTaxExcludingSurchargeAndSection239 is null
            || facts.PriorSection239IncomeTax is null
            || facts.PriorPeriodStart is null
            || facts.PriorPeriodEnd is null)
        {
            companyClass = CompanyPaymentClass.Unresolved;
            blockers.Add("The preceding accounting period and its Corporation Tax/section 239 liabilities are required for preliminary-tax classification.");
        }
        else
        {
            if (facts.PriorCorporationTaxExcludingSurchargeAndSection239 < 0 || facts.PriorSection239IncomeTax < 0)
                blockers.Add("Preceding-period Corporation Tax and section 239 Income Tax cannot be negative.");
            if (facts.PriorPeriodEnd.Value < facts.PriorPeriodStart.Value)
                blockers.Add("The preceding accounting-period end precedes its start.");
            if (facts.PriorPeriodEnd.Value >= facts.PeriodStart)
                blockers.Add("The preceding accounting period overlaps the current accounting period.");
            if (facts.PriorPeriodEnd.Value > facts.PriorPeriodStart.Value.AddMonths(12).AddDays(-1))
                blockers.Add("The preceding Corporation Tax accounting period exceeds 12 months and cannot be annualised automatically.");
            if (!facts.PriorLiabilityEvidenceComplete)
                blockers.Add("Preceding-period liability inputs lack retained CT1 or Revenue evidence.");

            annualisedPriorCt = AnnualisePriorLiability(
                Math.Max(0m, facts.PriorCorporationTaxExcludingSurchargeAndSection239.Value),
                facts.PriorPeriodStart.Value,
                facts.PriorPeriodEnd.Value);
            priorTotalForSafeHarbour = AnnualisePriorLiability(
                Math.Max(0m, facts.PriorCorporationTaxExcludingSurchargeAndSection239.Value)
                    + Math.Max(0m, facts.PriorSection239IncomeTax.Value),
                facts.PriorPeriodStart.Value,
                facts.PriorPeriodEnd.Value);
            companyClass = annualisedPriorCt > LargeCompanyThreshold
                ? CompanyPaymentClass.Large
                : CompanyPaymentClass.Small;
        }

        var bases = new List<SafeHarbourBasis>();
        var taxPayments = payments
            .Where(payment => payment.Kind != PaymentKind.InterestOrSurcharge
                && payment.Amount > 0
                && payment.PaymentDate <= facts.AsOfDate)
            .ToList();
        var safeHarbourAmount = 0m;
        bool? safeHarbourMet = null;
        var safeSchedule = new List<(DateOnly DueDate, decimal CumulativeAmount, string Code, string Label, string Basis)>();
        var acceleratedSchedule = new List<(DateOnly DueDate, decimal CumulativeAmount, string Code, string Label, string Basis)>();

        switch (companyClass)
        {
            case CompanyPaymentClass.StartUpExempt:
                bases.Add(new("startup-exemption", "Qualifying first-period start-up exemption", 0m, true));
                safeSchedule.Add((returnDue, currentTotal, "return-balance", "CT1 return and balance", "Full first-period liability due with the CT1 support handoff."));
                safeHarbourMet = true;
                break;

            case CompanyPaymentClass.Small:
            {
                var currentBasis = RoundMoney(currentTotal * 0.90m);
                var priorBasis = priorTotalForSafeHarbour ?? 0m;
                bases.Add(new("90-current", "90% of current-period CT plus section 239 Income Tax", currentBasis, true));
                bases.Add(new(
                    "100-prior",
                    "100% of annualised preceding-period CT plus section 239 Income Tax",
                    priorBasis,
                    !isShort));
                safeHarbourAmount = isShort ? currentBasis : Math.Min(currentBasis, priorBasis);
                safeSchedule.Add((prelimSecondDue, safeHarbourAmount, "preliminary-single", "Preliminary Tax single instalment", isShort
                    ? "Short accounting period: 90% current-period basis only."
                    : "Lower supported safe harbour: 90% current period or 100% annualised preceding period."));
                safeSchedule.Add((returnDue, currentTotal, "return-balance", "CT1 return and balance", "Balance of current-period tax."));
                acceleratedSchedule.Add((prelimSecondDue, safeHarbourAmount, "accelerated-safe-harbour", "Late or underpaid safe-harbour tranche", "Safe harbour was not paid in full and on time; full current liability is exposed from the preliminary-tax date."));
                acceleratedSchedule.Add((prelimSecondDue, currentTotal, "accelerated-balance", "Accelerated remaining current-period liability", "Safe harbour was not paid in full and on time; full current liability is exposed from the preliminary-tax date."));
                if (facts.AsOfDate >= prelimSecondDue)
                    safeHarbourMet = PaidBy(taxPayments, prelimSecondDue) + 0.005m >= safeHarbourAmount;
                break;
            }

            case CompanyPaymentClass.Large:
            {
                var currentFirst = RoundMoney(currentTotal * 0.45m);
                var priorFirst = priorTotalForSafeHarbour is null ? currentFirst : RoundMoney(priorTotalForSafeHarbour.Value * 0.50m);
                var firstRequired = facts.IsFirstAccountingPeriod ? currentFirst : Math.Min(currentFirst, priorFirst);
                var secondRequired = RoundMoney(currentTotal * 0.90m);
                bases.Add(new("45-current", "45% of current-period CT plus section 239 Income Tax", currentFirst, true));
                bases.Add(new("50-prior", "50% of annualised preceding-period CT plus section 239 Income Tax", priorFirst, !facts.IsFirstAccountingPeriod));
                bases.Add(new("90-current", "90% cumulative current-period CT plus section 239 Income Tax", secondRequired, true));
                safeHarbourAmount = secondRequired;
                if (IsLessThanSevenMonths(facts.PeriodStart, facts.PeriodEnd))
                {
                    safeSchedule.Add((prelimSecondDue, secondRequired, "preliminary-short", "Preliminary Tax short-period instalment", "Accounting period below seven months: 90% current-period liability in one instalment."));
                    acceleratedSchedule.Add((prelimSecondDue, currentTotal, "accelerated-preliminary", "Accelerated tax after preliminary-tax default", "Short-period 90% obligation was not paid in full and on time."));
                    if (facts.AsOfDate >= prelimSecondDue)
                        safeHarbourMet = PaidBy(taxPayments, prelimSecondDue) + 0.005m >= secondRequired;
                }
                else
                {
                    safeSchedule.Add((prelimFirstDue, firstRequired, "preliminary-first", "Preliminary Tax first instalment", facts.IsFirstAccountingPeriod
                        ? "First period above the start-up threshold: 45% current-period basis."
                        : "Lower supported first-instalment basis: 45% current period or 50% annualised preceding period."));
                    safeSchedule.Add((prelimSecondDue, secondRequired, "preliminary-second", "Preliminary Tax second instalment", "Cumulative payments to 90% of current-period liability."));
                    acceleratedSchedule.Add((prelimFirstDue, currentFirst, "accelerated-first", "Accelerated first instalment", "45% of current-period liability after preliminary-tax default."));
                    acceleratedSchedule.Add((prelimSecondDue, currentTotal, "accelerated-second", "Accelerated final preliminary instalment", "Full current-period liability exposed from the second preliminary-tax date after default."));
                    if (facts.AsOfDate >= prelimFirstDue)
                    {
                        var firstMet = PaidBy(taxPayments, prelimFirstDue) + 0.005m >= firstRequired;
                        safeHarbourMet = facts.AsOfDate < prelimSecondDue
                            ? firstMet
                            : firstMet && PaidBy(taxPayments, prelimSecondDue) + 0.005m >= secondRequired;
                    }
                }
                safeSchedule.Add((returnDue, currentTotal, "return-balance", "CT1 return and balance", "Balance of current-period tax."));
                break;
            }

            default:
                bases.Add(new("unresolved", "Preliminary-tax basis unresolved", 0m, false));
                break;
        }

        var useAcceleratedSchedule = safeHarbourMet == false && acceleratedSchedule.Count > 0;
        var operativeSchedule = useAcceleratedSchedule ? acceleratedSchedule : safeSchedule;
        var dueItems = operativeSchedule
            .OrderBy(item => item.DueDate)
            .Select(item => new DueItem(
                item.Code,
                item.Label,
                item.DueDate,
                item.CumulativeAmount,
                PaidBy(taxPayments, item.DueDate),
                Math.Max(0m, item.CumulativeAmount - PaidBy(taxPayments, item.DueDate)),
                item.Basis))
            .ToList();

        var interestSegments = CalculateInterestSegments(
            operativeSchedule.Select(item => (item.DueDate, item.CumulativeAmount, item.Basis)).ToList(),
            taxPayments,
            facts.AsOfDate,
            paymentDayInclusive: companyClass != CompanyPaymentClass.Large);
        var interest = RoundMoney(interestSegments.Sum(segment => segment.Interest));
        var lateFiling = CalculateLateFilingExposure(
            returnDue,
            facts.FiledDate,
            facts.AsOfDate,
            currentTotal,
            facts.CapitalAllowancesClaimed > 0 || facts.TradingLossReliefUsed > 0);
        if (lateFiling.IsLate)
        {
            warnings.Add(lateFiling.Detail);
            blockers.Add("Late CT1 filing exposure requires professional surcharge and relief-restriction review before the filing support can be closed.");
        }
        if (lateFiling.ReliefRestrictionExposure)
            warnings.Add("Late CT1 filing may restrict excess capital allowances, loss relief or group relief; quantify the restriction manually.");
        if (interest > 0)
            warnings.Add($"Indicative statutory-interest exposure through {facts.AsOfDate:yyyy-MM-dd}: EUR {interest:N2}.");
        if (safeHarbourMet == false)
            blockers.Add("Preliminary-tax safe harbour was not met in full and on time; accelerated-liability and statutory-interest exposure require professional review.");

        blockers = blockers.Distinct(StringComparer.Ordinal).ToList();
        warnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        var fingerprint = Fingerprint(facts, companyClass, safeHarbourAmount, dueItems, lateFiling);
        return new Result(
            OutputKind,
            IsCompleteCt1Return: false,
            DirectRosSubmissionSupported: false,
            companyClass,
            CompanyClassLabel(companyClass),
            isShort,
            currentTotal,
            annualisedPriorCt,
            prelimFirstDue,
            prelimSecondDue,
            returnDue,
            bases,
            safeHarbourAmount,
            safeHarbourMet,
            dueItems,
            taxPayments
                .Where(payment => payment.Kind is PaymentKind.PreliminaryFirst or PaymentKind.PreliminarySecondOrSingle)
                .Sum(payment => payment.Amount),
            taxPayments.Sum(payment => payment.Amount),
            interest,
            interestSegments,
            lateFiling,
            ManualReviewRequired: true,
            FilingSupportReady: blockers.Count == 0,
            blockers,
            warnings,
            fingerprint);
    }

    public static DateOnly PreliminaryFirstDueDate(DateOnly periodStart)
    {
        var lastDayWithinSixMonths = periodStart.AddMonths(6).AddDays(-1);
        return EarlierOf(lastDayWithinSixMonths, Day23(lastDayWithinSixMonths));
    }

    public static DateOnly PreliminarySecondOrSingleDueDate(DateOnly periodEnd)
    {
        var thirtyOneDaysBeforeEnd = periodEnd.AddDays(-31);
        return EarlierOf(thirtyOneDaysBeforeEnd, Day23(thirtyOneDaysBeforeEnd));
    }

    public static DateOnly ReturnAndBalanceDueDate(DateOnly periodEnd)
    {
        var nineMonthsAfterEnd = periodEnd.AddMonths(9);
        return EarlierOf(nineMonthsAfterEnd, Day23(nineMonthsAfterEnd));
    }

    private static decimal AnnualisePriorLiability(decimal liability, DateOnly start, DateOnly end)
    {
        if (end < start)
            return 0m;
        var twelveMonthEnd = start.AddMonths(12).AddDays(-1);
        if (end >= twelveMonthEnd)
            return liability;
        var days = end.DayNumber - start.DayNumber + 1;
        return days <= 0 ? 0m : RoundMoney(liability * 365m / days);
    }

    private static bool IsLessThanSevenMonths(DateOnly start, DateOnly end) =>
        end < start.AddMonths(7).AddDays(-1);

    private static decimal PaidBy(IEnumerable<Payment> payments, DateOnly date) =>
        payments.Where(payment => payment.PaymentDate <= date).Sum(payment => payment.Amount);

    private static List<InterestSegment> CalculateInterestSegments(
        IReadOnlyList<(DateOnly DueDate, decimal CumulativeAmount, string Basis)> cumulativeSchedule,
        IReadOnlyList<Payment> payments,
        DateOnly asOf,
        bool paymentDayInclusive)
    {
        var result = new List<InterestSegment>();
        var paymentBalances = payments
            .Where(payment => payment.PaymentDate <= asOf)
            .Select(payment => new MutablePayment(payment.PaymentDate, payment.Amount))
            .ToList();
        decimal priorCumulative = 0m;

        foreach (var item in cumulativeSchedule.OrderBy(item => item.DueDate))
        {
            var tranche = Math.Max(0m, item.CumulativeAmount - priorCumulative);
            priorCumulative = Math.Max(priorCumulative, item.CumulativeAmount);
            if (tranche <= 0 || item.DueDate > asOf)
                continue;

            foreach (var payment in paymentBalances.Where(payment => payment.Date <= item.DueDate && payment.Remaining > 0))
            {
                var applied = Math.Min(tranche, payment.Remaining);
                tranche -= applied;
                payment.Remaining -= applied;
                if (tranche <= 0)
                    break;
            }
            if (tranche <= 0)
                continue;

            var cursor = item.DueDate;
            foreach (var payment in paymentBalances.Where(payment => payment.Date > item.DueDate && payment.Remaining > 0).OrderBy(payment => payment.Date))
            {
                if (payment.Date > asOf)
                    break;
                var applied = Math.Min(tranche, payment.Remaining);
                if (applied <= 0)
                    continue;
                // Revenue's published small-company examples count the payment date, while
                // its large-company accelerated-schedule table closes each interest period
                // on the day before payment. Preserve both published conventions explicitly.
                var days = payment.Date.DayNumber - cursor.DayNumber + (paymentDayInclusive ? 1 : 0);
                if (days > 0)
                {
                    result.Add(new InterestSegment(
                        item.DueDate,
                        paymentDayInclusive ? payment.Date : payment.Date.AddDays(-1),
                        tranche,
                        days,
                        RoundMoney(tranche * DailyLatePaymentInterestRate * days),
                        item.Basis));
                }
                tranche -= applied;
                payment.Remaining -= applied;
                cursor = payment.Date.AddDays(1);
                if (tranche <= 0)
                    break;
            }

            if (tranche > 0 && cursor <= asOf)
            {
                var days = asOf.DayNumber - cursor.DayNumber + 1;
                result.Add(new InterestSegment(
                    item.DueDate,
                    asOf,
                    tranche,
                    days,
                    RoundMoney(tranche * DailyLatePaymentInterestRate * days),
                    item.Basis));
            }
        }

        return result
            .GroupBy(segment => new { segment.DueDate, segment.ThroughDate, segment.InclusiveDays, segment.Basis })
            .Select(group => new InterestSegment(
                group.Key.DueDate,
                group.Key.ThroughDate,
                group.Sum(segment => segment.Principal),
                group.Key.InclusiveDays,
                RoundMoney(group.Sum(segment => segment.Interest)),
                group.Key.Basis))
            .OrderBy(segment => segment.DueDate)
            .ThenBy(segment => segment.ThroughDate)
            .ToList();
    }

    private static LateFilingExposure CalculateLateFilingExposure(
        DateOnly returnDue,
        DateOnly? filedDate,
        DateOnly asOf,
        decimal taxDue,
        bool reliefClaimsPresent)
    {
        var exposureDate = filedDate ?? asOf;
        var isLate = exposureDate > returnDue;
        if (!isLate)
        {
            return new LateFilingExposure(
                false,
                returnDue,
                exposureDate,
                0,
                0m,
                0m,
                0m,
                false,
                "No late-filing surcharge exposure at the assessment date.");
        }

        var withinTwoMonths = exposureDate <= returnDue.AddMonths(2);
        var rate = withinTwoMonths ? 0.05m : 0.10m;
        var cap = withinTwoMonths ? 12_695m : 63_485m;
        var surcharge = RoundMoney(Math.Min(taxDue * rate, cap));
        var daysLate = exposureDate.DayNumber - returnDue.DayNumber;
        return new LateFilingExposure(
            true,
            returnDue,
            exposureDate,
            daysLate,
            rate,
            cap,
            surcharge,
            reliefClaimsPresent,
            $"Indicative late-return surcharge exposure is {rate:P0} of supported tax, capped at EUR {cap:N0}; Revenue confirmation is required.");
    }

    private static string Fingerprint(
        Facts facts,
        CompanyPaymentClass companyClass,
        decimal safeHarbour,
        IEnumerable<DueItem> dueItems,
        LateFilingExposure lateFiling)
    {
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        var rows = new List<string>
        {
            $"period:{facts.PeriodStart:yyyy-MM-dd}|{facts.PeriodEnd:yyyy-MM-dd}|first={facts.IsFirstAccountingPeriod}",
            $"current:{Money(facts.CurrentCorporationTax)}|s239={Money(facts.CurrentSection239IncomeTax)}|class={companyClass}|safe={Money(safeHarbour)}",
            $"prior:{facts.PriorCorporationTaxExcludingSurchargeAndSection239?.ToString("0.00", CultureInfo.InvariantCulture) ?? "null"}|s239={facts.PriorSection239IncomeTax?.ToString("0.00", CultureInfo.InvariantCulture) ?? "null"}|{facts.PriorPeriodStart:yyyy-MM-dd}|{facts.PriorPeriodEnd:yyyy-MM-dd}|evidence={facts.PriorLiabilityEvidenceComplete}|review={facts.ReviewEvidenceRetained}",
            $"flags:{facts.HasInterestLimitationRule}|{facts.UsesNotionalGroupPaymentAllocation}|{facts.HasDirtOrOtherWithholdingCredits}|{facts.HasOtherPreliminaryTaxAdjustments}|{facts.HasMandatoryElectronicFilingExemption}",
            $"support:{facts.CurrentTaxChargeSupported}|capital-allowances={Money(facts.CapitalAllowancesClaimed)}|trading-loss-relief={Money(facts.TradingLossReliefUsed)}",
            $"filed:{facts.FiledDate:yyyy-MM-dd}|asof:{facts.AsOfDate:yyyy-MM-dd}|late={Money(lateFiling.EstimatedSurcharge)}|relief-restriction={lateFiling.ReliefRestrictionExposure}"
        };
        rows.AddRange(facts.CurrentTaxBlockers.Order(StringComparer.Ordinal).Select(blocker => $"tax-blocker:{blocker}"));
        rows.AddRange(facts.Payments
            .OrderBy(payment => payment.PaymentDate)
            .ThenBy(payment => payment.Kind)
            .Select(payment => $"payment:{payment.PaymentDate:yyyy-MM-dd}|{Money(payment.Amount)}|{payment.Kind}|{payment.EvidenceReference}|{payment.ExternalPaymentReference}"));
        rows.AddRange(dueItems.Select(item => $"due:{item.Code}|{item.DueDate:yyyy-MM-dd}|{Money(item.CumulativeTaxRequired)}|{Money(item.PaidByDueDate)}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }

    private static DateOnly Day23(DateOnly date) => new(date.Year, date.Month, 23);

    private static DateOnly EarlierOf(DateOnly first, DateOnly second) => first <= second ? first : second;

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string CompanyClassLabel(CompanyPaymentClass value) => value switch
    {
        CompanyPaymentClass.StartUpExempt => "Qualifying start-up / no preliminary tax",
        CompanyPaymentClass.Small => "Small company preliminary-tax rules",
        CompanyPaymentClass.Large => "Large company preliminary-tax rules",
        _ => "Unresolved preliminary-tax class"
    };

    private sealed class MutablePayment(DateOnly date, decimal remaining)
    {
        public DateOnly Date { get; } = date;
        public decimal Remaining { get; set; } = remaining;
    }
}
