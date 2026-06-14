using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class PeriodStatusEndpoint
{
    public static async Task<IResult> UpdateAsync(
        int companyId,
        int id,
        PeriodStatusUpdate update,
        AccountsDbContext db,
        AuditService audit,
        FinancialStatementsService statements,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
        if (period is null) return Results.NotFound();

        if (EndpointInputs.ValidatePeriodStatusUpdate(period, update, user) is { } validationProblem)
            return validationProblem;

        if (update.Status is PeriodStatus.Finalised or PeriodStatus.Filed)
        {
            var outputName = update.Status is PeriodStatus.Filed
                ? "accounts filing"
                : "accounts finalisation";
            await statements.AssertFinalOutputReadinessAsync(companyId, id, outputName);
        }
        if (update.Status is PeriodStatus.Filed)
            await AssertFilingObligationsRecordedAsync(db, companyId, id);

        var oldValue = new
        {
            period.Status,
            period.LockedAt,
            period.LockedBy,
            period.ReopenedAt,
            period.ReopenedBy,
            period.ReopenReason
        };
        EndpointInputs.ApplyPeriodStatusUpdate(period, update, user, DateTime.UtcNow);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            id,
            "AccountingPeriod",
            id,
            "StatusUpdated",
            oldValue,
            new
            {
                period.Status,
                period.LockedAt,
                period.LockedBy,
                period.ReopenedAt,
                period.ReopenedBy,
                period.ReopenReason
            },
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(period);
    }

    private static async Task AssertFilingObligationsRecordedAsync(
        AccountsDbContext db,
        int companyId,
        int periodId)
    {
        var company = await db.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.IsCharitableOrganisation })
            .SingleAsync();
        var requiredTypes = company.IsCharitableOrganisation
            ? new[] { DeadlineType.CRO, DeadlineType.Revenue, DeadlineType.Charity }
            : new[] { DeadlineType.CRO, DeadlineType.Revenue };
        var deadlines = await db.FilingDeadlines
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.PeriodId == periodId)
            .ToListAsync();
        var issues = new List<string>();

        foreach (var type in requiredTypes)
        {
            var deadline = deadlines.FirstOrDefault(d => d.DeadlineType == type);
            if (deadline is null)
            {
                issues.Add($"{type} filing deadline has not been calculated");
                continue;
            }

            if (deadline.FiledDate is null)
                issues.Add($"{type} filing has not been recorded as filed");
            else if (string.IsNullOrWhiteSpace(deadline.FilingReference))
                issues.Add($"{type} filing reference has not been recorded");
        }

        if (issues.Count > 0)
            throw new BusinessRuleException(
                $"Cannot mark period as filed until filing obligations are recorded: {string.Join("; ", issues)}.");
    }
}
