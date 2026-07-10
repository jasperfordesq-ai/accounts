using System.Text;
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxSupportWorksheetExporterTests
{
    [Fact]
    public void CsvExport_LeadsWithNoCt1ControlsAndRetainsMappingsReconciliationsAndSources()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(CorporationTaxSupportWorksheetTests.InputForExporter());

        var csv = CorporationTaxSupportWorksheetExporter.Export(worksheet);

        Assert.StartsWith("WARNING,SUPPORT WORKSHEET ONLY - NOT A CT1 RETURN - NOTHING IS SUBMITTED TO REVENUE\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("Complete CT1 return,false", csv, StringComparison.Ordinal);
        Assert.Contains("Direct ROS submission supported,false", csv, StringComparison.Ordinal);
        Assert.Contains("Qualified accountant review required,true", csv, StringComparison.Ordinal);
        Assert.Contains("Sales / Receipts / Turnover,published-exact-field-label", csv, StringComparison.Ordinal);
        Assert.Contains("RECONCILIATIONS", csv, StringComparison.Ordinal);
        Assert.Contains("MANDATORY MANUAL COMPLETION", csv, StringComparison.Ordinal);
        Assert.Contains("OFFICIAL SOURCES", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("ready for submission", csv, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CORPORATION_TAX_SUPPORT_ONLY_NOT_CT1_period_42.csv", CorporationTaxSupportWorksheetExporter.FileName(42));
    }

    [Fact]
    public void CsvExport_EmitsUtf8BomAndEscapesCommaQuoteAndNewlineEvidence()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(CorporationTaxSupportWorksheetTests.InputForExporter());
        var firstField = worksheet.Fields[0] with { Note = "Review, retain \"signed\" evidence\nnext line" };
        worksheet = worksheet with { Fields = [firstField, .. worksheet.Fields.Skip(1)] };

        var bytes = CorporationTaxSupportWorksheetExporter.ExportUtf8(worksheet);
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("\"Review, retain \"\"signed\"\" evidence\nnext line\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvExport_FailsClosedIfSupportBoundaryFlagsAreEverRelaxed()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(CorporationTaxSupportWorksheetTests.InputForExporter()) with
        {
            DirectRosSubmissionSupported = true
        };

        Assert.Throws<InvalidOperationException>(() => CorporationTaxSupportWorksheetExporter.Export(worksheet));
    }

    [Fact]
    public void CsvExport_NeutralisesSpreadsheetFormulaInjectionInRetainedTextEvidence()
    {
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(CorporationTaxSupportWorksheetTests.InputForExporter());
        var firstField = worksheet.Fields[0] with { Note = "=HYPERLINK(\"https://attacker.invalid\",\"open\")" };
        worksheet = worksheet with { Fields = [firstField, .. worksheet.Fields.Skip(1)] };

        var csv = CorporationTaxSupportWorksheetExporter.Export(worksheet);

        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",=HYPERLINK", csv, StringComparison.Ordinal);
    }
}
