using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounts.Api.Services;

public class DocumentGeneratorService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    public async Task<byte[]> GenerateAccountsPackageAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId)
            ?? throw new InvalidOperationException("Period not found");

        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Small;
        var balanceSheet = await statementsService.GetBalanceSheetAsync(periodId);
        var pl = await statementsService.GetProfitAndLossAsync(periodId);

        // Get prior year balance sheet if available
        FinancialStatementsService.BalanceSheet? priorBs = null;
        var priorPeriod = await db.AccountingPeriods
            .Where(p => p.CompanyId == company.Id && p.PeriodEnd < period.PeriodStart)
            .OrderByDescending(p => p.PeriodEnd)
            .FirstOrDefaultAsync();
        if (priorPeriod != null)
        {
            try { priorBs = await statementsService.GetBalanceSheetAsync(priorPeriod.Id); } catch { }
        }

        var notes = await db.NotesDisclosures
            .Where(n => n.PeriodId == periodId && n.IsIncluded)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();

        var directors = company.Officers.Where(o => o.Role == OfficerRole.Director && o.ResignedDate == null).ToList();
        var secretary = company.Officers.FirstOrDefault(o => o.Role == OfficerRole.Secretary && o.ResignedDate == null);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginVertical(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                page.Header().Element(c => ComposeHeader(c, company));

                page.Content().Element(c =>
                {
                    c.Column(col =>
                    {
                        col.Spacing(15);

                        // Cover page info
                        ComposeCoverPage(col, company, period, regime);

                        col.Item().PageBreak();

                        // Directors' Report (unless micro)
                        if (regime != ElectedRegime.Micro)
                        {
                            ComposeDirectorsReport(col, company, period, directors, pl);
                            col.Item().PageBreak();
                        }

                        // Balance Sheet
                        ComposeBalanceSheet(col, company, period, balanceSheet, priorBs, directors);

                        col.Item().PageBreak();

                        // Profit and Loss (unless micro or abridged CRO filing)
                        if (regime != ElectedRegime.Micro && regime != ElectedRegime.SmallAbridged)
                        {
                            ComposeProfitAndLoss(col, company, period, pl);
                            col.Item().PageBreak();
                        }

                        // Statutory statement
                        ComposeStatutoryStatement(col, company, period, regime, directors);

                        col.Item().PageBreak();

                        // Notes
                        ComposeNotes(col, company, period, regime, balanceSheet, notes);
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

    private static void ComposeCoverPage(ColumnDescriptor col, Company company, AccountingPeriod period, ElectedRegime regime)
    {
        col.Item().PaddingTop(100).AlignCenter().Column(c =>
        {
            c.Spacing(10);
            c.Item().AlignCenter().Text(company.LegalName).Bold().FontSize(22);
            if (!string.IsNullOrEmpty(company.TradingName))
                c.Item().AlignCenter().Text($"(trading as {company.TradingName})").FontSize(12).Italic();

            c.Item().PaddingTop(20).AlignCenter().Text("FINANCIAL STATEMENTS").Bold().FontSize(14);

            c.Item().PaddingTop(10).AlignCenter().Text(
                $"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(12);

            c.Item().PaddingTop(30).AlignCenter().Text(regime switch
            {
                ElectedRegime.Micro => "(Prepared under the Micro Companies Regime)",
                ElectedRegime.SmallAbridged => "(Abridged Financial Statements for filing with the CRO)",
                ElectedRegime.Small => "(Prepared under the Small Companies Regime)",
                _ => ""
            }).FontSize(10).Italic().FontColor(Colors.Grey.Darken1);

            if (!string.IsNullOrEmpty(company.CroNumber))
                c.Item().PaddingTop(20).AlignCenter().Text($"Company Registration Number: {company.CroNumber}").FontSize(10);
        });
    }

    private static void ComposeDirectorsReport(ColumnDescriptor col, Company company, AccountingPeriod period, List<CompanyOfficer> directors, FinancialStatementsService.ProfitAndLoss pl)
    {
        col.Item().Text("DIRECTORS' REPORT").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();
        col.Item().PaddingTop(10).Text(t =>
        {
            t.Span("The directors present their report and the financial statements of ");
            t.Span(company.LegalName).Bold();
            t.Span($" for the financial year ended {period.PeriodEnd:dd MMMM yyyy}.");
        });

        col.Item().PaddingTop(10).Text("Principal Activities").Bold();
        col.Item().Text("The principal activity of the company during the financial year was its normal trading activities. There were no significant changes in the nature of the company's principal activities during the financial year.");

        col.Item().PaddingTop(10).Text("Results and Dividends").Bold();
        col.Item().Text($"The profit for the financial year after providing for corporation tax amounted to \u20ac{pl.ProfitAfterTax:N0}.");

        col.Item().PaddingTop(10).Text("Directors").Bold();
        col.Item().Text("The directors who held office during the financial year were:");
        foreach (var d in directors)
            col.Item().PaddingLeft(20).Text($"\u2022 {d.Name}");

        col.Item().PaddingTop(10).Text("Accounting Records").Bold();
        col.Item().Text("The directors acknowledge their responsibilities under Sections 281 to 285 of the Companies Act 2014 to keep adequate accounting records for the company.");

        col.Item().PaddingTop(10).Text("Statement on Relevant Audit Information").Bold();
        col.Item().Text("Each of the persons who are directors at the time when this Directors' Report is approved has confirmed that, so far as that director is aware, there is no relevant audit information of which the company's auditors are unaware, and the director has taken all the steps that ought to have been taken as a director in order to be aware of any relevant audit information and to establish that the company's auditors are aware of that information.");

        col.Item().PaddingTop(20).Text("Signed on behalf of the Board:").Italic();
        col.Item().PaddingTop(30).Text("___________________________");
        if (directors.Count > 0) col.Item().Text(directors[0].Name).Bold();
        col.Item().Text("Director");
        col.Item().PaddingTop(10).Text($"Date: {DateTime.Now:dd MMMM yyyy}");
    }

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
        col.Item().PaddingTop(10).Text($"Date: {DateTime.Now:dd MMMM yyyy}").FontSize(9);
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

    private static void ComposeProfitAndLoss(ColumnDescriptor col, Company company, AccountingPeriod period, FinancialStatementsService.ProfitAndLoss pl)
    {
        col.Item().Text("PROFIT AND LOSS ACCOUNT").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        col.Item().PaddingTop(15).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.ConstantColumn(100);
            });

            uint row = 1;
            table.Cell().Row(row).Column(2).AlignRight().Text("\u20ac").Bold().FontSize(9);

            row++; table.Cell().Row(row).Column(1).Text("Turnover").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(pl.Turnover)).FontSize(9);

            row++; table.Cell().Row(row).Column(1).Text("Cost of sales").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-pl.CostOfSales)).FontSize(9);

            row++; table.Cell().Row(row).Column(1).Text("Gross profit").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).Text(FormatEuro(pl.GrossProfit)).FontSize(9).Bold();

            row++; table.Cell().Row(row).Column(1).PaddingTop(8).Text("Administrative expenses").FontSize(9);
            table.Cell().Row(row).Column(2).PaddingTop(8).AlignRight().Text(FormatEuro(-pl.TotalOverheads)).FontSize(9);

            row++; table.Cell().Row(row).Column(1).Text("Operating profit").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).Text(FormatEuro(pl.OperatingProfit)).FontSize(9).Bold();

            if (pl.InterestPayable > 0)
            {
                row++; table.Cell().Row(row).Column(1).Text("Interest payable").FontSize(9);
                table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-pl.InterestPayable)).FontSize(9);
            }

            row++; table.Cell().Row(row).Column(1).PaddingTop(5).Text("Profit before taxation").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).PaddingTop(5).AlignRight().BorderTop(1).Text(FormatEuro(pl.ProfitBeforeTax)).FontSize(9).Bold();

            row++; table.Cell().Row(row).Column(1).Text("Tax on profit").FontSize(9);
            table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(-pl.TaxCharge)).FontSize(9);

            row++; table.Cell().Row(row).Column(1).PaddingTop(5).Text("Profit for the financial year").FontSize(9).Bold();
            table.Cell().Row(row).Column(2).PaddingTop(5).AlignRight().BorderTop(1).BorderBottom(2)
                .Text(FormatEuro(pl.ProfitAfterTax)).FontSize(9).Bold();
        });
    }

    private static void ComposeStatutoryStatement(ColumnDescriptor col, Company company, AccountingPeriod period, ElectedRegime regime, List<CompanyOfficer> directors)
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

            col.Item().PaddingTop(10).Text("The directors are availing of the exemption provided by Section 352(1)(b) of the Companies Act 2014 from the obligation to file abridged financial statements with the Registrar of Companies.");
        }
        else
        {
            col.Item().Text("STATEMENT OF DIRECTORS' RESPONSIBILITIES").Bold().FontSize(12);
            col.Item().PaddingTop(10).Text("The directors are responsible for preparing the directors' report and the financial statements in accordance with applicable Irish law and regulations. The directors have elected to prepare the financial statements in accordance with FRS 102 \"The Financial Reporting Standard applicable in the UK and Republic of Ireland\".");
        }

        // Audit exemption statement (for small companies)
        if (regime == ElectedRegime.Micro || regime == ElectedRegime.Small || regime == ElectedRegime.SmallAbridged)
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
        col.Item().PaddingTop(10).Text($"Date: {DateTime.Now:dd MMMM yyyy}").FontSize(9);
    }

    private static void ComposeNotes(ColumnDescriptor col, Company company, AccountingPeriod period, ElectedRegime regime,
        FinancialStatementsService.BalanceSheet bs, List<NotesDisclosure> notes)
    {
        col.Item().Text("NOTES TO THE FINANCIAL STATEMENTS").Bold().FontSize(14);
        col.Item().Text($"for the financial year ended {period.PeriodEnd:dd MMMM yyyy}").FontSize(10).Italic();

        int noteNum = 1;

        // Note 1: Accounting Policies
        col.Item().PaddingTop(15).Text($"{noteNum}. ACCOUNTING POLICIES").Bold().FontSize(10);
        col.Item().PaddingTop(5).Text("Basis of Preparation").Bold().FontSize(9);
        if (regime == ElectedRegime.Micro)
        {
            col.Item().Text("The financial statements have been prepared on the going concern basis and in accordance with FRS 105 \"The Financial Reporting Standard applicable to the Micro-entities Regime\" and the Companies Act 2014.").FontSize(9);
        }
        else
        {
            col.Item().Text("The financial statements have been prepared on the going concern basis and in accordance with FRS 102 \"The Financial Reporting Standard applicable in the UK and Republic of Ireland\" and the Companies Act 2014.").FontSize(9);
        }

        if (bs.FixedAssets.Total > 0)
        {
            col.Item().PaddingTop(5).Text("Tangible Fixed Assets and Depreciation").Bold().FontSize(9);
            col.Item().Text("Tangible fixed assets are stated at cost less accumulated depreciation. Depreciation is provided at rates calculated to write off the cost of each asset over its expected useful life.").FontSize(9);
        }

        noteNum++;

        // Note 2: Tangible Fixed Assets
        if (bs.FixedAssets.Categories.Count > 0)
        {
            col.Item().PaddingTop(15).Text($"{noteNum}. TANGIBLE FIXED ASSETS").Bold().FontSize(10);
            col.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                uint row = 1;
                table.Cell().Row(row).Column(1).Text("").FontSize(8);
                table.Cell().Row(row).Column(2).AlignRight().Text("Cost \u20ac").Bold().FontSize(8);
                table.Cell().Row(row).Column(3).AlignRight().Text("Depreciation \u20ac").Bold().FontSize(8);
                table.Cell().Row(row).Column(4).AlignRight().Text("NBV \u20ac").Bold().FontSize(8);

                foreach (var cat in bs.FixedAssets.Categories)
                {
                    row++;
                    table.Cell().Row(row).Column(1).Text(cat.Category).FontSize(9);
                    table.Cell().Row(row).Column(2).AlignRight().Text(FormatEuro(cat.Cost)).FontSize(9);
                    table.Cell().Row(row).Column(3).AlignRight().Text(FormatEuro(cat.Depreciation)).FontSize(9);
                    table.Cell().Row(row).Column(4).AlignRight().Text(FormatEuro(cat.Nbv)).FontSize(9);
                }

                row++;
                table.Cell().Row(row).Column(1).Text("Total").FontSize(9).Bold();
                table.Cell().Row(row).Column(2).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(bs.FixedAssets.Categories.Sum(c => c.Cost))).FontSize(9).Bold();
                table.Cell().Row(row).Column(3).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(bs.FixedAssets.Categories.Sum(c => c.Depreciation))).FontSize(9).Bold();
                table.Cell().Row(row).Column(4).AlignRight().BorderTop(1).BorderBottom(2).Text(FormatEuro(bs.FixedAssets.Total)).FontSize(9).Bold();
            });
            noteNum++;
        }

        // Note: Approval
        col.Item().PaddingTop(15).Text($"{noteNum}. APPROVAL OF FINANCIAL STATEMENTS").Bold().FontSize(10);
        col.Item().PaddingTop(5).Text($"The financial statements were approved and authorised for issue by the Board of Directors on {DateTime.Now:dd MMMM yyyy}.").FontSize(9);

        // Custom notes
        foreach (var note in notes)
        {
            noteNum++;
            col.Item().PaddingTop(15).Text($"{noteNum}. {note.Title.ToUpper()}").Bold().FontSize(10);
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
    public async Task<byte[]> GenerateCroFilingPackAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId)
            ?? throw new InvalidOperationException($"Period {periodId} not found");

        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Full;

        // For Medium/Full/Small (non-abridged), CRO pack is the full accounts package.
        if (regime == ElectedRegime.Medium || regime == ElectedRegime.Full || regime == ElectedRegime.Small)
            return await GenerateAccountsPackageAsync(periodId);

        return await GenerateAbridgedCroFilingPackAsync(period, regime);
    }

    private async Task<byte[]> GenerateAbridgedCroFilingPackAsync(AccountingPeriod period, ElectedRegime regime)
    {
        var company = period.Company;
        var balanceSheet = await statementsService.GetBalanceSheetAsync(period.Id);
        var notes = await db.NotesDisclosures
            .Where(n => n.PeriodId == period.Id && n.IsIncluded)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();
        var directors = company.Officers
            .Where(o => o.Role == OfficerRole.Director && o.ResignedDate == null)
            .ToList();

        FinancialStatementsService.BalanceSheet? priorBs = null;
        var priorPeriod = await db.AccountingPeriods
            .Where(p => p.CompanyId == company.Id && p.PeriodEnd < period.PeriodStart)
            .OrderByDescending(p => p.PeriodEnd)
            .FirstOrDefaultAsync();
        if (priorPeriod != null)
        {
            try { priorBs = await statementsService.GetBalanceSheetAsync(priorPeriod.Id); } catch { }
        }

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginVertical(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                page.Header().Element(c => ComposeHeader(c, company));

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
                    ComposeBalanceSheet(col, company, period, balanceSheet, priorBs, directors);
                    ComposeCroFilingStatements(col, company, regime, balanceSheet);

                    col.Item().PageBreak();

                    ComposeNotes(col, company, period, regime, balanceSheet, notes);
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

    private static void ComposeCroFilingStatements(ColumnDescriptor col, Company company, ElectedRegime regime, FinancialStatementsService.BalanceSheet balanceSheet)
    {
        col.Item().PaddingTop(15).Text("CRO FILING STATEMENTS").Bold().FontSize(10);

        if (regime == ElectedRegime.Micro)
        {
            col.Item().PaddingTop(5).Text($"The directors of {company.LegalName} state that the company has relied on the specified exemption contained in section 352 of the Companies Act 2014 on the grounds that it is entitled to the benefit of that exemption as a micro company, and that these abridged financial statements have been prepared from the statutory financial statements in accordance with section 353.").FontSize(8);
            col.Item().PaddingTop(5).Text("The company is claiming the micro company regime and, where the audit exemption conditions are satisfied, the audit exemption. Directors remain responsible for approving the financial statements and for ensuring the annual return is filed on CORE with the required single-PDF accounts and signature page.").FontSize(8);
        }
        else
        {
            col.Item().PaddingTop(5).Text($"The directors of {company.LegalName} state that the company has relied on the specified exemption contained in section 352 of the Companies Act 2014 on the grounds that it is entitled to the benefit of that exemption as a small company, and that these abridged financial statements have been prepared in accordance with section 353.").FontSize(8);
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
    public async Task<byte[]> GenerateSignaturePageAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId)
            ?? throw new InvalidOperationException($"Period {periodId} not found");

        var company = period.Company;
        var directors = company.Officers.Where(o => o.Role == OfficerRole.Director).ToList();
        var secretary = company.Officers.FirstOrDefault(o => o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary);
        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Full;
        var approvalDate = DateTime.UtcNow;

        var document = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                page.Content().Column(col =>
                {
                    col.Item().Text($"{company.LegalName}").Bold().FontSize(14);
                    if (company.CroNumber != null)
                        col.Item().Text($"CRO Number: {company.CroNumber}").FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(5).Text($"Financial Year Ended: {period.PeriodEnd:dd MMMM yyyy}").FontSize(10);

                    col.Item().PaddingTop(30).Text("CERTIFICATE").Bold().FontSize(12);
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

                    col.Item().PaddingTop(30).Text($"Date of approval: {approvalDate:dd MMMM yyyy}").FontSize(10);
                });
            });
        });

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }
}
