using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Diagnostics;

namespace Accounts.Api.Services;

public class DocumentGeneratorService(
    AccountsDbContext db,
    FinancialStatementsService statementsService,
    PlatformMetrics? platformMetrics = null)
{
    public async Task<byte[]> GenerateAccountsPackageAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.AccountsPackage,
            () => GenerateAccountsPackageAsync(companyId, periodId, DocumentPackagePurpose.StatutoryApproval, reviewArtifact: false));

    public async Task<byte[]> GenerateAccountsReviewPackageAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.AccountsPackage,
            () => GenerateAccountsPackageAsync(companyId, periodId, DocumentPackagePurpose.StatutoryApproval, reviewArtifact: true));

    public async Task<byte[]> GenerateAgmApprovalPackAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.AgmPack,
            () => GenerateAccountsPackageAsync(companyId, periodId, DocumentPackagePurpose.AgmApproval, reviewArtifact: false));

    public async Task<byte[]> GenerateAgmReviewPackAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.AgmPack,
            () => GenerateAccountsPackageAsync(companyId, periodId, DocumentPackagePurpose.AgmApproval, reviewArtifact: true));

    private async Task<byte[]> GenerateAccountsPackageAsync(
        int companyId,
        int periodId,
        DocumentPackagePurpose purpose,
        bool reviewArtifact)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException("Period not found");

        if (purpose == DocumentPackagePurpose.StatutoryApproval)
            await statementsService.AssertFinalOutputReadinessAsync(companyId, periodId, "accounts package");
        else if (purpose == DocumentPackagePurpose.AgmApproval)
            await statementsService.AssertFinalOutputReadinessAsync(companyId, periodId, "AGM approval pack");

        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Small;
        var balanceSheet = await statementsService.GetBalanceSheetAsync(companyId, periodId);
        var pl = await statementsService.GetProfitAndLossAsync(companyId, periodId);
        var auditExempt = period.FilingRegime?.AuditExempt == true;

        // Medium/Full regimes additionally require a Cash Flow Statement and a Statement of Changes
        // in Equity (FilingRegimeService.GetRequiredStatements).
        FinancialStatementsService.CashFlowStatement? cashFlow = null;
        FinancialStatementsService.EquityChanges? equityChanges = null;
        if (IncludesCashFlowAndEquity(regime))
        {
            cashFlow = await statementsService.GetCashFlowStatementAsync(companyId, periodId);
            equityChanges = await statementsService.GetEquityChangesAsync(companyId, periodId);
        }

        // Get the deterministic prior period comparatives when available. Full statutory/member
        // accounts must not silently drop the prior-year profit and loss comparative.
        FinancialStatementsService.BalanceSheet? priorBs = null;
        FinancialStatementsService.ProfitAndLoss? priorPl = null;
        var priorPeriod = await PeriodChronologyService
            .PriorPeriodQuery(db, company.Id, period.PeriodStart)
            .FirstOrDefaultAsync();
        if (priorPeriod != null)
        {
            try { priorBs = await statementsService.GetBalanceSheetAsync(company.Id, priorPeriod.Id); } catch { }
            try { priorPl = await statementsService.GetProfitAndLossAsync(company.Id, priorPeriod.Id); } catch { }
        }

        var notes = await db.NotesDisclosures
            .Where(n => n.PeriodId == periodId && n.IsIncluded)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();

        var directors = company.Officers.Where(o => o.Role == OfficerRole.Director && o.ResignedDate == null).ToList();
        var secretary = company.Officers.FirstOrDefault(o => o.Role == OfficerRole.Secretary && o.ResignedDate == null);

        // filing-directors-report-from-service: drive the report from retained, reviewed evidence.
        // The service supplies the period-effective officer timeline, principal-activities narrative,
        // dividend facts and any evidenced audit-information statement.
        var directorsReport = await new DirectorsReportService(db, statementsService).GenerateAsync(companyId, periodId);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginVertical(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                page.Header().Element(c => ComposeArtifactHeader(c, company, reviewArtifact));

                page.Content().Element(c =>
                {
                    c.Column(col =>
                    {
                        col.Spacing(15);

                        // Cover page info
                        ComposeCoverPage(col, company, period, regime, purpose);

                        col.Item().PageBreak();

                        // Directors' Report (unless micro)
                        if (regime != ElectedRegime.Micro)
                        {
                            ComposeDirectorsReport(col, period, directors, directorsReport);
                            col.Item().PageBreak();
                        }

                        // Independent Auditor's Report — required when the company is not availing of
                        // audit exemption. Rendered as a template for the appointed auditor to complete.
                        if (!auditExempt)
                        {
                            if (FilingReleaseGate.HasCompleteAuditorReportEvidence(period))
                                ComposeAttachedAuditorReportNotice(col, company, period);
                            else
                                ComposeAuditorsReport(col, company, period);
                            col.Item().PageBreak();
                        }

                        // Balance Sheet
                        ComposeBalanceSheet(col, company, period, balanceSheet, priorBs, directors);

                        col.Item().PageBreak();

                        // Full statutory/member-approval accounts include the profit and loss account
                        // for every regime, including Micro. Only an eligible reduced CRO filing copy
                        // may omit it.
                        if (ShouldIncludeProfitAndLoss(regime, purpose))
                        {
                            ComposeProfitAndLoss(col, company, period, pl, priorPeriod, priorPl);
                            col.Item().PageBreak();
                        }

                        // Cash Flow Statement + Statement of Changes in Equity (Medium/Full)
                        if (cashFlow != null && equityChanges != null)
                        {
                            ComposeCashFlowStatement(col, company, period, cashFlow);
                            col.Item().PageBreak();
                            ComposeStatementOfChangesInEquity(col, company, period, equityChanges);
                            col.Item().PageBreak();
                        }

                        // Statutory statement
                        ComposeStatutoryStatement(col, company, period, regime, directors, auditExempt);

                        col.Item().PageBreak();

                        // Notes
                        ComposeNotes(col, period, notes);
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, Company company)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text(company.LegalName).Bold().FontSize(8).FontColor(Colors.Grey.Medium);
            row.RelativeItem().AlignRight().Text(t =>
            {
                if (!string.IsNullOrEmpty(company.CroNumber))
                    t.Span($"CRO: {company.CroNumber}").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void ComposeCoverPage(ColumnDescriptor col, Company company, AccountingPeriod period, ElectedRegime regime, DocumentPackagePurpose purpose)
    {
        col.Item().PaddingTop(100).AlignCenter().Column(c =>
        {
            c.Spacing(10);
            c.Item().AlignCenter().Text(company.LegalName).Bold().FontSize(22);
            if (!string.IsNullOrEmpty(company.TradingName))
                c.Item().AlignCenter().Text($"(trading as {company.TradingName})").FontSize(12).Italic();
            c.Item().AlignCenter().Text($"Registered company name: {company.LegalName}").FontSize(10);

            c.Item().PaddingTop(20).AlignCenter().Text("FINANCIAL STATEMENTS").Bold().FontSize(14);

            c.Item().PaddingTop(10).AlignCenter().Text(
                $"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(12);

            c.Item().PaddingTop(30).AlignCenter().Text(PackageRegimeSubtitle(regime, purpose))
                .FontSize(10).Italic().FontColor(Colors.Grey.Darken1);

            if (!string.IsNullOrEmpty(company.CroNumber))
                c.Item().PaddingTop(20).AlignCenter().Text($"Company Registration Number: {company.CroNumber}").FontSize(10);
        });
    }

    private static void ComposeDirectorsReport(
        ColumnDescriptor col,
        AccountingPeriod period,
        List<CompanyOfficer> signingDirectors,
        DirectorsReportService.DirectorsReportData report)
    {
        col.Item().Text("DIRECTORS' REPORT").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();
        col.Item().PaddingTop(10).Text(t =>
        {
            t.Span("The directors present their report and the financial statements of ");
            t.Span(report.CompanyName).Bold();
            t.Span($" for the financial year ended {period.PeriodEnd:dd MMMM yyyy}.");
        });

        col.Item().PaddingTop(10).Text("Principal Activities").Bold();
        col.Item().Text(report.PrincipalActivities);

        col.Item().PaddingTop(10).Text("Results and Dividends").Bold();
        col.Item().Text(report.ResultsAndDividends);

        col.Item().PaddingTop(10).Text("Directors").Bold();
        col.Item().Text("The directors who held office during the financial year were:");
        foreach (var director in report.DirectorServicePeriods)
        {
            var appointed = FormatReportDate(director.AppointedDate);
            var service = director.ResignedDate is { } resigned
                ? $"appointed {appointed}; resigned {FormatReportDate(resigned)}"
                : $"appointed {appointed}; in office at the financial year end";
            col.Item().PaddingLeft(20).Text($"\u2022 {director.Name} ({service})");
        }

        col.Item().PaddingTop(10).Text("Accounting Records").Bold();
        col.Item().Text(report.AccountingRecordsStatement);

        if (!string.IsNullOrWhiteSpace(report.GoingConcernStatement))
        {
            col.Item().PaddingTop(10).Text("Going Concern").Bold();
            col.Item().Text(report.GoingConcernStatement);
        }

        if (!string.IsNullOrWhiteSpace(report.PostBalanceSheetEvents))
        {
            col.Item().PaddingTop(10).Text("Events Since the Balance Sheet Date").Bold();
            col.Item().Text(report.PostBalanceSheetEvents);
        }

        // Statement on relevant audit information \u2014 only when the company is NOT audit-exempt.
        if (!string.IsNullOrWhiteSpace(report.AuditInformationStatement))
        {
            col.Item().PaddingTop(10).Text("Statement on Relevant Audit Information").Bold();
            col.Item().Text(report.AuditInformationStatement);
        }

        col.Item().PaddingTop(20).Text("Signed on behalf of the Board:").Italic();
        col.Item().PaddingTop(30).Text("___________________________");
        if (signingDirectors.Count > 0)
            col.Item().Text(signingDirectors[0].Name).Bold();
        col.Item().Text("Director");
        col.Item().PaddingTop(10).Text($"Date: {ApprovalDateText(period)}");
    }

    // filing-approval-date-persisted: never invent a render date. Final outputs are blocked until the
    // board date is persisted; review surfaces show an explicit pending marker.
    private static string ApprovalDateText(AccountingPeriod period) =>
        period.ApprovalDate is { } approved
            ? approved.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-IE"))
            : "PENDING BOARD APPROVAL";

    private static string FormatReportDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            .ToString("dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-IE"));

    private static void ComposeBalanceSheet(ColumnDescriptor col, Company company, AccountingPeriod period,
        FinancialStatementsService.BalanceSheet bs, FinancialStatementsService.BalanceSheet? priorBs, List<CompanyOfficer> directors)
    {
        col.Item().Text("BALANCE SHEET").Bold().FontSize(14);
        col.Item().Text($"as at {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        col.Item().PaddingTop(15).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4); // Description
                columns.ConstantColumn(20); // Note ref
                columns.ConstantColumn(90); // Current year
                columns.ConstantColumn(90); // Prior year
            });

            // Header row
            table.Cell().Row(1).Column(3).AlignRight().Text($"{period.PeriodEnd:yyyy}").Bold().FontSize(9);
            table.Cell().Row(1).Column(4).AlignRight().Text(priorBs != null ? $"{period.PeriodStart.AddDays(-1):yyyy}" : "").Bold().FontSize(9);
            table.Cell().Row(1).Column(3).AlignRight().PaddingTop(10).Text("\u20ac").Bold().FontSize(9);
            table.Cell().Row(1).Column(4).AlignRight().PaddingTop(10).Text(priorBs != null ? "\u20ac" : "").Bold().FontSize(9);

            uint row = 2;

            // Fixed Assets
            AddBsSection(table, ref row, "FIXED ASSETS", true);
            AddBsLine(table, ref row, "Tangible assets", bs.FixedAssets.Total, priorBs?.FixedAssets.Total);
            AddBsTotal(table, ref row, "", bs.FixedAssets.Total, priorBs?.FixedAssets.Total);

            // Current Assets
            AddBsSection(table, ref row, "CURRENT ASSETS", true);
            if (bs.CurrentAssets.Stock > 0) AddBsLine(table, ref row, "Stock", bs.CurrentAssets.Stock, priorBs?.CurrentAssets.Stock);
            AddBsLine(table, ref row, "Debtors", bs.CurrentAssets.Debtors + bs.CurrentAssets.Prepayments, priorBs != null ? priorBs.CurrentAssets.Debtors + priorBs.CurrentAssets.Prepayments : null);
            AddBsLine(table, ref row, "Cash at bank and in hand", bs.CurrentAssets.Cash, priorBs?.CurrentAssets.Cash);
            AddBsTotal(table, ref row, "", bs.CurrentAssets.Total, priorBs?.CurrentAssets.Total);

            // Creditors within year
            AddBsSection(table, ref row, "CREDITORS: amounts falling due within one year", false);
            AddBsLine(table, ref row, "", -bs.CreditorsWithinYear.Total, priorBs != null ? -priorBs.CreditorsWithinYear.Total : null);

            AddBsTotal(table, ref row, "NET CURRENT ASSETS", bs.NetCurrentAssets, priorBs?.NetCurrentAssets);
            AddBsDoubleTotal(table, ref row, "TOTAL ASSETS LESS CURRENT LIABILITIES", bs.TotalAssetsLessCurrentLiabilities, priorBs?.TotalAssetsLessCurrentLiabilities);

            if (bs.CreditorsAfterYear.Total > 0)
            {
                AddBsSection(table, ref row, "CREDITORS: amounts falling due after more than one year", false);
                AddBsLine(table, ref row, "", -bs.CreditorsAfterYear.Total, priorBs != null ? -priorBs.CreditorsAfterYear.Total : null);
            }

            AddBsDoubleTotal(table, ref row, "NET ASSETS", bs.NetAssets, priorBs?.NetAssets);

            // Capital and Reserves
            AddBsSection(table, ref row, "CAPITAL AND RESERVES", true);
            AddBsLine(table, ref row, "Called up share capital", bs.CapitalAndReserves.ShareCapital, priorBs?.CapitalAndReserves.ShareCapital);
            AddBsLine(table, ref row, "Profit and loss account", bs.CapitalAndReserves.RetainedEarnings, priorBs?.CapitalAndReserves.RetainedEarnings);
            AddBsDoubleTotal(table, ref row, "SHAREHOLDERS' FUNDS", bs.CapitalAndReserves.Total, priorBs?.CapitalAndReserves.Total);

            if (!bs.Balances)
            {
                AddBsLine(table, ref row, "Unreconciled difference requiring review", bs.CapitalAndReserves.UnexplainedDifference, priorBs?.CapitalAndReserves.UnexplainedDifference);
            }
        });

        if (!bs.Balances)
        {
            col.Item().PaddingTop(10).Background(Colors.Orange.Lighten5).Border(1).BorderColor(Colors.Orange.Lighten2).Padding(8)
                .Text($"Preparation warning: this balance sheet has an unreconciled difference of {FormatEuro(bs.CapitalAndReserves.UnexplainedDifference)}. It should not be approved or filed until the underlying records, adjustments, or reserves have been reviewed.")
                .FontSize(8).FontColor(Colors.Orange.Darken4);
        }

        // Signature
        col.Item().PaddingTop(30).Text("The financial statements were approved and authorised for issue by the Board of Directors and were signed on its behalf by:").FontSize(9);
        col.Item().PaddingTop(20).Row(row =>
        {
            if (directors.Count > 0)
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("___________________________");
                    c.Item().Text(directors[0].Name).Bold().FontSize(9);
                    c.Item().Text("Director").FontSize(9);
                });
            }
            if (directors.Count > 1)
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("___________________________");
                    c.Item().Text(directors[1].Name).Bold().FontSize(9);
                    c.Item().Text("Director").FontSize(9);
                });
            }
        });
        col.Item().PaddingTop(10).Text($"Date: {ApprovalDateText(period)}").FontSize(9);
    }

    private static void AddBsSection(TableDescriptor table, ref uint row, string title, bool bold)
    {
        row++;
        table.Cell().Row(row).Column(1).ColumnSpan(4).PaddingTop(10)
            .Text(title).FontSize(9).Bold();
    }

    private static void AddBsLine(TableDescriptor table, ref uint row, string label, decimal current, decimal? prior)
    {
        row++;
        table.Cell().Row(row).Column(1).PaddingLeft(15).Text(label).FontSize(9);
        table.Cell().Row(row).Column(3).AlignRight().Text(FormatEuro(current)).FontSize(9);
        if (prior.HasValue)
            table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(prior.Value)).FontSize(9);
    }

    private static void AddBsTotal(TableDescriptor table, ref uint row, string label, decimal current, decimal? prior)
    {
        row++;
        table.Cell().Row(row).Column(1).Text(label).FontSize(9).Bold();
        table.Cell().Row(row).Column(3).AlignRight().BorderTop(1).Text(FormatEuro(current)).FontSize(9).Bold();
        if (prior.HasValue)
            table.Cell().Row(row).Column(4).AlignRight().BorderTop(1).Text(FormatEuro(prior.Value)).FontSize(9).Bold();
    }

    private static void AddBsDoubleTotal(TableDescriptor table, ref uint row, string label, decimal current, decimal? prior)
    {
        row++;
        table.Cell().Row(row).Column(1).PaddingTop(5).Text(label).FontSize(9).Bold();
        table.Cell().Row(row).Column(3).PaddingTop(5).AlignRight().BorderTop(1).BorderBottom(2)
            .Text(FormatEuro(current)).FontSize(9).Bold();
        if (prior.HasValue)
            table.Cell().Row(row).Column(4).PaddingTop(5).AlignRight().BorderTop(1).BorderBottom(2)
                .Text(FormatEuro(prior.Value)).FontSize(9).Bold();
    }

    private static void ComposeProfitAndLoss(
        ColumnDescriptor col,
        Company company,
        AccountingPeriod period,
        FinancialStatementsService.ProfitAndLoss pl,
        AccountingPeriod? priorPeriod,
        FinancialStatementsService.ProfitAndLoss? priorPl)
    {
        col.Item().Text("PROFIT AND LOSS ACCOUNT").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        col.Item().PaddingTop(15).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.ConstantColumn(80);
                columns.ConstantColumn(80);
            });

            uint row = 1;
            table.Cell().Row(row).Column(2).AlignRight().Text($"{period.PeriodEnd:yyyy}").Bold().FontSize(9);
            if (priorPeriod is not null && priorPl is not null)
                table.Cell().Row(row).Column(3).AlignRight().Text($"{priorPeriod.PeriodEnd:yyyy}").Bold().FontSize(9);

            row++;
            table.Cell().Row(row).Column(2).AlignRight().Text("\u20ac").Bold().FontSize(9);
            if (priorPl is not null)
                table.Cell().Row(row).Column(3).AlignRight().Text("\u20ac").Bold().FontSize(9);

            AddLine("Turnover", pl.Turnover, priorPl?.Turnover);
            AddLine("Cost of sales", -pl.CostOfSales, priorPl is null ? null : -priorPl.CostOfSales);
            AddLine("Gross profit", pl.GrossProfit, priorPl?.GrossProfit, bold: true, topBorder: true);

            if (pl.OtherIncome != 0 || (priorPl?.OtherIncome ?? 0) != 0)
                AddLine("Other operating income", pl.OtherIncome, priorPl?.OtherIncome, paddingTop: 8);

            AddLine("Administrative expenses", -pl.TotalOverheads, priorPl is null ? null : -priorPl.TotalOverheads, paddingTop: 8);
            AddLine("Operating profit", pl.OperatingProfit, priorPl?.OperatingProfit, bold: true, topBorder: true);

            if (pl.InterestPayable > 0 || (priorPl?.InterestPayable ?? 0) > 0)
                AddLine("Interest payable", -pl.InterestPayable, priorPl is null ? null : -priorPl.InterestPayable);

            AddLine("Profit before taxation", pl.ProfitBeforeTax, priorPl?.ProfitBeforeTax, bold: true, topBorder: true, paddingTop: 5);
            AddLine("Tax on profit", -pl.TaxCharge, priorPl is null ? null : -priorPl.TaxCharge);
            AddLine("Profit for the financial year", pl.ProfitAfterTax, priorPl?.ProfitAfterTax, bold: true, topBorder: true, bottomBorder: true, paddingTop: 5);

            void AddLine(
                string label,
                decimal current,
                decimal? prior,
                bool bold = false,
                bool topBorder = false,
                bool bottomBorder = false,
                float paddingTop = 0)
            {
                row++;
                IContainer labelCell = table.Cell().Row(row).Column(1);
                IContainer currentCell = table.Cell().Row(row).Column(2).AlignRight();
                if (paddingTop > 0)
                {
                    labelCell = labelCell.PaddingTop(paddingTop);
                    currentCell = currentCell.PaddingTop(paddingTop);
                }
                if (topBorder)
                    currentCell = currentCell.BorderTop(1);
                if (bottomBorder)
                    currentCell = currentCell.BorderBottom(2);

                var labelText = labelCell.Text(label).FontSize(9);
                var currentText = currentCell.Text(FormatEuro(current)).FontSize(9);
                if (bold)
                {
                    labelText.Bold();
                    currentText.Bold();
                }

                if (prior.HasValue)
                {
                    IContainer priorCell = table.Cell().Row(row).Column(3).AlignRight();
                    if (paddingTop > 0)
                        priorCell = priorCell.PaddingTop(paddingTop);
                    if (topBorder)
                        priorCell = priorCell.BorderTop(1);
                    if (bottomBorder)
                        priorCell = priorCell.BorderBottom(2);
                    var priorText = priorCell.Text(FormatEuro(prior.Value)).FontSize(9);
                    if (bold)
                        priorText.Bold();
                }
            }
        });
    }

    public static bool ShouldIncludeAuditExemptionStatement(ElectedRegime regime, bool auditExempt) =>
        auditExempt && (regime == ElectedRegime.Micro || regime == ElectedRegime.Small || regime == ElectedRegime.SmallAbridged);

    // Medium/Full regimes additionally render a Cash Flow Statement and a Statement of Changes in
    // Equity (FilingRegimeService.GetRequiredStatements).
    public static bool IncludesCashFlowAndEquity(ElectedRegime regime) =>
        regime == ElectedRegime.Medium || regime == ElectedRegime.Full;

    // The primary-statement section headings the accounts package renders for a regime. Kept in step
    // with the composition flow above and with FilingRegimeService.GetRequiredStatements; exposed for
    // testing so the rendered section set can be asserted without parsing the PDF.
    public static List<string> GetIncludedPrimaryStatements(ElectedRegime regime, DocumentPackagePurpose purpose, bool auditExempt)
    {
        var sections = new List<string>();
        if (regime != ElectedRegime.Micro)
            sections.Add("Directors' Report");
        if (!auditExempt)
            sections.Add("Independent Auditor's Report");
        sections.Add("Balance Sheet");
        if (ShouldIncludeProfitAndLoss(regime, purpose))
            sections.Add("Profit and Loss Account");
        if (IncludesCashFlowAndEquity(regime))
        {
            sections.Add("Cash Flow Statement");
            sections.Add("Statement of Changes in Equity");
        }
        sections.Add("Notes to the Financial Statements");
        return sections;
    }

    private static void ComposeAuditorsReport(ColumnDescriptor col, Company company, AccountingPeriod period)
    {
        col.Item().Text("INDEPENDENT AUDITOR'S REPORT").Bold().FontSize(14);
        col.Item().Text($"to the members of {company.LegalName}").FontSize(10).Italic();
        col.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8)
            .Text("TEMPLATE — to be completed and signed by the appointed statutory auditor. This company is not availing of audit exemption, so an audit report is required before the financial statements are approved and filed. The wording below is a standard template only; it is not an audit opinion and must be reviewed, completed and signed by a Registered Auditor.")
            .FontSize(8).FontColor(Colors.Grey.Darken2).Italic();
        if (!string.IsNullOrWhiteSpace(period.AuditorsReportReference))
            col.Item().PaddingTop(6).Text($"Auditor evidence reference recorded in the platform: {period.AuditorsReportReference}.")
                .FontSize(9)
                .Italic();

        col.Item().PaddingTop(10).Text("Opinion").Bold().FontSize(10);
        col.Item().Text($"We have audited the financial statements of {company.LegalName} for the financial year ended {period.PeriodEnd:dd MMMM yyyy}, which comprise the Profit and Loss Account, the Balance Sheet, the Cash Flow Statement, the Statement of Changes in Equity and the related notes, including a summary of significant accounting policies. The financial reporting framework that has been applied in their preparation is Irish law and FRS 102 \"The Financial Reporting Standard applicable in the UK and Republic of Ireland\".").FontSize(9);
        col.Item().PaddingTop(6).Text("In our opinion the financial statements: give a true and fair view of the assets, liabilities and financial position of the company as at the financial year end date and of its profit or loss for the financial year then ended; have been properly prepared in accordance with FRS 102; and have been properly prepared in accordance with the requirements of the Companies Act 2014. [To be confirmed by the auditor.]").FontSize(9);

        col.Item().PaddingTop(10).Text("Basis for Opinion").Bold().FontSize(10);
        col.Item().Text("We conducted our audit in accordance with International Standards on Auditing (Ireland) (ISAs (Ireland)) and applicable law. Our responsibilities under those standards are further described in the Auditor's Responsibilities section of our report. We are independent of the company in accordance with the ethical requirements that are relevant to our audit of the financial statements in Ireland, including the Ethical Standard issued by IAASA, and we have fulfilled our other ethical responsibilities in accordance with these requirements.").FontSize(9);

        col.Item().PaddingTop(10).Text("Respective Responsibilities of Directors and Auditor").Bold().FontSize(10);
        col.Item().Text("As explained more fully in the Statement of Directors' Responsibilities, the directors are responsible for the preparation of the financial statements and for being satisfied that they give a true and fair view. Our objectives are to obtain reasonable assurance about whether the financial statements as a whole are free from material misstatement, whether due to fraud or error, and to issue an auditor's report that includes our opinion.").FontSize(9);

        col.Item().PaddingTop(20).Text("___________________________");
        col.Item().Text("[Name of Statutory Auditor]").Bold().FontSize(9);
        col.Item().Text("Statutory Auditor").FontSize(9);
        col.Item().Text("for and on behalf of [Firm name and address]").FontSize(9);
        col.Item().PaddingTop(6).Text("Date: ___________________").FontSize(9);
    }

    private static void ComposeAttachedAuditorReportNotice(ColumnDescriptor col, Company company, AccountingPeriod period)
    {
        col.Item().Text("INDEPENDENT AUDITOR'S REPORT").Bold().FontSize(14);
        col.Item().Text($"to the members of {company.LegalName}").FontSize(10).Italic();
        col.Item().PaddingTop(12).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(12).Column(notice =>
        {
            notice.Spacing(6);
            notice.Item().Text("SIGNED REPORT RETAINED AS AN IMMUTABLE PDF ATTACHMENT").Bold().FontSize(10);
            notice.Item().Text(
                "The appointed auditor's actual signed report is embedded in this final PDF as the attachment "
                + "signed-auditor-report.pdf. This notice is not an auditor opinion and no platform-generated "
                + "template wording is represented as signed.").FontSize(9);
            notice.Item().Text($"Report reference: {period.AuditorsReportReference}").FontSize(9);
            notice.Item().Text($"Auditor: {period.AuditorsReportSignerName}, {period.AuditorsReportFirmName}").FontSize(9);
            notice.Item().Text($"Signed: {period.AuditorsReportSignedAt:dd MMMM yyyy}").FontSize(9);
            notice.Item().Text($"Retained report SHA-256: {period.AuditorsReportSha256}").FontSize(8);
        });
    }

    private static void ComposeCashFlowStatement(ColumnDescriptor col, Company company, AccountingPeriod period, FinancialStatementsService.CashFlowStatement cf)
    {
        col.Item().Text("CASH FLOW STATEMENT").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        col.Item().PaddingTop(15).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.ConstantColumn(110);
            });

            uint row = 1;
            table.Cell().Row(row).Column(2).AlignRight().Text("€").Bold().FontSize(9);

            row++; table.Cell().Row(row).Column(1).Text("Cash flows from operating activities").FontSize(9).Bold();

            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Operating profit").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.OperatingProfit)).FontSize(9);

            foreach (var adj in cf.OperatingAdjustments)
            {
                row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text(adj.Description).FontSize(9);
                table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(adj.Amount)).FontSize(9);
            }

            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Tax paid").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-cf.TaxPaid)).FontSize(9);

            row++; table.Cell().Row(row).Column(1).Text("Net cash generated from operating activities").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).Text(FormatEuro(cf.NetCashFromOperating)).FontSize(9).Bold();

            row++; table.Cell().Row(row).Column(1).PaddingTop(6).Text("Cash flows from investing activities").FontSize(9).Bold();
            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Purchase of tangible fixed assets").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-cf.CapitalExpenditurePurchases)).FontSize(9);
            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Proceeds from disposal of fixed assets").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.CapitalExpenditureDisposals)).FontSize(9);
            row++; table.Cell().Row(row).Column(1).Text("Net cash used in investing activities").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).Text(FormatEuro(cf.NetCashFromInvesting)).FontSize(9).Bold();

            row++; table.Cell().Row(row).Column(1).PaddingTop(6).Text("Cash flows from financing activities").FontSize(9).Bold();
            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("New loan drawdowns").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.LoanDrawdowns)).FontSize(9);
            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Repayment of borrowings").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-cf.LoanRepayments)).FontSize(9);
            row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Dividends paid").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-cf.DividendsPaid)).FontSize(9);
            if (cf.ShareIssues != 0)
            {
                row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Cash proceeds from share issues").FontSize(9);
                table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.ShareIssues)).FontSize(9);
            }
            if (cf.OtherFinancing != 0)
            {
                row++; table.Cell().Row(row).Column(1).PaddingLeft(15).Text("Other financing cash flows").FontSize(9);
                table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.OtherFinancing)).FontSize(9);
            }
            row++; table.Cell().Row(row).Column(1).Text("Net cash from financing activities").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).Text(FormatEuro(cf.NetCashFromFinancing)).FontSize(9).Bold();

            row++; table.Cell().Row(row).Column(1).PaddingTop(6).Text("Net increase/(decrease) in cash and cash equivalents").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.NetIncreaseInCash)).FontSize(9).Bold();
            row++; table.Cell().Row(row).Column(1).Text("Cash and cash equivalents at beginning of year").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cf.OpeningCash)).FontSize(9);
            row++; table.Cell().Row(row).Column(1).Text("Cash and cash equivalents at end of year").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(cf.ClosingCash)).FontSize(9).Bold();
        });
    }

    private static void ComposeStatementOfChangesInEquity(ColumnDescriptor col, Company company, AccountingPeriod period, FinancialStatementsService.EquityChanges eq)
    {
        col.Item().Text("STATEMENT OF CHANGES IN EQUITY").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        col.Item().PaddingTop(15).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.ConstantColumn(80);
                columns.ConstantColumn(90);
                columns.ConstantColumn(80);
            });

            uint row = 1;
            table.Cell().Row(row).Column(2).AlignRight().Text("Share capital €").Bold().FontSize(8);
            table.Cell().Row(row).Column(3).AlignRight().Text("Retained earnings €").Bold().FontSize(8);
            table.Cell().Row(row).Column(4).AlignRight().Text("Total €").Bold().FontSize(8);

            row++;
            table.Cell().Row(row).Column(1).Text("Balance at beginning of year").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(eq.OpeningShareCapital)).FontSize(9);
            table.Cell().Row(row).Column(3).AlignRight().Text(FormatEuro(eq.OpeningRetainedEarnings)).FontSize(9);
            table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(eq.OpeningTotal)).FontSize(9);

            row++;
            table.Cell().Row(row).Column(1).Text("Profit for the financial year").FontSize(9);
            table.Cell().Row(row).Column(3).AlignRight().Text(FormatEuro(eq.ProfitForYear)).FontSize(9);
            table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(eq.ProfitForYear)).FontSize(9);

            if (eq.SharesIssued != 0)
            {
                row++;
                table.Cell().Row(row).Column(1).Text("Issue of share capital").FontSize(9);
                table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(eq.SharesIssued)).FontSize(9);
                table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(eq.SharesIssued)).FontSize(9);
            }

            if (eq.DividendsPaid != 0)
            {
                row++;
                table.Cell().Row(row).Column(1).Text("Dividends paid").FontSize(9);
                table.Cell().Row(row).Column(3).AlignRight().Text(FormatEuro(-eq.DividendsPaid)).FontSize(9);
                table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(-eq.DividendsPaid)).FontSize(9);
            }

            if (eq.OtherReserveMovements != 0)
            {
                row++;
                table.Cell().Row(row).Column(1).Text("Other posted reserve movements").FontSize(9);
                table.Cell().Row(row).Column(2).AlignRight().Text("-").FontSize(9);
                table.Cell().Row(row).Column(3).AlignRight().Text(FormatEuro(eq.OtherReserveMovements)).FontSize(9);
                table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(eq.OtherReserveMovements)).FontSize(9);
            }

            row++;
            table.Cell().Row(row).Column(1).Text("Balance at end of year").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(eq.ClosingShareCapital)).FontSize(9).Bold();
            table.Cell().Row(row).Column(3).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(eq.ClosingRetainedEarnings)).FontSize(9).Bold();
            table.Cell().Row(row).Column(4).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(eq.ClosingTotal)).FontSize(9).Bold();
        });
    }

    public static bool ShouldIncludeProfitAndLoss(ElectedRegime regime, DocumentPackagePurpose purpose) =>
        purpose != DocumentPackagePurpose.CroFiling
        || regime is not ElectedRegime.Micro and not ElectedRegime.SmallAbridged;

    public static string PackageRegimeSubtitle(ElectedRegime regime, DocumentPackagePurpose purpose) =>
        regime switch
        {
            ElectedRegime.Micro when purpose == DocumentPackagePurpose.CroFiling =>
                "(Reduced CRO filing copy under the Micro Companies Regime; statutory accounts prepared under FRS 105)",
            ElectedRegime.Micro =>
                "(Full statutory accounts prepared under FRS 105 and the Micro Companies Regime)",
            ElectedRegime.SmallAbridged when purpose == DocumentPackagePurpose.CroFiling =>
                "(Abridged Financial Statements for filing with the CRO; derived from FRS 102 Section 1A statutory accounts)",
            ElectedRegime.SmallAbridged =>
                "(Full statutory accounts prepared under FRS 102 Section 1A and the Small Companies Regime)",
            ElectedRegime.Small =>
                "(Full statutory accounts prepared under FRS 102 Section 1A and the Small Companies Regime)",
            ElectedRegime.Medium or ElectedRegime.Full =>
                "(Full statutory accounts prepared under FRS 102)",
            _ => throw new ArgumentOutOfRangeException(nameof(regime), regime, "Unsupported filing regime.")
        };

    private static void ComposeStatutoryStatement(ColumnDescriptor col, Company company, AccountingPeriod period, ElectedRegime regime, List<CompanyOfficer> directors, bool auditExempt)
    {
        if (regime == ElectedRegime.Micro)
        {
            col.Item().Text("STATEMENT REQUIRED BY SECTION 280D OF THE COMPANIES ACT 2014").Bold().FontSize(12);
            col.Item().PaddingTop(10).Text(t =>
            {
                t.Span("The directors acknowledge that the company is availing itself of the exemption provided by Section 280D of the Companies Act 2014 and, as such, these financial statements have been prepared in accordance with FRS 105 ");
                t.Span("\"The Financial Reporting Standard applicable to the Micro-entities Regime\"").Italic();
                t.Span(".");
            });
        }
        else if (regime == ElectedRegime.SmallAbridged)
        {
            col.Item().Text("STATEMENT REQUIRED BY SECTION 352 OF THE COMPANIES ACT 2014").Bold().FontSize(12);
            col.Item().PaddingTop(10).Text("The directors acknowledge their obligations under Section 352 of the Companies Act 2014 to prepare financial statements which give a true and fair view of the assets, liabilities and financial position of the company as at the financial year end date and of the profit or loss of the company for the financial year and to otherwise comply with the provisions of the Companies Act 2014 relating to financial statements so far as they are applicable to the company.");

            col.Item().PaddingTop(10).Text("The directors are availing of the exemption provided by Section 352 of the Companies Act 2014 from filing the full documents otherwise required by Section 347. The annual return filing pack instead includes abridged financial statements and the extracted directors' report information required for a small company.");
        }
        else
        {
            col.Item().Text("STATEMENT OF DIRECTORS' RESPONSIBILITIES").Bold().FontSize(12);
            col.Item().PaddingTop(10).Text("The directors are responsible for preparing the directors' report and the financial statements in accordance with applicable Irish law and regulations. The directors have elected to prepare the financial statements in accordance with FRS 102 \"The Financial Reporting Standard applicable in the UK and Republic of Ireland\".");
        }

        if (ShouldIncludeAuditExemptionStatement(regime, auditExempt))
        {
            col.Item().PaddingTop(15).Text("AUDIT EXEMPTION STATEMENT").Bold().FontSize(12);
            col.Item().PaddingTop(10).Text("The directors have availed of the exemption from the requirement to have the financial statements audited under Section 360 of the Companies Act 2014.");
        }

        col.Item().PaddingTop(20).Text("Signed on behalf of the Board:").Italic();
        col.Item().PaddingTop(25).Row(row =>
        {
            if (directors.Count > 0)
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("___________________________");
                    c.Item().Text(directors[0].Name).Bold().FontSize(9);
                    c.Item().Text("Director").FontSize(9);
                });
            }
            if (directors.Count > 1)
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("___________________________");
                    c.Item().Text(directors[1].Name).Bold().FontSize(9);
                    c.Item().Text("Director").FontSize(9);
                });
            }
        });
        col.Item().PaddingTop(10).Text($"Date: {ApprovalDateText(period)}").FontSize(9);
    }

    private static void ComposeNotes(
        ColumnDescriptor col,
        AccountingPeriod period,
        IReadOnlyCollection<NotesDisclosure> notes)
    {
        col.Item().Text("NOTES TO THE FINANCIAL STATEMENTS").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        var noteNumber = 1;
        foreach (var note in notes.Where(note => note.IsIncluded).OrderBy(note => note.NoteNumber).ThenBy(note => note.Id))
        {
            col.Item().PaddingTop(15).Text($"{noteNumber++}. {note.Title.ToUpperInvariant()}").Bold().FontSize(10);
            if (!string.IsNullOrEmpty(note.Content))
                col.Item().PaddingTop(5).Text(note.Content).FontSize(9);
        }
    }

    private static string FormatEuro(decimal amount) =>
        amount < 0 ? $"({Math.Abs(amount):N0})" : $"{amount:N0}";

    /// <summary>
    /// Generate CRO filing pack — abridged/filleted per regime.
    /// Micro: balance sheet + notes only (no P&L, no directors' report).
    /// SmallAbridged: balance sheet + notes + directors' report (no P&L).
    /// Other regimes: full accounts package.
    /// </summary>
    public async Task<byte[]> GenerateCroFilingPackAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.CroFilingPack,
            () => GenerateCroFilingPackAsync(companyId, periodId, reviewArtifact: false));

    public async Task<byte[]> GenerateCroFilingReviewPackAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.CroFilingPack,
            () => GenerateCroFilingPackAsync(companyId, periodId, reviewArtifact: true));

    private async Task<byte[]> GenerateCroFilingPackAsync(int companyId, int periodId, bool reviewArtifact)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        if (period.FilingRegime == null)
            throw new BusinessRuleException("Confirm the filing regime before generating the CRO filing pack.");

        await AssertFinalDocumentReadinessAsync(companyId, periodId, "CRO filing pack");

        var regime = period.FilingRegime.ElectedRegime;

        // For Medium/Full/Small (non-abridged), CRO pack is the full accounts package.
        if (regime == ElectedRegime.Medium || regime == ElectedRegime.Full || regime == ElectedRegime.Small)
            return await GenerateAccountsPackageAsync(companyId, periodId, DocumentPackagePurpose.CroFiling, reviewArtifact);

        return await GenerateAbridgedCroFilingPackAsync(period, regime, reviewArtifact);
    }

    private async Task<byte[]> GenerateAbridgedCroFilingPackAsync(
        AccountingPeriod period,
        ElectedRegime regime,
        bool reviewArtifact)
    {
        var company = period.Company;
        var balanceSheet = await statementsService.GetBalanceSheetAsync(company.Id, period.Id);
        var notes = await db.NotesDisclosures
            .Where(n => n.PeriodId == period.Id && n.IsIncluded)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();
        if (regime == ElectedRegime.SmallAbridged)
            notes = notes.Where(note => !StatutoryNoteCodes.IsProfitAndLossNote(note.Code)).ToList();
        var directors = company.Officers
            .Where(o => o.Role == OfficerRole.Director && o.ResignedDate == null)
            .ToList();

        // filing-abridged-cro-directors-report: the SmallAbridged CRO pack includes the directors'
        // report (balance sheet + notes + directors' report, no P&L); micro does not.
        DirectorsReportService.DirectorsReportData? directorsReport = regime == ElectedRegime.SmallAbridged
            ? await new DirectorsReportService(db, statementsService).GenerateAsync(company.Id, period.Id)
            : null;

        FinancialStatementsService.BalanceSheet? priorBs = null;
        var priorPeriod = await PeriodChronologyService
            .PriorPeriodQuery(db, company.Id, period.PeriodStart)
            .FirstOrDefaultAsync();
        if (priorPeriod != null)
        {
            try { priorBs = await statementsService.GetBalanceSheetAsync(company.Id, priorPeriod.Id); } catch { }
        }

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginVertical(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                page.Header().Element(c => ComposeArtifactHeader(c, company, reviewArtifact));

                page.Content().Column(col =>
                {
                    col.Spacing(15);

                    col.Item().PaddingTop(70).AlignCenter().Column(c =>
                    {
                        c.Spacing(10);
                        c.Item().AlignCenter().Text(company.LegalName).Bold().FontSize(20);
                        c.Item().AlignCenter().Text(regime == ElectedRegime.Micro
                            ? "CRO FILING PACK - MICRO COMPANY"
                            : "CRO FILING PACK - ABRIDGED FINANCIAL STATEMENTS").Bold().FontSize(13);
                        c.Item().AlignCenter().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(11);
                        if (!string.IsNullOrWhiteSpace(company.CroNumber))
                            c.Item().AlignCenter().Text($"Company Registration Number: {company.CroNumber}").FontSize(9);
                    });

                    col.Item().PageBreak();

                    ComposeCompanyIdentification(col, company);
                    var regimeSubtitle = PackageRegimeSubtitle(regime, DocumentPackagePurpose.CroFiling);
                    if (!string.IsNullOrWhiteSpace(regimeSubtitle))
                        col.Item().PaddingTop(6).Text(regimeSubtitle).Italic().FontSize(9);

                    if (directorsReport != null)
                    {
                        col.Item().PageBreak();
                        ComposeDirectorsReport(col, period, directors, directorsReport);
                    }
                    col.Item().PageBreak();
                    ComposeBalanceSheet(col, company, period, balanceSheet, priorBs, directors);
                    ComposeCroFilingStatements(col, company, regime, balanceSheet, period.FilingRegime?.AuditExempt == true);

                    col.Item().PageBreak();

                    ComposeNotes(col, period, notes);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeCompanyIdentification(ColumnDescriptor col, Company company)
    {
        col.Item().Text("COMPANY IDENTIFICATION").Bold().FontSize(10);
        col.Item().PaddingTop(5).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(140);
                columns.RelativeColumn();
            });

            uint row = 1;
            AddInfoRow(table, ref row, "Name", company.LegalName);
            AddInfoRow(table, ref row, "Legal form", company.CompanyType.ToString());
            AddInfoRow(table, ref row, "Registered number", company.CroNumber ?? "Not recorded");
            AddInfoRow(table, ref row, "Place of registration", "Ireland");
            AddInfoRow(table, ref row, "Registered office", FormatAddress(company));
            AddInfoRow(table, ref row, "Winding up status", "The company is not being wound up.");
        });
    }

    private static void ComposeCroFilingStatements(ColumnDescriptor col, Company company, ElectedRegime regime, FinancialStatementsService.BalanceSheet balanceSheet, bool auditExempt)
    {
        col.Item().PaddingTop(15).Text("CRO FILING STATEMENTS").Bold().FontSize(10);

        if (regime == ElectedRegime.Micro)
        {
            col.Item().PaddingTop(5).Text($"The directors of {company.LegalName} state that the company has relied on the specified exemption contained in section 352 of the Companies Act 2014 on the grounds that it is entitled to the benefit of that exemption as a micro company, and that these abridged financial statements have been prepared from the statutory financial statements in accordance with section 353.").FontSize(8);
            col.Item().PaddingTop(5).Text(auditExempt
                ? "The company is claiming the micro company regime and audit exemption. Directors remain responsible for approving the financial statements and for ensuring the annual return is filed on CORE with the required single-PDF accounts and signature page."
                : "The company is claiming the micro company regime only. The current filing regime does not mark audit exemption as available, so no audit-exemption declaration is included.").FontSize(8);
        }
        else
        {
            col.Item().PaddingTop(5).Text($"The directors of {company.LegalName} state that the company has relied on the specified exemption contained in section 352 of the Companies Act 2014 on the grounds that it is entitled to the benefit of that exemption as a small company, and that these abridged financial statements have been prepared in accordance with section 353.").FontSize(8);
            if (auditExempt)
                col.Item().PaddingTop(5).Text("The company is also claiming audit exemption based on the confirmed filing regime.").FontSize(8);
        }

        if (!balanceSheet.Balances)
        {
            col.Item().PaddingTop(8).Background(Colors.Red.Lighten5).Border(1).BorderColor(Colors.Red.Lighten2).Padding(8)
                .Text("Filing blocker: the balance sheet does not currently balance. Do not upload this CRO pack until the unreconciled difference has been resolved.")
                .FontSize(8).FontColor(Colors.Red.Darken3);
        }
    }

    private static void AddInfoRow(TableDescriptor table, ref uint row, string label, string value)
    {
        table.Cell().Row(row).Column(1).PaddingBottom(3).Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
        table.Cell().Row(row).Column(2).PaddingBottom(3).Text(value).FontSize(8);
        row++;
    }

    private static string FormatAddress(Company company)
    {
        var parts = new[]
        {
            company.RegisteredOfficeAddress1,
            company.RegisteredOfficeAddress2,
            company.RegisteredOfficeCity,
            string.IsNullOrWhiteSpace(company.RegisteredOfficeCounty) ? null : $"Co. {company.RegisteredOfficeCounty}",
            company.RegisteredOfficeEircode
        }.Where(p => !string.IsNullOrWhiteSpace(p));

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Generate a separate signature page PDF for CRO upload.
    /// Contains typeset director/secretary signatures per s.347 Companies Act 2014.
    /// </summary>
    public async Task<byte[]> GenerateSignaturePageAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.SignaturePage,
            () => GenerateSignaturePageAsync(companyId, periodId, reviewArtifact: false));

    public async Task<byte[]> GenerateSignatureReviewPageAsync(int companyId, int periodId)
        => await TrackDocumentAsync(
            DocumentMetricKind.SignaturePage,
            () => GenerateSignaturePageAsync(companyId, periodId, reviewArtifact: true));

    private async Task<byte[]> GenerateSignaturePageAsync(int companyId, int periodId, bool reviewArtifact)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var directors = company.Officers
            .Where(o => o.Role == OfficerRole.Director && o.ResignedDate == null)
            .ToList();
        var secretary = company.Officers
            .FirstOrDefault(o => (o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary) && o.ResignedDate == null);

        if (period.FilingRegime == null)
            throw new BusinessRuleException("Confirm the filing regime before generating the CRO signature page.");
        if (directors.Count == 0)
            throw new BusinessRuleException("Record at least one active director before generating the CRO signature page.");
        if (secretary == null)
            throw new BusinessRuleException("Record an active company secretary before generating the CRO signature page.");

        await AssertFinalDocumentReadinessAsync(companyId, periodId, "CRO signature page");

        var regime = period.FilingRegime.ElectedRegime;
        var approvalDate = ApprovalDateText(period);

        var document = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                if (reviewArtifact)
                    page.Header().Element(ComposeReviewBanner);

                page.Content().Column(col =>
                {
                    col.Item().Text($"{company.LegalName}").Bold().FontSize(14);
                    if (company.CroNumber != null)
                        col.Item().Text($"CRO Number: {company.CroNumber}").FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(5).Text($"Financial Year Ended: {period.PeriodEnd:dd MMMM yyyy}").FontSize(10);

                    col.Item().PaddingTop(30).Text("CRO ACCOUNTS CERTIFICATION").Bold().FontSize(12);
                    col.Item().PaddingTop(10).Text(
                        $"We, the undersigned, being a director and secretary of {company.LegalName}, "
                        + "hereby certify that the financial statements annexed to the annual return "
                        + "are a true copy of those laid before, or to be laid before, the members at "
                        + "the next annual general meeting of the company."
                    ).FontSize(10);

                    col.Item().PaddingTop(30).Text("DIRECTORS:").Bold().FontSize(10);
                    foreach (var director in directors.Take(2))
                    {
                        col.Item().PaddingTop(20).Row(row =>
                        {
                            row.RelativeItem().Column(innerCol =>
                            {
                                innerCol.Item().Text("_________________________").FontSize(10);
                                innerCol.Item().PaddingTop(3).Text(director.Name).Bold().FontSize(10);
                                innerCol.Item().Text("Director").FontSize(9).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }

                    if (secretary != null)
                    {
                        col.Item().PaddingTop(30).Text("SECRETARY:").Bold().FontSize(10);
                        col.Item().PaddingTop(20).Row(row =>
                        {
                            row.RelativeItem().Column(innerCol =>
                            {
                                innerCol.Item().Text("_________________________").FontSize(10);
                                innerCol.Item().PaddingTop(3).Text(secretary.Name).Bold().FontSize(10);
                                innerCol.Item().Text("Secretary").FontSize(9).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }

                    col.Item().PaddingTop(30).Text($"Date of approval: {approvalDate}").FontSize(10);
                });
            });
        });

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }

    private async Task<byte[]> TrackDocumentAsync(DocumentMetricKind kind, Func<Task<byte[]>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;
        try
        {
            var result = await action();
            succeeded = true;
            return result;
        }
        finally
        {
            try { platformMetrics?.RecordDocument(kind, succeeded, stopwatch.Elapsed); }
            catch { /* Telemetry must never change document-generation behavior. */ }
        }
    }

    private async Task AssertFinalDocumentReadinessAsync(int companyId, int periodId, string documentName)
    {
        await statementsService.AssertFinalOutputReadinessAsync(companyId, periodId, documentName);
    }

    private static void ComposeArtifactHeader(IContainer container, Company company, bool reviewArtifact)
    {
        container.Column(column =>
        {
            if (reviewArtifact)
                column.Item().Element(ComposeReviewBanner);
            column.Item().Element(c => ComposeHeader(c, company));
        });
    }

    private static void ComposeReviewBanner(IContainer container)
    {
        container
            .PaddingBottom(6)
            .Background(Colors.Red.Lighten4)
            .Border(1)
            .BorderColor(Colors.Red.Medium)
            .PaddingVertical(5)
            .AlignCenter()
            .Text("DRAFT — NOT FOR FILING")
            .Bold()
            .FontSize(11)
            .FontColor(Colors.Red.Darken2);
    }
}

public enum DocumentPackagePurpose
{
    StatutoryApproval,
    AgmApproval,
    CroFiling
}
