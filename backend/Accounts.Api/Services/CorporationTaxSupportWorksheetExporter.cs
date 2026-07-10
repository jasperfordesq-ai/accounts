using System.Globalization;
using System.Text;

namespace Accounts.Api.Services;

/// <summary>
/// Produces a retained, human-readable support CSV. The export deliberately carries
/// non-CT1 and no-submission controls in both its metadata and filename contract.
/// </summary>
public static class CorporationTaxSupportWorksheetExporter
{
    public const string MediaType = "text/csv; charset=utf-8";

    public static string FileName(int periodId) =>
        $"CORPORATION_TAX_SUPPORT_ONLY_NOT_CT1_period_{periodId}.csv";

    public static byte[] ExportUtf8(CorporationTaxSupportWorksheetBuilder.Worksheet worksheet)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(Export(worksheet))];
    }

    public static string Export(CorporationTaxSupportWorksheetBuilder.Worksheet worksheet)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        if (worksheet.IsCompleteCt1Return || worksheet.DirectRosSubmissionSupported)
            throw new InvalidOperationException("A support worksheet export cannot carry CT1-complete or direct-ROS-submission flags.");

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "WARNING", worksheet.Warning },
            new[] { "Output kind", worksheet.OutputKind },
            new[] { "Complete CT1 return", "false" },
            new[] { "Direct ROS submission supported", "false" },
            new[] { "Qualified accountant review required", worksheet.QualifiedAccountantReviewRequired ? "true" : "false" },
            new[] { "Support worksheet ready", worksheet.SupportWorksheetReady ? "true" : "false" },
            new[] { "Company", worksheet.CompanyName },
            new[] { "Tax reference", worksheet.TaxReference },
            new[] { "Accounting period", $"{worksheet.PeriodStart} to {worksheet.PeriodEnd}" },
            new[] { "Mapping version", worksheet.MappingVersion },
            new[] { "Year-specific mapping available", worksheet.YearSpecificMappingAvailable ? "true" : "false" },
            new[] { "Generated as of", worksheet.GeneratedAsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            new[] { "Worksheet SHA-256", worksheet.WorksheetSha256 },
            Array.Empty<string>(),
            new[] { "WORKSHEET FIELDS" },
            new[] { "Panel number", "Panel title", "Published field label", "Mapping status", "Value type", "Support value", "Source", "Machine supported", "Review note" }
        };

        rows.AddRange(worksheet.Fields.Select(field => (IReadOnlyList<string>)
        [
            field.PublishedPanelNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
            field.PanelTitle,
            field.PublishedFieldLabel,
            field.MappingStatus,
            field.ValueType,
            field.NumericValue?.ToString("0.00", CultureInfo.InvariantCulture) ?? field.TextValue ?? "",
            field.Source,
            field.MachineSupported ? "true" : "false",
            field.Note
        ]));

        rows.Add([]);
        rows.Add(["RECONCILIATIONS"]);
        rows.Add(["Code", "Left", "Right", "Difference", "Reconciles", "Detail"]);
        rows.AddRange(worksheet.Reconciliations.Select(item => (IReadOnlyList<string>)
        [
            item.Code,
            item.Left.ToString("0.00", CultureInfo.InvariantCulture),
            item.Right.ToString("0.00", CultureInfo.InvariantCulture),
            item.Difference.ToString("0.00", CultureInfo.InvariantCulture),
            item.Reconciles ? "true" : "false",
            item.Detail
        ]));

        rows.Add([]);
        rows.Add(["BLOCKING REASONS"]);
        rows.AddRange(worksheet.BlockingReasons.Select(reason => (IReadOnlyList<string>)[reason]));
        rows.Add([]);
        rows.Add(["MANDATORY MANUAL COMPLETION"]);
        rows.AddRange(worksheet.ManualCompletionItems.Select(item => (IReadOnlyList<string>)[item]));
        rows.Add([]);
        rows.Add(["OFFICIAL SOURCES"]);
        rows.Add(["Code", "Title", "URL"]);
        rows.AddRange(worksheet.Sources.Select(source => (IReadOnlyList<string>)[source.Code, source.Title, source.Url]));

        return string.Join("\r\n", rows.Select(row => string.Join(",", row.Select(Escape)))) + "\r\n";
    }

    private static string Escape(string value)
    {
        value = NeutraliseSpreadsheetFormula(value);
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string NeutraliseSpreadsheetFormula(string value)
    {
        var candidate = value.TrimStart();
        if (candidate.Length == 0)
            return value;
        var formulaPrefix = candidate[0] is '=' or '+' or '@'
            || candidate[0] == '-'
            && !decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
        return formulaPrefix ? $"'{value}" : value;
    }
}
