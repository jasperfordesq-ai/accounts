using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public record GoingConcernInput(bool Confirmed, string? Note);
public record YearEndReviewInput(bool Confirmed, string? ConfirmedBy, string? Note);
public record OpeningBalanceInput(decimal Debit, decimal Credit, string? SourceNote, string? EnteredBy, bool Reviewed);
public record DirectorLoanMovementInput(
    DateOnly MovementDate,
    DirectorLoanMovementType MovementType,
    decimal Amount,
    string? EvidenceReference = null);

public record DirectorLoanInput(
    int? DirectorId,
    decimal OpeningBalance,
    decimal Advances,
    decimal Repayments,
    decimal ClosingBalance,
    decimal InterestRate,
    decimal InterestCharged,
    bool IsDocumented,
    string? LoanTerms,
    decimal MaxBalanceDuringYear,
    DirectorLoanCounterpartyType CounterpartyType = DirectorLoanCounterpartyType.Director,
    string? CounterpartyName = null,
    DirectorLoanArrangementType ArrangementType = DirectorLoanArrangementType.Loan,
    DateOnly? ArrangementDate = null,
    DirectorLoanTermsStatus TermsStatus = DirectorLoanTermsStatus.Unassessed,
    decimal AllowanceMade = 0,
    string? Section236PresumptionEvidenceReference = null,
    DirectorLoanComplianceBasis ComplianceBasis = DirectorLoanComplianceBasis.Unassessed,
    DirectorLoanRelevantAssetsBasis RelevantAssetsBasis = DirectorLoanRelevantAssetsBasis.Unassessed,
    decimal? RelevantAssetsAmount = null,
    DateOnly? RelevantAssetsAsOfDate = null,
    string? RelevantAssetsReference = null,
    bool NoPriorFinancialStatementsConfirmed = false,
    DirectorLoanRelevantAssetsFallReview RelevantAssetsFallReview = DirectorLoanRelevantAssetsFallReview.Unassessed,
    DateOnly? RelevantAssetsReductionAwarenessDate = null,
    DateOnly? TermsAmendedDate = null,
    string? TermsAmendmentEvidenceReference = null,
    string? ExceptionEvidenceReference = null,
    DateOnly? SapDeclarationDate = null,
    DateOnly? SapResolutionDate = null,
    DateOnly? SapActivityStartDate = null,
    DateOnly? SapCroFilingDate = null,
    string? SapDeclarationReference = null,
    string? SapResolutionReference = null,
    string? SapCroFilingReference = null,
    bool SapDeclarationCoversSection203Matters = false,
    DateOnly? ExpenseIncurredDate = null,
    DateOnly? ExpenseDischargedDate = null,
    bool OrdinaryCourseConfirmed = false,
    bool NoMoreFavourableTermsConfirmed = false,
    DirectorLoanReviewDecision ReviewDecision = DirectorLoanReviewDecision.Unreviewed,
    string? ReviewNote = null,
    IReadOnlyList<DirectorLoanMovementInput>? BalanceMovements = null);

