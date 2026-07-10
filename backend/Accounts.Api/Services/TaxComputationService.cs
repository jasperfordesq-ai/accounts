using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

/// <summary>
/// Produces corporation-tax support data for the deliberately bounded simple-company scope. This is
/// not a CT1 return generator. Unsupported facts remain visible in the response and block final tax
/// charge/readiness workflows instead of being silently ignored.
/// </summary>
public class TaxComputationService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    public const string OutputKind = "corporation-tax-support-data-not-ct1-return";

    public record TaxAdjustment(string Description, decimal Amount, string Basis);

    public record TaxSourceReference(string Code, string Title, string Url);

    public record TaxComputation(
        decimal AccountingProfit,
        List<TaxAdjustment> Adjustments,
        decimal TaxableProfit,
        decimal TradingLossAvailable,
        decimal CorporationTaxAt125,
        decimal CorporationTaxAt25,
        decimal TotalCorporationTax,
        decimal PreliminaryTaxPaid,
        decimal BalanceDue,
        string Notes,
        decimal TradingProfitBeforeLossRelief,
        decimal TradingProfitAfterLossRelief,
        decimal PassiveNonTradingIncome,
        decimal BroughtForwardTradingLoss,
        decimal TradingLossUsed,
        decimal TradingLossCarriedForward,
        decimal CapitalAllowances,
        decimal BalancingAllowances,
        decimal BalancingCharges,
        string SupportStatus,
        bool FinalTaxChargeSupported,
        bool ManualReviewRequired,
        string OutputKind,
        bool IsCompleteCt1Return,
        IReadOnlyList<string> BlockingReasons,
        IReadOnlyList<TaxSourceReference> Sources,
        string CalculationSha256
    );

    public record Ct1SupportData(
        string CompanyName,
        string TaxReference,
        string PeriodStart,
        string PeriodEnd,
        decimal Turnover,
        decimal GrossProfit,
        decimal NetProfit,
        decimal TaxableProfit,
        decimal TaxDue,
        decimal PreliminaryTaxPaid,
        decimal BalanceDue,
        List<TaxAdjustment> Adjustments,
        decimal TotalDirectorsFees,
        decimal TotalEmployeeCosts,
        decimal DepreciationCharged,
        decimal CapitalAllowances,
        decimal TradingLossAvailable,
        decimal TradingProfitBeforeLossRelief,
        decimal TradingProfitAfterLossRelief,
        decimal PassiveNonTradingIncome,
        decimal BroughtForwardTradingLoss,
        decimal TradingLossUsed,
        decimal TradingLossCarriedForward,
        decimal BalancingAllowances,
        decimal BalancingCharges,
        string SupportStatus,
        bool FinalTaxChargeSupported,
        bool ManualReviewRequired,
        string OutputKind,
        bool IsCompleteCt1Return,
        IReadOnlyList<string> BlockingReasons,
        IReadOnlyList<TaxSourceReference> Sources,
        string CalculationSha256
    );

    public record CorporationTaxScopeReviewInput(
        bool? IsCloseCompany,
        bool? IsServiceCompany,
        bool HasGroupOrConsortiumRelief,
        bool HasChargeableGains,
        bool HasForeignIncomeOrTaxCredits,
        bool HasExceptedTrade,
        bool HasOtherReliefsOrSpecialRegimes,
        bool DeclaredPassiveIncomePresent,
        bool PassiveIncomeClassificationReviewed,
        CorporationTaxLossTreatment LossTreatment,
        decimal BroughtForwardTradingLoss,
        string? BroughtForwardLossEvidence,
        string EvidenceNote);

    public record CapitalAllowanceClaimResult(int AssetId, decimal Cost, decimal Claim);

    private sealed record CapitalAllowanceSupport(
        List<CapitalAllowanceClaimResult> Claims,
        List<TaxAdjustment> Adjustments,
        List<string> BlockingReasons,
        decimal WearAndTearAllowances,
        decimal BalancingAllowances,
        decimal BalancingCharges);

    private sealed record CoreCalculation(
        AccountingPeriod Period,
        CorporationTaxScopeReview? ScopeReview,
        decimal AccountingProfit,
        List<TaxAdjustment> Adjustments,
        decimal TradingProfitBeforeLossRelief,
        decimal TradingProfitAfterLossRelief,
        decimal PassiveNonTradingIncome,
        decimal BroughtForwardTradingLoss,
        decimal CurrentPeriodTradingLoss,
        decimal TradingLossUsed,
        decimal TradingLossCarriedForward,
        decimal TradingTaxable,
        decimal NonTradingTaxable,
        decimal PreliminaryTaxPaid,
        CapitalAllowanceSupport CapitalAllowances,
        List<string> BlockingReasons,
        string CalculationSha256);

    public async Task<TaxComputation> ComputeAsync(int companyId, int periodId)
    {
        var core = await ComputeCoreAsync(companyId, periodId);
        var blockers = new List<string>(core.BlockingReasons);
        var retainedLoss = await db.CorporationTaxLossRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.PeriodId == periodId);

        if (retainedLoss is null)
        {
            blockers.Add("The period has no retained corporation-tax loss movement. Save the tax scope review to bind the calculation.");
        }
        else
        {
            if (!string.Equals(retainedLoss.CalculationSha256, core.CalculationSha256, StringComparison.OrdinalIgnoreCase))
                blockers.Add("Corporation-tax source data changed after the retained scope/loss review. Re-run and save the tax scope review.");
            if (retainedLoss.OpeningTradingLoss != core.BroughtForwardTradingLoss
                || retainedLoss.CurrentPeriodTradingLoss != core.CurrentPeriodTradingLoss
                || retainedLoss.TradingLossUsed != core.TradingLossUsed
                || retainedLoss.ClosingTradingLoss != core.TradingLossCarriedForward)
            {
                blockers.Add("The retained corporation-tax loss movement no longer reconciles to the current support calculation.");
            }
        }

        blockers = blockers.Distinct(StringComparer.Ordinal).ToList();
        var tradingTaxable = Math.Max(0m, core.TradingTaxable);
        var nonTradingTaxable = Math.Max(0m, core.NonTradingTaxable);
        var taxableProfit = tradingTaxable + nonTradingTaxable;
        var taxAt125 = decimal.Round(tradingTaxable * 0.125m, 2, MidpointRounding.AwayFromZero);
        var taxAt25 = decimal.Round(nonTradingTaxable * 0.25m, 2, MidpointRounding.AwayFromZero);
        var totalTax = taxAt125 + taxAt25;
        var balanceDue = totalTax - core.PreliminaryTaxPaid;
        var finalTaxChargeSupported = blockers.Count == 0;

        var noteParts = new List<string>
        {
            "Support data only: this is not a complete CT1 return and cannot be filed directly."
        };
        if (nonTradingTaxable > 0)
            noteParts.Add($"Non-trading income of €{nonTradingTaxable:N2} is shown at 25% for support purposes.");
        if (tradingTaxable > 0)
            noteParts.Add($"Trading profit after supported loss relief of €{tradingTaxable:N2} is shown at 12.5%.");
        if (core.TradingLossCarriedForward > 0)
            noteParts.Add($"Trading loss of €{core.TradingLossCarriedForward:N2} is available to carry forward in the support ledger; other loss claims/elections remain outside scope.");
        if (core.PreliminaryTaxPaid != 0)
            noteParts.Add($"Preliminary tax/credit recorded: €{core.PreliminaryTaxPaid:N2}.");
        if (!finalTaxChargeSupported)
            noteParts.Add("Final tax charge is blocked: " + string.Join("; ", blockers));

        return new TaxComputation(
            core.AccountingProfit,
            core.Adjustments,
            taxableProfit,
            core.CurrentPeriodTradingLoss,
            taxAt125,
            taxAt25,
            totalTax,
            core.PreliminaryTaxPaid,
            balanceDue,
            string.Join(" ", noteParts),
            core.TradingProfitBeforeLossRelief,
            core.TradingProfitAfterLossRelief,
            core.PassiveNonTradingIncome,
            core.BroughtForwardTradingLoss,
            core.TradingLossUsed,
            core.TradingLossCarriedForward,
            core.CapitalAllowances.WearAndTearAllowances,
            core.CapitalAllowances.BalancingAllowances,
            core.CapitalAllowances.BalancingCharges,
            finalTaxChargeSupported ? "machine-supported-simple-scope" : "manual-review-required",
            finalTaxChargeSupported,
            true,
            OutputKind,
            false,
            blockers,
            CorporationTaxRuleSources.All,
            core.CalculationSha256);
    }

    public async Task<Ct1SupportData> GetCt1SupportDataAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var computation = await ComputeAsync(companyId, periodId);
        var pl = await statementsService.GetProfitAndLossAsync(companyId, periodId);
        var payroll = await db.PayrollSummaries.AsNoTracking().FirstOrDefaultAsync(item => item.PeriodId == periodId);
        var depreciation = await db.DepreciationEntries
            .Where(item => item.PeriodId == periodId)
            .SumAsync(item => item.Charge);

        return new Ct1SupportData(
            period.Company.LegalName,
            period.Company.TaxReference ?? string.Empty,
            period.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            period.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            pl.Turnover,
            pl.GrossProfit,
            pl.ProfitBeforeTax,
            computation.TaxableProfit,
            computation.TotalCorporationTax,
            computation.PreliminaryTaxPaid,
            computation.BalanceDue,
            computation.Adjustments,
            payroll?.DirectorsFees ?? 0m,
            payroll is null
                ? 0m
                : payroll.GrossWages + payroll.DirectorsFees + payroll.EmployerPrsi + payroll.PensionContributions,
            depreciation,
            computation.CapitalAllowances,
            computation.TradingLossAvailable,
            computation.TradingProfitBeforeLossRelief,
            computation.TradingProfitAfterLossRelief,
            computation.PassiveNonTradingIncome,
            computation.BroughtForwardTradingLoss,
            computation.TradingLossUsed,
            computation.TradingLossCarriedForward,
            computation.BalancingAllowances,
            computation.BalancingCharges,
            computation.SupportStatus,
            computation.FinalTaxChargeSupported,
            computation.ManualReviewRequired,
            computation.OutputKind,
            computation.IsCompleteCt1Return,
            computation.BlockingReasons,
            computation.Sources,
            computation.CalculationSha256);
    }

    public async Task<TaxComputation> SaveScopeReviewAsync(
        int companyId,
        int periodId,
        CorporationTaxScopeReviewInput input,
        string preparedBy)
    {
        if (string.IsNullOrWhiteSpace(preparedBy))
            throw new BusinessRuleException("A named workflow actor is required for the corporation-tax scope review.");
        if (input.BroughtForwardTradingLoss < 0)
            throw new BusinessRuleException("Brought-forward trading losses cannot be negative.");
        var evidenceNote = input.EvidenceNote?.Trim() ?? string.Empty;
        if (evidenceNote.Length < 20)
            throw new BusinessRuleException("Corporation-tax scope evidence must contain at least 20 characters.");
        if (input.BroughtForwardTradingLoss > 0 && (input.BroughtForwardLossEvidence?.Trim().Length ?? 0) < 20)
            throw new BusinessRuleException("A brought-forward trading loss requires retained evidence of at least 20 characters.");
        if (input.IsCloseCompany == false && input.IsServiceCompany == true)
            throw new BusinessRuleException("A company cannot be marked as a close service company when close-company status is false.");

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync()
            : null;
        try
        {
            var period = await db.AccountingPeriods
                .Include(item => item.CorporationTaxScopeReview)
                .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId)
                ?? throw new ResourceNotFoundException($"Period {periodId} not found");

            var review = period.CorporationTaxScopeReview;
            if (review is null)
            {
                review = new CorporationTaxScopeReview
                {
                    PeriodId = periodId,
                    PreparedBy = preparedBy.Trim(),
                    EvidenceNote = evidenceNote
                };
                db.CorporationTaxScopeReviews.Add(review);
            }

            review.IsCloseCompany = input.IsCloseCompany;
            review.IsServiceCompany = input.IsServiceCompany;
            review.HasGroupOrConsortiumRelief = input.HasGroupOrConsortiumRelief;
            review.HasChargeableGains = input.HasChargeableGains;
            review.HasForeignIncomeOrTaxCredits = input.HasForeignIncomeOrTaxCredits;
            review.HasExceptedTrade = input.HasExceptedTrade;
            review.HasOtherReliefsOrSpecialRegimes = input.HasOtherReliefsOrSpecialRegimes;
            review.DeclaredPassiveIncomePresent = input.DeclaredPassiveIncomePresent;
            review.PassiveIncomeClassificationReviewed = input.PassiveIncomeClassificationReviewed;
            review.LossTreatment = input.LossTreatment;
            review.BroughtForwardTradingLoss = input.BroughtForwardTradingLoss;
            review.BroughtForwardLossEvidence = string.IsNullOrWhiteSpace(input.BroughtForwardLossEvidence)
                ? null
                : input.BroughtForwardLossEvidence.Trim();
            review.PreparedBy = preparedBy.Trim();
            review.PreparedAtUtc = DateTime.UtcNow;
            review.EvidenceNote = evidenceNote;
            await db.SaveChangesAsync();

            var core = await ComputeCoreAsync(companyId, periodId);
            var record = await db.CorporationTaxLossRecords
                .FirstOrDefaultAsync(item => item.PeriodId == periodId);
            if (record is null)
            {
                record = new CorporationTaxLossRecord
                {
                    PeriodId = periodId,
                    CalculationSha256 = core.CalculationSha256,
                    RecordedBy = preparedBy.Trim()
                };
                db.CorporationTaxLossRecords.Add(record);
            }

            record.OpeningTradingLoss = core.BroughtForwardTradingLoss;
            record.CurrentPeriodTradingLoss = core.CurrentPeriodTradingLoss;
            record.TradingLossUsed = core.TradingLossUsed;
            record.ClosingTradingLoss = core.TradingLossCarriedForward;
            record.Treatment = input.LossTreatment;
            record.CalculationSha256 = core.CalculationSha256;
            record.RecordedBy = preparedBy.Trim();
            record.RecordedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var computation = await ComputeAsync(companyId, periodId);
            if (transaction is not null)
                await transaction.CommitAsync();
            return computation;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task AssertFinalTaxChargeSupportedAsync(int companyId, int periodId)
    {
        var computation = await ComputeAsync(companyId, periodId);
        if (!computation.FinalTaxChargeSupported)
        {
            throw new BusinessRuleException(
                "Final corporation-tax charge and Revenue-ready handoff are blocked: "
                + string.Join("; ", computation.BlockingReasons));
        }
    }

    public async Task<List<CapitalAllowanceClaimResult>> ComputeCapitalAllowanceClaimsAsync(
        int companyId,
        DateOnly periodStart,
        DateOnly periodEnd) =>
        (await ComputeCapitalAllowanceSupportAsync(companyId, periodStart, periodEnd)).Claims;

    public async Task PersistCapitalAllowanceClaimsAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var existing = await db.CapitalAllowanceClaims
            .Where(item => item.PeriodId == periodId)
            .ToListAsync();
        db.CapitalAllowanceClaims.RemoveRange(existing);

        var claims = await ComputeCapitalAllowanceClaimsAsync(companyId, period.PeriodStart, period.PeriodEnd);
        foreach (var claim in claims)
        {
            db.CapitalAllowanceClaims.Add(new CapitalAllowanceClaim
            {
                AssetId = claim.AssetId,
                PeriodId = periodId,
                Cost = claim.Cost,
                Claim = claim.Claim
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<CoreCalculation> ComputeCoreAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(item => item.Company)
            .Include(item => item.CorporationTaxScopeReview)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var profitAndLoss = await statementsService.GetProfitAndLossAsync(companyId, periodId);
        var accountingProfit = profitAndLoss.ProfitBeforeTax;
        var passiveIncome = await statementsService.GetNonTradingIncomeAsync(companyId, periodId);
        var adjustments = new List<TaxAdjustment>();
        var blockers = new List<string>();

        var depreciation = await db.DepreciationEntries
            .Where(item => item.PeriodId == periodId)
            .SumAsync(item => item.Charge);
        if (depreciation != 0)
        {
            adjustments.Add(new TaxAdjustment(
                "Add back: depreciation",
                depreciation,
                "Accounting depreciation is not the capital-allowance claim."));
        }

        var nonDeductibleCategories = await db.AccountCategories
            .Where(category => (category.CompanyId == companyId || category.CompanyId == null && category.IsSystem)
                && category.Type == AccountCategoryType.Expense
                && category.TaxTreatment == TaxTreatment.NonDeductible)
            .Select(category => category.Id)
            .ToListAsync();

        // Bank expense entries are negative. Negating the signed amount adds an expense back and
        // deducts a refund/reversal. Preserve that sign so refunds cannot become extra tax.
        var transactionDisallowable = await db.ImportedTransactions
            .Where(transaction => transaction.PeriodId == periodId
                && transaction.CategoryId != null
                && nonDeductibleCategories.Contains(transaction.CategoryId.Value)
                && !transaction.IsDuplicate)
            .SumAsync(transaction => -transaction.Amount);

        // Manual-journal reversals preserve their direction through debit/credit orientation.
        var journalRows = await db.Adjustments
            .Where(adjustment => adjustment.PeriodId == periodId
                && (adjustment.DebitCategoryId != null && nonDeductibleCategories.Contains(adjustment.DebitCategoryId.Value)
                    || adjustment.CreditCategoryId != null && nonDeductibleCategories.Contains(adjustment.CreditCategoryId.Value)))
            .Select(adjustment => new
            {
                adjustment.Amount,
                DebitIsNonDeductible = adjustment.DebitCategoryId != null
                    && nonDeductibleCategories.Contains(adjustment.DebitCategoryId.Value),
                CreditIsNonDeductible = adjustment.CreditCategoryId != null
                    && nonDeductibleCategories.Contains(adjustment.CreditCategoryId.Value)
            })
            .ToListAsync();
        var journalDisallowable = journalRows.Sum(row =>
            (row.DebitIsNonDeductible ? row.Amount : 0m)
            - (row.CreditIsNonDeductible ? row.Amount : 0m));
        var signedDisallowable = transactionDisallowable + journalDisallowable;
        if (signedDisallowable != 0)
        {
            adjustments.Add(new TaxAdjustment(
                signedDisallowable > 0
                    ? "Add back: non-deductible expenses"
                    : "Deduct: non-deductible expense refunds/reversals",
                signedDisallowable,
                "Signed bank entries and debit/credit journals; refunds and reversals reduce the add-back."));
        }

        var capital = await ComputeCapitalAllowanceSupportAsync(companyId, period.PeriodStart, period.PeriodEnd);
        adjustments.AddRange(capital.Adjustments);
        blockers.AddRange(capital.BlockingReasons);

        var ambiguousIncome = await FindAmbiguousIncomeCategoriesAsync(companyId, periodId);
        if (ambiguousIncome.Count > 0)
        {
            blockers.Add(
                "Income categories require explicit trading/non-trading classification: "
                + string.Join(", ", ambiguousIncome));
        }

        var scope = period.CorporationTaxScopeReview;
        if (scope is null)
        {
            blockers.Add("Corporation-tax scope questionnaire has not been prepared for this period.");
        }
        else
        {
            AddScopeBlockers(period.Company, scope, passiveIncome, blockers);
        }

        var broughtForwardLoss = scope?.BroughtForwardTradingLoss ?? 0m;
        await AddLossContinuityBlockersAsync(period, scope, broughtForwardLoss, blockers);

        var tradingProfitBeforeLoss = accountingProfit - passiveIncome + adjustments.Sum(adjustment => adjustment.Amount);
        var currentPeriodLoss = Math.Max(0m, -tradingProfitBeforeLoss);
        var positiveTradingProfit = Math.Max(0m, tradingProfitBeforeLoss);
        var lossTreatment = scope?.LossTreatment ?? CorporationTaxLossTreatment.Unreviewed;
        var lossUsed = lossTreatment == CorporationTaxLossTreatment.CarryForwardSameTrade
            ? Math.Min(broughtForwardLoss, positiveTradingProfit)
            : 0m;
        var tradingProfitAfterLoss = positiveTradingProfit - lossUsed;
        var closingLoss = broughtForwardLoss - lossUsed + currentPeriodLoss;

        if (scope is not null)
        {
            var lossExists = currentPeriodLoss > 0 || broughtForwardLoss > 0;
            if (lossExists && scope.LossTreatment != CorporationTaxLossTreatment.CarryForwardSameTrade)
                blockers.Add("Trading losses exist, but the only supported treatment is same-trade carry-forward with first-available-profit use.");
            if (!lossExists && scope.LossTreatment is not CorporationTaxLossTreatment.NotApplicable
                and not CorporationTaxLossTreatment.CarryForwardSameTrade)
            {
                blockers.Add("The recorded loss treatment is inconsistent with a period that has no trading-loss balance.");
            }
        }

        var preliminaryTax = await db.TaxBalances
            .Where(item => item.PeriodId == periodId && item.TaxType == TaxType.CorporationTax)
            .Select(item => item.Paid)
            .FirstOrDefaultAsync();

        var fingerprint = CalculationFingerprint(
            period,
            scope,
            accountingProfit,
            passiveIncome,
            adjustments,
            broughtForwardLoss,
            currentPeriodLoss,
            lossUsed,
            closingLoss,
            preliminaryTax);

        return new CoreCalculation(
            period,
            scope,
            accountingProfit,
            adjustments,
            tradingProfitBeforeLoss,
            tradingProfitAfterLoss,
            passiveIncome,
            broughtForwardLoss,
            currentPeriodLoss,
            lossUsed,
            closingLoss,
            tradingProfitAfterLoss,
            Math.Max(0m, passiveIncome),
            preliminaryTax,
            capital,
            blockers.Distinct(StringComparer.Ordinal).ToList(),
            fingerprint);
    }

    private async Task<CapitalAllowanceSupport> ComputeCapitalAllowanceSupportAsync(
        int companyId,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var assets = await db.FixedAssets
            .Where(asset => asset.CompanyId == companyId
                && asset.AcquisitionDate <= periodEnd
                && (asset.DisposalDate == null || asset.DisposalDate >= periodStart))
            .OrderBy(asset => asset.Id)
            .ToListAsync();
        var claims = new List<CapitalAllowanceClaimResult>();
        var adjustments = new List<TaxAdjustment>();
        var blockers = new List<string>();
        decimal balancingAllowances = 0m;
        decimal balancingCharges = 0m;
        var periodFraction = PeriodYearFraction(periodStart, periodEnd);

        foreach (var asset in assets)
        {
            if (asset.CapitalAllowanceTreatment == CapitalAllowanceTreatment.Unreviewed
                || string.IsNullOrWhiteSpace(asset.CapitalAllowanceReviewedBy)
                || asset.CapitalAllowanceReviewedAtUtc is null
                || (asset.CapitalAllowanceEvidence?.Trim().Length ?? 0) < 20)
            {
                blockers.Add($"Fixed asset '{asset.Name}' has no retained capital-allowance treatment review.");
                continue;
            }

            if (asset.CapitalAllowanceTreatment == CapitalAllowanceTreatment.UnsupportedSpecialScheme)
            {
                blockers.Add($"Fixed asset '{asset.Name}' uses a capital-allowance scheme outside the supported 12.5% plant-and-machinery scope.");
                continue;
            }

            if (asset.CapitalAllowanceTreatment == CapitalAllowanceTreatment.NonQualifying)
            {
                if (asset.DisposalDate is { } nonQualifyingDisposal
                    && nonQualifyingDisposal >= periodStart
                    && nonQualifyingDisposal <= periodEnd)
                {
                    blockers.Add(
                        $"Fixed asset '{asset.Name}' is outside the supported plant-and-machinery allowance scope and was disposed in the period; chargeable-gain/loss review is required.");
                }
                continue;
            }

            var priorClaims = await db.CapitalAllowanceClaims
                .Where(claim => claim.AssetId == asset.Id && claim.Period.PeriodEnd < periodStart)
                .SumAsync(claim => claim.Claim);
            if (priorClaims > asset.Cost)
            {
                blockers.Add($"Fixed asset '{asset.Name}' has prior capital-allowance claims exceeding cost.");
                continue;
            }

            var taxWrittenDownValue = Math.Max(0m, asset.Cost - priorClaims);
            if (asset.DisposalDate is { } disposal && disposal >= periodStart && disposal <= periodEnd)
            {
                if (asset.DisposalProceeds is null)
                {
                    blockers.Add($"Fixed asset '{asset.Name}' was disposed in the period without retained disposal proceeds.");
                    continue;
                }
                if (priorClaims == 0)
                {
                    blockers.Add($"Fixed asset '{asset.Name}' was disposed without a retained prior capital-allowance claim history; manual balancing review is required.");
                    continue;
                }
                if (asset.DisposalProceeds > asset.Cost)
                    blockers.Add($"Fixed asset '{asset.Name}' has disposal proceeds above cost; chargeable-gain review is outside scope.");

                if (asset.DisposalProceeds < taxWrittenDownValue)
                {
                    var allowance = decimal.Round(taxWrittenDownValue - asset.DisposalProceeds.Value, 2, MidpointRounding.AwayFromZero);
                    balancingAllowances += allowance;
                    adjustments.Add(new TaxAdjustment(
                        $"Deduct: balancing allowance - {asset.Name}",
                        -allowance,
                        "Disposal proceeds below tax written-down value; support calculation under TCA 1997 Part 9."));
                }
                else if (asset.DisposalProceeds > taxWrittenDownValue)
                {
                    var charge = decimal.Round(
                        Math.Min(asset.DisposalProceeds.Value - taxWrittenDownValue, priorClaims),
                        2,
                        MidpointRounding.AwayFromZero);
                    balancingCharges += charge;
                    adjustments.Add(new TaxAdjustment(
                        $"Add: balancing charge - {asset.Name}",
                        charge,
                        "Disposal proceeds above tax written-down value; charge capped at allowances actually granted."));
                }

                continue;
            }

            if (asset.DisposalDate is not null && asset.DisposalDate <= periodEnd)
                continue;

            var remainingCost = Math.Max(0m, asset.Cost - priorClaims);
            var claimAmount = Math.Min(
                decimal.Round(asset.Cost * 0.125m * periodFraction, 2, MidpointRounding.AwayFromZero),
                remainingCost);
            if (claimAmount <= 0)
                continue;

            claims.Add(new CapitalAllowanceClaimResult(asset.Id, asset.Cost, claimAmount));
        }

        var wearAndTear = claims.Sum(claim => claim.Claim);
        if (wearAndTear != 0)
        {
            adjustments.Add(new TaxAdjustment(
                "Deduct: supported plant-and-machinery wear and tear",
                -wearAndTear,
                "Explicitly reviewed 12.5% plant-and-machinery assets only; short periods are pro-rated."));
        }

        return new CapitalAllowanceSupport(
            claims,
            adjustments,
            blockers,
            wearAndTear,
            balancingAllowances,
            balancingCharges);
    }

    private async Task<List<string>> FindAmbiguousIncomeCategoriesAsync(int companyId, int periodId)
    {
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        return ledger.Lines.Values
            .Where(line => line.Category.Type == AccountCategoryType.Income
                && !line.Category.IsNonTradingIncome
                && !line.Category.Code.StartsWith("4", StringComparison.Ordinal)
                && line.Credit != line.Debit)
            .Select(line => $"{line.Category.Code} {line.Category.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static void AddScopeBlockers(
        Company company,
        CorporationTaxScopeReview scope,
        decimal passiveIncome,
        ICollection<string> blockers)
    {
        if (scope.IsCloseCompany is null)
            blockers.Add("Close-company status has not been answered.");
        else if (scope.IsCloseCompany == true)
            blockers.Add("Close-company and service-company surcharges are outside the automated corporation-tax scope.");
        if (scope.IsCloseCompany == true && scope.IsServiceCompany is null)
            blockers.Add("Close service-company status has not been answered.");
        if (company.IsGroupMember || scope.HasGroupOrConsortiumRelief)
            blockers.Add("Group/consortium relief and group-company tax computations are outside scope.");
        if (scope.HasChargeableGains)
            blockers.Add("Chargeable gains and development-land computations are outside scope.");
        if (scope.HasForeignIncomeOrTaxCredits)
            blockers.Add("Foreign income, participation exemptions and double-tax credits are outside scope.");
        if (scope.HasExceptedTrade)
            blockers.Add("Excepted trades and special 25% trading-rate cases are outside scope.");
        if (scope.HasOtherReliefsOrSpecialRegimes)
            blockers.Add("Other relief claims, credits and special corporation-tax regimes are outside scope.");

        var computedPassiveIncomePresent = passiveIncome != 0;
        if (scope.DeclaredPassiveIncomePresent != computedPassiveIncomePresent)
            blockers.Add("Declared passive-income presence does not agree with the classified ledger.");
        if (computedPassiveIncomePresent && !scope.PassiveIncomeClassificationReviewed)
            blockers.Add("Passive/non-trading income classification has not been explicitly reviewed.");
        if (passiveIncome < 0)
            blockers.Add("Passive/non-trading losses and their relief treatment are outside the automated corporation-tax scope.");
    }

    private async Task AddLossContinuityBlockersAsync(
        AccountingPeriod period,
        CorporationTaxScopeReview? scope,
        decimal broughtForwardLoss,
        ICollection<string> blockers)
    {
        if (scope is null)
            return;

        var priorPeriod = await db.AccountingPeriods
            .AsNoTracking()
            .Include(candidate => candidate.CorporationTaxLossRecord)
            .Where(candidate => candidate.CompanyId == period.CompanyId
                && candidate.PeriodEnd < period.PeriodStart)
            .OrderByDescending(candidate => candidate.PeriodEnd)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync();
        if (priorPeriod is null)
        {
            if (broughtForwardLoss > 0 && (scope.BroughtForwardLossEvidence?.Trim().Length ?? 0) < 20)
                blockers.Add("Opening trading-loss take-on has no retained evidence reference.");
            return;
        }

        var prior = priorPeriod.CorporationTaxLossRecord;
        if (prior is null)
        {
            blockers.Add(
                $"Immediately preceding period {priorPeriod.PeriodEnd:yyyy-MM-dd} has no retained corporation-tax loss movement; continuity and first-available-profit use cannot be established.");
        }
        else if (prior.ClosingTradingLoss != broughtForwardLoss)
        {
            blockers.Add(
                $"Brought-forward trading loss ({broughtForwardLoss:N2}) does not match the latest retained closing loss ({prior.ClosingTradingLoss:N2}).");
        }
    }

    private static string CalculationFingerprint(
        AccountingPeriod period,
        CorporationTaxScopeReview? scope,
        decimal accountingProfit,
        decimal passiveIncome,
        IEnumerable<TaxAdjustment> adjustments,
        decimal broughtForwardLoss,
        decimal currentLoss,
        decimal lossUsed,
        decimal closingLoss,
        decimal preliminaryTax)
    {
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static string Bool(bool? value) => value is null ? "null" : value.Value ? "true" : "false";
        var adjustmentRows = adjustments
            .OrderBy(adjustment => adjustment.Description, StringComparer.Ordinal)
            .ThenBy(adjustment => adjustment.Amount)
            .Select(adjustment => $"{adjustment.Description}|{Money(adjustment.Amount)}|{adjustment.Basis}");
        var canonical = string.Join("\n",
        [
            $"period:{period.Id}|{period.PeriodStart:yyyy-MM-dd}|{period.PeriodEnd:yyyy-MM-dd}",
            $"profit:{Money(accountingProfit)}|passive:{Money(passiveIncome)}|preliminary:{Money(preliminaryTax)}",
            $"loss:{Money(broughtForwardLoss)}|{Money(currentLoss)}|{Money(lossUsed)}|{Money(closingLoss)}",
            $"scope:{Bool(scope?.IsCloseCompany)}|{Bool(scope?.IsServiceCompany)}|{scope?.HasGroupOrConsortiumRelief}|{scope?.HasChargeableGains}|{scope?.HasForeignIncomeOrTaxCredits}|{scope?.HasExceptedTrade}|{scope?.HasOtherReliefsOrSpecialRegimes}|{scope?.DeclaredPassiveIncomePresent}|{scope?.PassiveIncomeClassificationReviewed}|{scope?.LossTreatment}",
            $"scope-evidence:{scope?.BroughtForwardLossEvidence}|{scope?.EvidenceNote}",
            .. adjustmentRows
        ]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static decimal PeriodYearFraction(DateOnly start, DateOnly end)
    {
        var days = end.DayNumber - start.DayNumber + 1;
        if (days <= 0)
            return 0m;
        return Math.Min(1m, days / 365m);
    }
}

public static class CorporationTaxRuleSources
{
    public static readonly TaxComputationService.TaxSourceReference Rates = new(
        "revenue-ct-basis-of-charge",
        "Revenue: Corporation Tax basis of charge",
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax/basis-of-charge.aspx");

    public static readonly TaxComputationService.TaxSourceReference CapitalAllowances = new(
        "revenue-ct-capital-allowances",
        "Revenue: Capital allowances and deductions",
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax/capital-allowances-and-deductions.aspx");

    public static readonly TaxComputationService.TaxSourceReference TradingLosses = new(
        "revenue-ct-trading-losses",
        "Revenue: Corporation Tax trading losses",
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax/trading-losses.aspx");

    public static readonly TaxComputationService.TaxSourceReference CloseCompanies = new(
        "revenue-close-companies",
        "Revenue: Close companies",
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/close-companies/index.aspx");

    public static readonly TaxComputationService.TaxSourceReference CloseCompanySurcharge = new(
        "revenue-close-company-surcharge",
        "Revenue: Surcharge on undistributed income",
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/close-companies/surcharge.aspx");

    public static readonly IReadOnlyList<TaxComputationService.TaxSourceReference> All =
    [
        Rates,
        CapitalAllowances,
        TradingLosses,
        CloseCompanies,
        CloseCompanySurcharge
    ];
}
