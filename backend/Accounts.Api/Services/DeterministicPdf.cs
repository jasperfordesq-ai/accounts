using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Api.Entities;
using QuestPDF.Infrastructure;

namespace Accounts.Api.Services;

/// <summary>
/// Retained final PDFs are bound by SHA-256 (the directors' s.324 approval binds the exact accounts
/// PDF; CRO and Charity package approvals bind their artifacts). QuestPDF stamps the render time into
/// /CreationDate and /ModDate, and qpdf (used to embed the signed auditor report) writes a
/// time-derived trailer /ID, so two renders of identical content would otherwise differ and every
/// re-download would look like new accounts. These helpers pin both to values derived from the
/// source, so the same source always yields the same bytes.
/// </summary>
public static partial class DeterministicPdf
{
    /// <summary>The period's board approval date, or its end date before one is recorded.</summary>
    public static DateOnly StableDate(AccountingPeriod period) => period.ApprovalDate ?? period.PeriodEnd;

    /// <summary>Document metadata whose creation and modification dates are fixed at 00:00 UTC on <paramref name="stableDate"/>.</summary>
    public static DocumentMetadata Metadata(DateOnly stableDate)
    {
        var at = new DateTimeOffset(stableDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new DocumentMetadata { CreationDate = at, ModifiedDate = at };
    }

    /// <summary>
    /// Replaces the final trailer's /ID (two hex strings) with a value derived from the rest of the
    /// file. The replacement has the same length, so cross-reference offsets are unchanged. A PDF
    /// without a trailer /ID is returned unchanged.
    /// </summary>
    public static byte[] WithContentDerivedTrailerId(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var startXref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startXref < 0)
            return pdf;
        var offsetMatch = XrefOffset().Match(text, startXref + "startxref".Length);
        if (!offsetMatch.Success
            || !int.TryParse(offsetMatch.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sectionStart)
            || sectionStart < 0
            || sectionStart >= startXref)
            return pdf;

        // Only the final cross-reference section (table + trailer, or an xref stream dictionary):
        // an embedded attachment may carry its own /ID, which must not be touched.
        var id = TrailerId().Match(text, sectionStart, startXref - sectionStart);
        if (!id.Success)
            return pdf;

        var result = pdf.ToArray();
        var first = id.Groups[1];
        var second = id.Groups[2];
        Array.Fill(result, (byte)'0', first.Index, first.Length);
        Array.Fill(result, (byte)'0', second.Index, second.Length);
        var digest = Convert.ToHexStringLower(SHA256.HashData(result));
        WriteHex(result, first.Index, first.Length, digest);
        WriteHex(result, second.Index, second.Length, digest);
        return result;
    }

    private static void WriteHex(byte[] target, int index, int length, string digest)
    {
        for (var i = 0; i < length; i++)
            target[index + i] = (byte)digest[i % digest.Length];
    }

    [GeneratedRegex(@"\G\s*(\d+)")]
    private static partial Regex XrefOffset();

    [GeneratedRegex(@"/ID\s*\[\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\]")]
    private static partial Regex TrailerId();
}