public static class DirectorLoanInputs
{
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, int periodId, DirectorLoanInput input)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.PeriodStart, p.PeriodEnd })
            .FirstOrDefaultAsync();
        if (period is null)
            return Results.NotFound(new { error = "Accounting period not found for this company." });

        if (input.CounterpartyType == DirectorLoanCounterpartyType.GroupCompany)
        {
            if (input.DirectorId is not null || string.IsNullOrWhiteSpace(input.CounterpartyName))
                return Results.BadRequest(new { error = "An intra-group arrangement requires a named group-company counterparty and must not be attributed to an individual director." });
        }
        else
        {
            if (input.DirectorId is not { } directorId)
                return Results.BadRequest(new { error = "A director or connected-person arrangement must identify the related director." });

            var directorServedDuringPeriod = await db.CompanyOfficers.AnyAsync(o =>
                o.Id == directorId
                && o.CompanyId == companyId
                && o.Role == OfficerRole.Director
                && o.AppointedDate != null
                && o.AppointedDate <= period.PeriodEnd
                && (o.ResignedDate == null || o.ResignedDate >= period.PeriodStart));
            if (!directorServedDuringPeriod)
                return Results.BadRequest(new { error = "The related director must have a recorded appointment date and have served this company during the accounting period." });

            if (input.CounterpartyType == DirectorLoanCounterpartyType.ConnectedPerson
                && string.IsNullOrWhiteSpace(input.CounterpartyName))
            {
                return Results.BadRequest(new { error = "A connected-person arrangement requires the connected person's name." });
            }
        }

        var monetaryValues = new[]
        {
            input.OpeningBalance,
            input.Advances,
            input.Repayments,
            input.ClosingBalance,
            input.InterestRate,
            input.InterestCharged,
            input.AllowanceMade,
            input.MaxBalanceDuringYear
        };
        if (monetaryValues.Any(value => value < 0))
            return Results.BadRequest(new { error = "Director-loan balances, movements, interest and allowances cannot be negative." });

        if (!Near(input.ClosingBalance, input.OpeningBalance + input.Advances - input.Repayments))
            return Results.BadRequest(new { error = "Closing balance must equal opening balance plus advances less repayments." });

        if (input.MaxBalanceDuringYear + 0.01m < Math.Max(input.OpeningBalance, input.ClosingBalance))
            return Results.BadRequest(new { error = "Maximum balance cannot be lower than the opening or closing balance." });

        if (input.ArrangementDate is { } arrangementDate && arrangementDate > period.PeriodEnd)
            return Results.BadRequest(new { error = "Arrangement date cannot be after the accounting period end." });

        var movements = input.BalanceMovements ?? [];
        if (movements.Any(movement =>
                movement.Amount <= 0
                || movement.MovementDate < period.PeriodStart
                || movement.MovementDate > period.PeriodEnd
                || input.ArrangementDate is { } start && movement.MovementDate < start))
        {
            return Results.BadRequest(new { error = "Every dated balance movement must have a positive amount, fall within the period, and not predate the arrangement." });
        }

        if (movements.Count > 0)
        {
            var advances = movements
                .Where(movement => movement.MovementType == DirectorLoanMovementType.Advance)
                .Sum(movement => movement.Amount);
            var repayments = movements
                .Where(movement => movement.MovementType == DirectorLoanMovementType.Repayment)
                .Sum(movement => movement.Amount);
            if (!Near(advances, input.Advances) || !Near(repayments, input.Repayments))
                return Results.BadRequest(new { error = "Dated movement totals must reconcile to the recorded advances and repayments." });

            var runningBalance = input.OpeningBalance;
            var derivedMaximum = runningBalance;
            foreach (var movement in movements.OrderBy(movement => movement.MovementDate))
            {
                runningBalance += movement.MovementType == DirectorLoanMovementType.Advance
                    ? movement.Amount
                    : -movement.Amount;
                if (runningBalance < -0.01m)
                    return Results.BadRequest(new { error = "Dated repayments cannot reduce the director-loan balance below zero." });
                derivedMaximum = Math.Max(derivedMaximum, runningBalance);
            }

            if (!Near(runningBalance, input.ClosingBalance) || !Near(derivedMaximum, input.MaxBalanceDuringYear))
                return Results.BadRequest(new { error = "Dated movements must reconcile to the closing and maximum balances." });
        }

        if (input.ReviewDecision != DirectorLoanReviewDecision.Unreviewed
            && (input.ReviewNote?.Trim().Length ?? 0) < 20)
        {
            return Results.BadRequest(new { error = "A compliance decision requires a review note of at least 20 characters." });
        }

        if (input.SapDeclarationDate is { } declaration
            && input.SapResolutionDate is { } resolution
            && (declaration > resolution || declaration < resolution.AddDays(-30)))
        {
            return Results.BadRequest(new { error = "The SAP directors' declaration must be made no earlier than 30 days before, and no later than, the special resolution." });
        }

        if (input.SapResolutionDate is { } sapResolution
            && input.SapActivityStartDate is { } activityStart
            && (sapResolution > activityStart || sapResolution.AddMonths(12) < activityStart))
        {
            return Results.BadRequest(new { error = "The restricted activity must begin on or after, and within 12 months of, the SAP special resolution." });
        }

        if (input.SapActivityStartDate is { } sapStart
            && input.SapCroFilingDate is { } filingDate
            && (filingDate < sapStart || filingDate > sapStart.AddDays(21)))
        {
            return Results.BadRequest(new { error = "The SAP declaration copy must be filed no later than 21 days after the restricted activity begins." });
        }

        return null;
    }

    public static DirectorLoan ToEntity(int periodId, DirectorLoanInput input)
    {
        var entity = new DirectorLoan { PeriodId = periodId };
        Apply(entity, input);
        return entity;
    }

    public static void Apply(DirectorLoan entity, DirectorLoanInput input)
    {
        entity.DirectorId = input.DirectorId;
        entity.CounterpartyType = input.CounterpartyType;
        entity.CounterpartyName = Clean(input.CounterpartyName);
        entity.ArrangementType = input.ArrangementType;
        entity.ArrangementDate = input.ArrangementDate;
        entity.OpeningBalance = input.OpeningBalance;
        entity.Advances = input.Advances;
        entity.Repayments = input.Repayments;
        entity.ClosingBalance = input.ClosingBalance;
        entity.TermsStatus = input.TermsStatus;
        entity.InterestRate = input.InterestRate;
        entity.InterestCharged = input.InterestCharged;
        entity.AllowanceMade = input.AllowanceMade;
        entity.Section236PresumptionEvidenceReference = Clean(input.Section236PresumptionEvidenceReference);
        entity.IsDocumented = input.TermsStatus is DirectorLoanTermsStatus.WrittenComplete
            or DirectorLoanTermsStatus.WrittenAmbiguousRepayment
            or DirectorLoanTermsStatus.WrittenAmbiguousInterest
            or DirectorLoanTermsStatus.WrittenAmbiguousRepaymentAndInterest;
        entity.LoanTerms = Clean(input.LoanTerms);
        entity.MaxBalanceDuringYear = input.MaxBalanceDuringYear;
        entity.ComplianceBasis = input.ComplianceBasis;
        entity.RelevantAssetsBasis = input.RelevantAssetsBasis;
        entity.RelevantAssetsAmount = input.RelevantAssetsAmount;
        entity.RelevantAssetsAsOfDate = input.RelevantAssetsAsOfDate;
        entity.RelevantAssetsReference = Clean(input.RelevantAssetsReference);
        entity.NoPriorFinancialStatementsConfirmed = input.NoPriorFinancialStatementsConfirmed;
        entity.RelevantAssetsFallReview = input.RelevantAssetsFallReview;
        entity.RelevantAssetsReductionAwarenessDate = input.RelevantAssetsReductionAwarenessDate;
        entity.TermsAmendedDate = input.TermsAmendedDate;
        entity.TermsAmendmentEvidenceReference = Clean(input.TermsAmendmentEvidenceReference);
        entity.ExceptionEvidenceReference = Clean(input.ExceptionEvidenceReference);
        entity.SapDeclarationDate = input.SapDeclarationDate;
        entity.SapResolutionDate = input.SapResolutionDate;
        entity.SapActivityStartDate = input.SapActivityStartDate;
        entity.SapCroFilingDate = input.SapCroFilingDate;
        entity.SapDeclarationReference = Clean(input.SapDeclarationReference);
        entity.SapResolutionReference = Clean(input.SapResolutionReference);
        entity.SapCroFilingReference = Clean(input.SapCroFilingReference);
        entity.SapDeclarationCoversSection203Matters = input.SapDeclarationCoversSection203Matters;
        entity.ExpenseIncurredDate = input.ExpenseIncurredDate;
        entity.ExpenseDischargedDate = input.ExpenseDischargedDate;
        entity.OrdinaryCourseConfirmed = input.OrdinaryCourseConfirmed;
        entity.NoMoreFavourableTermsConfirmed = input.NoMoreFavourableTermsConfirmed;
        entity.ReviewDecision = input.ReviewDecision;
        entity.ReviewNote = Clean(input.ReviewNote);

        entity.BalanceMovements.Clear();
        foreach (var movement in input.BalanceMovements ?? [])
        {
            entity.BalanceMovements.Add(new DirectorLoanMovement
            {
                MovementDate = movement.MovementDate,
                MovementType = movement.MovementType,
                Amount = movement.Amount,
                EvidenceReference = Clean(movement.EvidenceReference)
            });
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Near(decimal left, decimal right) => Math.Abs(left - right) <= 0.01m;
}

public static class LoanInputs
{
    public static IResult? Validate(LoanInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Lender))
            return Results.BadRequest(new { error = "Loan lender is required." });
        if (input.DrawdownDate is null)
            return Results.BadRequest(new { error = "Loan drawdown date is required." });
        if (input.BalanceAsOfDate is null)
            return Results.BadRequest(new { error = "Loan balance as-of date is required." });
        if (input.BalanceAsOfDate < input.DrawdownDate)
            return Results.BadRequest(new { error = "Loan balance as-of date cannot be before the drawdown date." });

        return null;
    }
}

