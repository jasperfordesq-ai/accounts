using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;
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
        ApiAccessService apiAccess,
        FilingReleaseGate? releaseGate = null,
        AccountingConcurrencyCoordinator? concurrency = null,
        IdempotencyService? idempotency = null,
        [FromServices] TimeProvider? clock = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        idempotency ??= new IdempotencyService(db);
        try
        {
            var command = await IdempotencyHttpContract.ExecuteAsync(
                context,
                idempotency,
                user,
                IdempotencyOperations.PeriodStatus,
                new { companyId, periodId = id, update },
                async cancellationToken =>
                {
                    concurrency ??= new AccountingConcurrencyCoordinator(db);
                    await using var concurrencyLease = await concurrency.AcquirePeriodAsync(
                        companyId,
                        id,
                        cancellationToken);

                    // Every readiness query, immutable snapshot and status write below runs while the same
                    // PostgreSQL transaction owns both the period advisory lock and its FOR UPDATE row lock.
                    var period = await db.AccountingPeriods.FirstOrDefaultAsync(
                        p => p.Id == id && p.CompanyId == companyId,
                        cancellationToken)
                        ?? throw new ResourceNotFoundException($"Period {id} not found");

                    if (EndpointInputs.ValidatePeriodStatusUpdate(period, update, user) is { } validationProblem)
                        throw new PeriodStatusValidationException(validationProblem);

                    if (update.Status is PeriodStatus.Finalised or PeriodStatus.Filed)
                    {
                        // Fix the board-approval date before readiness validation, then refresh the coded approval
                        // note in the same advisory-locked transaction. If readiness fails, the transaction is not
                        // committed and neither value leaks into the draft period.
                        // The default is today's date in Ireland (Europe/Dublin), not the UTC date.
                        var irishToday = DateOnly.FromDateTime(
                            TimeZoneInfo.ConvertTimeFromUtc(
                                (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime,
                                DeadlineService.ResolveIrelandTimeZone()));
                        if (update.ApprovalDate is { } requestedDate)
                        {
                            await AssertBoardApprovalDateCanBeSetAsync(db, period, requestedDate, irishToday, cancellationToken);
                            period.ApprovalDate = requestedDate;
                        }
                        else
                        {
                            period.ApprovalDate ??= irishToday;
                        }
                        await NotesDisclosureService.RefreshApprovalNoteAsync(db, period);

                        var outputName = update.Status is PeriodStatus.Filed
                            ? "accounts filing"
                            : "accounts finalisation";
                        await statements.AssertFinalOutputReadinessAsync(companyId, id, outputName);

                        // accounting-retained-earnings-snapshot: capture the closing reserves at finalisation so a
                        // later period reads a fixed opening-reserves figure rather than recomputing prior-year P&L.
                        var snapshotBalanceSheet = await statements.GetBalanceSheetAsync(companyId, id);
                        period.ClosingRetainedEarnings = snapshotBalanceSheet.CapitalAndReserves.RetainedEarnings;
                    }
                    if (update.Status is PeriodStatus.Filed)
                        await AssertFilingObligationsRecordedAsync(
                            db,
                            releaseGate ?? new FilingReleaseGate(db),
                            companyId,
                            id,
                            AuthenticatedIdentity.AuditUserId(user));

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
                    await db.SaveChangesAsync(cancellationToken);
                    await DomainAuditCoverage.LogAsync(
                        audit,
                        context,
                        companyId,
                        id,
                        nameof(AccountingPeriod),
                        id,
                        AuditEventCodes.AccountingPeriodStatusChanged,
                        oldValue,
                        DomainAuditCoverage.PeriodSnapshot(period),
                        cancellationToken);
                    await concurrencyLease.CommitIfOwnedAsync(cancellationToken);
                    return new IdempotencyOperationOutcome<AccountingPeriod>(
                        period,
                        nameof(AccountingPeriod),
                        period.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                });
            return command.Error ?? IdempotencyHttpContract.JsonResult(command.Execution!);
        }
        catch (PeriodStatusValidationException validation)
        {
            return validation.Result;
        }
        catch (ResourceNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private sealed class PeriodStatusValidationException(IResult result)
        : BusinessRuleException("Period status validation failed.")
    {
        public IResult Result { get; } = result;
    }

    /// <summary>
    /// Companies Act 2014 s.324: the board approves the accounts on or after the period end and not in
    /// the future. A recorded date may be corrected only while no current directors' approval binds
    /// it; that approval is withdrawn first (on the Filing tab), or by regenerating changed accounts.
    /// </summary>
    private static async Task AssertBoardApprovalDateCanBeSetAsync(
        AccountsDbContext db,
        AccountingPeriod period,
        DateOnly requestedDate,
        DateOnly irishToday,
        CancellationToken cancellationToken)
    {
        if (requestedDate < period.PeriodEnd)
            throw new BusinessRuleException(
                $"The board approval date must be on or after the period end ({period.PeriodEnd:yyyy-MM-dd}).");
        if (requestedDate > irishToday)
            throw new BusinessRuleException("The board approval date cannot be in the future.");
        if (period.ApprovalDate is not { } storedDate || storedDate == requestedDate)
            return;

        var cro = await db.CroFilingPackages.AsNoTracking()
            .SingleOrDefaultAsync(c => c.PeriodId == period.Id, cancellationToken);
        var events = await db.DirectorsApprovalEvents.AsNoTracking()
            .Where(e => e.PeriodId == period.Id)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);
        var pages = await db.DirectorsApprovalPages.AsNoTracking()
            .Where(p => p.PeriodId == period.Id)
            .ToListAsync(cancellationToken);
        if (DirectorsApprovalService.ResolveCurrent(events, pages, cro) is not null)
            throw new BusinessRuleException(
                "A directors' approval is recorded for the current approval date; withdraw it on the Filing tab before changing the date.");
    }

    private static async Task AssertFilingObligationsRecordedAsync(
        AccountsDbContext db,
        FilingReleaseGate releaseGate,
        int companyId,
        int periodId,
        string auditUserId)
    {
        var isCharitableOrganisation = await db.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.IsCharitableOrganisation)
            .SingleAsync();
        // Revenue applies unless the company holds a charitable tax exemption and nothing is confirmed due.
        var requiredTypes = new List<DeadlineType> { DeadlineType.CRO };
        if (await CorporationTaxObligation.RevenueAppliesAsync(db, periodId))
            requiredTypes.Add(DeadlineType.Revenue);
        if (isCharitableOrganisation)
            requiredTypes.Add(DeadlineType.Charity);
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

        foreach (var deadline in deadlines.Where(d => requiredTypes.Contains(d.DeadlineType)))
        {
            var workflow = deadline.DeadlineType switch
            {
                DeadlineType.CRO => FilingReleaseWorkflow.Cro,
                DeadlineType.Revenue => FilingReleaseWorkflow.Revenue,
                DeadlineType.Charity => FilingReleaseWorkflow.Charity,
                _ => throw new ArgumentOutOfRangeException()
            };
            await releaseGate.AssertCanRecordFiledAsync(
                companyId,
                periodId,
                workflow,
                deadline.FilingReference,
                auditUserId);
        }
    }
}
