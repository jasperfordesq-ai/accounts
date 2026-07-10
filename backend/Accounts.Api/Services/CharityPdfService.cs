using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Diagnostics;

namespace Accounts.Api.Services;

public sealed class CharityPdfService(PlatformMetrics? platformMetrics = null)
{
    private static readonly string Navy = "#18324A";
    private static readonly string Teal = "#087E8B";
    private static readonly string Pale = "#EAF4F5";
    private static readonly string Ink = "#1E293B";

    public byte[] GenerateSofa(CharityArtifactEvidence evidence, bool reviewCopy) =>
        TrackDocument(() => GenerateSofaCore(evidence, reviewCopy));

    private static byte[] GenerateSofaCore(CharityArtifactEvidence evidence, bool reviewCopy)
    {
        var document = Document.Create(root =>
        {
            root.Page(page =>
            {
                ConfigurePage(page, evidence, "STATEMENT OF FINANCIAL ACTIVITIES", reviewCopy);
                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Element(c => ComposeIdentity(c, evidence));
                    column.Item().Text("Statement of Financial Activities (incorporating an income and expenditure account)")
                        .FontSize(14).Bold().FontColor(Navy);
                    column.Item().Text($"For the year ended {evidence.Period.PeriodEnd:dd MMMM yyyy}")
                        .FontSize(10).FontColor(Colors.Grey.Darken2);

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2.4f);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Header(header =>
                        {
                            HeaderCell(header, "Fund");
                            HeaderCell(header, "Opening");
                            HeaderCell(header, "Income");
                            HeaderCell(header, "Expenditure");
                            HeaderCell(header, "Transfers");
                            HeaderCell(header, "Gains / losses");
                            HeaderCell(header, "Closing");
                        });

                        foreach (var group in AllFundGroups(evidence.Sofa))
                        {
                            GroupCell(table, group.Label);
                            foreach (var fund in group.Lines)
                            {
                                BodyCell(table, fund.FundName);
                                MoneyCell(table, fund.OpeningBalance);
                                MoneyCell(table, fund.IncomingResources);
                                MoneyCell(table, fund.ResourcesExpended, parentheses: true);
                                MoneyCell(table, fund.Transfers);
                                MoneyCell(table, fund.GainsLosses);
                                MoneyCell(table, fund.ClosingBalance, bold: true);
                            }
                        }

                        TotalCell(table, "Total funds");
                        TotalMoneyCell(table, evidence.Sofa.TotalOpeningFunds);
                        TotalMoneyCell(table, evidence.Sofa.TotalIncoming);
                        TotalMoneyCell(table, evidence.Sofa.TotalExpended, parentheses: true);
                        TotalMoneyCell(table, evidence.Sofa.TotalTransfers);
                        TotalMoneyCell(table, evidence.Sofa.TotalGainsLosses);
                        TotalMoneyCell(table, evidence.Sofa.TotalClosingFunds);
                    });

                    column.Item().PaddingTop(8).Background(Pale).BorderLeft(4).BorderColor(Teal).Padding(10).Column(box =>
                    {
                        box.Item().Text("Reconciliation control").Bold().FontColor(Navy);
                        box.Item().Text($"SoFA closing funds: {Money(evidence.Reconciliation.TotalClosingFunds)}");
                        box.Item().Text($"Balance-sheet net assets: {Money(evidence.Reconciliation.BalanceSheetNetAssets)}");
                        box.Item().Text($"Difference: {Money(evidence.Reconciliation.Difference)} - RECONCILED")
                            .Bold().FontColor(Teal);
                    });

                    column.Item().PaddingTop(5).Text(
                        $"Framework: {evidence.SorpDecision.FrameworkCode}, Tier {evidence.SorpDecision.Tier}; "
                        + $"SoFA basis supported: {evidence.SorpDecision.SofaBasis}.")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);
                });
            });
        });
        return document.GeneratePdf();
    }

    public byte[] GenerateTrusteesAnnualReport(CharityArtifactEvidence evidence, bool reviewCopy) =>
        TrackDocument(() => GenerateTrusteesAnnualReportCore(evidence, reviewCopy));

    private static byte[] GenerateTrusteesAnnualReportCore(CharityArtifactEvidence evidence, bool reviewCopy)
    {
        var tar = evidence.TrusteesReport;
        var document = Document.Create(root =>
        {
            root.Page(page =>
            {
                ConfigurePage(page, evidence, "TRUSTEES' ANNUAL REPORT", reviewCopy);
                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Element(c => ComposeIdentity(c, evidence));
                    column.Item().Text("Trustees' Annual Report").FontSize(17).Bold().FontColor(Navy);
                    column.Item().Text($"For the year ended {evidence.Period.PeriodEnd:dd MMMM yyyy}")
                        .FontSize(10).FontColor(Colors.Grey.Darken2);

                    Section(column, "Reference and administrative details", section =>
                    {
                        KeyValue(section, "Charities Regulator number", tar.CharityNumber);
                        KeyValue(section, "CRO number", tar.CroNumber);
                        KeyValue(section, "Reporting period", $"{tar.PeriodStart} to {tar.PeriodEnd}");
                        KeyValue(section, "Annual-return due date", tar.FilingDeadline);
                    });

                    Section(column, "Trustees who served during the period", section =>
                    {
                        foreach (var trustee in tar.Trustees)
                        {
                            var service = trustee.ResignedDate is null
                                ? $"appointed {trustee.AppointedDate:dd MMM yyyy}"
                                : $"appointed {trustee.AppointedDate:dd MMM yyyy}; resigned {trustee.ResignedDate:dd MMM yyyy}";
                            section.Item().Text(text =>
                            {
                                text.Span(trustee.Name).Bold();
                                text.Span($" - {service}").FontColor(Colors.Grey.Darken2);
                            });
                        }
                        section.Item().PaddingTop(3).Text(
                            $"Population accepted by {evidence.Period.CharityFilingPackage!.TrusteeReviewedBy} "
                            + $"at {evidence.Period.CharityFilingPackage.TrusteeReviewedAtUtc:yyyy-MM-dd HH:mm} UTC; "
                            + $"evidence {evidence.Period.CharityFilingPackage.TrusteeReviewReference}.")
                            .FontSize(8).FontColor(Colors.Grey.Darken2);
                    });

                    Section(column, "Objectives and activities", section =>
                    {
                        section.Item().Text(tar.CharitableObjectives);
                        section.Item().PaddingTop(4).Text("Principal activities").Bold().FontColor(Navy);
                        section.Item().Text(tar.PrincipalActivities);
                    });

                    Section(column, "Financial review", section =>
                    {
                        KeyValue(section, "Income", Money(tar.TotalIncome));
                        KeyValue(section, "Expenditure", Money(tar.TotalExpenditure));
                        KeyValue(section, "Net movement in funds", Money(tar.NetMovement));
                        KeyValue(section, "Closing funds / net assets", Money(tar.ClosingFunds));
                    });

                    Section(column, "Governance and trustee disclosures", section =>
                    {
                        KeyValue(section, "Governance Code answer", tar.GovernanceCodeCompliant == true ? "Yes" : "No");
                        KeyValue(section, "Governance evidence", tar.GovernanceEvidenceReference ?? "");
                        KeyValue(section, "Reviewed by", $"{tar.GovernanceReviewedBy} at {tar.GovernanceReviewedAtUtc:yyyy-MM-dd HH:mm} UTC");
                        if (!string.IsNullOrWhiteSpace(tar.GovernanceCodeNote))
                            KeyValue(section, "Governance note", tar.GovernanceCodeNote!);
                        KeyValue(section, "Trustee remuneration", tar.TrusteeRemunerationPaid ? Money(tar.TrusteeRemunerationAmount) : "None reported");
                        if (!string.IsNullOrWhiteSpace(tar.TrusteeExpensesDetails))
                            KeyValue(section, "Trustee expenses", tar.TrusteeExpensesDetails!);
                        KeyValue(section, "International transfers", tar.HasInternationalTransfers ? "Reported - see retained details" : "None reported");
                        if (!string.IsNullOrWhiteSpace(tar.InternationalTransferDetails))
                            KeyValue(section, "Transfer details", tar.InternationalTransferDetails!);
                    });

                    column.Item().PaddingTop(4).Text(
                        "This artifact is generated from the reconciled accounting and retained review evidence identified in the release manifest. "
                        + "It is not submitted directly to the CRO, Revenue or the Charities Regulator by this platform.")
                        .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                });
            });
        });
        return document.GeneratePdf();
    }

    private static void ConfigurePage(
        PageDescriptor page,
        CharityArtifactEvidence evidence,
        string documentType,
        bool reviewCopy)
    {
        page.Size(PageSizes.A4);
        page.PageColor(Colors.White);
        page.MarginHorizontal(42);
        page.MarginVertical(34);
        page.DefaultTextStyle(x => x.FontFamily("Helvetica").FontSize(9).FontColor(Ink));
        page.Header().Column(header =>
        {
            if (reviewCopy)
            {
                header.Item().Background("#FFF1F2").Border(1).BorderColor("#E11D48").Padding(7)
                    .AlignCenter().Text("REVIEW COPY - NOT APPROVED FOR FINAL USE")
                    .Bold().FontColor("#BE123C");
                header.Item().PaddingBottom(8);
            }
            header.Item().Row(row =>
            {
                row.RelativeItem().Text(documentType).Bold().FontSize(8).FontColor(Teal);
                row.ConstantItem(180).AlignRight().Text(evidence.SourceFingerprintSha256[..16])
                    .FontSize(7).FontColor(Colors.Grey.Medium);
            });
            header.Item().PaddingBottom(5).LineHorizontal(1).LineColor(Navy);
        });
        page.Footer().Row(row =>
        {
            row.RelativeItem().Text(
                $"{evidence.SorpDecision.FrameworkCode} | Source fingerprint {evidence.SourceFingerprintSha256[..16]}...")
                .FontSize(7).FontColor(Colors.Grey.Medium);
            row.ConstantItem(90).AlignRight()
                .DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Medium))
                .Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static void ComposeIdentity(IContainer container, CharityArtifactEvidence evidence)
    {
        container.Background(Navy).Padding(12).Column(column =>
        {
            column.Item().Text(evidence.Period.Company.LegalName).Bold().FontSize(15).FontColor(Colors.White);
            column.Item().Text(
                $"Charity no. {evidence.CharityInfo.CharityNumber} | CRO no. {evidence.Period.Company.CroNumber ?? "Not recorded"}")
                .FontSize(8).FontColor("#D9EAF0");
        });
    }

    private static IEnumerable<(string Label, IReadOnlyList<FundLine> Lines)> AllFundGroups(SofaData sofa)
    {
        if (sofa.UnrestrictedFunds.Count > 0) yield return ("Unrestricted and designated funds", sofa.UnrestrictedFunds);
        if (sofa.RestrictedFunds.Count > 0) yield return ("Restricted funds", sofa.RestrictedFunds);
        if (sofa.EndowmentFunds.Count > 0) yield return ("Endowment funds", sofa.EndowmentFunds);
    }

    private static void HeaderCell(TableCellDescriptor cells, string text) =>
        cells.Cell().Background(Navy).PaddingVertical(6).PaddingHorizontal(4)
            .Text(text).Bold().FontSize(7).FontColor(Colors.White);

    private static void GroupCell(TableDescriptor table, string text) =>
        table.Cell().ColumnSpan(7).Background(Pale).Padding(5).Text(text).Bold().FontColor(Navy);

    private static void BodyCell(TableDescriptor table, string text) =>
        table.Cell().BorderBottom(0.5f).BorderColor("#CBD5E1").Padding(4).Text(text).FontSize(8);

    private static void MoneyCell(TableDescriptor table, decimal amount, bool parentheses = false, bool bold = false)
    {
        var cell = table.Cell().BorderBottom(0.5f).BorderColor("#CBD5E1").Padding(4).AlignRight();
        var value = cell.Text(FormatAmount(amount, parentheses)).FontSize(8);
        if (bold) value.Bold();
    }

    private static void TotalCell(TableDescriptor table, string text) =>
        table.Cell().Background(Navy).Padding(5).Text(text).Bold().FontColor(Colors.White);

    private static void TotalMoneyCell(TableDescriptor table, decimal amount, bool parentheses = false) =>
        table.Cell().Background(Navy).Padding(5).AlignRight().Text(FormatAmount(amount, parentheses))
            .Bold().FontSize(8).FontColor(Colors.White);

    private static void Section(ColumnDescriptor root, string title, Action<ColumnDescriptor> compose)
    {
        root.Item().EnsureSpace(70).Column(section =>
        {
            section.Spacing(3);
            section.Item().BorderBottom(1).BorderColor(Teal).PaddingBottom(3)
                .Text(title).Bold().FontSize(11).FontColor(Navy);
            compose(section);
        });
    }

    private static void KeyValue(ColumnDescriptor column, string label, string value) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(145).Text(label).Bold().FontColor(Navy);
            row.RelativeItem().Text(value);
        });

    private static string FormatAmount(decimal amount, bool parentheses)
    {
        var absolute = Math.Abs(amount).ToString("N2");
        if (amount < 0 || parentheses && amount != 0)
            return $"({absolute})";
        return absolute;
    }

    private static string Money(decimal amount) => amount < 0
        ? $"(EUR {Math.Abs(amount):N2})"
        : $"EUR {amount:N2}";

    private byte[] TrackDocument(Func<byte[]> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;
        try
        {
            var result = action();
            succeeded = true;
            return result;
        }
        finally
        {
            try { platformMetrics?.RecordDocument(DocumentMetricKind.CharityPack, succeeded, stopwatch.Elapsed); }
            catch { /* Telemetry must never change charity document behavior. */ }
        }
    }
}