public static class LoanBalanceSnapshotInputs
{
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, LoanBalanceSnapshotInput input)
    {
        var loanBelongsToCompany = await db.Loans.AnyAsync(l => l.Id == input.LoanId && l.CompanyId == companyId);
        if (!loanBelongsToCompany)
            return Results.BadRequest(new { error = "Loan is not available for this company." });

        if (input.OpeningBalance < 0
            || input.Drawdowns < 0
            || input.Repayments < 0
            || input.ClosingBalance < 0
            || input.DueWithinYear < 0
            || input.DueAfterYear < 0)
            return Results.BadRequest(new { error = "Loan snapshot amounts cannot be negative." });

        var expectedClosing = input.OpeningBalance + input.Drawdowns - input.Repayments;
        if (Math.Abs(expectedClosing - input.ClosingBalance) > 0.01m)
            return Results.BadRequest(new { error = "Loan snapshot closing balance must equal opening balance plus drawdowns less repayments." });

        if (Math.Abs(input.DueWithinYear + input.DueAfterYear - input.ClosingBalance) > 0.01m)
            return Results.BadRequest(new { error = "Loan due split must agree to the closing balance." });

        return null;
    }
}

public static class ShareCapitalInputs
{
    public static IResult? Validate(ShareCapitalInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ShareClass))
            return Results.BadRequest(new { error = "Share class is required." });
        if (input.IssueDate is null)
            return Results.BadRequest(new { error = "Share issue date is required." });
        if (input.CancelledDate is not null && input.CancelledDate < input.IssueDate)
            return Results.BadRequest(new { error = "Share cancellation date cannot be before the issue date." });

        return null;
    }
}

