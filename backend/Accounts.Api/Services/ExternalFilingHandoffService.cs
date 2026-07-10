using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Api.Services;

public sealed class ExternalFilingHandoffService(
    AccountsDbContext db,
    ExternalFilingHandoffRepository repository,
    FilingReleaseIdentityProvider releaseIdentity,
    CorporationTaxFilingSupportService taxSupportService,
    AuditService audit)
{
    private static readonly JsonSerializerOptions IntegrityJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed record AuthorityInput(
        ExternalFilingWorkflow Workflow,
        ExternalFilingAuthorityKind Kind,
        string LegalName,
        string? PracticeName,
        string? MaskedPresenterOrTain,
        string AuthorityScope,
        string EngagementReference,
        string ExternalAuthorityReference,
        DateTime EffectiveFromUtc,
        DateTime? EffectiveUntilUtc,
        byte[] EvidenceArtifact,
        string EvidenceSha256,
        string EvidenceMediaType,
        string EvidenceFileName);

    public sealed record CroOfficerInput(
        int OfficerId,
        string FirstName,
        string LastName,
        ExternalHandoffAddress Address,
        string IdentityType,
        string IdentityEvidenceReference,
        string IdentityEvidenceSha256,
        string? PresenterNotificationEmail,
        string OtherDirectorshipsEvidenceReference,
        bool ProtectedIdentifierEntryConfirmed);

    public sealed record CroSnapshotInput(
        DateOnly MadeUpToDate,
        string AnnualReturnDateElection,
        bool FinancialStatementsAnnexed,
        bool AuditExemptionClaimed,
        string? AuditorReference,
        string ReportingCurrency,
        bool PoliticalDonationsOverThreshold,
        decimal PoliticalDonationsAmount,
        string PoliticalDonationsEvidenceReference,
        IReadOnlyList<CroOfficerInput> Officers,
        IReadOnlyList<B1ShareholderHandoff> Shareholders,
        IReadOnlyList<B1AllotmentHandoff> Allotments,
        bool NoAllotmentsInReturnPeriodConfirmed,
        string? ShareholdersListPdfSha256,
        Guid? SupersedesSnapshotId,
        string? AmendmentReason);

    public sealed record RevenueSnapshotInput(
        DateOnly? AsOfDate,
        bool UnsupportedSectionsReviewed,
        IReadOnlyList<string> ManualCt1CompletionItems,
        Guid? SupersedesSnapshotId,
        string? AmendmentReason);

    public sealed record OutcomeCommand(
        ExternalFilingOutcomeKind Outcome,
        string? ExternalReference,
        DateTime? ExternalOccurredAtUtc,
        string? Reason,
        DateTime? CorrectionDeadlineUtc,
        string? EvidenceReference,
        byte[]? EvidenceArtifact,
        string? EvidenceSha256,
        Guid? SupersedingSnapshotId);

    public sealed record SnapshotResponse(
        ExternalFilingHandoffDocument Document,
        string ArtifactSha256);

    public sealed record OutcomeResponse(
        long EventId,
        Guid SnapshotId,
        string SnapshotArtifactSha256,
        ExternalFilingOutcomeKind Outcome,
        string? ExternalReference,
        DateTime? ExternalOccurredAtUtc,
        string? Reason,
        DateTime? CorrectionDeadlineUtc,
        string? EvidenceReference,
        string? EvidenceSha256,
        Guid? SupersedingSnapshotId,
        string? SupersedingSnapshotArtifactSha256,
        ExternalFilingActor RecordedBy,
        DateTime RecordedAtUtc);

    public sealed record CroOfficerPreparation(
        int OfficerId,
        string Name,
        string Role,
        DateOnly? AppointedDate,
        DateOnly? ResignedDate,
        string? SourceAddress);

    public sealed record PreparationContext(
        string LegalName,
        string? CroNumber,
        string? TaxReference,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        DateOnly? AnnualReturnDate,
        ExternalHandoffAddress RegisteredOffice,
        string ReportingCurrency,
        IReadOnlyList<CroOfficerPreparation> Officers);

    public sealed record Workspace(
        int TenantId,
        int CompanyId,
        int PeriodId,
        bool DirectCroSubmissionSupported,
        bool DirectRosSubmissionSupported,
        PreparationContext Preparation,
        IReadOnlyList<ExternalFilingAuthoritySnapshot> Authorities,
        IReadOnlyList<SnapshotResponse> Snapshots,
        IReadOnlyList<OutcomeResponse> Outcomes,
        IReadOnlyList<string> SourceGaps);

    public async Task<Workspace> GetWorkspaceAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Include(item => item.Company)
            .ThenInclude(company => company.Officers)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var tenantId = period.Company.TenantId
            ?? throw new BusinessRuleException("The company must belong to a tenant before filing handoff evidence can be read.");
        var authorities = await repository.AuthoritiesAsync(companyId, cancellationToken);
        var snapshots = await repository.SnapshotsAsync(companyId, periodId, cancellationToken);
        var outcomes = await repository.OutcomesAsync(companyId, periodId, cancellationToken);
        var parsedSnapshots = snapshots.Select(ToSnapshotResponse).ToList();
        return new Workspace(
            tenantId,
            companyId,
            periodId,
            DirectCroSubmissionSupported: false,
            DirectRosSubmissionSupported: false,
            new PreparationContext(
                period.Company.LegalName,
                period.Company.CroNumber,
                period.Company.TaxReference,
                period.PeriodStart,
                period.PeriodEnd,
                period.Company.AnnualReturnDate,
                CompanyAddress(period.Company),
                "EUR",
                period.Company.Officers
                    .OrderBy(item => item.Role)
                    .ThenBy(item => item.Name)
                    .Select(item => new CroOfficerPreparation(
                        item.Id,
                        item.Name,
                        item.Role.ToString(),
                        item.AppointedDate,
                        item.ResignedDate,
                        item.Address))
                    .ToList()),
            authorities.Select(ToAuthoritySnapshot).ToList(),
            parsedSnapshots,
            outcomes.Select(ToOutcomeResponse).ToList(),
            SourceGaps(parsedSnapshots, authorities));
    }

    public async Task<ExternalFilingAuthoritySnapshot> RecordAuthorityAsync(
        int companyId,
        int periodId,
        AuthorityInput input,
        ExternalFilingActor actor,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await RequiredTenantPeriodAsync(companyId, periodId, cancellationToken);
        ValidateActor(actor);
        ValidateAuthorityInput(input);
        var candidate = releaseIdentity.GetRequiredCandidate();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await repository.AcquireWorkflowLockAsync(companyId, input.Workflow, cancellationToken);
        var predecessor = await repository.LatestAuthorityAsync(companyId, input.Workflow, cancellationToken);
        var now = DateTime.UtcNow;
        var authority = new FilingAuthorityEngagement
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Version = (predecessor?.Version ?? 0) + 1,
            SupersedesAuthorityId = predecessor?.Id,
            Workflow = input.Workflow.ToString(),
            Kind = input.Kind.ToString(),
            Status = ExternalFilingAuthorityStatus.Active.ToString(),
            LegalName = input.LegalName.Trim(),
            PracticeName = Optional(input.PracticeName),
            MaskedPresenterOrTain = Optional(input.MaskedPresenterOrTain),
            AuthorityScope = input.AuthorityScope.Trim(),
            EngagementReference = input.EngagementReference.Trim(),
            ExternalAuthorityReference = input.ExternalAuthorityReference.Trim(),
            EffectiveFromUtc = input.EffectiveFromUtc,
            EffectiveUntilUtc = input.EffectiveUntilUtc,
            RevokedAtUtc = null,
            AuthorityEvidenceArtifact = input.EvidenceArtifact.ToArray(),
            AuthorityEvidenceSha256 = input.EvidenceSha256.ToLowerInvariant(),
            EvidenceMediaType = input.EvidenceMediaType.Trim().ToLowerInvariant(),
            EvidenceFileName = input.EvidenceFileName.Trim(),
            ReviewedByUserId = actor.UserId,
            ReviewedByDisplayName = actor.DisplayName,
            ReviewedByRole = actor.Role,
            ReviewedAtUtc = now,
            ReleaseCandidate = candidate,
            RecordSha256 = string.Empty,
            CreatedByUserId = actor.UserId,
            CreatedByDisplayName = actor.DisplayName,
            CreatedByRole = actor.Role,
            CreatedAtUtc = now
        };
        authority.RecordSha256 = HashObject(AuthorityIntegrityProjection(authority));
        repository.Add(authority);
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(FilingAuthorityEngagement),
            periodId,
            AuditEventCodes.ExternalFilingAuthorityRecorded,
            predecessor is null ? null : new { predecessor.Id, predecessor.Version, predecessor.Status },
            new { authority.Id, authority.Version, authority.Workflow, authority.Kind, authority.Status, authority.AuthorityEvidenceSha256, authority.RecordSha256 },
            actor.UserId,
            tenantId,
            actorDisplayName: actor.DisplayName,
            cancellationToken: cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return ToAuthoritySnapshot(authority);
    }

    public async Task<ExternalFilingAuthoritySnapshot> RevokeAuthorityAsync(
        int companyId,
        int periodId,
        long authorityId,
        string reason,
        ExternalFilingActor actor,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await RequiredTenantPeriodAsync(companyId, periodId, cancellationToken);
        ValidateActor(actor);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new BusinessRuleException("Authority revocation requires a reason of at least 10 characters.");
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var original = await repository.AuthorityAsync(authorityId, companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Authority {authorityId} not found");
        var workflow = Enum.Parse<ExternalFilingWorkflow>(original.Workflow);
        await repository.AcquireWorkflowLockAsync(companyId, workflow, cancellationToken);
        var latest = await repository.LatestAuthorityAsync(companyId, workflow, cancellationToken);
        if (latest?.Id != original.Id || original.Status != ExternalFilingAuthorityStatus.Active.ToString())
            throw new BusinessRuleException("Only the latest active authority record can be revoked.");
        var now = DateTime.UtcNow;
        var revoked = new FilingAuthorityEngagement
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Version = original.Version + 1,
            SupersedesAuthorityId = original.Id,
            Workflow = original.Workflow,
            Kind = original.Kind,
            Status = ExternalFilingAuthorityStatus.Revoked.ToString(),
            LegalName = original.LegalName,
            PracticeName = original.PracticeName,
            MaskedPresenterOrTain = original.MaskedPresenterOrTain,
            AuthorityScope = $"{original.AuthorityScope} Revoked: {reason.Trim()}",
            EngagementReference = original.EngagementReference,
            ExternalAuthorityReference = original.ExternalAuthorityReference,
            EffectiveFromUtc = original.EffectiveFromUtc,
            EffectiveUntilUtc = original.EffectiveUntilUtc,
            RevokedAtUtc = now,
            AuthorityEvidenceArtifact = original.AuthorityEvidenceArtifact.ToArray(),
            AuthorityEvidenceSha256 = original.AuthorityEvidenceSha256,
            EvidenceMediaType = original.EvidenceMediaType,
            EvidenceFileName = original.EvidenceFileName,
            ReviewedByUserId = actor.UserId,
            ReviewedByDisplayName = actor.DisplayName,
            ReviewedByRole = actor.Role,
            ReviewedAtUtc = now,
            ReleaseCandidate = releaseIdentity.GetRequiredCandidate(),
            RecordSha256 = string.Empty,
            CreatedByUserId = actor.UserId,
            CreatedByDisplayName = actor.DisplayName,
            CreatedByRole = actor.Role,
            CreatedAtUtc = now
        };
        revoked.RecordSha256 = HashObject(AuthorityIntegrityProjection(revoked));
        repository.Add(revoked);
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(FilingAuthorityEngagement),
            periodId,
            AuditEventCodes.ExternalFilingAuthorityRevoked,
            new { original.Id, original.Version, original.Status },
            new { revoked.Id, revoked.Version, revoked.Status, Reason = reason.Trim(), revoked.RecordSha256 },
            actor.UserId,
            tenantId,
            actorDisplayName: actor.DisplayName,
            cancellationToken: cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return ToAuthoritySnapshot(revoked);
    }

    public async Task<SnapshotResponse> GenerateCroSnapshotAsync(
        int companyId,
        int periodId,
        CroSnapshotInput input,
        ExternalFilingActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await repository.AcquireWorkflowLockAsync(companyId, ExternalFilingWorkflow.CroB1, cancellationToken);
        var period = await db.AccountingPeriods
            .Include(item => item.Company).ThenInclude(company => company.Officers)
            .Include(item => item.Company).ThenInclude(company => company.ShareCapitals)
            .Include(item => item.CroFilingPackage)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var tenantId = period.Company.TenantId
            ?? throw new BusinessRuleException("The company must belong to a tenant before filing handoff evidence can be retained.");
        var candidate = releaseIdentity.GetRequiredCandidate();
        var authority = await RequireCurrentAuthorityAsync(companyId, ExternalFilingWorkflow.CroB1, candidate, DateTime.UtcNow, cancellationToken);
        var package = period.CroFilingPackage
            ?? throw new BusinessRuleException("Generate and approve the CRO accounts/signature artifacts before creating a B1 handoff snapshot.");
        RequireSha(package.AccountsPdfSha256, "CRO accounts PDF SHA-256");
        RequireSha(package.SignaturePageSha256, "CRO signature page SHA-256");
        RequireSha(package.ApprovedArtifactManifestSha256, "Qualified-review artifact manifest SHA-256");
        if (package.AccountsPdfArtifact is not { Length: > 0 } || package.SignaturePageArtifact is not { Length: > 0 })
            throw new BusinessRuleException("The exact retained CRO accounts and signature artifacts are required.");
        if (!string.Equals(package.ApprovedReleaseCandidate, candidate, StringComparison.Ordinal))
            throw new BusinessRuleException("The approved CRO artifact manifest belongs to a different release candidate.");
        if (period.Company.AnnualReturnDate is not { } annualReturnDate)
            throw new BusinessRuleException("Confirm the exact CRO Annual Return Date before creating a B1 handoff snapshot.");

        var officerEntities = period.Company.Officers.ToDictionary(item => item.Id);
        var officers = input.Officers.Select(item =>
        {
            if (!officerEntities.TryGetValue(item.OfficerId, out var source))
                throw new BusinessRuleException($"Officer {item.OfficerId} does not belong to the company.");
            return new B1OfficerHandoff(
                source.Id,
                item.FirstName,
                item.LastName,
                source.Role.ToString(),
                source.AppointedDate,
                source.ResignedDate,
                item.Address,
                item.IdentityType,
                item.IdentityEvidenceReference,
                item.IdentityEvidenceSha256,
                item.PresenterNotificationEmail,
                item.OtherDirectorshipsEvidenceReference,
                ProtectedIdentifierEntryRequired: true);
        }).ToList();
        var shareClasses = period.Company.ShareCapitals
            .Where(item => item.CancelledDate is null)
            .Select(item => new B1ShareClassHandoff(
                item.ShareClass,
                input.ReportingCurrency.Trim().ToUpperInvariant(),
                item.NominalValue,
                item.NumberIssued,
                item.TotalValue,
                item.IsFullyPaid ? item.TotalValue : 0m,
                item.IsFullyPaid ? 0m : item.TotalValue))
            .ToList();
        var facts = new B1ManualHandoffFacts(
            period.Company.CroNumber ?? string.Empty,
            period.Company.LegalName,
            period.Company.CompanyType.ToString(),
            annualReturnDate,
            input.MadeUpToDate,
            input.AnnualReturnDateElection,
            CompanyAddress(period.Company),
            period.PeriodStart,
            period.PeriodEnd,
            input.FinancialStatementsAnnexed,
            period.IsFirstYear,
            input.AuditExemptionClaimed,
            input.AuditorReference,
            input.ReportingCurrency.Trim().ToUpperInvariant(),
            input.PoliticalDonationsOverThreshold,
            input.PoliticalDonationsAmount,
            input.PoliticalDonationsEvidenceReference,
            package.SignedByDirector ?? string.Empty,
            package.SignedBySecretary ?? string.Empty,
            officers,
            shareClasses,
            input.Shareholders,
            input.Allotments,
            package.AccountsPdfSha256!,
            package.SignaturePageSha256!,
            input.ShareholdersListPdfSha256);
        var fields = BuildCroFields(period, package, input, officers, shareClasses);
        var attachments = new List<ExternalFilingAttachment>
        {
            new("accounts-pdf", "cro-accounts.pdf", "application/pdf", package.AccountsPdfArtifact.Length, package.AccountsPdfSha256!, "Retained CRO accounts artifact"),
            new("signature-page", "cro-signature-page.pdf", "application/pdf", package.SignaturePageArtifact.Length, package.SignaturePageSha256!, "Retained CRO signature artifact")
        };
        var request = BuildRequest(
            tenantId,
            companyId,
            period,
            ExternalFilingWorkflow.CroB1,
            actor,
            authority,
            package.ApprovedArtifactManifestSha256!,
            candidate,
            facts,
            null,
            fields,
            attachments);
        var built = await BuildSnapshotAsync(request, input.SupersedesSnapshotId, input.AmendmentReason, cancellationToken);
        var entity = SnapshotEntity(built, authority.Id);
        if (built.Document.SupersedesSnapshotId is { } predecessorId)
        {
            entity.SupersedesSnapshotRecordId = (await repository.SnapshotAsync(companyId, periodId, predecessorId, cancellationToken))?.Id
                ?? throw new BusinessRuleException("The exact predecessor snapshot record is unavailable.");
        }
        repository.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await LogSnapshotAsync(entity, actor, tenantId, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new SnapshotResponse(built.Document, built.ArtifactSha256);
    }

    public async Task<SnapshotResponse> GenerateRevenueSnapshotAsync(
        int companyId,
        int periodId,
        RevenueSnapshotInput input,
        ExternalFilingActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await repository.AcquireWorkflowLockAsync(companyId, ExternalFilingWorkflow.RevenueCt1Support, cancellationToken);
        var period = await db.AccountingPeriods
            .Include(item => item.Company)
            .Include(item => item.RevenueFilingPackage)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var tenantId = period.Company.TenantId
            ?? throw new BusinessRuleException("The company must belong to a tenant before filing handoff evidence can be retained.");
        var candidate = releaseIdentity.GetRequiredCandidate();
        var authority = await RequireCurrentAuthorityAsync(companyId, ExternalFilingWorkflow.RevenueCt1Support, candidate, DateTime.UtcNow, cancellationToken);
        var package = period.RevenueFilingPackage
            ?? throw new BusinessRuleException("Retain the reviewed Revenue package before creating a CT1 support handoff snapshot.");
        RequireSha(package.ApprovedArtifactManifestSha256, "Qualified-review Revenue manifest SHA-256");
        if (!string.Equals(package.ApprovedReleaseCandidate, candidate, StringComparison.Ordinal))
            throw new BusinessRuleException("The approved Revenue manifest belongs to a different release candidate.");
        var support = await taxSupportService.GetAsync(companyId, periodId, input.AsOfDate, cancellationToken);
        var worksheetBytes = JsonSerializer.SerializeToUtf8Bytes(support.Worksheet, IntegrityJson);
        var worksheetSha = ExternalFilingHandoffArtifactBuilder.ComputeSha256(worksheetBytes);
        var tax = support.FilingSupport;
        var facts = new RevenueCt1SupportHandoffFacts(
            period.Company.LegalName,
            period.Company.TaxReference ?? string.Empty,
            period.PeriodStart,
            period.PeriodEnd,
            ExternalFilingHandoffArtifactBuilder.Ct1SupportOutputKind,
            false,
            tax.CalculationSha256,
            worksheetSha,
            package.IxbrlSha256,
            package.ExternalValidationResponseSha256,
            package.ExternalValidationReference,
            tax.CurrentTotalTaxForPaymentSupport,
            tax.PreliminaryTaxPaymentsRecorded,
            Math.Max(0m, tax.CurrentTotalTaxForPaymentSupport - tax.TaxPaymentsRecorded),
            tax.FilingSupportReady ? "ReadyForQualifiedReview" : "Blocked",
            tax.BlockingReasons,
            input.ManualCt1CompletionItems);
        var fields = BuildRevenueFields(period, package, facts, input.UnsupportedSectionsReviewed);
        var attachments = new List<ExternalFilingAttachment>
        {
            new("ct1-support", "ct1-support.json", "application/json", worksheetBytes.Length, worksheetSha, "Retained corporation-tax support worksheet")
        };
        if (package.IxbrlArtifact is { Length: > 0 } && package.IxbrlSha256 is { Length: 64 })
            attachments.Add(new("ixbrl", "revenue-ixbrl.xhtml", "application/xhtml+xml", package.IxbrlArtifact.Length, package.IxbrlSha256, "Retained Revenue iXBRL artifact"));
        var request = BuildRequest(
            tenantId,
            companyId,
            period,
            ExternalFilingWorkflow.RevenueCt1Support,
            actor,
            authority,
            package.ApprovedArtifactManifestSha256!,
            candidate,
            null,
            facts,
            fields,
            attachments);
        var built = await BuildSnapshotAsync(request, input.SupersedesSnapshotId, input.AmendmentReason, cancellationToken);
        var entity = SnapshotEntity(built, authority.Id);
        if (built.Document.SupersedesSnapshotId is { } predecessorId)
        {
            entity.SupersedesSnapshotRecordId = (await repository.SnapshotAsync(companyId, periodId, predecessorId, cancellationToken))?.Id
                ?? throw new BusinessRuleException("The exact predecessor snapshot record is unavailable.");
        }
        repository.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await LogSnapshotAsync(entity, actor, tenantId, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new SnapshotResponse(built.Document, built.ArtifactSha256);
    }

    public async Task<OutcomeResponse> RecordOutcomeAsync(
        int companyId,
        int periodId,
        Guid snapshotId,
        OutcomeCommand command,
        ExternalFilingActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        var tenantId = await RequiredTenantPeriodAsync(companyId, periodId, cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var snapshot = await repository.SnapshotAsync(companyId, periodId, snapshotId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Snapshot {snapshotId} not found");
        var workflow = Enum.Parse<ExternalFilingWorkflow>(snapshot.Workflow);
        await repository.AcquireWorkflowLockAsync(companyId, workflow, cancellationToken);
        var latestSnapshot = await repository.LatestSnapshotAsync(companyId, periodId, workflow, cancellationToken);
        if (latestSnapshot?.Id != snapshot.Id && command.Outcome != ExternalFilingOutcomeKind.SupersededByAmendment)
            throw new BusinessRuleException("Only the latest immutable handoff snapshot can advance external workflow state.");
        var previous = await repository.LatestOutcomeAsync(snapshot.Id, cancellationToken);
        var parsed = ToBuild(snapshot);
        ExternalFilingHandoffSnapshot? successorEntity = null;
        ExternalFilingHandoffBuild? successor = null;
        if (command.SupersedingSnapshotId is { } successorId)
        {
            successorEntity = await repository.SnapshotAsync(companyId, periodId, successorId, cancellationToken)
                ?? throw new ResourceNotFoundException($"Superseding snapshot {successorId} not found");
            successor = ToBuild(successorEntity);
        }
        var evidenceHash = command.EvidenceArtifact is { Length: > 0 }
            ? ExternalFilingHandoffArtifactBuilder.ComputeSha256(command.EvidenceArtifact)
            : null;
        if (evidenceHash is not null && !string.Equals(evidenceHash, command.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The external outcome evidence bytes do not match their supplied SHA-256.");
        var input = new ExternalFilingOutcomeInput(
            command.Outcome,
            snapshot.SnapshotId,
            snapshot.ArtifactSha256,
            command.ExternalReference,
            command.ExternalOccurredAtUtc,
            command.Reason,
            command.CorrectionDeadlineUtc,
            command.EvidenceReference,
            command.EvidenceSha256,
            successorEntity?.SnapshotId,
            successorEntity?.ArtifactSha256);
        var now = DateTime.UtcNow;
        ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
            parsed,
            input,
            previous is null ? null : Enum.Parse<ExternalFilingOutcomeKind>(previous.Outcome),
            now,
            successor);
        var entity = new ExternalFilingOutcomeEvent
        {
            TenantId = tenantId,
            CompanyId = companyId,
            PeriodId = periodId,
            SnapshotRecordId = snapshot.Id,
            SnapshotId = snapshot.SnapshotId,
            SnapshotArtifactSha256 = snapshot.ArtifactSha256,
            Sequence = (previous?.Sequence ?? 0) + 1,
            Outcome = command.Outcome.ToString(),
            ExternalReference = Optional(command.ExternalReference),
            ExternalOccurredAtUtc = command.ExternalOccurredAtUtc,
            Reason = Optional(command.Reason),
            CorrectionDeadlineUtc = command.CorrectionDeadlineUtc,
            EvidenceReference = Optional(command.EvidenceReference),
            EvidenceArtifact = command.EvidenceArtifact?.ToArray(),
            EvidenceSha256 = evidenceHash,
            SupersedingSnapshotRecordId = successorEntity?.Id,
            SupersedingSnapshotId = successorEntity?.SnapshotId,
            SupersedingSnapshotArtifactSha256 = successorEntity?.ArtifactSha256,
            RecordedByUserId = actor.UserId,
            RecordedByDisplayName = actor.DisplayName,
            RecordedByRole = actor.Role,
            RecordedAtUtc = now,
            EventSha256 = string.Empty
        };
        entity.EventSha256 = HashObject(OutcomeIntegrityProjection(entity));
        repository.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(ExternalFilingOutcomeEvent),
            periodId,
            AuditEventCodes.ExternalFilingOutcomeRecorded,
            previous is null ? null : new { previous.Id, previous.Sequence, previous.Outcome },
            new { entity.Id, entity.Sequence, entity.Outcome, entity.SnapshotId, entity.SnapshotArtifactSha256, entity.SupersedingSnapshotId, entity.SupersedingSnapshotArtifactSha256, entity.EventSha256 },
            actor.UserId,
            tenantId,
            actorDisplayName: actor.DisplayName,
            cancellationToken: cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return ToOutcomeResponse(entity);
    }

    public async Task<(byte[] Bytes, string Sha256)> GetArtifactAsync(
        int companyId,
        int periodId,
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        await RequiredTenantPeriodAsync(companyId, periodId, cancellationToken);
        var snapshot = await repository.SnapshotAsync(companyId, periodId, snapshotId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Snapshot {snapshotId} not found");
        _ = ExternalFilingHandoffArtifactBuilder.ParseRetainedArtifact(snapshot.ArtifactBytes, snapshot.ArtifactSha256);
        return (snapshot.ArtifactBytes.ToArray(), snapshot.ArtifactSha256);
    }

    public async Task AssertCurrentEvidenceForTransitionAsync(
        int companyId,
        int periodId,
        ExternalFilingWorkflow workflow,
        ExternalFilingOutcomeKind requiredOutcome,
        string? externalReference,
        DateTime atUtc,
        CancellationToken cancellationToken = default)
    {
        var candidate = releaseIdentity.GetRequiredCandidate();
        var authority = await RequireCurrentAuthorityAsync(companyId, workflow, candidate, atUtc, cancellationToken);
        var snapshot = await repository.LatestSnapshotAsync(companyId, periodId, workflow, cancellationToken)
            ?? throw new FilingReleaseBlockedException("Create and retain an immutable external filing handoff snapshot first.");
        var built = ToBuild(snapshot);
        if (!built.Document.ReadyForManualHandoff
            || !string.Equals(snapshot.ReleaseCandidate, candidate, StringComparison.Ordinal)
            || snapshot.AuthorityId != authority.Id
            || !string.Equals(snapshot.AuthorityEvidenceSha256, authority.AuthorityEvidenceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new FilingReleaseBlockedException("The latest external filing handoff snapshot is stale, blocked, or bound to superseded authority.");
        }
        var outcome = await repository.LatestOutcomeAsync(snapshot.Id, cancellationToken)
            ?? throw new FilingReleaseBlockedException("Record manual handoff readiness and the genuine external outcome first.");
        if (!string.Equals(outcome.Outcome, requiredOutcome.ToString(), StringComparison.Ordinal))
            throw new FilingReleaseBlockedException($"The latest immutable handoff outcome must be {requiredOutcome}.");
        if (externalReference is not null
            && !string.Equals(outcome.ExternalReference?.Trim(), externalReference.Trim(), StringComparison.Ordinal))
        {
            throw new FilingReleaseBlockedException("The filing reference does not match the exact retained external outcome.");
        }
    }

    private async Task<ExternalFilingHandoffBuild> BuildSnapshotAsync(
        ExternalFilingHandoffBuildRequest request,
        Guid? supersedesSnapshotId,
        string? amendmentReason,
        CancellationToken cancellationToken)
    {
        if (supersedesSnapshotId is null)
        {
            var latest = await repository.LatestSnapshotAsync(request.CompanyId, request.PeriodId, request.Workflow, cancellationToken);
            if (latest is not null)
                throw new BusinessRuleException("An existing handoff chain must be continued through a linked amendment.");
            return ExternalFilingHandoffArtifactBuilder.BuildInitial(request);
        }
        var predecessor = await repository.SnapshotAsync(request.CompanyId, request.PeriodId, supersedesSnapshotId.Value, cancellationToken)
            ?? throw new ResourceNotFoundException($"Predecessor snapshot {supersedesSnapshotId} not found");
        var latestSnapshot = await repository.LatestSnapshotAsync(request.CompanyId, request.PeriodId, request.Workflow, cancellationToken);
        if (latestSnapshot?.Id != predecessor.Id)
            throw new BusinessRuleException("An amendment must link the current head of the immutable snapshot chain.");
        return ExternalFilingHandoffArtifactBuilder.BuildAmendment(request, ToBuild(predecessor), amendmentReason ?? string.Empty);
    }

    private ExternalFilingHandoffBuildRequest BuildRequest(
        int tenantId,
        int companyId,
        AccountingPeriod period,
        ExternalFilingWorkflow workflow,
        ExternalFilingActor actor,
        FilingAuthorityEngagement authority,
        string qualifiedManifest,
        string candidate,
        B1ManualHandoffFacts? cro,
        RevenueCt1SupportHandoffFacts? revenue,
        IReadOnlyList<ExternalHandoffField> fields,
        IReadOnlyList<ExternalFilingAttachment> attachments) => new(
            Guid.NewGuid(),
            tenantId,
            companyId,
            period.Id,
            workflow,
            period.PeriodStart,
            period.PeriodEnd,
            DateTime.UtcNow,
            actor,
            ToAuthoritySnapshot(authority),
            qualifiedManifest,
            candidate,
            cro,
            revenue,
            fields,
            attachments);

    private async Task<FilingAuthorityEngagement> RequireCurrentAuthorityAsync(
        int companyId,
        ExternalFilingWorkflow workflow,
        string candidate,
        DateTime atUtc,
        CancellationToken cancellationToken)
    {
        var authority = await repository.LatestAuthorityAsync(companyId, workflow, cancellationToken)
            ?? throw new FilingReleaseBlockedException($"Record current {workflow} authority and engagement evidence first.");
        var evidenceHash = authority.AuthorityEvidenceArtifact is { Length: > 0 }
            ? ExternalFilingHandoffArtifactBuilder.ComputeSha256(authority.AuthorityEvidenceArtifact)
            : null;
        if (authority.Status != ExternalFilingAuthorityStatus.Active.ToString()
            || authority.EffectiveFromUtc > atUtc
            || authority.EffectiveUntilUtc is { } until && until < atUtc
            || authority.RevokedAtUtc is { } revoked && revoked <= atUtc
            || !string.Equals(authority.ReleaseCandidate, candidate, StringComparison.Ordinal)
            || !string.Equals(evidenceHash, authority.AuthorityEvidenceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new FilingReleaseBlockedException($"The latest {workflow} authority is expired, revoked, stale, or fails retained evidence integrity.");
        }
        return authority;
    }

    private static ExternalFilingHandoffSnapshot SnapshotEntity(ExternalFilingHandoffBuild built, long authorityId) => new()
    {
        SnapshotId = built.Document.SnapshotId,
        TenantId = built.Document.TenantId,
        CompanyId = built.Document.CompanyId,
        PeriodId = built.Document.PeriodId,
        Workflow = built.Document.Workflow.ToString(),
        Version = built.Document.Version,
        SupersedesSnapshotId = built.Document.SupersedesSnapshotId,
        SupersedesArtifactSha256 = built.Document.SupersedesArtifactSha256,
        AmendmentReason = built.Document.AmendmentReason,
        SchemaVersion = built.Document.SchemaVersion,
        ArtifactBytes = built.ArtifactBytes.ToArray(),
        ArtifactSha256 = built.ArtifactSha256,
        SourceFingerprintSha256 = built.Document.SourceFingerprintSha256,
        AuthorityId = authorityId,
        AuthorityEvidenceSha256 = built.Document.Authority.AuthorityEvidenceSha256,
        QualifiedReviewManifestSha256 = built.Document.QualifiedReviewManifestSha256,
        ReleaseCandidate = built.Document.ReleaseCandidate,
        DirectSubmissionSupported = false,
        IsCompleteExternalReturn = false,
        ReadyForManualHandoff = built.Document.ReadyForManualHandoff,
        PreparedByUserId = built.Document.PreparedBy.UserId,
        PreparedByDisplayName = built.Document.PreparedBy.DisplayName,
        PreparedByRole = built.Document.PreparedBy.Role,
        PreparedAtUtc = built.Document.PreparedAtUtc
    };

    private static ExternalFilingHandoffBuild ToBuild(ExternalFilingHandoffSnapshot snapshot)
    {
        var document = ExternalFilingHandoffArtifactBuilder.ParseRetainedArtifact(snapshot.ArtifactBytes, snapshot.ArtifactSha256);
        if (document.SnapshotId != snapshot.SnapshotId
            || document.TenantId != snapshot.TenantId
            || document.CompanyId != snapshot.CompanyId
            || document.PeriodId != snapshot.PeriodId
            || document.Workflow.ToString() != snapshot.Workflow
            || document.Version != snapshot.Version
            || document.SupersedesSnapshotId != snapshot.SupersedesSnapshotId
            || !string.Equals(document.SupersedesArtifactSha256, snapshot.SupersedesArtifactSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(document.SourceFingerprintSha256, snapshot.SourceFingerprintSha256, StringComparison.OrdinalIgnoreCase)
            || document.DirectSubmissionSupported
            || document.IsCompleteExternalReturn)
        {
            throw new BusinessRuleException("Retained handoff artifact scalar identity does not match its immutable database envelope.");
        }
        return new ExternalFilingHandoffBuild(document, snapshot.ArtifactBytes.ToArray(), snapshot.ArtifactSha256);
    }

    private static SnapshotResponse ToSnapshotResponse(ExternalFilingHandoffSnapshot snapshot)
    {
        var built = ToBuild(snapshot);
        return new SnapshotResponse(built.Document, built.ArtifactSha256);
    }

    private static ExternalFilingAuthoritySnapshot ToAuthoritySnapshot(FilingAuthorityEngagement authority) => new(
        authority.Id,
        authority.TenantId,
        authority.CompanyId,
        Enum.Parse<ExternalFilingWorkflow>(authority.Workflow),
        Enum.Parse<ExternalFilingAuthorityKind>(authority.Kind),
        Enum.Parse<ExternalFilingAuthorityStatus>(authority.Status),
        authority.LegalName,
        authority.PracticeName,
        authority.MaskedPresenterOrTain,
        authority.AuthorityScope,
        authority.EngagementReference,
        authority.ExternalAuthorityReference,
        authority.EffectiveFromUtc,
        authority.EffectiveUntilUtc,
        authority.RevokedAtUtc,
        authority.AuthorityEvidenceSha256,
        authority.EvidenceMediaType,
        authority.EvidenceFileName,
        new ExternalFilingActor(authority.ReviewedByUserId, authority.ReviewedByDisplayName, authority.ReviewedByRole),
        authority.ReviewedAtUtc,
        authority.ReleaseCandidate);

    private static OutcomeResponse ToOutcomeResponse(ExternalFilingOutcomeEvent item) => new(
        item.Id,
        item.SnapshotId,
        item.SnapshotArtifactSha256,
        Enum.Parse<ExternalFilingOutcomeKind>(item.Outcome),
        item.ExternalReference,
        item.ExternalOccurredAtUtc,
        item.Reason,
        item.CorrectionDeadlineUtc,
        item.EvidenceReference,
        item.EvidenceSha256,
        item.SupersedingSnapshotId,
        item.SupersedingSnapshotArtifactSha256,
        new ExternalFilingActor(item.RecordedByUserId, item.RecordedByDisplayName, item.RecordedByRole),
        item.RecordedAtUtc);

    private async Task LogSnapshotAsync(
        ExternalFilingHandoffSnapshot snapshot,
        ExternalFilingActor actor,
        int tenantId,
        CancellationToken cancellationToken) =>
        await audit.LogAsync(
            snapshot.CompanyId,
            snapshot.PeriodId,
            nameof(ExternalFilingHandoffSnapshot),
            snapshot.PeriodId,
            AuditEventCodes.ExternalFilingHandoffSnapshotCreated,
            null,
            new { snapshot.Id, snapshot.SnapshotId, snapshot.Workflow, snapshot.Version, snapshot.SupersedesSnapshotId, snapshot.SupersedesArtifactSha256, snapshot.ArtifactSha256, snapshot.SourceFingerprintSha256, snapshot.AuthorityId, snapshot.AuthorityEvidenceSha256, snapshot.ReadyForManualHandoff },
            actor.UserId,
            tenantId,
            actorDisplayName: actor.DisplayName,
            cancellationToken: cancellationToken);

    private async Task<int> RequiredTenantPeriodAsync(int companyId, int periodId, CancellationToken cancellationToken)
    {
        var tenantId = await db.AccountingPeriods
            .Where(item => item.Id == periodId && item.CompanyId == companyId)
            .Select(item => item.Company.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        return tenantId ?? throw new ResourceNotFoundException($"Period {periodId} not found");
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static IReadOnlyList<ExternalHandoffField> BuildCroFields(
        AccountingPeriod period,
        CroFilingPackage package,
        CroSnapshotInput input,
        IReadOnlyList<B1OfficerHandoff> officers,
        IReadOnlyList<B1ShareClassHandoff> shareClasses)
    {
        var company = period.Company;
        var hasDirector = officers.Any(item => item.Role == OfficerRole.Director.ToString() && item.ResignedDate is null);
        var hasSecretary = officers.Any(item => (item.Role is nameof(OfficerRole.Secretary) or nameof(OfficerRole.CompanySecretary)) && item.ResignedDate is null);
        var protectedComplete = officers.Count > 0 && input.Officers.All(item => item.ProtectedIdentifierEntryConfirmed);
        var directorshipsComplete = officers.Count > 0 && officers.All(item => !string.IsNullOrWhiteSpace(item.OtherDirectorshipsEvidenceReference));
        var allotmentsComplete = input.Allotments.Count > 0 || input.NoAllotmentsInReturnPeriodConfirmed;
        return
        [
            Field("b1.company.cro-number", "Company", "CRO number", company.CroNumber, "Company legal identity"),
            Field("b1.company.legal-name", "Company", "Company legal name", company.LegalName, "Company legal identity"),
            Field("b1.company.type", "Company", "Company type", company.CompanyType.ToString(), "Company legal identity"),
            Field("b1.return.annual-return-date", "Return", "Annual Return Date", company.AnnualReturnDate?.ToString("yyyy-MM-dd"), "Confirmed CRO ARD evidence"),
            Field("b1.return.made-up-to-date", "Return", "Made-up-to date", input.MadeUpToDate.ToString("yyyy-MM-dd"), "B1 preparation input"),
            Field("b1.return.ard-election", "Return", "ARD retain/adopt election", input.AnnualReturnDateElection, "B1 preparation input"),
            Field("b1.office.registered-office", "Registered office", "Registered office", AddressDisplay(CompanyAddress(company)), "Company registered-office evidence"),
            Field("b1.accounts.financial-year-start", "Financial statements", "Financial year start", period.PeriodStart.ToString("yyyy-MM-dd"), "Accounting period"),
            Field("b1.accounts.financial-year-end", "Financial statements", "Financial year end", period.PeriodEnd.ToString("yyyy-MM-dd"), "Accounting period"),
            Field("b1.accounts.annexed-or-exemption", "Financial statements", "Financial statements annexed / first-return exemption", input.FinancialStatementsAnnexed ? "Financial statements annexed" : period.IsFirstYear ? "First-return exemption asserted" : null, "B1 preparation input"),
            Field("b1.accounts.audit-basis", "Financial statements", "Audit basis", input.AuditExemptionClaimed ? "Audit exemption claimed" : input.AuditorReference, "Filing-regime and auditor evidence"),
            Field("b1.officers.directors", "Officers", "Acting directors", hasDirector ? string.Join(", ", officers.Where(item => item.Role == nameof(OfficerRole.Director)).Select(item => $"{item.FirstName} {item.LastName}")) : null, "Company officer register"),
            Field("b1.officers.secretary", "Officers", "Acting secretary", hasSecretary ? string.Join(", ", officers.Where(item => item.Role is nameof(OfficerRole.Secretary) or nameof(OfficerRole.CompanySecretary)).Select(item => $"{item.FirstName} {item.LastName}")) : null, "Company officer register"),
            Field("b1.officers.protected-identity-entry", "Officers", "Protected director identity entry", protectedComplete ? "Confirmed entered in CORE; protected identifier not retained" : null, "Protected identity evidence references", isProtected: true),
            Field("b1.officers.other-directorships", "Officers", "Other directorship declarations", directorshipsComplete ? "Retained for every listed officer" : null, "Officer declaration evidence"),
            Field("b1.capital.share-classes", "Share capital", "Share classes", shareClasses.Count > 0 ? $"{shareClasses.Count} retained class row(s)" : null, "Company share-capital register"),
            Field("b1.members.shareholders", "Members", "Shareholder list", input.Shareholders.Count > 0 ? $"{input.Shareholders.Count} retained member row(s)" : null, "Register of members evidence"),
            Field("b1.members.allotments", "Members", "Allotments", allotmentsComplete ? input.Allotments.Count == 0 ? "No allotments confirmed for return period" : $"{input.Allotments.Count} retained allotment row(s)" : null, "Allotment/B5 evidence"),
            Field("b1.donations.political", "Disclosures", "Political donations disclosure", string.IsNullOrWhiteSpace(input.PoliticalDonationsEvidenceReference) ? null : input.PoliticalDonationsOverThreshold ? $"Over threshold: {input.PoliticalDonationsAmount}" : "No donations over threshold", input.PoliticalDonationsEvidenceReference),
            Field("b1.presenter.identity", "Presenter", "Presenter identity", package.ApprovedBy, "Approved CRO package"),
            Field("b1.presenter.authority", "Presenter", "Presenter authority", "Current authority bound by ID and SHA-256", "Filing authority engagement ledger"),
            Field("b1.signing.director", "Signing", "Director signatory", package.SignedByDirector, "Retained CRO signature evidence"),
            Field("b1.signing.secretary", "Signing", "Secretary signatory", package.SignedBySecretary, "Retained CRO signature evidence"),
            Field("b1.attachments.accounts-pdf", "Attachments", "Accounts PDF", package.AccountsPdfSha256, "Retained CRO accounts artifact"),
            Field("b1.attachments.signature-page", "Attachments", "Signature page", package.SignaturePageSha256, "Retained CRO signature artifact")
        ];
    }

    private static IReadOnlyList<ExternalHandoffField> BuildRevenueFields(
        AccountingPeriod period,
        RevenueFilingPackage package,
        RevenueCt1SupportHandoffFacts facts,
        bool unsupportedReviewed) =>
    [
        Field("ct1.company.tax-reference", "Company", "Tax reference", facts.TaxReference, "Company tax identity"),
        Field("ct1.period.start", "Period", "Period start", period.PeriodStart.ToString("yyyy-MM-dd"), "Accounting period"),
        Field("ct1.period.end", "Period", "Period end", period.PeriodEnd.ToString("yyyy-MM-dd"), "Accounting period"),
        Field("ct1.support.output-kind", "Tax support", "Output kind", facts.OutputKind, "Corporation-tax support contract"),
        Field("ct1.support.calculation-hash", "Tax support", "Calculation SHA-256", facts.CalculationSha256, "Corporation-tax computation"),
        Field("ct1.support.worksheet-hash", "Tax support", "Worksheet SHA-256", facts.WorksheetArtifactSha256, "Corporation-tax support worksheet"),
        Field("ct1.support.tax-due", "Tax support", "Corporation tax due", facts.CorporationTaxDue.ToString("0.00"), "Corporation-tax support worksheet"),
        Field("ct1.support.preliminary-tax", "Tax support", "Preliminary tax paid", facts.PreliminaryTaxPaid.ToString("0.00"), "Corporation-tax payment evidence"),
        Field("ct1.support.balance-due", "Tax support", "Balance due", facts.BalanceDue.ToString("0.00"), "Corporation-tax support worksheet"),
        Field("ct1.ixbrl.artifact-hash", "iXBRL", "iXBRL artifact SHA-256", package.IxbrlSha256, "Retained Revenue iXBRL artifact"),
        Field("ct1.ixbrl.external-validation", "iXBRL", "External validation evidence", package.ExternalValidationResponseSha256, package.ExternalValidationReference ?? "Trusted external validator evidence"),
        Field("ct1.agent.tain", "ROS authority", "Masked TAIN", "Bound in authority record", "Filing authority engagement ledger"),
        Field("ct1.agent.engagement", "ROS authority", "Agent engagement", "Current engagement bound by SHA-256", "Filing authority engagement ledger"),
        Field("ct1.manual.unsupported-sections-reviewed", "Manual CT1", "Unsupported CT1 sections", unsupportedReviewed && facts.ManualCt1CompletionItems.Count == 0 ? "Qualified accountant confirmed live ROS completion" : null, "Qualified accountant live ROS review")
    ];

    private static ExternalHandoffField Field(
        string code,
        string section,
        string label,
        string? value,
        string source,
        bool isProtected = false) => new(
            code,
            section,
            label,
            Optional(value),
            string.IsNullOrWhiteSpace(value)
                ? isProtected ? ExternalHandoffFieldStatus.ProtectedManualEntry : ExternalHandoffFieldStatus.Missing
                : ExternalHandoffFieldStatus.Complete,
            source,
            string.IsNullOrWhiteSpace(value) ? $"{label} is missing or requires review." : null,
            isProtected);

    private static ExternalHandoffAddress CompanyAddress(Company company) => new(
        company.RegisteredOfficeAddress1,
        company.RegisteredOfficeAddress2,
        company.RegisteredOfficeCity,
        company.RegisteredOfficeCounty,
        company.RegisteredOfficeEircode,
        null,
        null);

    private static string AddressDisplay(ExternalHandoffAddress address) => string.Join(", ", new[]
    {
        address.Line1, address.Line2, address.Line3, address.Line4, address.Line5, address.Line6, address.Line7
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static void ValidateAuthorityInput(AuthorityInput input)
    {
        if (input.EvidenceArtifact is not { Length: > 0 })
            throw new BusinessRuleException("Retained authority evidence bytes are required.");
        RequireSha(input.EvidenceSha256, "Authority evidence SHA-256");
        var actual = ExternalFilingHandoffArtifactBuilder.ComputeSha256(input.EvidenceArtifact);
        if (!string.Equals(actual, input.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Authority evidence bytes do not match their SHA-256.");
        if (input.EffectiveFromUtc.Kind != DateTimeKind.Utc
            || input.EffectiveUntilUtc is { Kind: not DateTimeKind.Utc }
            || input.EffectiveUntilUtc is { } until && until <= input.EffectiveFromUtc)
            throw new BusinessRuleException("Authority effective dates must be a valid UTC range.");
        if (input.Workflow == ExternalFilingWorkflow.CroB1
            && input.Kind is not ExternalFilingAuthorityKind.CroPresenter and not ExternalFilingAuthorityKind.CroElectronicFilingAgent)
            throw new BusinessRuleException("CRO handoff authority must identify a presenter or electronic filing agent.");
        if (input.Workflow == ExternalFilingWorkflow.RevenueCt1Support && input.Kind != ExternalFilingAuthorityKind.RevenueRosAgent)
            throw new BusinessRuleException("Revenue handoff authority must identify a ROS agent.");
        if (string.IsNullOrWhiteSpace(input.LegalName)
            || string.IsNullOrWhiteSpace(input.AuthorityScope)
            || string.IsNullOrWhiteSpace(input.EngagementReference)
            || string.IsNullOrWhiteSpace(input.ExternalAuthorityReference)
            || string.IsNullOrWhiteSpace(input.EvidenceMediaType)
            || string.IsNullOrWhiteSpace(input.EvidenceFileName))
            throw new BusinessRuleException("Authority identity, scope, references and retained evidence metadata are required.");
        ExternalFilingHandoffArtifactBuilder.AssertOpaqueReference(input.EngagementReference, "Engagement reference");
        ExternalFilingHandoffArtifactBuilder.AssertOpaqueReference(input.ExternalAuthorityReference, "External authority reference");
        if (!string.IsNullOrWhiteSpace(input.MaskedPresenterOrTain)
            && input.MaskedPresenterOrTain.Any(char.IsDigit)
            && !input.MaskedPresenterOrTain.Contains('*', StringComparison.Ordinal))
            throw new BusinessRuleException("Presenter/TAIN identifiers exposed in authority responses must be masked.");
    }

    private static void ValidateActor(ExternalFilingActor actor)
    {
        if (string.IsNullOrWhiteSpace(actor.UserId) || string.IsNullOrWhiteSpace(actor.DisplayName) || string.IsNullOrWhiteSpace(actor.Role))
            throw new BusinessRuleException("A named authenticated actor and role are required.");
    }

    private static void RequireSha(string? value, string label)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new BusinessRuleException($"{label} must be a 64-character hexadecimal SHA-256.");
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string HashObject(object value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, IntegrityJson)));

    private static object AuthorityIntegrityProjection(FilingAuthorityEngagement item) => new
    {
        item.TenantId, item.CompanyId, item.Version, item.SupersedesAuthorityId, item.Workflow, item.Kind, item.Status,
        item.LegalName, item.PracticeName, item.MaskedPresenterOrTain, item.AuthorityScope, item.EngagementReference,
        item.ExternalAuthorityReference, item.EffectiveFromUtc, item.EffectiveUntilUtc, item.RevokedAtUtc,
        item.AuthorityEvidenceSha256, item.EvidenceMediaType, item.EvidenceFileName, item.ReviewedByUserId,
        item.ReviewedByDisplayName, item.ReviewedByRole, item.ReviewedAtUtc, item.ReleaseCandidate,
        item.CreatedByUserId, item.CreatedByDisplayName, item.CreatedByRole, item.CreatedAtUtc
    };

    private static object OutcomeIntegrityProjection(ExternalFilingOutcomeEvent item) => new
    {
        item.TenantId, item.CompanyId, item.PeriodId, item.SnapshotRecordId, item.SnapshotId,
        item.SnapshotArtifactSha256, item.Sequence, item.Outcome, item.ExternalReference, item.ExternalOccurredAtUtc,
        item.Reason, item.CorrectionDeadlineUtc, item.EvidenceReference, item.EvidenceSha256,
        item.SupersedingSnapshotRecordId, item.SupersedingSnapshotId, item.SupersedingSnapshotArtifactSha256,
        item.RecordedByUserId, item.RecordedByDisplayName, item.RecordedByRole, item.RecordedAtUtc
    };

    private static IReadOnlyList<string> SourceGaps(
        IReadOnlyList<SnapshotResponse> snapshots,
        IReadOnlyList<FilingAuthorityEngagement> authorities)
    {
        var gaps = new List<string>();
        if (!authorities.Any(item => item.Workflow == ExternalFilingWorkflow.CroB1.ToString() && item.Status == ExternalFilingAuthorityStatus.Active.ToString()))
            gaps.Add("Current CRO presenter/EFA authority and B77 evidence are not recorded.");
        if (!authorities.Any(item => item.Workflow == ExternalFilingWorkflow.RevenueCt1Support.ToString() && item.Status == ExternalFilingAuthorityStatus.Active.ToString()))
            gaps.Add("Current ROS agent/TAIN engagement evidence is not recorded.");
        if (!snapshots.Any(item => item.Document.Workflow == ExternalFilingWorkflow.CroB1))
            gaps.Add("No immutable field-by-field B1/shareholder/allotment snapshot has been retained.");
        if (!snapshots.Any(item => item.Document.Workflow == ExternalFilingWorkflow.RevenueCt1Support))
            gaps.Add("No immutable CT1 support/iXBRL handoff snapshot has been retained.");
        gaps.Add("Raw PPSN/IPN/RBO identifiers remain protected manual CORE entries and are never retained in general handoff JSON.");
        gaps.Add("Bounded corporation-tax support is never a complete CT1; unsupported panels require qualified-accountant completion in ROS.");
        return gaps.Distinct(StringComparer.Ordinal).ToList();
    }
}
