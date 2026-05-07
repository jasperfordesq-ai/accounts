using System.Text;
using System.Globalization;
using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class IxbrlService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    public async Task<byte[]> GenerateIxbrlAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingRegime)
            .FirstAsync(p => p.Id == periodId);

        var company = period.Company;
        var bs = await statementsService.GetBalanceSheetAsync(periodId);
        var pl = await statementsService.GetProfitAndLossAsync(periodId);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
        sb.AppendLine("      xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"");
        sb.AppendLine("      xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2020-02-12\"");
        sb.AppendLine("      xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"");
        sb.AppendLine("      xmlns:iso4217=\"http://www.xbrl.org/2003/iso4217\"");
        sb.AppendLine("      xmlns:ie-common=\"http://xbrl.frc.org.uk/ie/FRS-102/2022-01-01/ie-common\"");
        sb.AppendLine("      xmlns:ie-direp=\"http://xbrl.frc.org.uk/ie/FRS-102/2022-01-01/ie-direp\"");
        sb.AppendLine("      xmlns:core=\"http://xbrl.frc.org.uk/FRS-102/2022-01-01/core\"");
        sb.AppendLine("      xmlns:bus=\"http://xbrl.frc.org.uk/FRS-102/2022-01-01/bus\"");
        sb.AppendLine("      xml:lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine($"<title>{Escape(company.LegalName)} \u2014 Financial Statements {period.PeriodEnd:yyyy}</title>");
        sb.AppendLine("<style>body { font-family: Arial, sans-serif; font-size: 10pt; margin: 40px; } table { border-collapse: collapse; width: 100%; } td, th { padding: 4px 8px; } .amount { text-align: right; } .bold { font-weight: bold; } h1, h2, h3 { margin-top: 20px; }</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Hidden XBRL context
        sb.AppendLine("<ix:header>");
        sb.AppendLine("<ix:hidden>");
        sb.AppendLine("<ix:references>");
        sb.AppendLine("<link:schemaRef xmlns:link=\"http://www.xbrl.org/2003/linkbase\" xlink:type=\"simple\" xlink:href=\"http://xbrl.frc.org.uk/ie/FRS-102/2022-01-01/ie-FRS-102-2022-01-01.xsd\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" />");
        sb.AppendLine("</ix:references>");
        sb.AppendLine("<ix:resources>");
        sb.AppendLine($"<xbrli:context id=\"current\" xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">");
        sb.AppendLine($"  <xbrli:entity><xbrli:identifier scheme=\"http://www.cro.ie/\">{Escape(company.CroNumber ?? "")}</xbrli:identifier></xbrli:entity>");
        sb.AppendLine($"  <xbrli:period><xbrli:startDate>{period.PeriodStart:yyyy-MM-dd}</xbrli:startDate><xbrli:endDate>{period.PeriodEnd:yyyy-MM-dd}</xbrli:endDate></xbrli:period>");
        sb.AppendLine("</xbrli:context>");
        sb.AppendLine($"<xbrli:context id=\"instant\" xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">");
        sb.AppendLine($"  <xbrli:entity><xbrli:identifier scheme=\"http://www.cro.ie/\">{Escape(company.CroNumber ?? "")}</xbrli:identifier></xbrli:entity>");
        sb.AppendLine($"  <xbrli:period><xbrli:instant>{period.PeriodEnd:yyyy-MM-dd}</xbrli:instant></xbrli:period>");
        sb.AppendLine("</xbrli:context>");
        sb.AppendLine("<xbrli:unit id=\"EUR\" xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>");
        sb.AppendLine("</ix:resources>");
        sb.AppendLine("</ix:hidden>");
        sb.AppendLine("</ix:header>");

        // Company info
        sb.AppendLine($"<h1>{Escape(company.LegalName)}</h1>");
        sb.AppendLine($"<h2>Financial Statements for the year ended {period.PeriodEnd:dd MMMM yyyy}</h2>");
        sb.AppendLine("<p><strong>Internal validation note:</strong> this inline XBRL file is generated from the platform's mapped accounts data. External ROS/iXBRL validation remains required before Revenue filing.</p>");
        if (!string.IsNullOrEmpty(company.CroNumber))
            sb.AppendLine($"<p>Company Registration Number: <ix:nonNumeric name=\"ie-common:CompanyRegistrationNumber\" contextRef=\"instant\">{Escape(company.CroNumber)}</ix:nonNumeric></p>");

        // Balance Sheet
        sb.AppendLine("<h2>Balance Sheet</h2>");
        sb.AppendLine($"<p>as at {period.PeriodEnd:dd MMMM yyyy}</p>");
        sb.AppendLine("<table>");
        AddIxbrlRow(sb, "Tangible fixed assets", "core:TangibleFixedAssets", bs.FixedAssets.Total);
        AddIxbrlRow(sb, "Stock", "core:Stocks", bs.CurrentAssets.Stock);
        AddIxbrlRow(sb, "Debtors", "core:Debtors", bs.CurrentAssets.Debtors + bs.CurrentAssets.Prepayments);
        AddIxbrlRow(sb, "Cash at bank and in hand", "core:CashBankInHand", bs.CurrentAssets.Cash);
        AddIxbrlRow(sb, "Total current assets", "core:CurrentAssets", bs.CurrentAssets.Total, true);
        AddIxbrlRow(sb, "Creditors: due within one year", "core:CreditorsAmountsFallingDueWithinOneYear", -bs.CreditorsWithinYear.Total);
        AddIxbrlRow(sb, "Net current assets", "core:NetCurrentAssetsLiabilities", bs.NetCurrentAssets, true);
        AddIxbrlRow(sb, "Total assets less current liabilities", "core:TotalAssetsLessCurrentLiabilities", bs.TotalAssetsLessCurrentLiabilities, true);
        if (bs.CreditorsAfterYear.Total > 0)
            AddIxbrlRow(sb, "Creditors: due after one year", "core:CreditorsAmountsFallingDueAfterOneYear", -bs.CreditorsAfterYear.Total);
        AddIxbrlRow(sb, "Net assets", "core:NetAssetsLiabilities", bs.NetAssets, true);
        sb.AppendLine("<tr><td colspan=\"2\">&nbsp;</td></tr>");
        AddIxbrlRow(sb, "Share capital", "core:CalledUpShareCapital", bs.CapitalAndReserves.ShareCapital);
        AddIxbrlRow(sb, "Profit and loss account", "core:ProfitLossAccountReserve", bs.CapitalAndReserves.RetainedEarnings);
        AddIxbrlRow(sb, "Shareholders' funds", "core:ShareholderFunds", bs.CapitalAndReserves.Total, true);
        sb.AppendLine("</table>");

        // P&L
        sb.AppendLine("<h2>Profit and Loss Account</h2>");
        sb.AppendLine($"<p>for the year ended {period.PeriodEnd:dd MMMM yyyy}</p>");
        sb.AppendLine("<table>");
        AddIxbrlRow(sb, "Turnover", "core:TurnoverGrossRevenue", pl.Turnover);
        AddIxbrlRow(sb, "Cost of sales", "core:CostSales", -pl.CostOfSales);
        AddIxbrlRow(sb, "Gross profit", "core:GrossProfitLoss", pl.GrossProfit, true);
        AddIxbrlRow(sb, "Administrative expenses", "core:AdministrativeExpenses", -pl.TotalOverheads);
        AddIxbrlRow(sb, "Operating profit", "core:OperatingProfitLoss", pl.OperatingProfit, true);
        AddIxbrlRow(sb, "Tax on profit", "core:TaxTaxCreditOnProfitOrLossOnOrdinaryActivities", -pl.TaxCharge);
        AddIxbrlRow(sb, "Profit for the year", "core:ProfitLossForPeriod", pl.ProfitAfterTax, true);
        sb.AppendLine("</table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AddIxbrlRow(StringBuilder sb, string label, string concept, decimal amount, bool bold = false)
    {
        var cls = bold ? " class=\"bold\"" : "";
        var factValue = Math.Round(amount, 0).ToString(CultureInfo.InvariantCulture);
        var displayValue = amount < 0 ? $"({Math.Abs(amount):N0})" : $"{amount:N0}";
        sb.AppendLine($"<tr{cls}>");
        sb.AppendLine($"  <td>{label}</td>");
        sb.AppendLine($"  <td class=\"amount\"><ix:nonFraction name=\"{concept}\" contextRef=\"instant\" unitRef=\"EUR\" decimals=\"0\" format=\"ixt:num-dot-decimal\" title=\"{displayValue}\">{factValue}</ix:nonFraction></td>");
        sb.AppendLine("</tr>");
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? s;
}