// Guards the figure-bearing year-end rows (debtors/creditors/inventory/fixed assets/dividends) so a
// customer fat-fingering a negative amount, a blank description or a zero useful life gets a clear 400
// instead of a silently corrupted balance sheet or a downstream 500 (G3 — customer inputs are safe).
public static class YearEndFigureInputs
{
    public static IResult? ForDebtor(DebtorInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Debtor name is required." });
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Debtor amount cannot be negative." });
        return null;
    }

    public static IResult? ForCreditor(CreditorInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Creditor name is required." });
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Creditor amount cannot be negative." });
        return null;
    }

    public static IResult? ForInventory(InventoryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return Results.BadRequest(new { error = "Inventory description is required." });
        if (input.Value < 0)
            return Results.BadRequest(new { error = "Inventory value cannot be negative." });
        return null;
    }

    public static IResult? ForFixedAsset(FixedAssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Fixed asset name is required." });
        if (string.IsNullOrWhiteSpace(input.Category))
            return Results.BadRequest(new { error = "Fixed asset category is required." });
        if (input.Cost < 0)
            return Results.BadRequest(new { error = "Fixed asset cost cannot be negative." });
        if (input.ResidualValue < 0 || input.ResidualValue > input.Cost)
            return Results.BadRequest(new { error = "Fixed asset residual value must be between zero and cost." });
        if (input.UsefulLifeYears < 1)
            return Results.BadRequest(new { error = "Fixed asset useful life must be at least one year." });
        if (input.DisposalProceeds is < 0)
            return Results.BadRequest(new { error = "Fixed asset disposal proceeds cannot be negative." });
        if (input.DisposalDate is { } disposal && disposal < input.AcquisitionDate)
            return Results.BadRequest(new { error = "Fixed asset disposal date cannot be before the acquisition date." });
        if (input.DisposalDate is not null && input.DisposalProceeds is null)
            return Results.BadRequest(new { error = "Disposed fixed assets require disposal proceeds." });
        if (input.CapitalAllowanceTreatment != CapitalAllowanceTreatment.Unreviewed
            && (input.CapitalAllowanceEvidence?.Trim().Length ?? 0) < 20)
        {
            return Results.BadRequest(new
            {
                error = "A reviewed capital-allowance treatment requires evidence of at least 20 characters."
            });
        }
        return null;
    }

    public static IResult? ForDividend(DividendInput input)
    {
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Dividend amount cannot be negative." });
        if (input.DateDeclared is { } declared && input.DatePaid is { } paid && paid < declared)
            return Results.BadRequest(new { error = "Dividend payment date cannot be before the declaration date." });
        return null;
    }
}
