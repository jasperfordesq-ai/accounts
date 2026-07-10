using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed class ExternalFilingHandoffRepository(AccountsDbContext db)
{
    public Task<FilingAuthorityEngagement?> LatestAuthorityAsync(
        int companyId,
        ExternalFilingWorkflow workflow,
        CancellationToken cancellationToken = default) =>
        db.FilingAuthorityEngagements
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Workflow == workflow.ToString())
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<FilingAuthorityEngagement?> AuthorityAsync(
        long authorityId,
        int companyId,
        CancellationToken cancellationToken = default) =>
        db.FilingAuthorityEngagements
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == authorityId && item.CompanyId == companyId, cancellationToken);

    public Task<List<FilingAuthorityEngagement>> AuthoritiesAsync(
        int companyId,
        CancellationToken cancellationToken = default) =>
        db.FilingAuthorityEngagements
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Workflow)
            .ThenBy(item => item.Version)
            .ToListAsync(cancellationToken);

    public Task<ExternalFilingHandoffSnapshot?> LatestSnapshotAsync(
        int companyId,
        int periodId,
        ExternalFilingWorkflow workflow,
        CancellationToken cancellationToken = default) =>
        db.ExternalFilingHandoffSnapshots
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId
                && item.PeriodId == periodId
                && item.Workflow == workflow.ToString())
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ExternalFilingHandoffSnapshot?> SnapshotAsync(
        int companyId,
        int periodId,
        Guid snapshotId,
        CancellationToken cancellationToken = default) =>
        db.ExternalFilingHandoffSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CompanyId == companyId
                && item.PeriodId == periodId
                && item.SnapshotId == snapshotId, cancellationToken);

    public Task<List<ExternalFilingHandoffSnapshot>> SnapshotsAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default) =>
        db.ExternalFilingHandoffSnapshots
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.PeriodId == periodId)
            .OrderBy(item => item.Workflow)
            .ThenBy(item => item.Version)
            .ToListAsync(cancellationToken);

    public Task<List<ExternalFilingOutcomeEvent>> OutcomesAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default) =>
        db.ExternalFilingOutcomeEvents
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.PeriodId == periodId)
            .OrderBy(item => item.RecordedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

    public Task<ExternalFilingOutcomeEvent?> LatestOutcomeAsync(
        long snapshotRecordId,
        CancellationToken cancellationToken = default) =>
        db.ExternalFilingOutcomeEvents
            .AsNoTracking()
            .Where(item => item.SnapshotRecordId == snapshotRecordId)
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(FilingAuthorityEngagement authority) => db.FilingAuthorityEngagements.Add(authority);
    public void Add(ExternalFilingHandoffSnapshot snapshot) => db.ExternalFilingHandoffSnapshots.Add(snapshot);
    public void Add(ExternalFilingOutcomeEvent outcome) => db.ExternalFilingOutcomeEvents.Add(outcome);

    public async Task AcquireWorkflowLockAsync(
        int companyId,
        ExternalFilingWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
            return;
        var workflowKey = workflow == ExternalFilingWorkflow.CroB1 ? 1 : 2;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({companyId}, {90_000 + workflowKey})",
            cancellationToken);
    }
}
