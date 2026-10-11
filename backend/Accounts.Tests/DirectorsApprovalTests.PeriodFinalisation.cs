using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>The board approval date recorded at finalisation cannot move under a recorded s.324 approval.</summary>
public sealed partial class DirectorsApprovalTests
{
    [Fact]
    public async Task Finalising_WithADifferentBoardDate_IsRefusedWhileADirectorsApprovalIsCurrent()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        await service.RecordApprovalAsync(p.CompanyId, p.Id, RecordInput(page.Id, Pdf("signed"), Pdf("minute")), Owner);
        Assert.NotNull(await service.GetCurrentApprovalAsync(p.Id));
        var context = EndpointContext("Owner", "PUT", $"/api/companies/{p.CompanyId}/periods/{p.Id}/status", p.CompanyId);
        context.Request.Headers["Idempotency-Key"] = $"period-status:{Guid.NewGuid():N}";

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PeriodStatusEndpoint.UpdateAsync(
            p.CompanyId, p.Id,
            // After the period end and not in the future, so only the recorded approval blocks it.
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null, new DateOnly(2026, 8, 15)),
            db, new AuditService(db), new FinancialStatementsService(db),
            context,
            NoApiKey()));

        Assert.Contains("A directors' approval is recorded for the current approval date", error.Message);
        Assert.Contains("regenerate the accounts", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        Assert.Equal(new DateOnly(2026, 11, 20), reloaded.ApprovalDate);
        Assert.NotEqual(PeriodStatus.Finalised, reloaded.Status);
    }
}
