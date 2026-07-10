using System.Security.Cryptography;
using System.Text.Json;
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxFilingSupportFixtureTests
{
    private const string FixtureSha256 = "b9d0268c111bb45c5e4498e8bf7cb95854d518e347025f3fb44a5c208da4ec8c";

    [Fact]
    public void IndependentFixture_IsBytePinnedOfficiallySourcedAndPendingQualifiedReview()
    {
        var bytes = File.ReadAllBytes(FixturePath());
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        Assert.Equal(FixtureSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("irish-corporation-tax-filing-support-v1", root.GetProperty("fixtureId").GetString());
        Assert.Equal("pending-qualified-accountant", root.GetProperty("independentReviewStatus").GetString());
        Assert.Equal(
            ["revenue-payment-and-filing", "revenue-tdm-41a-07-02"],
            root.GetProperty("officialSources")
                .EnumerateArray()
                .Select(source => source.GetProperty("code").GetString())
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OfficialDeadlineExamples_AreReproducedFromExternalFixture()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(FixturePath()));

        foreach (var scenario in document.RootElement.GetProperty("deadlineScenarios").EnumerateArray())
        {
            var start = DateOnly.Parse(scenario.GetProperty("periodStart").GetString()!);
            var end = DateOnly.Parse(scenario.GetProperty("periodEnd").GetString()!);

            if (scenario.TryGetProperty("expectedFirstPreliminaryDue", out var first))
            {
                Assert.Equal(
                    DateOnly.Parse(first.GetString()!),
                    CorporationTaxFilingSupportCalculator.PreliminaryFirstDueDate(start));
            }

            if (scenario.TryGetProperty("expectedSecondOrSinglePreliminaryDue", out var second))
            {
                Assert.Equal(
                    DateOnly.Parse(second.GetString()!),
                    CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(end));
            }

            if (scenario.TryGetProperty("expectedReturnAndBalanceDue", out var balance))
            {
                Assert.Equal(
                    DateOnly.Parse(balance.GetString()!),
                    CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(end));
            }
        }
    }

    [Fact]
    public void OfficialCalculationExamples_AreReproducedWithoutClaimingHumanAcceptance()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(FixturePath()));

        foreach (var scenario in document.RootElement.GetProperty("calculationScenarios").EnumerateArray())
        {
            var result = CorporationTaxFilingSupportCalculator.Calculate(Facts(scenario));
            var code = scenario.GetProperty("code").GetString();

            Assert.Equal(
                Enum.Parse<CorporationTaxFilingSupportCalculator.CompanyPaymentClass>(
                    scenario.GetProperty("expectedCompanyClass").GetString()!),
                result.CompanyClass);
            Assert.Equal(scenario.GetProperty("expectedSafeHarbour").GetDecimal(), result.PreliminaryTaxSafeHarbourAmount);
            Assert.Equal(scenario.GetProperty("expectedSafeHarbourMet").GetBoolean(), result.SafeHarbourMet);
            Assert.Equal(CorporationTaxFilingSupportCalculator.OutputKind, result.OutputKind);
            Assert.False(result.IsCompleteCt1Return);
            Assert.False(result.DirectRosSubmissionSupported);
            Assert.True(result.ManualReviewRequired);

            if (scenario.TryGetProperty("expectedAcceleratedFirst", out var acceleratedFirst))
            {
                Assert.Equal(
                    acceleratedFirst.GetDecimal(),
                    Assert.Single(result.DueItems, item => item.Code == "accelerated-first").CumulativeTaxRequired);
                Assert.Equal(
                    scenario.GetProperty("expectedAcceleratedSecond").GetDecimal(),
                    Assert.Single(result.DueItems, item => item.Code == "accelerated-second").CumulativeTaxRequired);
            }

            if (!scenario.TryGetProperty("expectedInterest", out var expectedInterest))
                continue;

            Assert.Equal(expectedInterest.GetDecimal(), result.EstimatedLatePaymentInterest);
            // The TDM displays whole-euro illustrative amounts (including a large-company
            // table whose displayed rows do not use one uniform rounding convention).
            // Preserve exact statutory-rate arithmetic above and separately prove that it
            // remains within one euro of Revenue's displayed illustrative total.
            Assert.InRange(
                Math.Abs(
                    result.EstimatedLatePaymentInterest
                    - scenario.GetProperty("expectedRevenueRoundedInterest").GetDecimal()),
                0m,
                1m);
            var expectedSegments = scenario.GetProperty("expectedInterestSegments").EnumerateArray().ToList();
            Assert.Equal(expectedSegments.Count, result.InterestSegments.Count);
            for (var index = 0; index < expectedSegments.Count; index++)
            {
                Assert.Equal(expectedSegments[index].GetProperty("principal").GetDecimal(), result.InterestSegments[index].Principal);
                Assert.Equal(expectedSegments[index].GetProperty("days").GetInt32(), result.InterestSegments[index].InclusiveDays);
                Assert.Equal(expectedSegments[index].GetProperty("interest").GetDecimal(), result.InterestSegments[index].Interest);
            }

            Assert.False(result.FilingSupportReady);
            Assert.Contains(result.BlockingReasons, reason => reason.Contains("safe harbour", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(code);
        }
    }

    private static CorporationTaxFilingSupportCalculator.Facts Facts(JsonElement scenario)
    {
        var periodStart = DateOnly.Parse(scenario.GetProperty("periodStart").GetString()!);
        var periodEnd = DateOnly.Parse(scenario.GetProperty("periodEnd").GetString()!);
        var payments = scenario.GetProperty("payments")
            .EnumerateArray()
            .Select(payment => new CorporationTaxFilingSupportCalculator.Payment(
                DateOnly.Parse(payment.GetProperty("date").GetString()!),
                payment.GetProperty("amount").GetDecimal(),
                Enum.Parse<CorporationTaxFilingSupportCalculator.PaymentKind>(payment.GetProperty("kind").GetString()!),
                "Retained external Revenue-example payment evidence reference."))
            .ToList();

        return new CorporationTaxFilingSupportCalculator.Facts(
            periodStart,
            periodEnd,
            IsFirstAccountingPeriod: false,
            scenario.GetProperty("currentCorporationTax").GetDecimal(),
            OptionalMoney(scenario, "currentSection239IncomeTax"),
            scenario.GetProperty("priorCorporationTax").GetDecimal(),
            OptionalMoney(scenario, "priorSection239IncomeTax"),
            periodStart.AddYears(-1),
            periodEnd.AddYears(-1),
            PriorLiabilityEvidenceComplete: true,
            ReviewEvidenceRetained: true,
            payments,
            scenario.TryGetProperty("filedDate", out var filedDate) ? DateOnly.Parse(filedDate.GetString()!) : null,
            DateOnly.Parse(scenario.GetProperty("asOfDate").GetString()!),
            CurrentTaxChargeSupported: true,
            CurrentTaxBlockers: [],
            HasInterestLimitationRule: false,
            UsesNotionalGroupPaymentAllocation: false,
            HasDirtOrOtherWithholdingCredits: false,
            HasOtherPreliminaryTaxAdjustments: false,
            HasMandatoryElectronicFilingExemption: false,
            CapitalAllowancesClaimed: 0m,
            TradingLossReliefUsed: 0m);
    }

    private static decimal OptionalMoney(JsonElement scenario, string propertyName) =>
        scenario.TryGetProperty(propertyName, out var value) ? value.GetDecimal() : 0m;

    private static string FixturePath() => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "corporation-tax-filing-support-independent-v1.json");
}
