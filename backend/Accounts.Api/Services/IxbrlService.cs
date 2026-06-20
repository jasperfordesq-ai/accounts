using System.Text;
using System.Globalization;
using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class IxbrlService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    private const string TaxonomyDate = "2026-01-01";
    private const string IrishFrs102Namespace = $"https://xbrl.frc.org.uk/ireland/FRS-102/{TaxonomyDate}";
    private const string IrishCommonNamespace = $"https://xbrl.frc.org.uk/ireland/common/{TaxonomyDate}";
    private const string CoreFrs102Namespace = $"http://xbrl.frc.org.uk/FRS-102/{TaxonomyDate}";
    private const string BusinessNamespace = $"http://xbrl.frc.org.uk/general/{TaxonomyDate}/business";
    private const string SchemaRef = $"https://xbrl.frc.org.uk/ireland/FRS-102/{TaxonomyDate}/ie-FRS-102-{TaxonomyDate}.xsd";

    public virtual async Task<byte[]> GenerateFinalIxbrlAsync(int companyId, int periodId)
    {
        var periodBelongsToCompany = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (!periodBelongsToCompany)
            throw new ResourceNotFoundException($"Period {periodId} not found");

        var blockers = (await statementsService.GetFinalOutputReadinessBlockersAsync(companyId, periodId)).Take(9).ToList();
        var package = await db.RevenueFilingPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PeriodId == periodId);

        if (!InternalIxbrlChecksPassed(package))
            blockers.Add("Internal iXBRL checks have not passed");

        blockers = blockers.Distinct().ToList();
        if (blockers.Count > 0)
            throw new BusinessRuleException(
                $"Cannot generate final iXBRL until readiness blockers are resolved: {string.Join("; ", blockers)}");

        return await GenerateIxbrlAsync(companyId, periodId);
    }

    public virtual async Task<byte[]> GenerateIxbrlAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var bs = await statementsService.GetBalanceSheetAsync(period.CompanyId, periodId);
        var pl = await statementsService.GetProfitAndLossAsync(period.CompanyId, periodId);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
        sb.AppendLine("      xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"");
        sb.AppendLine("      xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2020-02-12\"");
        sb.AppendLine("      xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"");
        sb.AppendLine("      xmlns:iso4217=\"http://www.xbrl.org/2003/iso4217\"");
        sb.AppendLine($"      xmlns:ie-FRS-102=\"{IrishFrs102Namespace}\"");
        sb.AppendLine($"      xmlns:ie-common=\"{IrishCommonNamespace}\"");
        sb.AppendLine($"      xmlns:core=\"{CoreFrs102Namespace}\"");
        sb.AppendLine($"      xmlns:bus=\"{BusinessNamespace}\"");
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
        sb.AppendLine($"<link:schemaRef xmlns:link=\"http://www.xbrl.org/2003/linkbase\" xlink:type=\"simple\" xlink:href=\"{SchemaRef}\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" />");
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
        AddIxbrlRow(sb, "Turnover", "core:TurnoverGrossRevenue", pl.Turnover, contextRef: "current");
        AddIxbrlRow(sb, "Cost of sales", "core:CostSales", -pl.CostOfSales, contextRef: "current");
        AddIxbrlRow(sb, "Gross profit", "core:GrossProfitLoss", pl.GrossProfit, bold: true, contextRef: "current");
        if (pl.OtherIncome != 0)
            AddIxbrlRow(sb, "Other operating income", "core:OtherOperatingIncome", pl.OtherIncome, contextRef: "current");
        AddIxbrlRow(sb, "Administrative expenses", "core:AdministrativeExpenses", -pl.TotalOverheads, contextRef: "current");
        AddIxbrlRow(sb, "Operating profit", "core:OperatingProfitLoss", pl.OperatingProfit, bold: true, contextRef: "current");
        if (pl.InterestPayable != 0)
            AddIxbrlRow(sb, "Interest payable and similar charges", "core:InterestPayableSimilarChargesFinanceCosts", -pl.InterestPayable, contextRef: "current");
        AddIxbrlRow(sb, "Profit before taxation", "core:ProfitLossOnOrdinaryActivitiesBeforeTax", pl.ProfitBeforeTax, bold: true, contextRef: "current");
        AddIxbrlRow(sb, "Tax on profit", "core:TaxTaxCreditOnProfitOrLossOnOrdinaryActivities", -pl.TaxCharge, contextRef: "current");
        AddIxbrlRow(sb, "Profit for the year", "core:ProfitLossForPeriod", pl.ProfitAfterTax, bold: true, contextRef: "current");
        sb.AppendLine("</table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AddIxbrlRow(StringBuilder sb, string label, string concept, decimal amount, bool bold = false, string contextRef = "instant")
    {
        var cls = bold ? " class=\"bold\"" : "";
        var factValue = Math.Round(amount, 0).ToString(CultureInfo.InvariantCulture);
        var displayValue = amount < 0 ? $"({Math.Abs(amount):N0})" : $"{amount:N0}";
        sb.AppendLine($"<tr{cls}>");
        sb.AppendLine($"  <td>{label}</td>");
        sb.AppendLine($"  <td class=\"amount\"><ix:nonFraction name=\"{concept}\" contextRef=\"{contextRef}\" unitRef=\"EUR\" decimals=\"0\" format=\"ixt:num-dot-decimal\" title=\"{displayValue}\">{factValue}</ix:nonFraction></td>");
        sb.AppendLine("</tr>");
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static bool InternalIxbrlChecksPassed(Accounts.Api.Entities.RevenueFilingPackage? package) =>
        package?.IxbrlGenerated == true
        && package.IxbrlValidationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) == true;
}
