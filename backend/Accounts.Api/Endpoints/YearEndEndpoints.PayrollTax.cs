using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static async Task<IResult> GetPayrollSummaryEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var payroll = await db.PayrollSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PeriodId == periodId);
        return TypedResults.Ok(payroll);
    }

    public static async Task<IResult> UpsertPayrollSummaryEndpointAsync(
        int companyId,
        int periodId,
        PayrollSummaryInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (input.GrossWages < 0
            || input.DirectorsFees < 0
            || input.EmployerPrsi < 0
            || input.PensionContributions < 0
            || input.StaffCount < 0)
        {
            return Results.BadRequest(new { error = "Payroll amounts and staff count must not be negative." });
        }

        var item = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        var wasCreated = item is null;
        var oldValue = item is null ? null : PayrollSummarySnapshot(item);

        if (item == null)
        {
            item = input.ToEntity(periodId);
            db.PayrollSummaries.Add(item);
        }
        else
        {
            item.GrossWages = input.GrossWages;
            item.DirectorsFees = input.DirectorsFees;
            item.EmployerPrsi = input.EmployerPrsi;
            item.PensionContributions = input.PensionContributions;
            item.StaffCount = input.StaffCount;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "PayrollSummary",
            item.Id,
            AuditEventCodes.PayrollSummaryUpserted,
            oldValue,
            new
            {
                item.GrossWages,
                item.DirectorsFees,
                item.EmployerPrsi,
                item.PensionContributions,
                item.StaffCount,
                WasCreated = wasCreated
            },
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> UpsertTaxBalanceEndpointAsync(
        int companyId,
        int periodId,
        TaxType taxType,
        TaxBalanceInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        [FromServices] DeadlineService? deadlines = null,
        [FromServices] ILogger<DeadlineService>? deadlineLogger = null)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        // accounting-tax-balance-internal-consistency: the triple must be self-consistent. A verbatim
        // inconsistent triple (e.g. Balance != Liability - Paid) mis-states creditors and profit-after-
        // tax downstream. Liability and Paid are amounts and cannot be negative; the outstanding Balance
        // must equal Liability - Paid (a negative Balance is a legitimate overpayment / refund due).
        if (input.Liability < 0 || input.Paid < 0)
            return Results.BadRequest(new { error = "Tax liability and amount paid must not be negative." });
        if (Math.Abs(input.Balance - (input.Liability - input.Paid)) > 0.005m)
            return Results.BadRequest(new { error = "Tax balance must equal liability minus amount paid." });

        var item = await db.TaxBalances.FirstOrDefaultAsync(t => t.PeriodId == periodId && t.TaxType == taxType);
        var wasCreated = item is null;
        var oldValue = item is null ? null : TaxBalanceSnapshot(item);
        var wasConfirmedDue = item?.ConfirmedDueAt is not null;
        if (item == null)
        {
            item = input.ToEntity(periodId, taxType);
            db.TaxBalances.Add(item);
        }
        else
        {
            // A due confirmation is given for the liability it was made on; a different amount needs a new one.
            if (item.Liability != input.Liability)
            {
                item.ConfirmedDueBy = null;
                item.ConfirmedDueAt = null;
                item.ConfirmedDueReason = null;
                item.ConfirmedDueLiability = null;
                item.ConfirmedTaxableProfitOutsideExemption = null;
            }
            item.Liability = input.Liability;
            item.Paid = input.Paid;
            item.Balance = input.Balance;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "TaxBalance",
            item.Id,
            AuditEventCodes.TaxBalanceUpserted,
            oldValue,
            new
            {
                item.TaxType,
                item.Liability,
                item.Paid,
                item.Balance,
                item.ConfirmedDueBy,
                item.ConfirmedDueAt,
                item.ConfirmedDueReason,
                item.ConfirmedDueLiability,
                item.ConfirmedTaxableProfitOutsideExemption,
                WasCreated = wasCreated
            },
            AuditUserId(context));
        if (wasConfirmedDue && item.ConfirmedDueAt is null)
            await RecalculateDeadlinesForCorporationTaxDueAsync(db, audit, deadlines, deadlineLogger, companyId, periodId, AuditUserId(context));
        return Results.Ok(item);
    }

    /// <summary>
    /// Revenue (CT1) obligations follow an exempt company's due confirmation, so the period's deadlines
    /// are recalculated in the same request: the Revenue deadline appears (or an unfiled one goes) at
    /// once. The recalculation is atomic (a savepoint in the request's transaction, or its own
    /// transaction): if it is refused for an unrelated reason, every partial deadline change is rolled
    /// back, the confirmation stands, a warning is logged, and finalisation refuses until the deadlines
    /// are recalculated as usual.
    /// </summary>
    private static async Task RecalculateDeadlinesForCorporationTaxDueAsync(
        AccountsDbContext db,
        AuditService audit,
        DeadlineService? deadlines,
        ILogger? logger,
        int companyId,
        int periodId,
        string? userId)
    {
        const string savepoint = "corporation_tax_due_deadlines";
        var relational = db.Database.IsRelational();
        var ownedTransaction = relational && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync()
            : null;
        try
        {
            if (relational && ownedTransaction is null)
                await db.Database.CurrentTransaction!.CreateSavepointAsync(savepoint);
            await (deadlines ?? new DeadlineService(db, audit)).CalculateDeadlinesAsync(companyId, periodId, userId);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync();
            else if (relational)
                await db.Database.CurrentTransaction!.ReleaseSavepointAsync(savepoint);
        }
        catch (BusinessRuleException refusal)
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync();
            else if (relational)
                await db.Database.CurrentTransaction!.RollbackToSavepointAsync(savepoint);
            // The confirmation was saved before the recalculation; anything still tracked is partial.
            db.ChangeTracker.Clear();
            // Identifiers and the exception type only: never the refusal text or personal data.
            logger?.LogWarning(
                "Deadline recalculation after a corporation-tax due change was refused for company {CompanyId} period {PeriodId} ({ExceptionType}); deadlines will be recalculated on the next workspace load.",
                companyId,
                periodId,
                refusal.GetType().Name);
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    private const int MaxCorporationTaxDueReasonLength = 1000;

    /// <summary>
    /// For a company holding a charitable tax exemption, records that the entered corporation-tax
    /// liability is due (for example on income outside the exemption), so it is provided for as for
    /// a company without the exemption. Owner/Reviewer only, with recent authenticator verification
    /// (RecentAuthenticationMiddleware); who confirmed is the authenticated principal.
    /// </summary>
    public static async Task<IResult> ConfirmCorporationTaxDueEndpointAsync(
        int companyId,
        int periodId,
        CorporationTaxDueConfirmationInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        [FromServices] DeadlineService? deadlines = null,
        [FromServices] ILogger<DeadlineService>? deadlineLogger = null)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;
        var user = AuthContext.GetUser(context);
        if (user is null || user.Role is not "Owner" and not "Reviewer")
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxCorporationTaxDueReasonLength)
            return Results.BadRequest(new { error = "Give the reason the corporation tax liability is due (up to 1000 characters)." });
        if (input.TaxableProfitOutsideExemption is not { } attestedProfit || attestedProfit < 0 || attestedProfit > 999_999_999_999m)
            return Results.BadRequest(new { error = "Give the taxable profit from activities outside the charitable exemption (0 or more)." });
        var exempt = await db.Companies
            .Where(company => company.Id == companyId)
            .Select(company => company.HoldsCharitableTaxExemption)
            .FirstOrDefaultAsync();
        if (!exempt)
            return Results.BadRequest(new { error = "Only a company holding a charitable tax exemption confirms its corporation tax liability; this company's liability is provided for automatically." });
        var item = await db.TaxBalances.FirstOrDefaultAsync(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax);
        if (item is null || item.Liability <= 0)
            return Results.BadRequest(new { error = "There is no corporation tax liability entered for this period to confirm." });

        var oldValue = TaxBalanceSnapshot(item);
        item.ConfirmedDueBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        item.ConfirmedDueAt = DateTime.UtcNow;
        item.ConfirmedDueReason = reason;
        item.ConfirmedDueLiability = item.Liability;
        item.ConfirmedTaxableProfitOutsideExemption = decimal.Round(attestedProfit, 2, MidpointRounding.AwayFromZero);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "TaxBalance",
            item.Id,
            AuditEventCodes.TaxBalanceCorporationTaxDueConfirmed,
            oldValue,
            TaxBalanceSnapshot(item),
            AuthenticatedIdentity.AuditUserId(user));
        await RecalculateDeadlinesForCorporationTaxDueAsync(db, audit, deadlines, deadlineLogger, companyId, periodId, AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(item);
    }
}
