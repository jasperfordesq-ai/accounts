using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxSupportWorksheetTests
{
    [Fact]
    public void Published2025Worksheet_MapsSupportedFieldsAndReconcilesWithoutClaimingACompleteReturn()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(InputForExporter(2025, preliminaryPayment: 1_000m));

        Assert.Equal(CorporationTaxSupportWorksheetBuilder.OutputKind, worksheet.OutputKind);
        Assert.False(worksheet.IsCompleteCt1Return);
        Assert.False(worksheet.DirectRosSubmissionSupported);
        Assert.True(worksheet.QualifiedAccountantReviewRequired);
        Assert.True(worksheet.YearSpecificMappingAvailable);
        Assert.Equal("Revenue-CT1-2025-Part-38-02-01J", worksheet.MappingVersion);
        Assert.Contains("NOT A CT1 RETURN", worksheet.Warning, StringComparison.Ordinal);

        var wages = Assert.Single(worksheet.Fields, field => field.Code == "salaries-wages");
        var directors = Assert.Single(worksheet.Fields, field => field.Code == "directors-remuneration");
        Assert.Equal(40_000m, wages.NumericValue);
        Assert.Equal(8_000m, directors.NumericValue);
        Assert.NotEqual(wages.NumericValue, directors.NumericValue);
        Assert.Equal(3, directors.PublishedPanelNumber);
        Assert.Equal("Directors' remuneration including fees, bonuses, etc", directors.PublishedFieldLabel);
        Assert.Equal("published-exact-field-label", directors.MappingStatus);

        Assert.All(worksheet.Reconciliations, reconciliation => Assert.True(reconciliation.Reconciles, reconciliation.Detail));
        Assert.True(worksheet.SupportWorksheetReady);
        Assert.NotEmpty(worksheet.ManualCompletionItems);
        Assert.Matches("^[a-f0-9]{64}$", worksheet.WorksheetSha256);
    }

    [Fact]
    public void UnpublishedYearMapping_BlocksExactHandoffWhileRetainingOrientationRows()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(InputForExporter(2026, preliminaryPayment: 1_000m));

        Assert.False(worksheet.YearSpecificMappingAvailable);
        Assert.False(worksheet.SupportWorksheetReady);
        Assert.Contains(worksheet.BlockingReasons, reason => reason.Contains("No year-specific Revenue CT1", StringComparison.Ordinal));
        Assert.Contains(worksheet.Fields, field => field.MappingStatus == "latest-published-field-orientation");
        Assert.False(worksheet.IsCompleteCt1Return);
    }

    [Fact]
    public void AggregateAndDatedPreliminaryPayments_MustReconcile()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(InputForExporter(2025, preliminaryPayment: 900m));

        var paymentReconciliation = Assert.Single(
            worksheet.Reconciliations,
            reconciliation => reconciliation.Code == "payment-ledger");
        Assert.False(paymentReconciliation.Reconciles);
        Assert.Equal(100m, paymentReconciliation.Difference);
        Assert.False(worksheet.SupportWorksheetReady);
        Assert.Contains(worksheet.BlockingReasons, reason => reason.Contains("payment-ledger", StringComparison.Ordinal));
    }

    internal static CorporationTaxSupportWorksheetBuilder.WorksheetInput InputForExporter(int year = 2025, decimal preliminaryPayment = 1_000m)
    {
        var periodStart = new DateOnly(year, 1, 1);
        var periodEnd = new DateOnly(year, 12, 31);
        var preliminary = CorporationTaxFilingSupportCalculator.Calculate(new(
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            IsFirstAccountingPeriod: false,
            CurrentCorporationTax: 7_500m,
            CurrentSection239IncomeTax: 0m,
            PriorCorporationTaxExcludingSurchargeAndSection239: 1_000m,
            PriorSection239IncomeTax: 0m,
            PriorPeriodStart: periodStart.AddYears(-1),
            PriorPeriodEnd: periodEnd.AddYears(-1),
            PriorLiabilityEvidenceComplete: true,
            ReviewEvidenceRetained: true,
            Payments:
            [
                new CorporationTaxFilingSupportCalculator.Payment(
                    CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(periodEnd),
                    preliminaryPayment,
                    CorporationTaxFilingSupportCalculator.PaymentKind.PreliminarySecondOrSingle,
                    "Retained ROS payment confirmation for worksheet fixture.")
            ],
            FiledDate: null,
            AsOfDate: CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(periodEnd),
            CurrentTaxChargeSupported: true,
            CurrentTaxBlockers: [],
            HasInterestLimitationRule: false,
            UsesNotionalGroupPaymentAllocation: false,
            HasDirtOrOtherWithholdingCredits: false,
            HasOtherPreliminaryTaxAdjustments: false,
            HasMandatoryElectronicFilingExemption: false,
            CapitalAllowancesClaimed: 1_000m,
            TradingLossReliefUsed: 0m));
        var tax = new TaxComputationService.Ct1SupportData(
            CompanyName: "Worksheet Limited",
            TaxReference: "1234567A",
            PeriodStart: periodStart.ToString("yyyy-MM-dd"),
            PeriodEnd: periodEnd.ToString("yyyy-MM-dd"),
            Turnover: 100_000m,
            GrossProfit: 60_000m,
            NetProfit: 55_000m,
            TaxableProfit: 56_000m,
            TaxDue: 7_500m,
            PreliminaryTaxPaid: 1_000m,
            BalanceDue: 6_500m,
            Adjustments:
            [
                new TaxComputationService.TaxAdjustment("Add back: depreciation", 1_000m, "Fixture"),
                new TaxComputationService.TaxAdjustment("Deduct: capital allowances", -1_000m, "Fixture")
            ],
            TotalDirectorsFees: 8_000m,
            TotalEmployeeCosts: 54_400m,
            DepreciationCharged: 1_000m,
            CapitalAllowances: 1_000m,
            TradingLossAvailable: 0m,
            TradingProfitBeforeLossRelief: 52_000m,
            TradingProfitAfterLossRelief: 52_000m,
            PassiveNonTradingIncome: 4_000m,
            BroughtForwardTradingLoss: 0m,
            TradingLossUsed: 0m,
            TradingLossCarriedForward: 0m,
            BalancingAllowances: 0m,
            BalancingCharges: 0m,
            SupportStatus: "machine-supported-simple-scope",
            FinalTaxChargeSupported: true,
            ManualReviewRequired: true,
            OutputKind: TaxComputationService.OutputKind,
            IsCompleteCt1Return: false,
            BlockingReasons: [],
            Sources: CorporationTaxRuleSources.All,
            CalculationSha256: new string('a', 64));
        return new CorporationTaxSupportWorksheetBuilder.WorksheetInput(
            "Worksheet Limited",
            "1234567A",
            periodStart,
            periodEnd,
            tax,
            preliminary,
            GrossWagesExcludingDirectors: 40_000m,
            EmployerPrsiAndPension: 6_400m,
            GeneratedAsOf: CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(periodEnd));
    }
}
