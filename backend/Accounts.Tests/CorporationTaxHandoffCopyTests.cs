using System.Reflection;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

// Batch C-2: the CT1/ROS support and handoff copy follows the product position (the directors approve;
// an accountant's review is optional). The manual-completion gates themselves are unchanged.
public sealed class CorporationTaxHandoffCopyTests
{
    [Fact]
    public void Ct1SupportWorksheet_ManualCompletionItems_LeaveTheFinalHandoffReviewToAnOwnerOrReviewer()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(CorporationTaxSupportWorksheetTests.InputForExporter());

        Assert.Contains(
            "Have an Owner or Reviewer review the final ROS handoff in FilingBridge (an accountant's review is optional) and retain external validation/payment references.",
            worksheet.ManualCompletionItems);
        Assert.DoesNotContain(worksheet.ManualCompletionItems, item => item.Contains("qualified accountant", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(worksheet.ManualCompletionItems, item => item.Contains("directors", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, worksheet.ManualCompletionItems.Count);
    }

    [Theory]
    [InlineData(true, "Live ROS completion of every unsupported section confirmed")]
    [InlineData(false, null)]
    public void RevenueHandoffField_ForTheUnsupportedCt1Sections_DoesNotNameAQualifiedAccountant(bool reviewed, string? expectedValue)
    {
        var build = typeof(ExternalFilingHandoffService).GetMethod("BuildRevenueFields", BindingFlags.NonPublic | BindingFlags.Static)!;
        var facts = new RevenueCt1SupportHandoffFacts(
            "Example Trading Limited", "1234567A", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
            ExternalFilingHandoffArtifactBuilder.Ct1SupportOutputKind, false, new string('a', 64), new string('b', 64),
            null, null, null, 0m, 0m, 0m, "Blocked", [], []);
        var period = new AccountingPeriod { PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31) };

        var fields = (IReadOnlyList<ExternalHandoffField>)build.Invoke(null, [period, new RevenueFilingPackage(), facts, reviewed])!;

        var field = Assert.Single(fields, item => item.FieldCode == "ct1.manual.unsupported-sections-reviewed");
        Assert.Equal(expectedValue, field.Value);
        Assert.Equal("Live ROS review of the unsupported CT1 sections", field.SourceReference);
        Assert.Equal(reviewed ? ExternalHandoffFieldStatus.Complete : ExternalHandoffFieldStatus.Missing, field.Status);
    }
}
