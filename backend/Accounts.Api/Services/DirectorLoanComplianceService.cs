using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

/// <summary>
/// Evidence-led assessment of Companies Act 2014 sections 202, 203, 236, 238 to 245,
/// 307 and 308. The service deliberately fails closed: an arrangement is not treated as
/// permitted merely because its closing balance happens to be below a current-period balance-
/// sheet percentage. Release-level qualified-accountant acceptance remains a separate gate.
/// </summary>
public sealed class DirectorLoanComplianceService(
    AccountsDbContext db,
    FinancialStatementsService statementsService)
{
    public const decimal AppropriateRatePercent = 5m;
    public const decimal Section240ThresholdPercent = 10m;
    public const decimal Section308IndividualDisclosureThreshold = 7_500m;

    public sealed record DirectorLoanLegalSource(string Code, string Title, string Url);

    public sealed record DirectorLoanMovementDetail(
        DateOnly MovementDate,
        DirectorLoanMovementType MovementType,
        decimal Amount,
        string? EvidenceReference);

    public sealed record DirectorLoanDetail(
        int Id,
        string CounterpartyName,
        string? RelatedDirectorName,
        DirectorLoanCounterpartyType CounterpartyType,
        DirectorLoanArrangementType ArrangementType,
        DateOnly? ArrangementDate,
        decimal OpeningBalance,
        decimal Advances,
        decimal Repayments,
        decimal AllowanceMade,
        decimal MaxDuringYear,
        decimal ClosingBalance,
        decimal InterestRate,
        decimal InterestCharged,
        decimal Section236PresumedInterest,
        DirectorLoanTermsStatus TermsStatus,
        string? MainConditions,
        DirectorLoanComplianceBasis ComplianceBasis,
        decimal? RelevantAssets,
        decimal? Section240Threshold,
        bool Section240StrictlyBelowThreshold,
        bool Section307DisclosureRequired,
        DirectorLoanReviewDecision ReviewDecision,
        string? ReviewedBy,
        string? ReviewerRole,
        DateTime? ReviewedAtUtc,
        bool ReadyForFinalOutput,
        IReadOnlyList<string> BlockingIssues,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<DirectorLoanMovementDetail> BalanceMovements);

    public sealed record DirectorLoanSignOffPacket(
        string State,
        bool ReadyForArrangementReview,
        bool ReadyForFinalOutput,
        IReadOnlyList<string> OpenBlockers,
        IReadOnlyList<string> OpenWarnings,
        IReadOnlyList<DirectorLoanLegalSource> LegalSources);

    public sealed record DirectorLoanComplianceResult(
        decimal TotalDirectorLoans,
        decimal AggregateMaximumExposure,
        decimal DisclosureAggregateMaximumExposure,
        decimal? DisclosureOpeningNetAssets,
        decimal? DisclosureClosingNetAssets,
        decimal Section236PresumedInterest,
        bool HasUnresolvedComplianceBlockers,
        bool RequiresAlternativeLegalBasis,
        IReadOnlyList<DirectorLoanDetail> Loans,
        IReadOnlyList<string> BlockingIssues,
        IReadOnlyList<string> Warnings,
        DirectorLoanSignOffPacket SignOffPacket,
        IReadOnlyList<DirectorLoanLegalSource> LegalSources,
        string? Warning);

    public static readonly IReadOnlyList<DirectorLoanLegalSource> LegalSources =
    [
        new("companies-act-2014-s202", "Companies Act 2014, section 202 — Summary Approval Procedure", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/202/revised/en/html"),
        new("companies-act-2014-s203", "Companies Act 2014, section 203 — declaration for transactions with directors", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/203/revised/en/html"),
        new("companies-act-2014-s236", "Companies Act 2014, section 236 — evidential presumptions for loan terms", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/236/revised/en/html"),
        new("companies-act-2014-s238", "Companies Act 2014, section 238(2) — relevant assets", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/238/revised/en/html"),
        new("companies-act-2014-s239", "Companies Act 2014, section 239 — prohibition of loans and related arrangements", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/239/revised/en/html"),
        new("companies-act-2014-s240", "Companies Act 2014, section 240 — arrangements strictly below 10% of relevant assets", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/240/revised/en/html"),
        new("companies-act-2014-s241", "Companies Act 2014, section 241 — later reduction in relevant assets", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/241/revised/en/html"),
        new("companies-act-2014-s242", "Companies Act 2014, section 242 — SAP exception", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/242/revised/en/html"),
        new("companies-act-2014-s243", "Companies Act 2014, section 243 — intra-group transactions", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/243/revised/en/html"),
        new("companies-act-2014-s244", "Companies Act 2014, section 244 — vouched directors' expenses", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/244/revised/en/html"),
        new("companies-act-2014-s245", "Companies Act 2014, section 245 — ordinary-course business transactions", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/245/revised/en/html"),
        new("companies-act-2014-s307", "Companies Act 2014, section 307 — financial-statement disclosures", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/307/revised/en/html"),
        new("companies-act-2014-s308", "Companies Act 2014, section 308 — scope and €7,500 individual exemption", "https://revisedacts.lawreform.ie/eli/2014/act/38/section/308/revised/en/html")
    ];

    public async Task<DirectorLoanComplianceResult> GetComplianceStatusAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(candidate => candidate.Company)
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == periodId && candidate.CompanyId == companyId)
            ?? throw new InvalidOperationException($"Period {periodId} was not found for company {companyId}.");

        var loans = await db.DirectorLoans
            .Include(loan => loan.Director)
            .Include(loan => loan.BalanceMovements)
            .Where(loan => loan.PeriodId == periodId
                && loan.Period.CompanyId == companyId
                && (loan.CounterpartyType == DirectorLoanCounterpartyType.GroupCompany && loan.DirectorId == null
                    || loan.DirectorId != null && loan.Director!.CompanyId == companyId))
            .OrderBy(loan => loan.Id)
            .AsNoTracking()
            .ToListAsync();

        var aggregateMaximumExposure = CalculateAggregateMaximumExposure(loans, period.PeriodStart, section240CountedOnly: true);
        var disclosureAggregateMaximumExposure = CalculateAggregateMaximumExposure(loans, period.PeriodStart, section240CountedOnly: false);
        var totalClosing = loans.Sum(loan => Math.Max(0, loan.ClosingBalance));
        var globalBlockers = new List<string>();
        var globalWarnings = new List<string>();
        var details = new List<DirectorLoanDetail>();

        var priorPeriodId = await db.AccountingPeriods
            .Where(candidate => candidate.CompanyId == companyId && candidate.PeriodEnd < period.PeriodStart)
            .OrderByDescending(candidate => candidate.PeriodEnd)
            .Select(candidate => (int?)candidate.Id)
            .FirstOrDefaultAsync();
        var openingNetAssets = priorPeriodId is { } priorId
            ? await TryGetNetAssetsAsync(companyId, priorId)
            : null;
        var closingNetAssets = await TryGetNetAssetsAsync(companyId, periodId);

        var maximumByDirector = loans
            .Where(loan => loan.DirectorId is not null)
            .GroupBy(loan => loan.DirectorId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(loan => Math.Max(0, DerivedMaximum(loan))));

        foreach (var loan in loans)
        {
            var blockers = new List<string>();
            var warnings = new List<string>();
            var label = CounterpartyLabel(loan);

            void Block(string message) => blockers.Add(message);
            void Warn(string message) => warnings.Add(message);

            ValidateCounterpartyAndTimeline(loan, period, Block);
            ValidateBalanceLedger(loan, period, Block);
            ValidateTermsAndInterestEvidence(loan, Warn, Block);

            var presumedInterest = RequiresSection236InterestPresumption(loan.TermsStatus)
                ? CalculateTimeWeightedInterest(loan, period.PeriodStart, period.PeriodEnd, AppropriateRatePercent)
                : 0m;
            if (presumedInterest > loan.InterestCharged + 0.01m)
            {
                if (string.IsNullOrWhiteSpace(loan.Section236PresumptionEvidenceReference))
                {
                    Block($"Section 236 presumed interest of €{presumedInterest:N2} exceeds recorded interest and no retained rebuttal or accounting evidence is referenced.");
                }
                else
                {
                    Warn($"Section 236 presumed interest of €{presumedInterest:N2} exceeds recorded interest; retained rebuttal/accounting evidence must be checked by the reviewer.");
                }
            }

            decimal? threshold = loan.RelevantAssetsAmount is { } relevantAssets
                ? relevantAssets * (Section240ThresholdPercent / 100m)
                : null;
            var section240StrictlyBelow = threshold is > 0 && aggregateMaximumExposure < threshold.Value;
            ValidateClaimedLegalBasis(
                loan,
                period,
                aggregateMaximumExposure,
                totalClosing,
                threshold,
                section240StrictlyBelow,
                Block,
                Warn);
            ValidateReview(loan, Block);

            var disclosureRequired = loan.DirectorId is not { } directorId
                || maximumByDirector.GetValueOrDefault(directorId) > Section308IndividualDisclosureThreshold;
            if (disclosureRequired && closingNetAssets is null)
                Block("Section 307 disclosure percentages cannot be completed because closing net assets are unavailable.");
            if (disclosureRequired && priorPeriodId is not null && openingNetAssets is null)
                Block("Section 307 comparative disclosure percentages cannot be completed because prior-period net assets are unavailable.");

            var prefixedBlockers = blockers.Select(message => $"{label}: {message}").ToArray();
            var prefixedWarnings = warnings.Select(message => $"{label}: {message}").ToArray();
            globalBlockers.AddRange(prefixedBlockers);
            globalWarnings.AddRange(prefixedWarnings);

            details.Add(new DirectorLoanDetail(
                loan.Id,
                label,
                loan.Director?.Name,
                loan.CounterpartyType,
                loan.ArrangementType,
                loan.ArrangementDate,
                loan.OpeningBalance,
                loan.Advances,
                loan.Repayments,
                loan.AllowanceMade,
                DerivedMaximum(loan),
                loan.ClosingBalance,
                loan.InterestRate,
                loan.InterestCharged,
                presumedInterest,
                loan.TermsStatus,
                loan.LoanTerms,
                loan.ComplianceBasis,
                loan.RelevantAssetsAmount,
                threshold,
                section240StrictlyBelow,
                disclosureRequired,
                loan.ReviewDecision,
                loan.ReviewedBy,
                loan.ReviewerRole,
                loan.ReviewedAtUtc,
                prefixedBlockers.Length == 0,
                prefixedBlockers,
                prefixedWarnings,
                loan.BalanceMovements
                    .OrderBy(movement => movement.MovementDate)
                    .ThenBy(movement => movement.Id)
                    .Select(movement => new DirectorLoanMovementDetail(
                        movement.MovementDate,
                        movement.MovementType,
                        movement.Amount,
                        movement.EvidenceReference))
                    .ToArray()));
        }

        globalBlockers = globalBlockers.Distinct(StringComparer.Ordinal).ToList();
        globalWarnings = globalWarnings.Distinct(StringComparer.Ordinal).ToList();
        var readyForArrangementReview = details.Count == 0 || details.All(detail =>
            detail.BlockingIssues.All(issue => issue.Contains("explicitly accepted", StringComparison.OrdinalIgnoreCase)));
        var readyForFinalOutput = globalBlockers.Count == 0;
        var state = readyForFinalOutput
            ? "accepted"
            : readyForArrangementReview
                ? "review-required"
                : "evidence-incomplete";
        var signOffPacket = new DirectorLoanSignOffPacket(
            state,
            readyForArrangementReview,
            readyForFinalOutput,
            globalBlockers,
            globalWarnings,
            LegalSources);

        var presumedInterestTotal = details.Sum(detail => detail.Section236PresumedInterest);
        return new DirectorLoanComplianceResult(
            totalClosing,
            aggregateMaximumExposure,
            disclosureAggregateMaximumExposure,
            openingNetAssets,
            closingNetAssets,
            presumedInterestTotal,
            globalBlockers.Count > 0,
            details.Any(detail => detail.ComplianceBasis == DirectorLoanComplianceBasis.Section240BelowTenPercent
                && !detail.Section240StrictlyBelowThreshold),
            details,
            globalBlockers,
            globalWarnings,
            signOffPacket,
            LegalSources,
            globalBlockers.FirstOrDefault() ?? globalWarnings.FirstOrDefault());
    }

    public async Task<string> GenerateSection307NoteAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == periodId && candidate.CompanyId == companyId)
            ?? throw new InvalidOperationException($"Period {periodId} was not found for company {companyId}.");
        var current = await GetComplianceStatusAsync(companyId, periodId);
        var priorPeriodId = await db.AccountingPeriods
            .Where(candidate => candidate.CompanyId == companyId && candidate.PeriodEnd < period.PeriodStart)
            .OrderByDescending(candidate => candidate.PeriodEnd)
            .Select(candidate => (int?)candidate.Id)
            .FirstOrDefaultAsync();
        var prior = priorPeriodId is { } priorId
            ? await GetComplianceStatusAsync(companyId, priorId)
            : null;

        if (current.Loans.Count == 0 && (prior?.Loans.Count ?? 0) == 0)
            return "No loans, quasi-loans, credit transactions, guarantees or security arrangements within sections 307 and 308 of the Companies Act 2014 subsisted during the current or preceding financial year.";

        var lines = new List<string>
        {
            "Transactions with directors and connected persons — Companies Act 2014, sections 307 and 308",
            ""
        };
        AppendDisclosureYear(lines, "Current financial year", current);
        AppendDisclosureYear(lines, "Preceding financial year", prior);
        return string.Join("\n", lines).TrimEnd();
    }

    private static void AppendDisclosureYear(
        List<string> lines,
        string heading,
        DirectorLoanComplianceResult? result)
    {
        lines.Add(heading);
        if (result is null || result.Loans.Count == 0)
        {
            lines.Add("  No arrangements recorded.");
            lines.Add("");
            return;
        }

        foreach (var loan in result.Loans)
        {
            lines.Add($"  {loan.CounterpartyName} ({Humanise(loan.ArrangementType)}):");
            if (loan.CounterpartyType == DirectorLoanCounterpartyType.ConnectedPerson)
                lines.Add($"    Related director: {loan.RelatedDirectorName ?? "not retained"}");
            lines.Add($"    Opening balance: €{loan.OpeningBalance:N2}");
            lines.Add($"    Advances: €{loan.Advances:N2}");
            lines.Add($"    Repayments: €{loan.Repayments:N2}");
            lines.Add($"    Allowance for failure or anticipated failure to repay: €{loan.AllowanceMade:N2}");
            lines.Add($"    Maximum outstanding: €{loan.MaxDuringYear:N2}");
            lines.Add($"    Closing balance: €{loan.ClosingBalance:N2}");
            lines.Add($"    Interest rate indicated: {loan.InterestRate:N2}%");
            lines.Add($"    Written-terms status: {Humanise(loan.TermsStatus)}");
            lines.Add($"    Other main conditions: {(string.IsNullOrWhiteSpace(loan.MainConditions) ? "not retained" : loan.MainConditions)}");
            lines.Add($"    Compliance basis recorded: {Humanise(loan.ComplianceBasis)}");
        }

        var totalOpening = result.Loans.Sum(loan => loan.OpeningBalance);
        var totalAdvances = result.Loans.Sum(loan => loan.Advances);
        var totalRepayments = result.Loans.Sum(loan => loan.Repayments);
        var totalAllowance = result.Loans.Sum(loan => loan.AllowanceMade);
        var totalMaximum = result.DisclosureAggregateMaximumExposure;
        var totalClosing = result.Loans.Sum(loan => loan.ClosingBalance);
        lines.Add($"  Aggregate opening balance: €{totalOpening:N2}");
        lines.Add($"  Aggregate advances: €{totalAdvances:N2}");
        lines.Add($"  Aggregate repayments: €{totalRepayments:N2}");
        lines.Add($"  Aggregate allowance: €{totalAllowance:N2}");
        lines.Add($"  Aggregate maximum outstanding: €{totalMaximum:N2}");
        lines.Add($"  Aggregate closing balance: €{totalClosing:N2}");
        lines.Add($"  Opening balance as a percentage of opening net assets: {Percentage(totalOpening, result.DisclosureOpeningNetAssets)}");
        lines.Add($"  Closing balance as a percentage of closing net assets: {Percentage(totalClosing, result.DisclosureClosingNetAssets)}");
        if (result.DisclosureClosingNetAssets is > 0
            && result.DisclosureAggregateMaximumExposure > result.DisclosureClosingNetAssets.Value * 0.10m)
        {
            lines.Add($"  The maximum aggregate amount exceeded 10% of closing net assets ({result.DisclosureAggregateMaximumExposure / result.DisclosureClosingNetAssets.Value:P2}).");
        }
        lines.Add("");
    }

    private static void ValidateCounterpartyAndTimeline(
        DirectorLoan loan,
        AccountingPeriod period,
        Action<string> block)
    {
        if (loan.ArrangementDate is null)
            block("Arrangement date has not been retained.");
        else if (loan.ArrangementDate > period.PeriodEnd)
            block("Arrangement date falls after the reporting period.");

        if (loan.CounterpartyType == DirectorLoanCounterpartyType.GroupCompany)
        {
            if (loan.DirectorId is not null || string.IsNullOrWhiteSpace(loan.CounterpartyName))
                block("An intra-group arrangement must identify the group company and not an individual director.");
            return;
        }

        if (loan.DirectorId is null || loan.Director is null)
        {
            block("The related director is not retained.");
            return;
        }
        if (loan.Director.CompanyId != period.CompanyId
            || loan.Director.Role != OfficerRole.Director
            || loan.Director.AppointedDate is null
            || loan.Director.AppointedDate > period.PeriodEnd
            || loan.Director.ResignedDate is { } resigned && resigned < period.PeriodStart)
        {
            block("The related officer does not have verified director service during the reporting period.");
        }
        if (loan.CounterpartyType == DirectorLoanCounterpartyType.ConnectedPerson
            && string.IsNullOrWhiteSpace(loan.CounterpartyName))
        {
            block("The connected person's name has not been retained.");
        }
    }

    private static void ValidateBalanceLedger(
        DirectorLoan loan,
        AccountingPeriod period,
        Action<string> block)
    {
        if (new[]
            {
                loan.OpeningBalance,
                loan.Advances,
                loan.Repayments,
                loan.ClosingBalance,
                loan.InterestRate,
                loan.InterestCharged,
                loan.AllowanceMade,
                loan.MaxBalanceDuringYear
            }.Any(value => value < 0))
        {
            block("Balances, interest and allowances must be non-negative.");
        }

        if (!Near(loan.ClosingBalance, loan.OpeningBalance + loan.Advances - loan.Repayments))
            block("Opening balance plus advances less repayments does not reconcile to closing balance.");

        var movements = loan.BalanceMovements
            .OrderBy(movement => movement.MovementDate)
            .ThenBy(movement => movement.Id)
            .ToArray();
        if ((loan.Advances > 0 || loan.Repayments > 0) && movements.Length == 0)
            block("Dated advance and repayment movements have not been retained.");
        if (movements.Any(movement => movement.Amount <= 0
                || movement.MovementDate < period.PeriodStart
                || movement.MovementDate > period.PeriodEnd
                || loan.ArrangementDate is { } start && movement.MovementDate < start))
        {
            block("One or more dated movements is outside the reporting/arrangement timeline or has a non-positive amount.");
        }
        if (movements.Any(movement => string.IsNullOrWhiteSpace(movement.EvidenceReference)))
            block("Every dated balance movement requires a retained evidence reference.");

        if (movements.Length == 0)
            return;

        var advances = movements
            .Where(movement => movement.MovementType == DirectorLoanMovementType.Advance)
            .Sum(movement => movement.Amount);
        var repayments = movements
            .Where(movement => movement.MovementType == DirectorLoanMovementType.Repayment)
            .Sum(movement => movement.Amount);
        if (!Near(advances, loan.Advances) || !Near(repayments, loan.Repayments))
            block("Dated movement totals do not reconcile to advances and repayments.");

        var runningBalance = loan.OpeningBalance;
        var maximum = runningBalance;
        foreach (var movement in movements)
        {
            runningBalance += movement.MovementType == DirectorLoanMovementType.Advance
                ? movement.Amount
                : -movement.Amount;
            if (runningBalance < -0.01m)
                block("A dated repayment reduces the balance below zero.");
            maximum = Math.Max(maximum, runningBalance);
        }
        if (!Near(runningBalance, loan.ClosingBalance))
            block("The dated movement ledger does not reconcile to closing balance.");
        if (!Near(maximum, loan.MaxBalanceDuringYear))
            block("The recorded maximum does not agree with the dated movement ledger.");
    }

    private static void ValidateTermsAndInterestEvidence(
        DirectorLoan loan,
        Action<string> warn,
        Action<string> block)
    {
        if (loan.TermsStatus == DirectorLoanTermsStatus.Unassessed)
            block("Written-term and section 236 presumption status has not been assessed.");
        if (loan.TermsStatus is DirectorLoanTermsStatus.WrittenComplete
            or DirectorLoanTermsStatus.WrittenAmbiguousRepayment
            or DirectorLoanTermsStatus.WrittenAmbiguousInterest
            or DirectorLoanTermsStatus.WrittenAmbiguousRepaymentAndInterest
            && string.IsNullOrWhiteSpace(loan.LoanTerms))
        {
            block("Written or partially written terms are claimed but their main conditions are not retained.");
        }
        if (loan.TermsStatus == DirectorLoanTermsStatus.NotWritten)
            warn("Section 236 evidential presumptions apply because terms are not in writing.");
        else if (loan.TermsStatus is DirectorLoanTermsStatus.WrittenAmbiguousRepayment
                 or DirectorLoanTermsStatus.WrittenAmbiguousInterest
                 or DirectorLoanTermsStatus.WrittenAmbiguousRepaymentAndInterest)
            warn("Section 236 evidential presumptions apply to the ambiguous written terms.");
    }

    private static void ValidateClaimedLegalBasis(
        DirectorLoan loan,
        AccountingPeriod period,
        decimal aggregateMaximumExposure,
        decimal aggregateClosingExposure,
        decimal? threshold,
        bool section240StrictlyBelow,
        Action<string> block,
        Action<string> warn)
    {
        switch (loan.ComplianceBasis)
        {
            case DirectorLoanComplianceBasis.Unassessed:
                block("No sections 240 or 242 to 245 legal basis has been assessed.");
                return;

            case DirectorLoanComplianceBasis.Section240BelowTenPercent:
                ValidateRelevantAssetsEvidence(loan, block);
                if (threshold is null || threshold <= 0)
                    block("Section 240 cannot be relied on where the evidenced relevant-assets amount is zero, negative or missing.");
                else if (!section240StrictlyBelow)
                    block($"Aggregate maximum exposure of €{aggregateMaximumExposure:N2} is not strictly below the section 240 threshold of €{threshold:N2}.");
                ValidateRelevantAssetsFallReview(loan, aggregateClosingExposure, threshold, block);
                return;

            case DirectorLoanComplianceBasis.Section242SummaryApprovalProcedure:
                ValidateSapEvidence(loan, block);
                return;

            case DirectorLoanComplianceBasis.Section243IntraGroup:
                if (loan.CounterpartyType != DirectorLoanCounterpartyType.GroupCompany)
                    block("Section 243 is an intra-group exception and cannot support an arrangement with an individual director or connected person.");
                if (string.IsNullOrWhiteSpace(loan.ExceptionEvidenceReference))
                    block("Section 243 group-relationship evidence is not retained.");
                return;

            case DirectorLoanComplianceBasis.Section244VouchedExpense:
                if (loan.CounterpartyType == DirectorLoanCounterpartyType.GroupCompany)
                    block("Section 244 applies to a director's properly incurred vouched expenditure, not a group company.");
                if (loan.ExpenseIncurredDate is null || string.IsNullOrWhiteSpace(loan.ExceptionEvidenceReference))
                    block("Section 244 requires the expense-incurred date and vouched expenditure evidence.");
                if (loan.ExpenseIncurredDate is { } incurred)
                {
                    var deadline = incurred.AddMonths(6);
                    if (loan.ExpenseDischargedDate is { } discharged)
                    {
                        if (discharged < incurred || discharged > deadline)
                            block("Section 244 liability was not discharged within six months of being incurred.");
                    }
                    else if (deadline <= period.PeriodEnd)
                    {
                        block("Section 244 liability is overdue and no discharge date is retained.");
                    }
                    else
                    {
                        warn($"Section 244 discharge remains due by {deadline:yyyy-MM-dd}.");
                    }
                }
                return;

            case DirectorLoanComplianceBasis.Section245OrdinaryBusiness:
                if (!loan.OrdinaryCourseConfirmed || !loan.NoMoreFavourableTermsConfirmed)
                    block("Section 245 requires confirmation of ordinary-course business and terms no more favourable than for an equivalent unconnected person.");
                if (string.IsNullOrWhiteSpace(loan.ExceptionEvidenceReference))
                    block("Section 245 comparator and ordinary-course evidence is not retained.");
                return;

            default:
                block("The claimed legal basis is not supported by this compliance evaluator.");
                return;
        }
    }

    private static void ValidateRelevantAssetsEvidence(DirectorLoan loan, Action<string> block)
    {
        if (loan.RelevantAssetsBasis == DirectorLoanRelevantAssetsBasis.Unassessed
            || loan.RelevantAssetsAmount is null
            || loan.RelevantAssetsAsOfDate is null
            || string.IsNullOrWhiteSpace(loan.RelevantAssetsReference))
        {
            block("Section 238(2) relevant-assets basis, amount, as-of date and source reference must all be retained.");
        }
        if (loan.RelevantAssetsBasis == DirectorLoanRelevantAssetsBasis.CalledUpShareCapitalNoPriorStatements
            && !loan.NoPriorFinancialStatementsConfirmed)
        {
            block("Called-up share capital may be used only where no entity financial statements have previously been prepared and laid.");
        }
        if (loan.ArrangementDate is { } arrangementDate
            && loan.RelevantAssetsAsOfDate is { } assetsDate
            && assetsDate > arrangementDate)
        {
            block("The relevant-assets evidence post-dates the arrangement and cannot establish the section 240 position when it was entered into.");
        }
    }

    private static void ValidateRelevantAssetsFallReview(
        DirectorLoan loan,
        decimal aggregateClosingExposure,
        decimal? threshold,
        Action<string> block)
    {
        switch (loan.RelevantAssetsFallReview)
        {
            case DirectorLoanRelevantAssetsFallReview.Unassessed:
                block("The section 241 later-reduction-in-relevant-assets review is incomplete.");
                break;
            case DirectorLoanRelevantAssetsFallReview.FallRemainedBelowLimit:
                if (threshold is null || threshold <= 0 || aggregateClosingExposure >= threshold)
                    block("The claimed post-fall exposure is not strictly below the section 240 limit.");
                break;
            case DirectorLoanRelevantAssetsFallReview.TermsAmendedWithinTwoMonths:
                if (loan.RelevantAssetsReductionAwarenessDate is not { } awareness
                    || loan.TermsAmendedDate is not { } amended
                    || amended < awareness
                    || amended > awareness.AddMonths(2)
                    || string.IsNullOrWhiteSpace(loan.TermsAmendmentEvidenceReference))
                {
                    block("Section 241 remediation requires awareness and amendment dates within two months plus retained amendment evidence.");
                }
                if (threshold is null || threshold <= 0 || aggregateClosingExposure >= threshold)
                    block("The amended arrangements have not been evidenced as returning aggregate exposure strictly below the limit.");
                break;
            case DirectorLoanRelevantAssetsFallReview.SapArrangementNotCounted:
                block("An arrangement relying on SAP should use the section 242 basis; SAP arrangements are not counted under section 240.");
                break;
        }
    }

    private static void ValidateSapEvidence(DirectorLoan loan, Action<string> block)
    {
        if (loan.SapDeclarationDate is not { } declaration
            || loan.SapResolutionDate is not { } resolution
            || loan.SapActivityStartDate is not { } activityStart
            || loan.SapCroFilingDate is not { } croFiling)
        {
            block("SAP declaration, special-resolution, activity-start and CRO-filing dates must all be retained.");
            return;
        }
        if (declaration > resolution || declaration < resolution.AddDays(-30))
            block("The SAP directors' declaration was not made within the 30 days before the special resolution.");
        if (resolution > activityStart || resolution.AddMonths(12) < activityStart)
            block("The restricted activity did not begin on or after, and within 12 months of, the SAP resolution.");
        if (croFiling < activityStart || croFiling > activityStart.AddDays(21))
            block("The section 203 declaration copy was not filed within 21 days after the restricted activity began.");
        if (!loan.SapDeclarationCoversSection203Matters)
            block("The retained SAP declaration is not confirmed to cover every section 203 matter and 12-month solvency opinion.");
        if (string.IsNullOrWhiteSpace(loan.SapDeclarationReference)
            || string.IsNullOrWhiteSpace(loan.SapResolutionReference)
            || string.IsNullOrWhiteSpace(loan.SapCroFilingReference))
        {
            block("SAP declaration, resolution and CRO filing evidence references must all be retained.");
        }
    }

    private static void ValidateReview(DirectorLoan loan, Action<string> block)
    {
        if (loan.ReviewDecision != DirectorLoanReviewDecision.Accepted)
        {
            block(loan.ReviewDecision == DirectorLoanReviewDecision.RemediationRequired
                ? "The arrangement review explicitly requires remediation."
                : "The arrangement has not been explicitly accepted by a named professional reviewer.");
            return;
        }
        if (string.IsNullOrWhiteSpace(loan.ReviewedBy)
            || string.IsNullOrWhiteSpace(loan.ReviewerRole)
            || loan.ReviewedAtUtc is null
            || loan.ReviewedAtUtc.Value.Kind != DateTimeKind.Utc
            || loan.ReviewedAtUtc > DateTime.UtcNow.AddMinutes(1)
            || (loan.ReviewNote?.Trim().Length ?? 0) < 20)
        {
            block("Accepted review evidence requires a named reviewer, role, valid UTC timestamp and substantive note.");
        }
    }

    private async Task<decimal?> TryGetNetAssetsAsync(int companyId, int periodId)
    {
        try
        {
            var balanceSheet = await statementsService.GetBalanceSheetAsync(companyId, periodId);
            return balanceSheet.NetAssets;
        }
        catch
        {
            return null;
        }
    }

    private static decimal CalculateAggregateMaximumExposure(
        IReadOnlyList<DirectorLoan> loans,
        DateOnly periodStart,
        bool section240CountedOnly)
    {
        var counted = loans
            .Where(loan => !section240CountedOnly
                || loan.ComplianceBasis != DirectorLoanComplianceBasis.Section242SummaryApprovalProcedure
                    && loan.CounterpartyType != DirectorLoanCounterpartyType.GroupCompany)
            .ToArray();
        if (counted.Length == 0)
            return 0;

        var dates = counted
            .SelectMany(loan => loan.BalanceMovements.Select(movement => movement.MovementDate))
            .Append(periodStart)
            .Distinct()
            .OrderBy(date => date)
            .ToArray();
        return dates.Max(date => counted.Sum(loan => Math.Max(0, BalanceAt(loan, date))));
    }

    private static decimal BalanceAt(DirectorLoan loan, DateOnly date) =>
        loan.OpeningBalance + loan.BalanceMovements
            .Where(movement => movement.MovementDate <= date)
            .Sum(movement => movement.MovementType == DirectorLoanMovementType.Advance
                ? movement.Amount
                : -movement.Amount);

    private static decimal DerivedMaximum(DirectorLoan loan)
    {
        var running = loan.OpeningBalance;
        var maximum = running;
        foreach (var movement in loan.BalanceMovements
                     .OrderBy(movement => movement.MovementDate)
                     .ThenBy(movement => movement.Id))
        {
            running += movement.MovementType == DirectorLoanMovementType.Advance
                ? movement.Amount
                : -movement.Amount;
            maximum = Math.Max(maximum, running);
        }
        return loan.BalanceMovements.Count == 0
            ? Math.Max(loan.MaxBalanceDuringYear, Math.Max(loan.OpeningBalance, loan.ClosingBalance))
            : maximum;
    }

    public static decimal CalculateTimeWeightedInterest(
        DirectorLoan loan,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal annualRatePercent)
    {
        var balance = loan.OpeningBalance;
        var cursor = periodStart;
        var interest = 0m;
        foreach (var movement in loan.BalanceMovements
                     .Where(movement => movement.MovementDate >= periodStart && movement.MovementDate <= periodEnd)
                     .OrderBy(movement => movement.MovementDate)
                     .ThenBy(movement => movement.Id))
        {
            var days = movement.MovementDate.DayNumber - cursor.DayNumber;
            interest += Math.Max(0, balance) * annualRatePercent / 100m * days / 365m;
            balance += movement.MovementType == DirectorLoanMovementType.Advance
                ? movement.Amount
                : -movement.Amount;
            cursor = movement.MovementDate;
        }
        var finalDays = periodEnd.AddDays(1).DayNumber - cursor.DayNumber;
        interest += Math.Max(0, balance) * annualRatePercent / 100m * finalDays / 365m;
        return decimal.Round(interest, 2, MidpointRounding.AwayFromZero);
    }

    private static bool RequiresSection236InterestPresumption(DirectorLoanTermsStatus status) =>
        status is DirectorLoanTermsStatus.NotWritten
            or DirectorLoanTermsStatus.WrittenAmbiguousInterest
            or DirectorLoanTermsStatus.WrittenAmbiguousRepaymentAndInterest;

    private static string CounterpartyLabel(DirectorLoan loan) =>
        loan.CounterpartyName
        ?? loan.Director?.Name
        ?? (loan.DirectorId is { } directorId ? $"Director #{directorId}" : $"Arrangement #{loan.Id}");

    private static string Percentage(decimal amount, decimal? netAssets) =>
        netAssets is > 0 ? (amount / netAssets.Value).ToString("P2") : "not meaningful (net assets are zero, negative or unavailable)";

    private static string Humanise<TEnum>(TEnum value) where TEnum : struct, Enum =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<!^)([A-Z])", " $1");

    private static bool Near(decimal left, decimal right) => Math.Abs(left - right) <= 0.01m;
}
