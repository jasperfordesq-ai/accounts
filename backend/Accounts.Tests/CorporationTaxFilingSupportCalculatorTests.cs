using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxFilingSupportCalculatorTests
{
    [Fact]
    public void RevenueWorkedExamples_UseTheEarlierOfCalendarAnchorAndRosDay23()
    {
        Assert.Equal(
            new DateOnly(2022, 6, 23),
            CorporationTaxFilingSupportCalculator.PreliminaryFirstDueDate(new DateOnly(2022, 1, 1)));
        Assert.Equal(
            new DateOnly(2022, 11, 23),
            CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(new DateOnly(2022, 12, 31)));

        Assert.Equal(
            new DateOnly(2022, 7, 4),
            CorporationTaxFilingSupportCalculator.PreliminaryFirstDueDate(new DateOnly(2022, 1, 5)));
        Assert.Equal(
            new DateOnly(2022, 12, 4),
            CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(new DateOnly(2023, 1, 4)));

        Assert.Equal(
            new DateOnly(2022, 9, 23),
            CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(new DateOnly(2021, 12, 31)));
        Assert.Equal(
            new DateOnly(2024, 9, 5),
            CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(new DateOnly(2023, 12, 5)));
    }

    [Fact]
    public void RevenueSmallCompanyUnderpaymentExample_ReproducesRoundedInterest()
    {
        var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(
            currentCorporationTax: 125_000m,
            priorCorporationTax: 160_000m,
            asOf: new DateOnly(2022, 9, 23),
            filedDate: new DateOnly(2022, 9, 23),
            payments:
            [
                Payment(new DateOnly(2021, 11, 23), 100_000m),
                Payment(new DateOnly(2022, 9, 23), 25_000m, CorporationTaxFilingSupportCalculator.PaymentKind.Balance)
            ]));

        Assert.Equal(CorporationTaxFilingSupportCalculator.CompanyPaymentClass.Small, result.CompanyClass);
        Assert.Equal(112_500m, result.PreliminaryTaxSafeHarbourAmount);
        Assert.False(result.SafeHarbourMet);
        Assert.Equal(125_000m, Assert.Single(result.DueItems, item => item.Code == "accelerated-balance").CumulativeTaxRequired);
        Assert.Equal(1_669.88m, result.EstimatedLatePaymentInterest);
        Assert.Equal(1_670m, decimal.Round(result.EstimatedLatePaymentInterest, 0, MidpointRounding.AwayFromZero));
        var segment = Assert.Single(result.InterestSegments);
        Assert.Equal(305, segment.InclusiveDays);
        Assert.Equal(25_000m, segment.Principal);
    }

    [Fact]
    public void RevenueLatePaymentExample_ChargesLateNinetyPercentAndRemainingBalanceFromPreliminaryDate()
    {
        var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(
            currentCorporationTax: 125_000m,
            priorCorporationTax: 160_000m,
            asOf: new DateOnly(2022, 9, 23),
            filedDate: new DateOnly(2022, 9, 23),
            payments:
            [
                Payment(new DateOnly(2021, 12, 20), 112_500m),
                Payment(new DateOnly(2022, 9, 23), 12_500m, CorporationTaxFilingSupportCalculator.PaymentKind.Balance)
            ]));

        Assert.False(result.SafeHarbourMet);
        Assert.Equal(1_524.79m, result.EstimatedLatePaymentInterest);
        Assert.Contains(result.InterestSegments, segment => segment.Principal == 112_500m && segment.InclusiveDays == 28 && segment.Interest == 689.85m);
        Assert.Contains(result.InterestSegments, segment => segment.Principal == 12_500m && segment.InclusiveDays == 305 && segment.Interest == 834.94m);
    }

    [Fact]
    public void SmallCompany_PrecedingYearSafeHarbourDefersTheBalanceToReturnDate()
    {
        var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(
            currentCorporationTax: 200_000m,
            priorCorporationTax: 100_000m,
            asOf: new DateOnly(2022, 9, 23),
            filedDate: new DateOnly(2022, 9, 23),
            payments:
            [
                Payment(new DateOnly(2021, 11, 23), 100_000m),
                Payment(new DateOnly(2022, 9, 23), 100_000m, CorporationTaxFilingSupportCalculator.PaymentKind.Balance)
            ]));

        Assert.True(result.SafeHarbourMet);
        Assert.Equal(100_000m, result.PreliminaryTaxSafeHarbourAmount);
        Assert.Equal(0m, result.EstimatedLatePaymentInterest);
        Assert.True(result.FilingSupportReady);
    }

    [Fact]
    public void LargeCompany_FirstAndSecondInstalmentsFailClosedToAcceleratedCurrentLiability()
    {
        var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(
            currentCorporationTax: 7_000_000m,
            priorCorporationTax: 6_300_000m,
            asOf: new DateOnly(2023, 9, 23),
            filedDate: new DateOnly(2023, 9, 23),
            payments:
            [
                Payment(new DateOnly(2022, 6, 23), 2_950_000m, CorporationTaxFilingSupportCalculator.PaymentKind.PreliminaryFirst),
                Payment(new DateOnly(2022, 11, 23), 3_350_000m),
                Payment(new DateOnly(2023, 9, 23), 700_000m, CorporationTaxFilingSupportCalculator.PaymentKind.Balance)
            ]));

        Assert.Equal(CorporationTaxFilingSupportCalculator.CompanyPaymentClass.Large, result.CompanyClass);
        Assert.False(result.SafeHarbourMet);
        Assert.Equal(6_300_000m, result.PreliminaryTaxSafeHarbourAmount);
        Assert.Collection(
            result.DueItems,
            first =>
            {
                Assert.Equal("accelerated-first", first.Code);
                Assert.Equal(3_150_000m, first.CumulativeTaxRequired);
            },
            second =>
            {
                Assert.Equal("accelerated-second", second.Code);
                Assert.Equal(7_000_000m, second.CumulativeTaxRequired);
            });
        Assert.False(result.FilingSupportReady);
    }

    [Fact]
    public void QualifyingStartUp_DefersSubTwoHundredThousandLiabilityToReturnDate()
    {
        var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(
            currentCorporationTax: 125_000m,
            priorCorporationTax: null,
            isFirstAccountingPeriod: true,
            asOf: new DateOnly(2022, 9, 23),
            filedDate: new DateOnly(2022, 9, 23),
            payments: [Payment(new DateOnly(2022, 9, 23), 125_000m, CorporationTaxFilingSupportCalculator.PaymentKind.Balance)]));

        Assert.Equal(CorporationTaxFilingSupportCalculator.CompanyPaymentClass.StartUpExempt, result.CompanyClass);
        Assert.Equal(0m, result.PreliminaryTaxSafeHarbourAmount);
        Assert.True(result.SafeHarbourMet);
        Assert.Equal(new DateOnly(2022, 9, 23), Assert.Single(result.DueItems).DueDate);
        Assert.Equal(0m, result.EstimatedLatePaymentInterest);
    }

    [Theory]
    [InlineData("2022-10-01", 0.05, 5000, false)]
    [InlineData("2022-12-01", 0.10, 10000, true)]
    public void LateReturn_ExposesFiveOrTenPercentSurchargeAndReliefRestriction(
        string exposureDate,
        decimal expectedRate,
        decimal expectedSurcharge,
        bool beyondTwoMonths)
    {
        var date = DateOnly.Parse(exposureDate);
        var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(
            currentCorporationTax: 100_000m,
            priorCorporationTax: 80_000m,
            asOf: date,
            filedDate: date,
            capitalAllowances: 2_000m,
            payments: [Payment(new DateOnly(2021, 11, 23), 80_000m)]));

        Assert.True(result.LateFiling.IsLate);
        Assert.Equal(expectedRate, result.LateFiling.Rate);
        Assert.Equal(expectedSurcharge, result.LateFiling.EstimatedSurcharge);
        Assert.True(result.LateFiling.ReliefRestrictionExposure);
        Assert.Equal(beyondTwoMonths, result.LateFiling.DaysLate > 61);
    }

    [Fact]
    public void ComplexPaymentRulesAndUnretainedEvidence_BlockFilingSupport()
    {
        var facts = Facts(
            currentCorporationTax: 100_000m,
            priorCorporationTax: 80_000m,
            asOf: new DateOnly(2021, 11, 1),
            payments: []);
        facts = facts with
        {
            PriorLiabilityEvidenceComplete = false,
            HasInterestLimitationRule = true,
            UsesNotionalGroupPaymentAllocation = true,
            HasDirtOrOtherWithholdingCredits = true,
            HasOtherPreliminaryTaxAdjustments = true,
            HasMandatoryElectronicFilingExemption = true
        };

        var result = CorporationTaxFilingSupportCalculator.Calculate(facts);

        Assert.False(result.FilingSupportReady);
        Assert.Contains(result.BlockingReasons, reason => reason.Contains("Interest Limitation Rule", StringComparison.Ordinal));
        Assert.Contains(result.BlockingReasons, reason => reason.Contains("group preliminary-tax", StringComparison.Ordinal));
        Assert.Contains(result.BlockingReasons, reason => reason.Contains("withholding-tax", StringComparison.Ordinal));
        Assert.Contains(result.BlockingReasons, reason => reason.Contains("21-day", StringComparison.Ordinal));
        Assert.Contains(result.BlockingReasons, reason => reason.Contains("liability inputs", StringComparison.Ordinal));
    }

    [Fact]
    public void ShortCurrentPeriod_UsesNinetyPercentCurrentBasisRatherThanPriorYearSafeHarbour()
    {
        var facts = Facts(
            currentCorporationTax: 100_000m,
            priorCorporationTax: 20_000m,
            asOf: new DateOnly(2021, 5, 23),
            payments: [Payment(new DateOnly(2021, 5, 23), 90_000m)]);
        facts = facts with { PeriodEnd = new DateOnly(2021, 6, 30) };

        var result = CorporationTaxFilingSupportCalculator.Calculate(facts);

        Assert.True(result.IsShortAccountingPeriod);
        Assert.Equal(90_000m, result.PreliminaryTaxSafeHarbourAmount);
        Assert.True(result.SafeHarbourMet);
    }

    [Fact]
    public void ShortPriorPeriod_IsAnnualisedButSection239DoesNotChangeSmallCompanyClassification()
    {
        var facts = Facts(
            currentCorporationTax: 100_000m,
            priorCorporationTax: 100_000m,
            asOf: new DateOnly(2021, 11, 23),
            payments: [Payment(new DateOnly(2021, 11, 23), 90_000m)]);
        facts = facts with
        {
            PriorPeriodStart = new DateOnly(2020, 1, 1),
            PriorPeriodEnd = new DateOnly(2020, 6, 30),
            PriorSection239IncomeTax = 80_000m
        };

        var result = CorporationTaxFilingSupportCalculator.Calculate(facts);

        Assert.Equal(200_549.45m, result.AnnualisedPriorCorporationTax);
        Assert.Equal(CorporationTaxFilingSupportCalculator.CompanyPaymentClass.Large, result.CompanyClass);

        facts = facts with
        {
            PriorCorporationTaxExcludingSurchargeAndSection239 = 90_000m,
            PriorSection239IncomeTax = 100_000m
        };
        result = CorporationTaxFilingSupportCalculator.Calculate(facts);
        Assert.Equal(180_494.51m, result.AnnualisedPriorCorporationTax);
        Assert.Equal(CorporationTaxFilingSupportCalculator.CompanyPaymentClass.Small, result.CompanyClass);
    }

    [Fact]
    public void CalculationIdentity_ChangesWhenUnderlyingTaxBlockerEvidenceChanges()
    {
        var facts = Facts(
            currentCorporationTax: 100_000m,
            priorCorporationTax: 80_000m,
            asOf: new DateOnly(2021, 11, 1),
            payments: []);
        facts = facts with { CurrentTaxChargeSupported = false, CurrentTaxBlockers = ["Unsupported close-company surcharge."] };
        var first = CorporationTaxFilingSupportCalculator.Calculate(facts);

        var second = CorporationTaxFilingSupportCalculator.Calculate(facts with
        {
            CurrentTaxBlockers = ["Unsupported chargeable-gains computation."]
        });

        Assert.NotEqual(first.CalculationSha256, second.CalculationSha256);
    }

    [Fact]
    public void MissingRetainedReview_BlocksEvenWhenArithmeticOtherwisePasses()
    {
        var facts = Facts(
            currentCorporationTax: 100_000m,
            priorCorporationTax: 80_000m,
            asOf: new DateOnly(2021, 11, 23),
            payments: [Payment(new DateOnly(2021, 11, 23), 80_000m)]) with
        {
            ReviewEvidenceRetained = false
        };

        var result = CorporationTaxFilingSupportCalculator.Calculate(facts);

        Assert.False(result.FilingSupportReady);
        Assert.Contains(result.BlockingReasons, reason => reason.Contains("No retained preliminary-tax basis review", StringComparison.Ordinal));
    }

    private static CorporationTaxFilingSupportCalculator.Payment Payment(
        DateOnly date,
        decimal amount,
        CorporationTaxFilingSupportCalculator.PaymentKind kind = CorporationTaxFilingSupportCalculator.PaymentKind.PreliminarySecondOrSingle) =>
        new(date, amount, kind, "Retained ROS payment confirmation reference for automated fixture.");

    private static CorporationTaxFilingSupportCalculator.Facts Facts(
        decimal currentCorporationTax,
        decimal? priorCorporationTax,
        DateOnly asOf,
        IReadOnlyList<CorporationTaxFilingSupportCalculator.Payment> payments,
        bool isFirstAccountingPeriod = false,
        DateOnly? filedDate = null,
        decimal capitalAllowances = 0m) =>
        new(
            PeriodStart: new DateOnly(2021, 1, 1),
            PeriodEnd: new DateOnly(2021, 12, 31),
            IsFirstAccountingPeriod: isFirstAccountingPeriod,
            CurrentCorporationTax: currentCorporationTax,
            CurrentSection239IncomeTax: 0m,
            PriorCorporationTaxExcludingSurchargeAndSection239: priorCorporationTax,
            PriorSection239IncomeTax: priorCorporationTax is null ? null : 0m,
            PriorPeriodStart: priorCorporationTax is null ? null : new DateOnly(2020, 1, 1),
            PriorPeriodEnd: priorCorporationTax is null ? null : new DateOnly(2020, 12, 31),
            PriorLiabilityEvidenceComplete: priorCorporationTax is not null,
            ReviewEvidenceRetained: true,
            Payments: payments,
            FiledDate: filedDate,
            AsOfDate: asOf,
            CurrentTaxChargeSupported: true,
            CurrentTaxBlockers: [],
            HasInterestLimitationRule: false,
            UsesNotionalGroupPaymentAllocation: false,
            HasDirtOrOtherWithholdingCredits: false,
            HasOtherPreliminaryTaxAdjustments: false,
            HasMandatoryElectronicFilingExemption: false,
            CapitalAllowancesClaimed: capitalAllowances,
            TradingLossReliefUsed: 0m);
}
