using System.Data;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Api.Services;

public sealed record CompanyQuarantineRequest(string Confirmation, string Reason);

public sealed record CompanyQuarantineOutcome(
    int CompanyId,
    string CompanyLegalName,
    string Status,
    int EvidenceId,
    string EvidenceSha256,
    DateTime OccurredAtUtc,
    IReadOnlyDictionary<string, long> Inventory,
    long TotalDependentRows);

public sealed record QuarantinedCompanySummary(
    int CompanyId,
    string LegalName,
    DateTime QuarantinedAtUtc,
    string QuarantinedByDisplayName,
    string Reason,
    string EvidenceSha256);

public sealed class CompanyQuarantineService(AccountsDbContext db, AuditService audit)
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MinimumReasonLength = 20;
    private const int MaximumReasonLength = 2_000;

    public async Task<CompanyQuarantineOutcome> QuarantineAsync(
        int companyId,
        CompanyQuarantineRequest request,
        AuthenticatedUser actor,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        RequireOwner(actor);
        var reason = ValidateRequest(request);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await AcquireCompanyWriteBoundaryAsync(companyId, cancellationToken);

        var company = await LoadCompanyForActorAsync(companyId, actor, cancellationToken);
        if (company.IsQuarantined)
            throw new BusinessRuleException("The company is already quarantined.");
        RequireExactConfirmation(company, request.Confirmation);
        await EnsureNoLockedOrFinalisedPeriodsAsync(companyId, cancellationToken);

        var inventory = await new CompanyDependentInventoryService(db)
            .CaptureAsync(companyId, cancellationToken);
        var occurredAtUtc = EvidenceTimestampUtc();
        var evidence = await CreateEvidenceAsync(
            company,
            "Quarantined",
            actor,
            reason,
            request.Confirmation,
            inventory,
            requestId,
            occurredAtUtc,
            cancellationToken);

        company.IsQuarantined = true;
        company.QuarantinedAtUtc = occurredAtUtc;
        company.QuarantinedByUserId = AuthenticatedIdentity.AuditUserId(actor);
        company.QuarantinedByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor);
        company.QuarantineReason = reason;
        company.QuarantineEvidenceSha256 = evidence.EvidenceSha256;
        company.UpdatedAt = occurredAtUtc;
        db.CompanyQuarantineEvents.Add(evidence);

        await audit.LogAsync(
            company.Id,
            null,
            "Company",
            company.Id,
            AuditEventCodes.CompanyQuarantined,
            new { IsQuarantined = false },
            new
            {
                IsQuarantined = true,
                Reason = reason,
                Confirmation = request.Confirmation,
                Inventory = inventory.TableCounts,
                inventory.TotalDependentRows,
                inventory.Sha256,
                EvidenceSha256 = evidence.EvidenceSha256
            },
            AuthenticatedIdentity.AuditUserId(actor),
            actor.TenantId,
            requestId,
            AuthenticatedIdentity.ReviewerDisplayName(actor),
            cancellationToken: cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return Outcome(company, evidence, inventory);
    }

    public async Task<CompanyQuarantineOutcome> RecoverAsync(
        int companyId,
        CompanyQuarantineRequest request,
        AuthenticatedUser actor,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        RequireOwner(actor);
        var reason = ValidateRequest(request);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await AcquireCompanyWriteBoundaryAsync(companyId, cancellationToken);

        var company = await LoadCompanyForActorAsync(companyId, actor, cancellationToken);
        if (!company.IsQuarantined)
            throw new BusinessRuleException("Only a quarantined company can be recovered.");
        RequireExactConfirmation(company, request.Confirmation);

        var inventory = await new CompanyDependentInventoryService(db)
            .CaptureAsync(companyId, cancellationToken);
        var occurredAtUtc = EvidenceTimestampUtc();
        var evidence = await CreateEvidenceAsync(
            company,
            "Recovered",
            actor,
            reason,
            request.Confirmation,
            inventory,
            requestId,
            occurredAtUtc,
            cancellationToken);

        company.IsQuarantined = false;
        company.QuarantinedAtUtc = null;
        company.QuarantinedByUserId = null;
        company.QuarantinedByDisplayName = null;
        company.QuarantineReason = null;
        company.QuarantineEvidenceSha256 = null;
        company.UpdatedAt = occurredAtUtc;
        db.CompanyQuarantineEvents.Add(evidence);

        await audit.LogAsync(
            company.Id,
            null,
            "Company",
            company.Id,
            AuditEventCodes.CompanyRecovered,
            new { IsQuarantined = true },
            new
            {
                IsQuarantined = false,
                Reason = reason,
                Confirmation = request.Confirmation,
                Inventory = inventory.TableCounts,
                inventory.TotalDependentRows,
                inventory.Sha256,
                EvidenceSha256 = evidence.EvidenceSha256
            },
            AuthenticatedIdentity.AuditUserId(actor),
            actor.TenantId,
            requestId,
            AuthenticatedIdentity.ReviewerDisplayName(actor),
            cancellationToken: cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return Outcome(company, evidence, inventory);
    }

    public async Task<IReadOnlyList<QuarantinedCompanySummary>> ListAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        RequireOwner(actor);
        return await db.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(company => company.TenantId == actor.TenantId && company.IsQuarantined)
            .OrderBy(company => company.LegalName)
            .Select(company => new QuarantinedCompanySummary(
                company.Id,
                company.LegalName,
                company.QuarantinedAtUtc!.Value,
                company.QuarantinedByDisplayName!,
                company.QuarantineReason!,
                company.QuarantineEvidenceSha256!))
            .ToListAsync(cancellationToken);
    }

    private async Task<CompanyQuarantineEvent> CreateEvidenceAsync(
        Company company,
        string eventType,
        AuthenticatedUser actor,
        string reason,
        string confirmation,
        CompanyDependentInventory inventory,
        string? requestId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var previousHash = await db.CompanyQuarantineEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.CompanyId == company.Id)
            .OrderByDescending(item => item.Id)
            .Select(item => item.EvidenceSha256)
            .FirstOrDefaultAsync(cancellationToken);
        var evidence = new CompanyQuarantineEvent
        {
            CompanyId = company.Id,
            TenantId = company.TenantId
                ?? throw new BusinessRuleException("The company must belong to a tenant before it can be quarantined."),
            CompanyLegalName = company.LegalName,
            EventType = eventType,
            ActorUserId = AuthenticatedIdentity.AuditUserId(actor),
            ActorDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor),
            ActorRole = actor.Role,
            Reason = reason,
            TypedConfirmation = confirmation,
            InventoryJson = inventory.CanonicalJson,
            InventorySha256 = inventory.Sha256,
            TotalDependentRows = inventory.TotalDependentRows,
            PreviousEvidenceSha256 = previousHash,
            EvidenceSha256 = string.Empty,
            RequestId = Normalize(requestId, 128),
            OccurredAtUtc = occurredAtUtc
        };
        evidence.EvidenceSha256 = CompanyQuarantineEvidenceIntegrity.ComputeHash(evidence);
        return evidence;
    }

    private async Task<Company> LoadCompanyForActorAsync(
        int companyId,
        AuthenticatedUser actor,
        CancellationToken cancellationToken) =>
        await db.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                company => company.Id == companyId && company.TenantId == actor.TenantId,
                cancellationToken)
        ?? throw new ResourceNotFoundException($"Company {companyId} not found");

    private async Task EnsureNoLockedOrFinalisedPeriodsAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var blocked = await db.AccountingPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(period => period.CompanyId == companyId)
            .Where(period => period.LockedAt != null
                || period.Status == PeriodStatus.Finalised
                || period.Status == PeriodStatus.Filed)
            .OrderByDescending(period => period.PeriodEnd)
            .Select(period => new { period.PeriodEnd, period.Status, period.LockedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (blocked is not null)
        {
            throw new BusinessRuleException(
                $"Company quarantine is blocked because the period ended {blocked.PeriodEnd:yyyy-MM-dd} is locked or {blocked.Status}. Reopen it before quarantining the company.");
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
            return null;
        // Advisory locks serialize period creation and every protected write. ReadCommitted avoids a
        // stale MVCC snapshot when quarantine waits behind a writer that then commits under that lock.
        return await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    }

    private async Task AcquireCompanyWriteBoundaryAsync(int companyId, CancellationToken cancellationToken)
    {
        if (!UsesPostgres())
            return;

        // Matches PeriodChronologyService so period creation cannot race the inventory snapshot.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({companyId})",
            cancellationToken);
        var periodIds = await db.AccountingPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(period => period.CompanyId == companyId)
            .OrderBy(period => period.Id)
            .Select(period => period.Id)
            .ToListAsync(cancellationToken);
        foreach (var periodId in periodIds)
            await AccountingConcurrencyCoordinator.AcquireAdvisoryLockAsync(db, periodId, cancellationToken);
    }

    private bool UsesPostgres() =>
        db.Database.IsRelational()
        && string.Equals(db.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal);

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private static void RequireOwner(AuthenticatedUser actor)
    {
        if (!string.Equals(actor.Role, "Owner", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Only an Owner may quarantine or recover a company.");
    }

    private static string ValidateRequest(CompanyQuarantineRequest request)
    {
        if (request is null)
            throw new BusinessRuleException("Typed confirmation and a reason are required.");
        var reason = request.Reason?.Trim() ?? string.Empty;
        var reasonCharacters = reason.EnumerateRunes().Count();
        if (reasonCharacters < MinimumReasonLength || reasonCharacters > MaximumReasonLength)
        {
            throw new BusinessRuleException(
                $"A specific reason between {MinimumReasonLength} and {MaximumReasonLength} characters is required.");
        }
        return reason;
    }

    private static void RequireExactConfirmation(Company company, string confirmation)
    {
        if (!string.Equals(confirmation, company.LegalName, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(
                $"Typed confirmation must exactly match the legal name \"{company.LegalName}\".");
        }
    }

    private static CompanyQuarantineOutcome Outcome(
        Company company,
        CompanyQuarantineEvent evidence,
        CompanyDependentInventory inventory) =>
        new(
            company.Id,
            company.LegalName,
            evidence.EventType,
            evidence.Id,
            evidence.EvidenceSha256,
            evidence.OccurredAtUtc,
            inventory.TableCounts,
            inventory.TotalDependentRows);

    private static DateTime EvidenceTimestampUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
