using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed class CorporationTaxFilingSupportService(
    AccountsDbContext db,
    TaxComputationService taxService,
    AuditService audit)
{
    private const decimal MaxMoney = 999_999_999_999_999.99m;

    public sealed record ReviewInput(
        DateOnly? PriorPeriodStart,
        DateOnly? PriorPeriodEnd,
        decimal? PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239,
        decimal? PriorPeriodSection239IncomeTax,
        decimal CurrentPeriodSection239IncomeTax,
        string? PriorLiabilityEvidenceReference,
        bool HasInterestLimitationRule,
        bool UsesNotionalGroupPaymentAllocation,
        bool HasDirtOrOtherWithholdingCredits,
        bool HasOtherPreliminaryTaxAdjustments,
        bool HasMandatoryElectronicFilingExemption,
        string EvidenceNote);

    public sealed record PaymentInput(
        DateOnly PaymentDate,
        decimal Amount,
        CorporationTaxPaymentKind Kind,
        string EvidenceReference,
        string? ExternalPaymentReference);

    public sealed record ReviewEvidence(
        int Id,
        int PeriodId,
        DateOnly? PriorPeriodStart,
        DateOnly? PriorPeriodEnd,
        decimal? PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239,
        decimal? PriorPeriodSection239IncomeTax,
        decimal CurrentPeriodSection239IncomeTax,
        string? PriorLiabilityEvidenceReference,
        bool HasInterestLimitationRule,
        bool UsesNotionalGroupPaymentAllocation,
        bool HasDirtOrOtherWithholdingCredits,
        bool HasOtherPreliminaryTaxAdjustments,
        bool HasMandatoryElectronicFilingExemption,
        string EvidenceNote,
        string PreparedBy,
        DateTime PreparedAtUtc);

    public sealed record PaymentEvidence(
        int Id,
        int PeriodId,
        DateOnly PaymentDate,
        decimal Amount,
        CorporationTaxPaymentKind Kind,
        string EvidenceReference,
        string? ExternalPaymentReference,
        string RecordedBy,
        DateTime RecordedAtUtc);

    public sealed record Response(
        ReviewEvidence? Review,
        IReadOnlyList<PaymentEvidence> Payments,
        CorporationTaxFilingSupportCalculator.Result FilingSupport,
        CorporationTaxSupportWorksheetBuilder.Worksheet Worksheet);

    public async Task<Response> GetAsync(
        int companyId,
        int periodId,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var asOf = asOfDate ?? today;
        if (asOf > today)
            throw new BusinessRuleException("The filing-support assessment date cannot be in the future.");

        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var review = await db.CorporationTaxFilingSupportReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PeriodId == periodId, cancellationToken);
        var payments = await db.CorporationTaxPaymentRecords
            .AsNoTracking()
            .Where(item => item.PeriodId == periodId && !item.IsVoided && item.PaymentDate <= asOf)
            .OrderBy(item => item.PaymentDate)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var filedDate = await db.FilingDeadlines
            .AsNoTracking()
            .Where(item => item.PeriodId == periodId && item.DeadlineType == DeadlineType.Revenue)
            .Select(item => item.FiledDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (filedDate is { } recordedFiledDate && recordedFiledDate > asOf)
            filedDate = null;

        var tax = await taxService.GetCt1SupportDataAsync(companyId, periodId);
        var calculatorPayments = payments.Select(payment => new CorporationTaxFilingSupportCalculator.Payment(
            payment.PaymentDate,
            payment.Amount,
            CalculatorPaymentKind(payment.Kind),
            payment.EvidenceReference,
            payment.ExternalPaymentReference)).ToList();
        var calculation = CorporationTaxFilingSupportCalculator.Calculate(new(
            period.PeriodStart,
            period.PeriodEnd,
            period.IsFirstYear,
            tax.TaxDue,
            review?.CurrentPeriodSection239IncomeTax ?? 0m,
            review?.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239,
            review?.PriorPeriodSection239IncomeTax,
            review?.PriorPeriodStart,
            review?.PriorPeriodEnd,
            PriorLiabilityEvidenceComplete: review?.PriorLiabilityEvidenceReference?.Trim().Length >= 20,
            ReviewEvidenceRetained: review is not null,
            calculatorPayments,
            filedDate,
            asOf,
            tax.FinalTaxChargeSupported,
            tax.BlockingReasons,
            review?.HasInterestLimitationRule ?? false,
            review?.UsesNotionalGroupPaymentAllocation ?? false,
            review?.HasDirtOrOtherWithholdingCredits ?? false,
            review?.HasOtherPreliminaryTaxAdjustments ?? false,
            review?.HasMandatoryElectronicFilingExemption ?? false,
            tax.CapitalAllowances,
            tax.TradingLossUsed));

        var payroll = await db.PayrollSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PeriodId == periodId, cancellationToken);
        var worksheet = CorporationTaxSupportWorksheetBuilder.Build(new(
            period.Company.LegalName,
            period.Company.TaxReference ?? string.Empty,
            period.PeriodStart,
            period.PeriodEnd,
            tax,
            calculation,
            payroll?.GrossWages ?? 0m,
            payroll is null ? 0m : payroll.EmployerPrsi + payroll.PensionContributions,
            asOf));

        return new Response(
            review is null ? null : ToEvidence(review),
            payments.Select(ToEvidence).ToList(),
            calculation,
            worksheet);
    }

    public async Task<Response> SaveReviewAsync(
        int companyId,
        int periodId,
        ReviewInput input,
        string actorUserId,
        string actorDisplayName,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireActor(actorDisplayName);
        var period = await db.AccountingPeriods
            .Include(item => item.CorporationTaxFilingSupportReview)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        ValidateReview(input, period);

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var review = period.CorporationTaxFilingSupportReview;
        var oldValue = review is null ? null : ToEvidence(review);
        if (review is null)
        {
            review = new CorporationTaxFilingSupportReview { PeriodId = periodId };
            db.CorporationTaxFilingSupportReviews.Add(review);
        }

        review.PriorPeriodStart = input.PriorPeriodStart;
        review.PriorPeriodEnd = input.PriorPeriodEnd;
        review.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 = input.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239;
        review.PriorPeriodSection239IncomeTax = input.PriorPeriodSection239IncomeTax;
        review.CurrentPeriodSection239IncomeTax = input.CurrentPeriodSection239IncomeTax;
        review.PriorLiabilityEvidenceReference = NormalizeOptional(input.PriorLiabilityEvidenceReference);
        review.HasInterestLimitationRule = input.HasInterestLimitationRule;
        review.UsesNotionalGroupPaymentAllocation = input.UsesNotionalGroupPaymentAllocation;
        review.HasDirtOrOtherWithholdingCredits = input.HasDirtOrOtherWithholdingCredits;
        review.HasOtherPreliminaryTaxAdjustments = input.HasOtherPreliminaryTaxAdjustments;
        review.HasMandatoryElectronicFilingExemption = input.HasMandatoryElectronicFilingExemption;
        review.EvidenceNote = input.EvidenceNote.Trim();
        review.PreparedBy = actor;
        review.PreparedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var response = await GetAsync(companyId, periodId, cancellationToken: cancellationToken);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(CorporationTaxFilingSupportReview),
            review.Id,
            AuditEventCodes.CorporationTaxFilingSupportReviewUpserted,
            oldValue,
            new { Review = ToEvidence(review), response.FilingSupport.CalculationSha256, response.Worksheet.WorksheetSha256 },
            actorUserId,
            requestId: requestId,
            actorDisplayName: actor,
            cancellationToken: cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<Response> RecordPaymentAsync(
        int companyId,
        int periodId,
        PaymentInput input,
        string actorUserId,
        string actorDisplayName,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireActor(actorDisplayName);
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        ValidatePayment(input, period);
        var evidence = input.EvidenceReference.Trim();
        var duplicate = await db.CorporationTaxPaymentRecords.AnyAsync(item =>
            item.PeriodId == periodId
            && !item.IsVoided
            && item.PaymentDate == input.PaymentDate
            && item.Amount == input.Amount
            && item.Kind == input.Kind
            && item.EvidenceReference == evidence,
            cancellationToken);
        if (duplicate)
            throw new BusinessRuleException("This Corporation Tax payment evidence is already recorded for the period.");

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var payment = new CorporationTaxPaymentRecord
        {
            PeriodId = periodId,
            PaymentDate = input.PaymentDate,
            Amount = input.Amount,
            Kind = input.Kind,
            EvidenceReference = evidence,
            ExternalPaymentReference = NormalizeOptional(input.ExternalPaymentReference),
            RecordedBy = actor,
            RecordedAtUtc = DateTime.UtcNow
        };
        db.CorporationTaxPaymentRecords.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _ = exception;
            throw new BusinessRuleException("This Corporation Tax payment evidence could not be recorded; verify that it is not a duplicate.");
        }

        var response = await GetAsync(companyId, periodId, cancellationToken: cancellationToken);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(CorporationTaxPaymentRecord),
            payment.Id,
            AuditEventCodes.CorporationTaxPaymentEvidenceRecorded,
            null,
            new { Payment = ToEvidence(payment), response.FilingSupport.CalculationSha256, response.Worksheet.WorksheetSha256 },
            actorUserId,
            requestId: requestId,
            actorDisplayName: actor,
            cancellationToken: cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<Response> VoidPaymentAsync(
        int companyId,
        int periodId,
        int paymentId,
        string actorUserId,
        string actorDisplayName,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireActor(actorDisplayName);
        var payment = await db.CorporationTaxPaymentRecords
            .Include(item => item.Period)
            .FirstOrDefaultAsync(item => item.Id == paymentId
                && item.PeriodId == periodId
                && item.Period.CompanyId == companyId
                && !item.IsVoided,
                cancellationToken)
            ?? throw new ResourceNotFoundException($"Corporation Tax payment evidence {paymentId} not found");
        var oldValue = ToEvidence(payment);

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        payment.IsVoided = true;
        payment.VoidedBy = actor;
        payment.VoidedAtUtc = DateTime.UtcNow;
        payment.VoidReason = "Incorrect payment-evidence row removed from the active Corporation Tax tracker.";
        await db.SaveChangesAsync(cancellationToken);

        var response = await GetAsync(companyId, periodId, cancellationToken: cancellationToken);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(CorporationTaxPaymentRecord),
            payment.Id,
            AuditEventCodes.CorporationTaxPaymentEvidenceVoided,
            oldValue,
            new { payment.IsVoided, payment.VoidedBy, payment.VoidedAtUtc, payment.VoidReason, response.FilingSupport.CalculationSha256 },
            actorUserId,
            requestId: requestId,
            actorDisplayName: actor,
            cancellationToken: cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static void ValidateReview(ReviewInput input, AccountingPeriod period)
    {
        if (input.CurrentPeriodSection239IncomeTax < 0 || input.CurrentPeriodSection239IncomeTax > MaxMoney)
            throw new BusinessRuleException("Current-period section 239 Income Tax must be a valid non-negative monetary amount.");
        var evidenceNote = input.EvidenceNote?.Trim() ?? string.Empty;
        if (evidenceNote.Length is < 20 or > 2000)
            throw new BusinessRuleException("Preliminary-tax review evidence must contain between 20 and 2,000 characters.");

        var hasAnyPrior = input.PriorPeriodStart is not null
            || input.PriorPeriodEnd is not null
            || input.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 is not null
            || input.PriorPeriodSection239IncomeTax is not null
            || !string.IsNullOrWhiteSpace(input.PriorLiabilityEvidenceReference);
        if (period.IsFirstYear)
        {
            if (hasAnyPrior)
                throw new BusinessRuleException("A first accounting period cannot carry preceding-period preliminary-tax inputs.");
            return;
        }

        if (input.PriorPeriodStart is null
            || input.PriorPeriodEnd is null
            || input.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 is null
            || input.PriorPeriodSection239IncomeTax is null)
        {
            throw new BusinessRuleException("A non-first accounting period requires exact preceding-period dates, Corporation Tax and section 239 amounts.");
        }
        if (input.PriorPeriodEnd.Value < input.PriorPeriodStart.Value)
            throw new BusinessRuleException("The preceding accounting-period end cannot precede its start.");
        if (input.PriorPeriodEnd.Value > input.PriorPeriodStart.Value.AddMonths(12).AddDays(-1))
            throw new BusinessRuleException("The preceding Corporation Tax accounting period cannot exceed 12 months.");
        if (input.PriorPeriodEnd.Value >= period.PeriodStart)
            throw new BusinessRuleException("The preceding accounting period must end before the current accounting period starts.");
        if (input.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239.Value < 0
            || input.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239.Value > MaxMoney
            || input.PriorPeriodSection239IncomeTax.Value < 0
            || input.PriorPeriodSection239IncomeTax.Value > MaxMoney)
        {
            throw new BusinessRuleException("Preceding-period Corporation Tax and section 239 amounts must be valid non-negative monetary amounts.");
        }
        var priorEvidence = input.PriorLiabilityEvidenceReference?.Trim() ?? string.Empty;
        if (priorEvidence.Length is < 20 or > 1000)
            throw new BusinessRuleException("Preceding-period liabilities require a retained evidence reference between 20 and 1,000 characters.");
    }

    private static void ValidatePayment(PaymentInput input, AccountingPeriod period)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (input.PaymentDate < period.PeriodStart)
            throw new BusinessRuleException("A Corporation Tax payment cannot precede the accounting period to which it is allocated.");
        if (input.PaymentDate > today)
            throw new BusinessRuleException("A future Corporation Tax payment cannot be recorded as retained evidence.");
        if (input.Amount <= 0 || input.Amount > MaxMoney)
            throw new BusinessRuleException("Corporation Tax payment amount must be a valid positive monetary amount.");
        var evidence = input.EvidenceReference?.Trim() ?? string.Empty;
        if (evidence.Length is < 20 or > 1000)
            throw new BusinessRuleException("Corporation Tax payment evidence must contain between 20 and 1,000 characters.");
        if ((input.ExternalPaymentReference?.Trim().Length ?? 0) > 200)
            throw new BusinessRuleException("The external Corporation Tax payment reference cannot exceed 200 characters.");
    }

    private static CorporationTaxFilingSupportCalculator.PaymentKind CalculatorPaymentKind(CorporationTaxPaymentKind kind) =>
        kind switch
        {
            CorporationTaxPaymentKind.PreliminaryFirst => CorporationTaxFilingSupportCalculator.PaymentKind.PreliminaryFirst,
            CorporationTaxPaymentKind.PreliminarySecondOrSingle => CorporationTaxFilingSupportCalculator.PaymentKind.PreliminarySecondOrSingle,
            CorporationTaxPaymentKind.Balance => CorporationTaxFilingSupportCalculator.PaymentKind.Balance,
            CorporationTaxPaymentKind.InterestOrSurcharge => CorporationTaxFilingSupportCalculator.PaymentKind.InterestOrSurcharge,
            _ => CorporationTaxFilingSupportCalculator.PaymentKind.Other
        };

    private static ReviewEvidence ToEvidence(CorporationTaxFilingSupportReview review) => new(
        review.Id,
        review.PeriodId,
        review.PriorPeriodStart,
        review.PriorPeriodEnd,
        review.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239,
        review.PriorPeriodSection239IncomeTax,
        review.CurrentPeriodSection239IncomeTax,
        review.PriorLiabilityEvidenceReference,
        review.HasInterestLimitationRule,
        review.UsesNotionalGroupPaymentAllocation,
        review.HasDirtOrOtherWithholdingCredits,
        review.HasOtherPreliminaryTaxAdjustments,
        review.HasMandatoryElectronicFilingExemption,
        review.EvidenceNote,
        review.PreparedBy,
        review.PreparedAtUtc);

    private static PaymentEvidence ToEvidence(CorporationTaxPaymentRecord payment) => new(
        payment.Id,
        payment.PeriodId,
        payment.PaymentDate,
        payment.Amount,
        payment.Kind,
        payment.EvidenceReference,
        payment.ExternalPaymentReference,
        payment.RecordedBy,
        payment.RecordedAtUtc);

    private static string RequireActor(string actor)
    {
        var value = actor?.Trim() ?? string.Empty;
        if (value.Length is < 2 or > 200)
            throw new BusinessRuleException("A named workflow actor is required for Corporation Tax filing-support evidence.");
        return value;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
