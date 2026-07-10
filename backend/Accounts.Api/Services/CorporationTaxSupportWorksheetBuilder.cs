using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

public static class CorporationTaxSupportWorksheetBuilder
{
    public const string OutputKind = "scoped-ct1-support-worksheet-not-submittable-return";

    public record WorksheetInput(
        string CompanyName,
        string TaxReference,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        TaxComputationService.Ct1SupportData TaxSupport,
        CorporationTaxFilingSupportCalculator.Result PreliminaryTax,
        decimal GrossWagesExcludingDirectors,
        decimal EmployerPrsiAndPension,
        DateOnly GeneratedAsOf);

    public record WorksheetField(
        string Code,
        int? PublishedPanelNumber,
        string PanelTitle,
        string PublishedFieldLabel,
        string MappingStatus,
        string ValueType,
        decimal? NumericValue,
        string? TextValue,
        string Source,
        bool MachineSupported,
        string Note);

    public record Reconciliation(
        string Code,
        decimal Left,
        decimal Right,
        decimal Difference,
        bool Reconciles,
        string Detail);

    public record SourceReference(string Code, string Title, string Url);

    public record Worksheet(
        string OutputKind,
        bool IsCompleteCt1Return,
        bool DirectRosSubmissionSupported,
        string Warning,
        string CompanyName,
        string TaxReference,
        string PeriodStart,
        string PeriodEnd,
        string MappingVersion,
        bool YearSpecificMappingAvailable,
        DateOnly GeneratedAsOf,
        IReadOnlyList<WorksheetField> Fields,
        IReadOnlyList<Reconciliation> Reconciliations,
        CorporationTaxFilingSupportCalculator.Result PreliminaryTax,
        bool SupportWorksheetReady,
        bool QualifiedAccountantReviewRequired,
        IReadOnlyList<string> BlockingReasons,
        IReadOnlyList<string> ManualCompletionItems,
        IReadOnlyList<SourceReference> Sources,
        string WorksheetSha256);

