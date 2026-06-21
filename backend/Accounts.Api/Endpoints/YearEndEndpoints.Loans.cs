using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static async Task<IResult> CreateLoanEndpointAsync(
        int companyId,
        Loan input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        if (LoanInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var effectiveDate = BankingEndpointInputs.Earliest(input.DrawdownDate, input.BalanceAsOfDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        input.CompanyId = companyId;
        db.Loans.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "Loan",
            input.Id,
            AuditEventCodes.LoanCreated,
            null,
            LoanSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/loans/{input.Id}", input);
    }

    public static async Task<IResult> UpdateLoanEndpointAsync(
        int companyId,
        int id,
        Loan input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.Loans.FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);
        if (item == null) return Results.NotFound();
        if (LoanInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var oldValue = LoanSnapshot(item);
        var existingEffectiveDate = BankingEndpointInputs.Earliest(item.DrawdownDate, item.BalanceAsOfDate);
        var inputEffectiveDate = BankingEndpointInputs.Earliest(input.DrawdownDate, input.BalanceAsOfDate);
        var effectiveDate = BankingEndpointInputs.Earliest(existingEffectiveDate, inputEffectiveDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        item.Lender = input.Lender;
        item.OriginalAmount = input.OriginalAmount;
        item.Balance = input.Balance;
        item.DrawdownDate = input.DrawdownDate;
        item.BalanceAsOfDate = input.BalanceAsOfDate;
        item.InterestRate = input.InterestRate;
        item.IsDirectorLoan = input.IsDirectorLoan;
        item.DueWithinYear = input.DueWithinYear;
        item.DueAfterYear = input.DueAfterYear;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "Loan",
            item.Id,
            AuditEventCodes.LoanUpdated,
            oldValue,
            LoanSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteLoanEndpointAsync(
        int companyId,
        int id,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.Loans.FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = LoanSnapshot(item);
        var effectiveDate = BankingEndpointInputs.Earliest(item.DrawdownDate, item.BalanceAsOfDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        db.Loans.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "Loan",
            id,
            AuditEventCodes.LoanDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateLoanBalanceSnapshotEndpointAsync(
        int companyId,
        int periodId,
        LoanBalanceSnapshot input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await LoanBalanceSnapshotInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
            return validationProblem;

        StampLoanBalanceSnapshot(input, periodId, context);
        db.LoanBalanceSnapshots.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "LoanBalanceSnapshot",
            input.Id,
            AuditEventCodes.LoanBalanceSnapshotCreated,
            null,
            LoanBalanceSnapshotSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/loan-balance-snapshots/{input.Id}", input);
    }

    public static async Task<IResult> UpdateLoanBalanceSnapshotEndpointAsync(
        int companyId,
        int periodId,
        int id,
        LoanBalanceSnapshot input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await LoanBalanceSnapshotInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
            return validationProblem;

        var item = await db.LoanBalanceSnapshots
            .Include(s => s.Loan)
            .FirstOrDefaultAsync(s => s.Id == id && s.PeriodId == periodId && s.Loan.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = LoanBalanceSnapshotSnapshot(item);
        item.LoanId = input.LoanId;
        item.OpeningBalance = input.OpeningBalance;
        item.Drawdowns = input.Drawdowns;
        item.Repayments = input.Repayments;
        item.ClosingBalance = input.ClosingBalance;
        item.DueWithinYear = input.DueWithinYear;
        item.DueAfterYear = input.DueAfterYear;
        item.Notes = input.Notes;
        StampLoanBalanceSnapshot(item, periodId, context);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "LoanBalanceSnapshot",
            item.Id,
            AuditEventCodes.LoanBalanceSnapshotUpdated,
            oldValue,
            LoanBalanceSnapshotSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteLoanBalanceSnapshotEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.LoanBalanceSnapshots
            .Include(s => s.Loan)
            .FirstOrDefaultAsync(s => s.Id == id && s.PeriodId == periodId && s.Loan.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = LoanBalanceSnapshotSnapshot(item);
        db.LoanBalanceSnapshots.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "LoanBalanceSnapshot",
            id,
            AuditEventCodes.LoanBalanceSnapshotDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateDirectorLoanEndpointAsync(
        int companyId,
        int periodId,
        DirectorLoanInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await DirectorLoanInputs.ValidateAsync(db, companyId, periodId, input) is { } validationProblem)
            return validationProblem;

        var loan = DirectorLoanInputs.ToEntity(periodId, input);
        db.DirectorLoans.Add(loan);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "DirectorLoan",
            loan.Id,
            AuditEventCodes.DirectorLoanCreated,
            null,
            DirectorLoanSnapshot(loan),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/director-loans/{loan.Id}", loan);
    }

    public static async Task<IResult> UpdateDirectorLoanEndpointAsync(
        int companyId,
        int periodId,
        int id,
        DirectorLoanInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await DirectorLoanInputs.ValidateAsync(db, companyId, periodId, input) is { } validationProblem)
            return validationProblem;

        var item = await db.DirectorLoans.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DirectorLoanSnapshot(item);
        item.DirectorId = input.DirectorId;
        item.OpeningBalance = input.OpeningBalance;
        item.Advances = input.Advances;
        item.Repayments = input.Repayments;
        item.ClosingBalance = input.ClosingBalance;
        item.InterestRate = input.InterestRate;
        item.InterestCharged = input.InterestCharged;
        item.IsDocumented = input.IsDocumented;
        item.LoanTerms = string.IsNullOrWhiteSpace(input.LoanTerms) ? null : input.LoanTerms.Trim();
        item.MaxBalanceDuringYear = input.MaxBalanceDuringYear;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "DirectorLoan",
            item.Id,
            AuditEventCodes.DirectorLoanUpdated,
            oldValue,
            DirectorLoanSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteDirectorLoanEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.DirectorLoans.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DirectorLoanSnapshot(item);
        db.DirectorLoans.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "DirectorLoan",
            id,
            AuditEventCodes.DirectorLoanDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

}
