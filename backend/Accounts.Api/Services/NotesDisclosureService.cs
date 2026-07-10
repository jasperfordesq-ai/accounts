using System.Globalization;
using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class NotesDisclosureService(AccountsDbContext db)
{
    private static readonly CultureInfo IrishCulture = CultureInfo.GetCultureInfo("en-IE");

    public async Task<List<NotesDisclosure>> GenerateNotesAsync(int companyId, int periodId)
    {
        var checklist = await BuildChecklistAsync(companyId, periodId);
        var existing = await db.NotesDisclosures
            .Where(note => note.PeriodId == periodId)
            .OrderBy(note => note.NoteNumber)
            .ThenBy(note => note.Id)
            .ToListAsync();
        var generatedExisting = existing
            .Where(note => note.IsRequired || StatutoryNoteCodes.IsStableCode(note.Code))
            .ToList();
        var customExisting = existing.Except(generatedExisting).ToList();

        db.NotesDisclosures.RemoveRange(generatedExisting);

        var generated = checklist.Candidates.Select((candidate, index) => new NotesDisclosure
        {
            PeriodId = periodId,
            NoteNumber = index + 1,
            Code = candidate.Code,
            Title = candidate.Title,
            Content = candidate.Content,
            IsRequired = true,
            IsIncluded = candidate.IsIncluded,
            ChecklistState = candidate.State,
            ReviewEvidence = candidate.ReviewEvidence,
            ReviewedBy = candidate.ReviewedBy,
            ReviewedAt = candidate.ReviewedAt
        }).ToList();

        var nextNumber = generated.Count + 1;
        foreach (var custom in customExisting)
        {
            custom.Code = null;
            custom.IsRequired = false;
            custom.ChecklistState = NoteChecklistState.Required;
            custom.NoteNumber = nextNumber++;
        }

        if (checklist.Period.FilingRegime is { } filingRegime)
        {
            filingRegime.RequiredNotesJson = JsonSerializer.Serialize(
                checklist.Candidates.Select(candidate => candidate.Code).ToList());
        }

        db.NotesDisclosures.AddRange(generated);
        await db.SaveChangesAsync();
        return await db.NotesDisclosures
            .Where(note => note.PeriodId == periodId)
            .OrderBy(note => note.NoteNumber)
            .ThenBy(note => note.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Returns release-blocking checklist failures without mutating persisted notes.
    /// </summary>
    public async Task<List<string>> GetChecklistIssuesAsync(int companyId, int periodId)
    {
        var checklist = await BuildChecklistAsync(companyId, periodId);
        var notes = await db.NotesDisclosures
            .Where(note => note.PeriodId == periodId)
            .ToListAsync();

        // Legacy test/upgrade rows pre-date stable codes. The production contract becomes strict as
        // soon as the coded generator has run (or FilingRegimeService has persisted coded requirements).
        var codedContract = notes.Any(note => StatutoryNoteCodes.IsStableCode(note.Code))
            || RequiredNotesJsonUsesStableCodes(checklist.Period.FilingRegime?.RequiredNotesJson);
        if (!codedContract)
            return notes.Any(note => note.IsIncluded)
                ? []
                : ["Notes to the financial statements not generated or reviewed"];

        var issues = new List<string>();
        var expectedByCode = checklist.Candidates.ToDictionary(candidate => candidate.Code, StringComparer.Ordinal);
        var generated = notes.Where(note => note.IsRequired || StatutoryNoteCodes.IsStableCode(note.Code)).ToList();

        foreach (var candidate in checklist.Candidates)
        {
            var matches = generated.Where(note => note.Code == candidate.Code).ToList();
            if (matches.Count != 1)
            {
                issues.Add(matches.Count == 0
                    ? $"Required statutory note {candidate.Code} is missing."
                    : $"Statutory note {candidate.Code} is duplicated ({matches.Count} rows)."
                );
                continue;
            }

            var note = matches[0];
            if (!string.Equals(note.Title, candidate.Title, StringComparison.Ordinal))
                issues.Add($"Statutory note {candidate.Code} has an unexpected title.");
            if (note.ChecklistState != candidate.State)
                issues.Add($"Statutory note {candidate.Code} has stale checklist state {note.ChecklistState}.");
            if (candidate.ReviewedAt is not null
                && (string.IsNullOrWhiteSpace(note.ReviewEvidence)
                    || string.IsNullOrWhiteSpace(note.ReviewedBy)
                    || note.ReviewedAt is null))
            {
                issues.Add($"Statutory note {candidate.Code} lacks retained review evidence.");
            }

            if (candidate.State == NoteChecklistState.NotApplicable)
            {
                if (note.IsIncluded)
                    issues.Add($"Not-applicable statutory note {candidate.Code} must not be rendered.");
                continue;
            }

            if (candidate.State == NoteChecklistState.ExplicitReview && !candidate.IsIncluded)
            {
                issues.Add($"Statutory note {candidate.Code} requires retained explicit-review or manual-handoff evidence.");
                continue;
            }

            if (!note.IsIncluded || string.IsNullOrWhiteSpace(note.Content))
            {
                issues.Add($"Required statutory note {candidate.Code} is not included with content.");
                continue;
            }

            if (!string.Equals(Normalise(note.Content), Normalise(candidate.Content), StringComparison.Ordinal))
                issues.Add($"Statutory note {candidate.Code} no longer reconciles to its source facts or statements.");

        }

        foreach (var note in generated.Where(note =>
                     StatutoryNoteCodes.IsStableCode(note.Code)
                     && !expectedByCode.ContainsKey(note.Code!)))
        {
            issues.Add($"Obsolete generated statutory note {note.Code} remains in the checklist.");
        }

        var included = notes.Where(note => note.IsIncluded).ToList();
        foreach (var title in expectedByCode.Values.Select(candidate => candidate.Title).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var titleCount = included.Count(note => string.Equals(note.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
            if (titleCount > 1)
                issues.Add($"Generated note title '{title}' is duplicated in rendered output.");
        }

        foreach (var note in included.Where(note => StatutoryNoteCodes.ContainsUnsupportedRepresentation(note.Content)))
        {
            var hasRetainedReview = note.ChecklistState == NoteChecklistState.ExplicitReview
                && !string.IsNullOrWhiteSpace(note.ReviewEvidence)
                && !string.IsNullOrWhiteSpace(note.ReviewedBy)
                && note.ReviewedAt is not null;
            if (!hasRetainedReview)
                issues.Add($"Note '{note.Title}' contains an unsupported representation without retained manual-review evidence.");
        }

        if (checklist.Period.ApprovalDate is null)
            issues.Add("Board approval date has not been persisted.");

        return issues.Distinct(StringComparer.Ordinal).ToList();
    }

    public static async Task RefreshApprovalNoteAsync(AccountsDbContext db, AccountingPeriod period)
    {
        if (period.ApprovalDate is not { } approvalDate)
            return;

        var note = await db.NotesDisclosures
            .FirstOrDefaultAsync(candidate => candidate.PeriodId == period.Id
                && candidate.Code == StatutoryNoteCodes.Approval);
        if (note is null)
            return;

        note.Content = ApprovalContent(approvalDate);
        note.ChecklistState = NoteChecklistState.Required;
        note.IsIncluded = true;
    }

    private async Task<Checklist> BuildChecklistAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(candidate => candidate.Company)
            .Include(candidate => candidate.FilingRegime)
            .Include(candidate => candidate.SizeClassification)
            .FirstOrDefaultAsync(candidate => candidate.Id == periodId && candidate.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Small;
        var sizeClass = period.SizeClassification?.OverrideClass
            ?? period.SizeClassification?.CalculatedClass
            ?? RegimeFallbackSize(regime);
        var confirmations = await db.YearEndReviewConfirmations
            .Where(review => review.PeriodId == periodId && review.Confirmed)
            .OrderByDescending(review => review.ConfirmedAt)
            .ToListAsync();
        var reviews = confirmations
            .GroupBy(review => review.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<NoteCandidate>();

        void Add(NoteCandidate candidate) => candidates.Add(candidate);

        var activeAssets = await db.FixedAssets
            .Where(asset => asset.CompanyId == companyId
                && asset.AcquisitionDate <= period.PeriodEnd
                && (asset.DisposalDate == null || asset.DisposalDate > period.PeriodEnd))
            .OrderBy(asset => asset.Category)
            .ThenBy(asset => asset.Name)
            .ToListAsync();
        var hasStock = await db.Inventories.AnyAsync(inventory => inventory.PeriodId == periodId);
        Add(Required(
            StatutoryNoteCodes.AccountingPolicies,
            AccountingPolicies(regime, activeAssets, hasStock)));

        Add(GoingConcernCandidate(period, reviews));

        if (regime == ElectedRegime.Micro)
        {
            var microDirectorLoans = await DirectorLoansAsync(companyId, periodId);
            var microDirectorLoanDisclosure = microDirectorLoans.Count > 0
                ? await new DirectorLoanComplianceService(db, new FinancialStatementsService(db))
                    .GenerateSection307NoteAsync(companyId, periodId)
                : null;
            Add(microDirectorLoans.Count > 0
                ? Required(StatutoryNoteCodes.DirectorTransactions, microDirectorLoanDisclosure!)
                : company.HasDirectorLoans
                    ? NotApplicableOrReview(StatutoryNoteCodes.DirectorTransactions, "director-loans", reviews)
                    : FactNotApplicable(StatutoryNoteCodes.DirectorTransactions, "The company is not recorded as having director loans."));
            Add(ApprovalCandidate(period));
            return new Checklist(period, candidates);
        }

        var statements = new FinancialStatementsService(db);
        var balanceSheet = await statements.GetBalanceSheetAsync(companyId, periodId);
        var profitAndLoss = regime == ElectedRegime.SmallAbridged
            ? null
            : await statements.GetProfitAndLossAsync(companyId, periodId);
        var equity = regime == ElectedRegime.SmallAbridged
            ? null
            : await statements.GetEquityChangesAsync(companyId, periodId);

        Add(balanceSheet.FixedAssets.Total != 0 || balanceSheet.FixedAssets.Categories.Count > 0
            ? Required(StatutoryNoteCodes.FixedAssets, FixedAssetContent(balanceSheet.FixedAssets))
            : FactNotApplicable(StatutoryNoteCodes.FixedAssets, "No posted fixed-asset balance exists at period end."));

        Add(balanceSheet.CurrentAssets.Stock != 0
            ? Required(
                StatutoryNoteCodes.Inventories,
                $"Inventories recognised at period end: {Money(balanceSheet.CurrentAssets.Stock)}")
            : NotApplicableOrReview(StatutoryNoteCodes.Inventories, "inventory", reviews));

        Add(balanceSheet.CurrentAssets.Debtors != 0 || balanceSheet.CurrentAssets.Prepayments != 0
            ? Required(StatutoryNoteCodes.Debtors, DebtorContent(balanceSheet.CurrentAssets))
            : NotApplicableOrReview(StatutoryNoteCodes.Debtors, "debtors", reviews));

        Add(balanceSheet.CreditorsWithinYear.Total != 0
            ? Required(StatutoryNoteCodes.CurrentCreditors, CurrentCreditorContent(balanceSheet.CreditorsWithinYear))
            : NotApplicableOrReview(StatutoryNoteCodes.CurrentCreditors, "creditors", reviews));

        Add(balanceSheet.CreditorsAfterYear.Total != 0
            ? Required(StatutoryNoteCodes.LongTermCreditors, LongTermCreditorContent(balanceSheet.CreditorsAfterYear))
            : FactNotApplicable(StatutoryNoteCodes.LongTermCreditors, "No posted long-term creditor balance exists at period end."));

        Add(company.CompanyType == CompanyType.CompanyLimitedByGuarantee
            ? FactNotApplicable(StatutoryNoteCodes.ShareCapital, "A company limited by guarantee has no share capital.")
            : balanceSheet.CapitalAndReserves.ShareCapital != 0
            ? Required(
                StatutoryNoteCodes.ShareCapital,
                $"Issued share capital at period end: {Money(balanceSheet.CapitalAndReserves.ShareCapital)}")
            : ExplicitReview(StatutoryNoteCodes.ShareCapital));
        Add(Required(StatutoryNoteCodes.Reserves, ReserveContent(balanceSheet.CapitalAndReserves)));

        var payroll = await db.PayrollSummaries.SingleOrDefaultAsync(item => item.PeriodId == periodId);
        Add(company.IsEmployer
            ? payroll is not null
                ? Required(StatutoryNoteCodes.Employees, EmployeeContent(payroll))
                : ExplicitReview(StatutoryNoteCodes.Employees)
            : FactNotApplicable(StatutoryNoteCodes.Employees, "The company is not recorded as an employer."));

        var directorLoans = await DirectorLoansAsync(companyId, periodId);
        var directorLoanDisclosure = directorLoans.Count > 0
            ? await new DirectorLoanComplianceService(db, new FinancialStatementsService(db))
                .GenerateSection307NoteAsync(companyId, periodId)
            : null;
        Add(directorLoans.Count > 0
            ? Required(StatutoryNoteCodes.DirectorTransactions, directorLoanDisclosure!)
            : company.HasDirectorLoans
                ? NotApplicableOrReview(StatutoryNoteCodes.DirectorTransactions, "director-loans", reviews)
                : FactNotApplicable(StatutoryNoteCodes.DirectorTransactions, "The company is not recorded as having director loans."));
        Add(ManualReview(StatutoryNoteCodes.DirectorRemuneration, "note-directors-remuneration", reviews));

        var events = await db.PostBalanceSheetEvents
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.EventDate)
            .ThenBy(item => item.Id)
            .ToListAsync();
        Add(events.Count > 0
            ? Required(StatutoryNoteCodes.PostBalanceSheetEvents, PostBalanceSheetContent(events))
            : NotApplicableOrReview(StatutoryNoteCodes.PostBalanceSheetEvents, "post-balance-sheet-events", reviews));

        var relatedParties = await db.RelatedPartyTransactions
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.PartyName)
            .ThenBy(item => item.Id)
            .ToListAsync();
        Add(relatedParties.Count > 0
            ? Required(StatutoryNoteCodes.RelatedParties, RelatedPartyContent(relatedParties))
            : NotApplicableOrReview(StatutoryNoteCodes.RelatedParties, "related-parties", reviews));

        if (company.IsGroupMember)
            Add(ManualReview(StatutoryNoteCodes.UltimateControllingParty, "note-ultimate-controlling-party", reviews));

        var contingencies = await db.ContingentLiabilities
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.Description)
            .ThenBy(item => item.Id)
            .ToListAsync();
        Add(contingencies.Count > 0
            ? Required(StatutoryNoteCodes.ContingentLiabilities, ContingencyContent(contingencies))
            : NotApplicableOrReview(StatutoryNoteCodes.ContingentLiabilities, "contingent-liabilities", reviews));

        if (profitAndLoss is not null && equity is not null)
        {
            Add(Required(StatutoryNoteCodes.Turnover, TurnoverContent(profitAndLoss)));
            Add(Required(StatutoryNoteCodes.TaxOnProfit, TaxContent(profitAndLoss)));

            var dividends = await db.Dividends
                .Where(item => item.PeriodId == periodId)
                .OrderBy(item => item.DateDeclared)
                .ThenBy(item => item.Id)
                .ToListAsync();
            Add(dividends.Count > 0 || equity.DividendsPaid != 0
                ? Required(StatutoryNoteCodes.Dividends, DividendContent(equity, dividends))
                : NotApplicableOrReview(StatutoryNoteCodes.Dividends, "dividends", reviews));
        }

        if (regime is ElectedRegime.Medium or ElectedRegime.Full || sizeClass >= CompanySizeClass.Medium)
        {
            Add(ManualReview(StatutoryNoteCodes.FinancialInstruments, "note-financial-instruments", reviews));
            Add(ManualReview(StatutoryNoteCodes.CapitalCommitments, "note-capital-commitments", reviews));
            Add(ManualReview(StatutoryNoteCodes.DeferredTax, "note-deferred-tax", reviews));
        }

        Add(ApprovalCandidate(period));
        return new Checklist(period, candidates);
    }

    private static NoteCandidate GoingConcernCandidate(
        AccountingPeriod period,
        IReadOnlyDictionary<string, YearEndReviewConfirmation> reviews)
    {
        if (!reviews.TryGetValue("going-concern", out var review))
            return ExplicitReview(StatutoryNoteCodes.GoingConcern);

        var content = period.GoingConcernConfirmed
            ? "Following their documented assessment, the directors consider the going concern basis of preparation appropriate."
            : !string.IsNullOrWhiteSpace(period.GoingConcernNote)
                ? period.GoingConcernNote.Trim()
                : null;
        if (string.IsNullOrWhiteSpace(content))
            return ExplicitReview(StatutoryNoteCodes.GoingConcern);
        return ReviewedRequired(StatutoryNoteCodes.GoingConcern, content, review);
    }

    private static NoteCandidate ApprovalCandidate(AccountingPeriod period) =>
        period.ApprovalDate is { } date
            ? Required(StatutoryNoteCodes.Approval, ApprovalContent(date))
            : ExplicitReview(StatutoryNoteCodes.Approval);

    private static string ApprovalContent(DateOnly date) =>
        $"The financial statements were approved and authorised for issue by the Board of Directors on {date.ToString("dd MMMM yyyy", IrishCulture)}.";

    private async Task<List<DirectorLoan>> DirectorLoansAsync(int companyId, int periodId) =>
        await db.DirectorLoans
            .Where(item => item.PeriodId == periodId
                && item.Period.CompanyId == companyId
                && (item.CounterpartyType == DirectorLoanCounterpartyType.GroupCompany && item.DirectorId == null
                    || item.DirectorId != null && item.Director!.CompanyId == companyId))
            .Include(item => item.Director)
            .Include(item => item.BalanceMovements.OrderBy(movement => movement.MovementDate).ThenBy(movement => movement.Id))
            .OrderBy(item => item.CounterpartyName ?? item.Director!.Name)
            .ThenBy(item => item.Id)
            .ToListAsync();

    private static NoteCandidate Required(string code, string content) => new(
        code,
        StatutoryNoteCodes.Titles[code],
        content,
        NoteChecklistState.Required,
        true,
        null,
        null,
        null);

    private static NoteCandidate ReviewedRequired(string code, string content, YearEndReviewConfirmation review) => new(
        code,
        StatutoryNoteCodes.Titles[code],
        content,
        NoteChecklistState.Required,
        true,
        ReviewEvidence(review),
        review.ConfirmedBy,
        review.ConfirmedAt);

    private static NoteCandidate FactNotApplicable(string code, string evidence) => new(
        code,
        StatutoryNoteCodes.Titles[code],
        null,
        NoteChecklistState.NotApplicable,
        false,
        evidence,
        null,
        null);

    private static NoteCandidate NotApplicableOrReview(
        string code,
        string reviewKey,
        IReadOnlyDictionary<string, YearEndReviewConfirmation> reviews) =>
        reviews.TryGetValue(reviewKey, out var review)
            ? new NoteCandidate(
                code,
                StatutoryNoteCodes.Titles[code],
                null,
                NoteChecklistState.NotApplicable,
                false,
                ReviewEvidence(review),
                review.ConfirmedBy,
                review.ConfirmedAt)
            : ExplicitReview(code);

    private static NoteCandidate ManualReview(
        string code,
        string reviewKey,
        IReadOnlyDictionary<string, YearEndReviewConfirmation> reviews)
    {
        if (!reviews.TryGetValue(reviewKey, out var review) || string.IsNullOrWhiteSpace(review.Note))
            return ExplicitReview(code);
        return new NoteCandidate(
            code,
            StatutoryNoteCodes.Titles[code],
            review.Note.Trim(),
            NoteChecklistState.ExplicitReview,
            true,
            ReviewEvidence(review),
            review.ConfirmedBy,
            review.ConfirmedAt);
    }

    private static NoteCandidate ExplicitReview(string code) => new(
        code,
        StatutoryNoteCodes.Titles[code],
        null,
        NoteChecklistState.ExplicitReview,
        false,
        null,
        null,
        null);

    private static string ReviewEvidence(YearEndReviewConfirmation review) =>
        $"Review section '{review.SectionKey}' confirmed at {review.ConfirmedAt:O}"
        + (string.IsNullOrWhiteSpace(review.Note) ? "." : $": {review.Note.Trim()}");

    private static string AccountingPolicies(
        ElectedRegime regime,
        IReadOnlyCollection<FixedAsset> assets,
        bool hasStock)
    {
        var standard = regime == ElectedRegime.Micro
            ? "FRS 105, The Financial Reporting Standard applicable to the Micro-entities Regime"
            : "FRS 102, The Financial Reporting Standard applicable in the UK and Republic of Ireland";
        var builder = new StringBuilder()
            .AppendLine("Basis of preparation")
            .AppendLine($"The financial statements have been prepared under the historical-cost convention in accordance with {standard} and the Companies Act 2014.")
            .AppendLine()
            .AppendLine("Currency")
            .AppendLine("The financial statements are presented in euro (€), the company's functional currency.")
            .AppendLine()
            .AppendLine("Revenue recognition")
            .Append("Revenue is recognised when the amount can be measured reliably and the related economic benefits are expected to flow to the company.");

        if (assets.Count > 0)
        {
            builder.AppendLine().AppendLine()
                .AppendLine("Tangible fixed assets and depreciation")
                .Append("Tangible fixed assets are measured at cost less accumulated depreciation and impairment. Depreciation is charged over each asset's recorded useful life down to its recorded residual value.");
        }
        if (hasStock)
        {
            builder.AppendLine().AppendLine()
                .AppendLine("Inventories")
                .Append("Inventories are measured at the lower of cost and net realisable value.");
        }
        builder.AppendLine().AppendLine()
            .AppendLine("Current taxation")
            .Append("Current corporation tax is recognised by reference to taxable profits and enacted tax rates.");
        return builder.ToString();
    }

    private static string FixedAssetContent(FinancialStatementsService.FixedAssetsSection section)
    {
        var builder = new StringBuilder();
        foreach (var line in section.Categories)
            builder.AppendLine($"{line.Category}: cost {Money(line.Cost)}; accumulated depreciation {Money(line.Depreciation)}; net book value {Money(line.Nbv)}");
        builder.Append($"Total net book value at period end: {Money(section.Total)}");
        return builder.ToString();
    }

    private static string DebtorContent(FinancialStatementsService.CurrentAssetsSection section) =>
        $"Trade and other debtors: {Money(section.Debtors)}\nPrepayments and accrued income: {Money(section.Prepayments)}\nTotal debtors and prepayments: {Money(section.Debtors + section.Prepayments)}";

    private static string CurrentCreditorContent(FinancialStatementsService.CreditorsWithinYearSection section) =>
        $"Trade creditors: {Money(section.TradeCreditors)}\nAccruals: {Money(section.Accruals)}\nTaxation and social insurance: {Money(section.TaxCreditors)}\nOther creditors: {Money(section.OtherCreditors)}\nTotal amounts falling due within one year: {Money(section.Total)}";

    private static string LongTermCreditorContent(FinancialStatementsService.CreditorsAfterYearSection section) =>
        $"Loans: {Money(section.Loans)}\nOther creditors: {Money(section.Other)}\nTotal amounts falling due after more than one year: {Money(section.Total)}";

    private static string ReserveContent(FinancialStatementsService.CapitalSection section) =>
        $"Opening retained earnings: {Money(section.OpeningRetainedEarnings)}\nProfit for the financial year: {Money(section.ProfitForYear)}\nDividends: {Money(section.DividendsPaid)}\nOther reserve movements: {Money(section.OtherReserveMovements)}\nClosing retained earnings: {Money(section.RetainedEarnings)}";

    private static string EmployeeContent(PayrollSummary payroll) =>
        $"Average number of employees: {payroll.StaffCount}\nWages and salaries: {Money(payroll.GrossWages)}\nEmployer PRSI: {Money(payroll.EmployerPrsi)}\nPension costs: {Money(payroll.PensionContributions)}\nTotal staff costs: {Money(payroll.GrossWages + payroll.EmployerPrsi + payroll.PensionContributions)}";

    private static string PostBalanceSheetContent(IEnumerable<PostBalanceSheetEvent> events) => string.Join(
        "\n",
        events.Select(item =>
            $"{item.EventDate.ToString("dd MMMM yyyy", IrishCulture)} ({(item.IsAdjusting ? "adjusting" : "non-adjusting")}): {item.Description}"
            + (item.FinancialImpact is { } impact ? $"; estimated financial impact {Money(impact)}" : string.Empty)));

    private static string RelatedPartyContent(IEnumerable<RelatedPartyTransaction> transactions) => string.Join(
        "\n",
        transactions.Select(item =>
            $"{item.PartyName} ({item.Relationship}) — {item.TransactionType}: {Money(item.Amount)}"
            + (item.BalanceOwed is { } balance ? $"; balance at period end {Money(balance)}" : string.Empty)
            + (string.IsNullOrWhiteSpace(item.Terms) ? string.Empty : $"; terms: {item.Terms.Trim()}")));

    private static string ContingencyContent(IEnumerable<ContingentLiability> contingencies) => string.Join(
        "\n",
        contingencies.Select(item =>
            $"{item.Description} ({item.Nature}); likelihood: {item.Likelihood}"
            + (item.EstimatedAmount is { } amount ? $"; estimated amount {Money(amount)}" : string.Empty)));

    private static string TurnoverContent(FinancialStatementsService.ProfitAndLoss statement) =>
        $"Turnover for the financial year: {Money(statement.Turnover)}\nOther operating income: {Money(statement.OtherIncome)}";

    private static string TaxContent(FinancialStatementsService.ProfitAndLoss statement) =>
        $"Current tax charge recognised in the profit and loss account: {Money(statement.TaxCharge)}";

    private static string DividendContent(
        FinancialStatementsService.EquityChanges equity,
        IReadOnlyCollection<Dividend> dividends)
    {
        var declared = dividends.Sum(item => item.Amount);
        return $"Dividends recorded in the year-end register: {Money(declared)}\nDividends recognised in the statement of changes in equity: {Money(equity.DividendsPaid)}";
    }

    private static string Money(decimal amount) =>
        $"€{amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static CompanySizeClass RegimeFallbackSize(ElectedRegime regime) => regime switch
    {
        ElectedRegime.Micro => CompanySizeClass.Micro,
        ElectedRegime.Small or ElectedRegime.SmallAbridged => CompanySizeClass.Small,
        ElectedRegime.Medium => CompanySizeClass.Medium,
        _ => CompanySizeClass.Large
    };

    private static bool RequiredNotesJsonUsesStableCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Any(StatutoryNoteCodes.IsStableCode);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Normalise(string? value) =>
        value?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed record Checklist(AccountingPeriod Period, List<NoteCandidate> Candidates);

    private sealed record NoteCandidate(
        string Code,
        string Title,
        string? Content,
        NoteChecklistState State,
        bool IsIncluded,
        string? ReviewEvidence,
        string? ReviewedBy,
        DateTime? ReviewedAt);
}
