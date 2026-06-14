using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class AccountingWriteGuard(AccountsDbContext db)
{
    public async Task<AccountingWriteDecision> CheckCompanyAccountingWriteAsync(int companyId, DateOnly? effectiveDate = null)
    {
        var lockedPeriod = await LatestLockedPeriodAsync(companyId);

        if (lockedPeriod is null)
            return AccountingWriteDecision.Allowed();

        if (effectiveDate is not null && effectiveDate > lockedPeriod.PeriodEnd)
            return AccountingWriteDecision.Allowed();

        return AccountingWriteDecision.Denied(
            $"Company-level accounting data is locked because the period ended {lockedPeriod.PeriodEnd:yyyy-MM-dd} is {lockedPeriod.Status}. Reopen the affected period before changing shared accounting records.");
    }

    public async Task<AccountingWriteDecision> CheckCompanyMasterDataWriteAsync(int companyId)
    {
        var lockedPeriod = await LatestLockedPeriodAsync(companyId);

        if (lockedPeriod is null)
            return AccountingWriteDecision.Allowed();

        return AccountingWriteDecision.Denied(
            $"Company master data is locked because the period ended {lockedPeriod.PeriodEnd:yyyy-MM-dd} is {lockedPeriod.Status}. Reopen the affected period before changing details used in statutory outputs.");
    }

    public async Task<IResult?> BlockIfCompanyAccountingLockedAsync(int companyId, DateOnly? effectiveDate = null)
    {
        var decision = await CheckCompanyAccountingWriteAsync(companyId, effectiveDate);
        return decision.CanWrite
            ? null
            : Results.Conflict(new { error = decision.Reason });
    }

    public async Task<IResult?> BlockIfCompanyMasterDataLockedAsync(int companyId)
    {
        var decision = await CheckCompanyMasterDataWriteAsync(companyId);
        return decision.CanWrite
            ? null
            : Results.Conflict(new { error = decision.Reason });
    }

    private async Task<LockedPeriod?> LatestLockedPeriodAsync(int companyId) =>
        await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Where(p => p.Status == PeriodStatus.Finalised || p.Status == PeriodStatus.Filed || p.LockedAt != null)
            .OrderByDescending(p => p.PeriodEnd)
            .Select(p => new LockedPeriod(p.PeriodEnd, p.Status))
            .FirstOrDefaultAsync();
}

public record AccountingWriteDecision(bool CanWrite, string? Reason)
{
    public static AccountingWriteDecision Allowed() => new(true, null);

    public static AccountingWriteDecision Denied(string reason) => new(false, reason);
}

public record LockedPeriod(DateOnly PeriodEnd, PeriodStatus Status);
