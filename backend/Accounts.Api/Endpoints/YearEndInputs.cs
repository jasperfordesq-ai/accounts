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
public record DirectorLoanInput(
    int DirectorId,
    decimal OpeningBalance,
    decimal Advances,
    decimal Repayments,
    decimal ClosingBalance,
    decimal InterestRate,
    decimal InterestCharged,
    bool IsDocumented,
    string? LoanTerms,
    decimal MaxBalanceDuringYear);

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

        var directorServedDuringPeriod = await db.CompanyOfficers.AnyAsync(o =>
            o.Id == input.DirectorId
            && o.CompanyId == companyId
            && o.Role == OfficerRole.Director
            && (o.AppointedDate == null || o.AppointedDate <= period.PeriodEnd)
            && (o.ResignedDate == null || o.ResignedDate >= period.PeriodStart));

        return directorServedDuringPeriod
            ? null
            : Results.BadRequest(new { error = "Director loan director must have served as a director of this company during the accounting period." });
    }

    public static DirectorLoan ToEntity(int periodId, DirectorLoanInput input) => new()
    {
        PeriodId = periodId,
        DirectorId = input.DirectorId,
        OpeningBalance = input.OpeningBalance,
        Advances = input.Advances,
        Repayments = input.Repayments,
        ClosingBalance = input.ClosingBalance,
        InterestRate = input.InterestRate,
        InterestCharged = input.InterestCharged,
        IsDocumented = input.IsDocumented,
        LoanTerms = string.IsNullOrWhiteSpace(input.LoanTerms) ? null : input.LoanTerms.Trim(),
        MaxBalanceDuringYear = input.MaxBalanceDuringYear
    };
}

public static class LoanInputs
{
    public static IResult? Validate(Loan input)
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
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, LoanBalanceSnapshot input)
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
    public static IResult? Validate(ShareCapital input)
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
    public static IResult? ForDebtor(Debtor input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Debtor name is required." });
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Debtor amount cannot be negative." });
        return null;
    }

    public static IResult? ForCreditor(Creditor input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Creditor name is required." });
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Creditor amount cannot be negative." });
        return null;
    }

    public static IResult? ForInventory(Inventory input)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return Results.BadRequest(new { error = "Inventory description is required." });
        if (input.Value < 0)
            return Results.BadRequest(new { error = "Inventory value cannot be negative." });
        return null;
    }

    public static IResult? ForFixedAsset(FixedAsset input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Fixed asset name is required." });
        if (string.IsNullOrWhiteSpace(input.Category))
            return Results.BadRequest(new { error = "Fixed asset category is required." });
        if (input.Cost < 0)
            return Results.BadRequest(new { error = "Fixed asset cost cannot be negative." });
        if (input.UsefulLifeYears < 1)
            return Results.BadRequest(new { error = "Fixed asset useful life must be at least one year." });
        if (input.DisposalProceeds is < 0)
            return Results.BadRequest(new { error = "Fixed asset disposal proceeds cannot be negative." });
        if (input.DisposalDate is { } disposal && disposal < input.AcquisitionDate)
            return Results.BadRequest(new { error = "Fixed asset disposal date cannot be before the acquisition date." });
        return null;
    }

    public static IResult? ForDividend(Dividend input)
    {
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Dividend amount cannot be negative." });
        if (input.DateDeclared is { } declared && input.DatePaid is { } paid && paid < declared)
            return Results.BadRequest(new { error = "Dividend payment date cannot be before the declaration date." });
        return null;
    }
}
