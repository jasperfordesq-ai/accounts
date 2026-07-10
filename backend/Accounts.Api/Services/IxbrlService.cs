using System.Text;
using System.Globalization;
using System.Diagnostics;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class IxbrlService(
    AccountsDbContext db,
    FinancialStatementsService statementsService,
    FilingReleaseGate? releaseGate = null,
    PlatformMetrics? platformMetrics = null)
{
    public virtual async Task<byte[]> GenerateFinalIxbrlAsync(int companyId, int periodId)
    {
        var artifact = await (releaseGate ?? new FilingReleaseGate(db))
            .GetFinalArtifactAsync(companyId, periodId, FilingReleaseArtifact.RevenueIxbrl);
        return artifact.Content;
    }

    public virtual async Task<byte[]> GenerateReviewIxbrlAsync(int companyId, int periodId)
    {
        // The only platform-produced Revenue document is deliberately a review prototype.
        // GenerateIxbrlAsync applies the conspicuous marker at the source so no caller can
        // obtain an unmarked version by bypassing this convenience method.
        return await GenerateIxbrlAsync(companyId, periodId);
    }

    public virtual async Task<byte[]> GenerateIxbrlAsync(int companyId, int periodId)
        => await TrackDocumentAsync(() => GenerateIxbrlCoreAsync(companyId, periodId));

    private async Task<byte[]> GenerateIxbrlCoreAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var taxonomy = RevenueIxbrlTaxonomySelector.Select(period.PeriodStart, period.FilingRegime?.ElectedRegime);
        if (!taxonomy.AcceptedByRevenue)
            throw new BusinessRuleException(
                $"Cannot generate iXBRL for period start {period.PeriodStart:yyyy-MM-dd} because no Revenue-accepted taxonomy is pinned for that effective date.");

        var irishFrs102Namespace = $"https://xbrl.frc.org.uk/ireland/FRS-102/{taxonomy.TaxonomyDate}";
        var irishCommonNamespace = $"https://xbrl.frc.org.uk/ireland/common/{taxonomy.TaxonomyDate}";
        var coreFrs102Namespace = $"http://xbrl.frc.org.uk/FRS-102/{taxonomy.TaxonomyDate}";
        var businessNamespace = $"http://xbrl.frc.org.uk/general/{taxonomy.TaxonomyDate}/business";
        var bs = await statementsService.GetBalanceSheetAsync(period.CompanyId, periodId);
        var pl = await statementsService.GetProfitAndLossAsync(period.CompanyId, periodId);

        // Prior-year comparatives (BL-08). ROS/CRO reject a single-year instance; the filed iXBRL
        // must carry a comparative column tagged against prior-period contexts.
        var priorPeriod = await PeriodChronologyService
            .PriorPeriodQuery(db, period.CompanyId, period.PeriodStart)
            .FirstOrDefaultAsync();
        FinancialStatementsService.BalanceSheet? priorBs = null;
        FinancialStatementsService.ProfitAndLoss? priorPl = null;
        if (priorPeriod != null)
        {
            try { priorBs = await statementsService.GetBalanceSheetAsync(period.CompanyId, priorPeriod.Id); } catch { /* prior period not computable — file current year only */ }
            try { priorPl = await statementsService.GetProfitAndLossAsync(period.CompanyId, priorPeriod.Id); } catch { }
        }
        var hasPrior = priorPeriod != null && priorBs != null && priorPl != null;
        if (!hasPrior) { priorBs = null; priorPl = null; }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
        sb.AppendLine("      xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"");
        sb.AppendLine("      xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2020-02-12\"");
        sb.AppendLine("      xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"");
        sb.AppendLine("      xmlns:iso4217=\"http://www.xbrl.org/2003/iso4217\"");
        sb.AppendLine($"      xmlns:ie-FRS-102=\"{irishFrs102Namespace}\"");
        sb.AppendLine($"      xmlns:ie-common=\"{irishCommonNamespace}\"");
        sb.AppendLine($"      xmlns:core=\"{coreFrs102Namespace}\"");
        sb.AppendLine($"      xmlns:bus=\"{businessNamespace}\"");
        sb.AppendLine("      xml:lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine($"<title>{Escape(company.LegalName)} \u2014 Financial Statements {period.PeriodEnd:yyyy}</title>");
        sb.AppendLine("<style>body { font-family: Arial, sans-serif; font-size: 10pt; margin: 40px; } table { border-collapse: collapse; width: 100%; } td, th { padding: 4px 8px; } .amount { text-align: right; } .bold { font-weight: bold; } h1, h2, h3 { margin-top: 20px; }</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div role=\"status\" data-artifact-status=\"draft-not-for-filing\" data-generation-support=\"manual-handoff-only\" style=\"border:3px solid #b91c1c;background:#fee2e2;color:#7f1d1d;padding:12px;margin-bottom:18px;text-align:center;font-weight:bold;font-size:16pt\">DRAFT - NOT FOR FILING - INCOMPLETE REVIEW PROTOTYPE</div>");

        // Hidden XBRL context
        sb.AppendLine("<ix:header>");
        sb.AppendLine("<ix:hidden>");
        sb.AppendLine("<ix:references>");
        sb.AppendLine($"<link:schemaRef xmlns:link=\"http://www.xbrl.org/2003/linkbase\" xlink:type=\"simple\" xlink:href=\"{taxonomy.SchemaRef}\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" />");
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
        if (hasPrior)
        {
            sb.AppendLine($"<xbrli:context id=\"prior\" xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">");
            sb.AppendLine($"  <xbrli:entity><xbrli:identifier scheme=\"http://www.cro.ie/\">{Escape(company.CroNumber ?? "")}</xbrli:identifier></xbrli:entity>");
            sb.AppendLine($"  <xbrli:period><xbrli:startDate>{priorPeriod!.PeriodStart:yyyy-MM-dd}</xbrli:startDate><xbrli:endDate>{priorPeriod.PeriodEnd:yyyy-MM-dd}</xbrli:endDate></xbrli:period>");
            sb.AppendLine("</xbrli:context>");
            sb.AppendLine($"<xbrli:context id=\"priorInstant\" xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">");
            sb.AppendLine($"  <xbrli:entity><xbrli:identifier scheme=\"http://www.cro.ie/\">{Escape(company.CroNumber ?? "")}</xbrli:identifier></xbrli:entity>");
            sb.AppendLine($"  <xbrli:period><xbrli:instant>{priorPeriod.PeriodEnd:yyyy-MM-dd}</xbrli:instant></xbrli:period>");
            sb.AppendLine("</xbrli:context>");
        }
        sb.AppendLine("<xbrli:unit id=\"EUR\" xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>");
        sb.AppendLine("</ix:resources>");
        sb.AppendLine("</ix:hidden>");
        sb.AppendLine("</ix:header>");

        // Company info
        sb.AppendLine($"<h1>{Escape(company.LegalName)}</h1>");
        sb.AppendLine($"<h2>Financial Statements for the year ended {period.PeriodEnd:dd MMMM yyyy}</h2>");
        sb.AppendLine($"<p><strong>Manual handoff required:</strong> {Escape(RevenueIxbrlGenerationPolicy.ManualHandoffReason)}</p>");
        if (!string.IsNullOrEmpty(company.CroNumber))
            sb.AppendLine($"<p>Company Registration Number: <ix:nonNumeric name=\"ie-common:CompanyRegistrationNumber\" contextRef=\"instant\">{Escape(company.CroNumber)}</ix:nonNumeric></p>");

        // Entity and report metadata (BL-09) — tagged so the instance carries the filer identity and
        // the period it covers, not just the financial figures.
        sb.AppendLine($"<p>Entity: <ix:nonNumeric name=\"bus:EntityCurrentLegalOrRegisteredName\" contextRef=\"instant\">{Escape(company.LegalName)}</ix:nonNumeric></p>");
        sb.AppendLine($"<p>Period of report: <ix:nonNumeric name=\"bus:StartDateForPeriodCoveredByReport\" contextRef=\"current\">{period.PeriodStart:yyyy-MM-dd}</ix:nonNumeric> to <ix:nonNumeric name=\"bus:EndDateForPeriodCoveredByReport\" contextRef=\"current\">{period.PeriodEnd:yyyy-MM-dd}</ix:nonNumeric></p>");

        // Rounded projections so the tagged subtotals cross-add against their rounded components.
        var rbs = RoundBalanceSheet(bs);
        var rpbs = priorBs != null ? RoundBalanceSheet(priorBs) : null;

        // Balance Sheet
        sb.AppendLine("<h2>Balance Sheet</h2>");
        sb.AppendLine($"<p>as at {period.PeriodEnd:dd MMMM yyyy}</p>");
        sb.AppendLine("<table>");
        if (hasPrior)
            sb.AppendLine($"<tr class=\"bold\"><td></td><td class=\"amount\">{period.PeriodEnd:yyyy}</td><td class=\"amount\">{priorPeriod!.PeriodEnd:yyyy}</td></tr>");
        AddIxbrlRow(sb, "Tangible fixed assets", "core:TangibleFixedAssets", rbs.FixedAssets, priorAmount: rpbs?.FixedAssets);
        AddIxbrlRow(sb, "Stock", "core:Stocks", rbs.Stock, priorAmount: rpbs?.Stock);
        AddIxbrlRow(sb, "Debtors", "core:Debtors", rbs.Debtors, priorAmount: rpbs?.Debtors);
        AddIxbrlRow(sb, "Cash at bank and in hand", "core:CashBankInHand", rbs.Cash, priorAmount: rpbs?.Cash);
        AddIxbrlRow(sb, "Total current assets", "core:CurrentAssets", rbs.CurrentAssetsTotal, true, priorAmount: rpbs?.CurrentAssetsTotal);
        AddIxbrlRow(sb, "Creditors: due within one year", "core:CreditorsAmountsFallingDueWithinOneYear", -rbs.CreditorsWithin, priorAmount: rpbs != null ? -rpbs.CreditorsWithin : null);
        AddIxbrlRow(sb, "Net current assets", "core:NetCurrentAssetsLiabilities", rbs.NetCurrentAssets, true, priorAmount: rpbs?.NetCurrentAssets);
        AddIxbrlRow(sb, "Total assets less current liabilities", "core:TotalAssetsLessCurrentLiabilities", rbs.TotalAssetsLessCurrent, true, priorAmount: rpbs?.TotalAssetsLessCurrent);
        if (rbs.CreditorsAfter > 0)
            AddIxbrlRow(sb, "Creditors: due after one year", "core:CreditorsAmountsFallingDueAfterOneYear", -rbs.CreditorsAfter, priorAmount: rpbs != null ? -rpbs.CreditorsAfter : null);
        AddIxbrlRow(sb, "Net assets", "core:NetAssetsLiabilities", rbs.NetAssets, true, priorAmount: rpbs?.NetAssets);
        sb.AppendLine("<tr><td colspan=\"3\">&#160;</td></tr>");
        AddIxbrlRow(sb, "Share capital", "core:CalledUpShareCapital", rbs.ShareCapital, priorAmount: rpbs?.ShareCapital);
        AddIxbrlRow(sb, "Profit and loss account", "core:ProfitLossAccountReserve", rbs.RetainedEarnings, priorAmount: rpbs?.RetainedEarnings);
        AddIxbrlRow(sb, "Shareholders' funds", "core:ShareholderFunds", rbs.ShareholdersFunds, true, priorAmount: rpbs?.ShareholdersFunds);
        sb.AppendLine("</table>");

        // Revenue receives private filing data. CRO Micro/abridgement presentation exemptions must
        // never remove Revenue-required P&L or Detailed P&L facts from the review prototype.
        // P&L
        sb.AppendLine("<h2>Profit and Loss Account</h2>");
        sb.AppendLine($"<p>for the year ended {period.PeriodEnd:dd MMMM yyyy}</p>");
        sb.AppendLine("<table>");
        if (hasPrior)
            sb.AppendLine($"<tr class=\"bold\"><td></td><td class=\"amount\">{period.PeriodEnd:yyyy}</td><td class=\"amount\">{priorPeriod!.PeriodEnd:yyyy}</td></tr>");
        var rpl = RoundProfitAndLoss(pl);
        var rppl = priorPl != null ? RoundProfitAndLoss(priorPl) : null;
        AddIxbrlRow(sb, "Turnover", "core:TurnoverGrossRevenue", rpl.Turnover, contextRef: "current", priorAmount: rppl?.Turnover, priorContextRef: "prior");
        AddIxbrlRow(sb, "Cost of sales", "core:CostSales", -rpl.CostOfSales, contextRef: "current", priorAmount: rppl != null ? -rppl.CostOfSales : null, priorContextRef: "prior");
        AddIxbrlRow(sb, "Gross profit", "core:GrossProfitLoss", rpl.GrossProfit, bold: true, contextRef: "current", priorAmount: rppl?.GrossProfit, priorContextRef: "prior");
        if (rpl.OtherIncome != 0)
            AddIxbrlRow(sb, "Other operating income", "core:OtherOperatingIncome", rpl.OtherIncome, contextRef: "current", priorAmount: rppl?.OtherIncome, priorContextRef: "prior");
        AddIxbrlRow(sb, "Administrative expenses", "core:AdministrativeExpenses", -rpl.TotalOverheads, contextRef: "current", priorAmount: rppl != null ? -rppl.TotalOverheads : null, priorContextRef: "prior");
        AddIxbrlRow(sb, "Operating profit", "core:OperatingProfitLoss", rpl.OperatingProfit, bold: true, contextRef: "current", priorAmount: rppl?.OperatingProfit, priorContextRef: "prior");
        if (rpl.InterestPayable != 0)
            AddIxbrlRow(sb, "Interest payable and similar charges", "core:InterestPayableSimilarChargesFinanceCosts", -rpl.InterestPayable, contextRef: "current", priorAmount: rppl != null ? -rppl.InterestPayable : null, priorContextRef: "prior");
        AddIxbrlRow(sb, "Profit before taxation", "core:ProfitLossOnOrdinaryActivitiesBeforeTax", rpl.ProfitBeforeTax, bold: true, contextRef: "current", priorAmount: rppl?.ProfitBeforeTax, priorContextRef: "prior");
        AddIxbrlRow(sb, "Tax on profit", "core:TaxTaxCreditOnProfitOrLossOnOrdinaryActivities", -rpl.TaxCharge, contextRef: "current", priorAmount: rppl != null ? -rppl.TaxCharge : null, priorContextRef: "prior");
        AddIxbrlRow(sb, "Profit for the year", "core:ProfitLossForPeriod", rpl.ProfitAfterTax, bold: true, contextRef: "current", priorAmount: rppl?.ProfitAfterTax, priorContextRef: "prior");
        sb.AppendLine("</table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AddIxbrlRow(StringBuilder sb, string label, string concept, decimal amount, bool bold = false, string contextRef = "instant", decimal? priorAmount = null, string priorContextRef = "priorInstant")
    {
        var cls = bold ? " class=\"bold\"" : "";
        sb.AppendLine($"<tr{cls}>");
        sb.AppendLine($"  <td>{label}</td>");
        AppendFactCell(sb, concept, amount, contextRef);
        if (priorAmount.HasValue)
            AppendFactCell(sb, concept, priorAmount.Value, priorContextRef);
        sb.AppendLine("</tr>");
    }

    private static void AppendFactCell(StringBuilder sb, string concept, decimal amount, string contextRef)
    {
        var factValue = Math.Round(amount, 0).ToString(CultureInfo.InvariantCulture);
        var displayValue = amount < 0 ? $"({Math.Abs(amount):N0})" : $"{amount:N0}";
        sb.AppendLine($"  <td class=\"amount\"><ix:nonFraction name=\"{concept}\" contextRef=\"{contextRef}\" unitRef=\"EUR\" decimals=\"0\" format=\"ixt:num-dot-decimal\" title=\"{displayValue}\">{factValue}</ix:nonFraction></td>");
    }

    // accounting-ixbrl-rounding-subtotals: round each leaf first, then derive every subtotal from the
    // ROUNDED leaves, so the tagged subtotals cross-add against their components (a ROS/CRO calc-check
    // rejects an instance whose subtotal != sum of its rounded children). Previously each fact was
    // rounded independently from the unrounded statement figures, so e.g. round(0.4)+round(0.4)=0 could
    // disagree with a separately-rounded total of round(0.8)=1.
    private sealed record RoundedBalanceSheet(
        decimal FixedAssets, decimal Stock, decimal Debtors, decimal Cash, decimal CurrentAssetsTotal,
        decimal CreditorsWithin, decimal NetCurrentAssets, decimal TotalAssetsLessCurrent,
        decimal CreditorsAfter, decimal NetAssets, decimal ShareCapital, decimal RetainedEarnings,
        decimal ShareholdersFunds);

    private static decimal RoundEuro(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static RoundedBalanceSheet RoundBalanceSheet(FinancialStatementsService.BalanceSheet bs)
    {
        var fixedAssets = RoundEuro(bs.FixedAssets.Total);
        var stock = RoundEuro(bs.CurrentAssets.Stock);
        var debtors = RoundEuro(bs.CurrentAssets.Debtors + bs.CurrentAssets.Prepayments);
        var cash = RoundEuro(bs.CurrentAssets.Cash);
        var currentAssetsTotal = stock + debtors + cash;
        var creditorsWithin = RoundEuro(bs.CreditorsWithinYear.Total);
        var netCurrentAssets = currentAssetsTotal - creditorsWithin;
        var totalAssetsLessCurrent = fixedAssets + netCurrentAssets;
        var creditorsAfter = RoundEuro(bs.CreditorsAfterYear.Total);
        var netAssets = totalAssetsLessCurrent - creditorsAfter;
        var shareCapital = RoundEuro(bs.CapitalAndReserves.ShareCapital);
        var retainedEarnings = RoundEuro(bs.CapitalAndReserves.RetainedEarnings);
        var shareholdersFunds = shareCapital + retainedEarnings;
        return new RoundedBalanceSheet(fixedAssets, stock, debtors, cash, currentAssetsTotal,
            creditorsWithin, netCurrentAssets, totalAssetsLessCurrent, creditorsAfter, netAssets,
            shareCapital, retainedEarnings, shareholdersFunds);
    }

    private sealed record RoundedProfitAndLoss(
        decimal Turnover, decimal CostOfSales, decimal GrossProfit, decimal OtherIncome,
        decimal TotalOverheads, decimal OperatingProfit, decimal InterestPayable, decimal ProfitBeforeTax,
        decimal TaxCharge, decimal ProfitAfterTax);

    private static RoundedProfitAndLoss RoundProfitAndLoss(FinancialStatementsService.ProfitAndLoss pl)
    {
        var turnover = RoundEuro(pl.Turnover);
        var costOfSales = RoundEuro(pl.CostOfSales);
        var grossProfit = turnover - costOfSales;
        var otherIncome = RoundEuro(pl.OtherIncome);
        var totalOverheads = RoundEuro(pl.TotalOverheads);
        var operatingProfit = grossProfit + otherIncome - totalOverheads;
        var interestPayable = RoundEuro(pl.InterestPayable);
        var profitBeforeTax = operatingProfit - interestPayable;
        var taxCharge = RoundEuro(pl.TaxCharge);
        var profitAfterTax = profitBeforeTax - taxCharge;
        return new RoundedProfitAndLoss(turnover, costOfSales, grossProfit, otherIncome, totalOverheads,
            operatingProfit, interestPayable, profitBeforeTax, taxCharge, profitAfterTax);
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private async Task<byte[]> TrackDocumentAsync(Func<Task<byte[]>> action)
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
            try { platformMetrics?.RecordDocument(DocumentMetricKind.Ixbrl, succeeded, stopwatch.Elapsed); }
            catch { /* Telemetry must never change iXBRL generation behavior. */ }
        }
    }

    private static bool InternalIxbrlChecksPassed(Accounts.Api.Entities.RevenueFilingPackage? package) =>
        package?.IxbrlGenerated == true
        && package.IxbrlValidationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) == true;
}