    public static Worksheet Build(WorksheetInput input)
    {
        var mapping = MappingForYear(input.PeriodEnd.Year);
        var tax = input.TaxSupport;
        var fields = new List<WorksheetField>
        {
            Text("company-name", 1, "Company Details", "Company name", mapping.PanelMappingStatus, input.CompanyName, "Company statutory profile", "Confirm the exact ROS company record."),
            Text("tax-reference", 1, "Company Details", "Tax reference", mapping.PanelMappingStatus, input.TaxReference, "Company tax profile", "Confirm the exact ROS registration before handoff."),
            Text("accounting-period-start", 1, "Company Details", "Accounting period start", mapping.PanelMappingStatus, input.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Accounting period", "A CT accounting period cannot exceed 12 months."),
            Text("accounting-period-end", 1, "Company Details", "Accounting period end", mapping.PanelMappingStatus, input.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Accounting period", "The CT1 mapping is year-specific by accounting-period end."),
            Money("trading-profit-before-loss", 2, "Trading Results", "Trading profit before supported loss relief", "published-panel-only", tax.TradingProfitBeforeLossRelief, "Corporation-tax support calculation", "Confirm the exact ROS Trading Results sub-field."),
            Money("capital-allowances", 2, "Trading Results", "Plant and machinery capital allowances", "published-panel-only", tax.CapitalAllowances, "Reviewed fixed-asset allowance schedule", "Only the bounded 12.5% plant-and-machinery scope is represented."),
            Money("trading-loss-used", 2, "Trading Results", "Same-trade loss brought forward used", "published-panel-only", tax.TradingLossUsed, "Retained corporation-tax loss ledger", "Other loss claims and elections are not represented."),
            Money("sales-turnover", 3, "Extracts from Accounts", "Sales / Receipts / Turnover", mapping.ExactFieldStatus, tax.Turnover, "Profit and loss statement", "Published 2025 Extracts from Accounts field label."),
            Money("gross-trading-profits", 3, "Extracts from Accounts", "Gross Trading Profits", mapping.ExactFieldStatus, tax.GrossProfit, "Profit and loss statement", "Published 2025 Extracts from Accounts field label; confirm purchases and inventory fields separately in ROS."),
            Money("salaries-wages", 3, "Extracts from Accounts", "Salaries / Wages", mapping.ExactFieldStatus, input.GrossWagesExcludingDirectors, "Payroll working paper", "Employee gross wages excluding directors' remuneration."),
            Money("staff-costs", 3, "Extracts from Accounts", "Staff costs", mapping.ExactFieldStatus, input.EmployerPrsiAndPension, "Payroll working paper", "Employer PRSI and pension support; confirm ROS composition."),
            Money("directors-remuneration", 3, "Extracts from Accounts", "Directors' remuneration including fees, bonuses, etc", mapping.ExactFieldStatus, tax.TotalDirectorsFees, "Payroll working paper", "Directors' remuneration is not inferred from gross employee wages."),
            Money("depreciation", 3, "Extracts from Accounts", "Depreciation/Amortisation, Goodwill/Capital write-off", mapping.ExactFieldStatus, tax.DepreciationCharged, "Depreciation schedule", "Published 2025 Extracts from Accounts field label."),
            Money("profit-before-tax", 3, "Extracts from Accounts", tax.NetProfit >= 0 ? "Profit on ordinary activities before taxation" : "Loss on ordinary activities before taxation", mapping.ExactFieldStatus, Math.Abs(tax.NetProfit), "Profit and loss statement", "ROS uses separate profit and loss fields."),
            Money("other-addbacks", 3, "Extracts from Accounts", "Other addbacks", mapping.ExactFieldStatus, tax.Adjustments.Where(adjustment => adjustment.Amount > 0).Sum(adjustment => adjustment.Amount), "Corporation-tax adjustment bridge", "Detailed ROS categorisation must be confirmed field by field."),
            Money("other-deductions", 3, "Extracts from Accounts", "Other deductions", mapping.ExactFieldStatus, -tax.Adjustments.Where(adjustment => adjustment.Amount < 0).Sum(adjustment => adjustment.Amount), "Corporation-tax adjustment bridge", "Detailed ROS categorisation must be confirmed field by field."),
            Money("passive-income", 4, "Irish Investment and Other Income", "Passive / non-trading income support", "published-panel-only", tax.PassiveNonTradingIncome, "Classified non-trading ledger", "Confirm the precise Case III/IV/V or rental ROS field manually."),
            Money("taxable-trading-profit", null, "Internal tax bridge", "Trading profit after supported loss relief", "internal-support-only", tax.TradingProfitAfterLossRelief, "Corporation-tax support calculation", "Not asserted as an exact ROS CT1 field."),
            Money("indicative-tax-due", null, "Internal tax bridge", "Supported Corporation Tax due", "internal-support-only", tax.TaxDue, "Corporation-tax support calculation", "Excludes every unsupported CT1 panel, claim, surcharge and credit."),
            Money("preliminary-tax-recorded", null, "Preliminary Tax tracker", "Tax payments recorded toward liability", "internal-support-only", input.PreliminaryTax.TaxPaymentsRecorded, "Dated retained payment ledger", "PTL1/PTL2 and balance-payment working paper only; no ROS payment is initiated."),
            Money("balance-due", null, "Preliminary Tax tracker", "Supported balance due / (overpayment)", "internal-support-only", input.PreliminaryTax.CurrentTotalTaxForPaymentSupport - input.PreliminaryTax.TaxPaymentsRecorded, "Tax support and payment ledger", "Section 239 Income Tax is included by the preliminary-tax tracker where entered.")
        };

        var reconciliations = new List<Reconciliation>
        {
            Reconcile(
                "tax-streams",
                tax.TaxableProfit,
                Math.Max(0m, tax.TradingProfitAfterLossRelief) + Math.Max(0m, tax.PassiveNonTradingIncome),
                "Taxable profit equals supported trading and non-trading streams."),
            Reconcile(
                "tax-charge",
                tax.BalanceDue,
                tax.TaxDue - tax.PreliminaryTaxPaid,
                "Supported balance due equals tax due less aggregate preliminary tax paid."),
            Reconcile(
                "payment-ledger",
                tax.PreliminaryTaxPaid,
                input.PreliminaryTax.PreliminaryTaxPaymentsRecorded,
                "Legacy aggregate preliminary-tax paid must equal the dated payment ledger before handoff."),
            Reconcile(
                "employee-costs",
                tax.TotalEmployeeCosts,
                input.GrossWagesExcludingDirectors + tax.TotalDirectorsFees + input.EmployerPrsiAndPension,
                "Total employee costs reconcile without treating gross wages as directors' remuneration.")
        };

        var blockers = new List<string>();
        blockers.AddRange(tax.BlockingReasons.Select(reason => $"Tax support: {reason}"));
        blockers.AddRange(input.PreliminaryTax.BlockingReasons.Select(reason => $"Preliminary Tax: {reason}"));
        blockers.AddRange(reconciliations
            .Where(reconciliation => !reconciliation.Reconciles)
            .Select(reconciliation => $"Worksheet reconciliation failed ({reconciliation.Code}): {reconciliation.Detail}"));
        if (!mapping.YearSpecificAvailable)
        {
            blockers.Add(
                $"No year-specific Revenue CT1 completion guide is pinned for accounting periods ending in {input.PeriodEnd.Year}; the latest 2025 panel labels are orientation only.");
        }
        if (string.IsNullOrWhiteSpace(input.TaxReference))
            blockers.Add("A Corporation Tax registration/tax reference is required for the manual handoff worksheet.");
        if (!string.Equals(tax.CompanyName, input.CompanyName, StringComparison.Ordinal)
            || !string.Equals(tax.TaxReference, input.TaxReference, StringComparison.Ordinal)
            || !string.Equals(tax.PeriodStart, input.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !string.Equals(tax.PeriodEnd, input.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            blockers.Add("The CT1 support data identity does not match the worksheet company, tax reference and accounting period.");
        }
        if (input.PreliminaryTax.PreliminaryFirstDueDate != CorporationTaxFilingSupportCalculator.PreliminaryFirstDueDate(input.PeriodStart)
            || input.PreliminaryTax.PreliminarySecondOrSingleDueDate != CorporationTaxFilingSupportCalculator.PreliminarySecondOrSingleDueDate(input.PeriodEnd)
            || input.PreliminaryTax.ReturnAndBalanceDueDate != CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(input.PeriodEnd))
        {
            blockers.Add("The preliminary-tax tracker dates do not match the worksheet accounting period.");
        }

        var manualItems = new List<string>
        {
            "Complete every mandatory Company Details declaration in the year-specific ROS CT1, including any aid, transfer-pricing, group and special-regime questions.",
            "Confirm each Trading Results and Extracts from Accounts field against the live ROS form; panel-only mappings are not field identifiers.",
            "Complete all applicable income, gains, deductions, reliefs, credits, surcharge, self-assessment and declaration panels not represented by this bounded worksheet.",
            "Complete and retain the separate Form 46G (Company) assessment where required.",
            "Have a named qualified accountant approve the final ROS handoff and retain external validation/payment references."
        };
        var sources = new List<SourceReference>
        {
            new("revenue-ct1-2025", "Revenue Form CT1 2025 completion guide", "https://www.revenue.ie/en/tax-professionals/tdm/income-tax-capital-gains-tax-corporation-tax/part-38/38-02-01J.pdf"),
            new("revenue-ct1-2024", "Revenue Form CT1 2024 completion guide", "https://www.revenue.ie/en/tax-professionals/tdm/income-tax-capital-gains-tax-corporation-tax/part-38/38-02-01I.pdf"),
            new("revenue-ct1-2023", "Revenue Form CT1 2023 completion guide", "https://www.revenue.ie/en/tax-professionals/tdm/income-tax-capital-gains-tax-corporation-tax/part-38/38-02-01H.pdf"),
            new("revenue-preliminary-tax", "Revenue Preliminary Corporation Tax", "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax-payment-and-filing/preliminary-ct.aspx"),
            new("revenue-preliminary-dates", "Revenue preliminary CT due dates", "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax-payment-and-filing/when-is-preliminary-ct-due.aspx"),
            new("revenue-payment-filing", "Revenue CT payment, filing, interest and surcharge", "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax-payment-and-filing/payment-and-filing.aspx")
        };
        blockers = blockers.Distinct(StringComparer.Ordinal).ToList();
        var hash = Fingerprint(input, fields, reconciliations, blockers);
        return new Worksheet(
            OutputKind,
            IsCompleteCt1Return: false,
            DirectRosSubmissionSupported: false,
            "SUPPORT WORKSHEET ONLY - NOT A CT1 RETURN - NOTHING IS SUBMITTED TO REVENUE",
            input.CompanyName,
            input.TaxReference,
            input.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            input.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            mapping.Version,
            mapping.YearSpecificAvailable,
            input.GeneratedAsOf,
            fields,
            reconciliations,
            input.PreliminaryTax,
            SupportWorksheetReady: blockers.Count == 0,
            QualifiedAccountantReviewRequired: true,
            blockers,
            manualItems,
            sources,
            hash);
    }

    private static WorksheetField Money(
        string code,
        int? panel,
        string title,
        string label,
        string status,
        decimal value,
        string source,
        string note) =>
        new(code, panel, title, label, status, "money", value, null, source, true, note);

    private static WorksheetField Text(
        string code,
        int? panel,
        string title,
        string label,
        string status,
        string value,
        string source,
        string note) =>
        new(code, panel, title, label, status, "text", null, value, source, true, note);

    private static Reconciliation Reconcile(string code, decimal left, decimal right, string detail)
    {
        var difference = decimal.Round(left - right, 2, MidpointRounding.AwayFromZero);
        return new Reconciliation(code, left, right, difference, Math.Abs(difference) < 0.01m, detail);
    }

    private static (string Version, bool YearSpecificAvailable, string PanelMappingStatus, string ExactFieldStatus) MappingForYear(int year) =>
        year switch
        {
            2025 => ("Revenue-CT1-2025-Part-38-02-01J", true, "published-panel", "published-exact-field-label"),
            2024 => ("Revenue-CT1-2024-Part-38-02-01I", true, "published-panel", "published-panel-only"),
            2023 => ("Revenue-CT1-2023-Part-38-02-01H", true, "published-panel", "published-panel-only"),
            _ => ("Revenue-CT1-2025-orientation-only", false, "latest-published-panel-orientation", "latest-published-field-orientation")
        };

    private static string Fingerprint(
        WorksheetInput input,
        IEnumerable<WorksheetField> fields,
        IEnumerable<Reconciliation> reconciliations,
        IEnumerable<string> blockers)
    {
        static string Money(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "null";
        var rows = new List<string>
        {
            $"company:{input.CompanyName}|{input.TaxReference}",
            $"period:{input.PeriodStart:yyyy-MM-dd}|{input.PeriodEnd:yyyy-MM-dd}|asof={input.GeneratedAsOf:yyyy-MM-dd}",
            $"tax:{input.TaxSupport.CalculationSha256}|prelim:{input.PreliminaryTax.CalculationSha256}"
        };
        rows.AddRange(fields.Select(field => $"field:{field.Code}|{field.PublishedPanelNumber}|{field.PublishedFieldLabel}|{Money(field.NumericValue)}|{field.TextValue}|{field.MappingStatus}"));
        rows.AddRange(reconciliations.Select(item => $"reconcile:{item.Code}|{Money(item.Left)}|{Money(item.Right)}|{item.Reconciles}"));
        rows.AddRange(blockers.Order(StringComparer.Ordinal).Select(blocker => $"blocker:{blocker}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }
}
