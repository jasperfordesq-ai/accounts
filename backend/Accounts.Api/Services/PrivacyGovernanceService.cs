using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed partial class PrivacyGovernanceService
{
    public const string CompaniesActRetentionBasis =
        "Companies Act 2014 section 285 and applicable Revenue record-keeping obligations";

    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AccountsDbContext db;
    private readonly PrivacyGovernanceConfig config;
    private readonly TimeProvider timeProvider;
    private readonly byte[] identifierHmacKey;

    public PrivacyGovernanceService(
        AccountsDbContext db,
        IOptions<PrivacyGovernanceConfig> config,
        IOptions<AuthSessionConfig> authSession,
        TimeProvider timeProvider)
    {
        this.db = db;
        this.config = config.Value;
        this.timeProvider = timeProvider;
        var rootKey = AuthSessionKey.DecodeRequired(authSession.Value.SigningKey);
        identifierHmacKey = HMACSHA256.HashData(
            rootKey,
            Encoding.UTF8.GetBytes("accounts/privacy/login-identifier-fingerprint/v1"));
        CryptographicOperations.ZeroMemory(rootKey);
    }

    public async Task RecordLoginAttemptAsync(
        string? attemptedIdentifier,
        int? tenantId,
        int? userId,
        string outcomeCode,
        string reasonCode,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIdentifier(attemptedIdentifier);
        var now = UtcNow();
        db.Set<LoginSecurityEvent>().Add(new LoginSecurityEvent
        {
            TenantId = tenantId,
            UserId = userId,
            IdentifierFingerprint = Fingerprint(normalized ?? "missing"),
            OutcomeCode = RequiredCode(outcomeCode, nameof(outcomeCode)),
            ReasonCode = RequiredCode(reasonCode, nameof(reasonCode)),
            CorrelationId = SafeCorrelationId(correlationId),
            OccurredAtUtc = now,
            ExpiresAtUtc = now.AddDays(config.LoginSecurityEventRetentionDays)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SubjectAccessArtifact> BuildSubjectAccessExportAsync(
        int tenantId,
        int subjectUserId,
        int requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var subject = await db.UserAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => user.TenantId == tenantId && user.Id == subjectUserId)
            .Select(user => new
            {
                user.Id,
                user.TenantId,
                user.Email,
                user.DisplayName,
                user.Role,
                user.IsActive,
                user.MustChangePassword,
                user.CreatedAt,
                user.UpdatedAt,
                user.LastLoginAt,
                user.InviteAcceptedAtUtc,
                user.DeactivatedAtUtc,
                user.OffboardedAtUtc,
                user.FailedLoginCount,
                user.LastFailedLoginAt,
                user.LockedUntilUtc
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The subject user was not found in this tenant.");

        var actorKeys = new[] { $"user:{subjectUserId}", subject.Email.Trim().ToLowerInvariant() };
        var assignments = await db.UserCompanyAccesses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(access => access.UserId == subjectUserId && access.User.TenantId == tenantId)
            .OrderBy(access => access.CompanyId)
            .Select(access => new { access.Id, access.CompanyId })
            .ToListAsync(cancellationToken);
        var lifecycle = await db.Set<UserLifecycleEvent>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && (item.TargetUserId == subjectUserId || item.ActorUserId == subjectUserId))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.TargetUserId,
                item.ActorUserId,
                item.EventType,
                item.DetailsJson,
                item.OccurredAtUtc
            })
            .ToListAsync(cancellationToken);
        var securityEvents = await db.Set<LoginSecurityEvent>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.UserId == subjectUserId)
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.OutcomeCode,
                item.ReasonCode,
                item.CorrelationId,
                item.OccurredAtUtc,
                item.ExpiresAtUtc
            })
            .ToListAsync(cancellationToken);
        var audit = await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.UserId != null && actorKeys.Contains(item.UserId))
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.CompanyId,
                item.PeriodId,
                item.EntityType,
                item.EntityId,
                item.Action,
                item.OldValueJson,
                item.NewValueJson,
                item.RequestId,
                item.Timestamp,
                item.PreviousIntegrityHash,
                item.IntegrityHash
            })
            .ToListAsync(cancellationToken);
        var professionalEvidence = await LocateProfessionalEvidenceAsync(tenantId, actorKeys, cancellationToken);
        var previousRequests = await db.Set<PrivacySubjectRequest>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SubjectUserId == subjectUserId)
            .OrderBy(item => item.RequestedAtUtc)
            .Select(item => new
            {
                item.Id,
                item.RequestKind,
                item.State,
                item.RequestedAtUtc,
                item.DecidedAtUtc,
                item.StatutoryRetentionOverrideApplied,
                item.StatutoryRetainUntilUtc
            })
            .ToListAsync(cancellationToken);

        var now = UtcNow();
        var request = new PrivacySubjectRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubjectUserId = subjectUserId,
            RequestKind = PrivacyRequestKinds.AccessExport,
            State = PrivacyRequestStates.Requested,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = now,
            MetadataExpiresAtUtc = now.AddYears(config.SubjectRequestMetadataRetentionYears)
        };
        db.Set<PrivacySubjectRequest>().Add(request);
        await db.SaveChangesAsync(cancellationToken);

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["auditEvents"] = audit.Count,
            ["companyAssignments"] = assignments.Count,
            ["lifecycleEvents"] = lifecycle.Count,
            ["loginSecurityEvents"] = securityEvents.Count,
            ["professionalEvidenceReferences"] = professionalEvidence.Count,
            ["previousPrivacyRequests"] = previousRequests.Count,
            ["userProfile"] = 1
        };
        var artifact = new
        {
            schemaVersion = "privacy-subject-access-v1",
            requestId = request.Id,
            generatedAtUtc = now,
            tenantId,
            subjectUserId,
            controllerReviewRequiredBeforeDisclosure = true,
            omittedCredentialFields = new[]
            {
                "PasswordHash",
                "PasswordSalt",
                "MfaSecret",
                "RecoveryCode",
                "ActionToken",
                "SessionCookie"
            },
            counts,
            userProfile = subject,
            companyAssignments = assignments,
            lifecycleEvents = lifecycle,
            loginSecurityEvents = securityEvents,
            auditActorRecords = audit,
            professionalEvidenceReferences = professionalEvidence
                .OrderBy(item => item.OccurredAtUtc)
                .ThenBy(item => item.RecordType, StringComparer.Ordinal)
                .ThenBy(item => item.RecordId, StringComparer.Ordinal)
                .ToArray(),
            privacyRequests = previousRequests
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, ExportJson);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        request.ExportSha256 = sha256;
        request.ExportByteCount = bytes.LongLength;
        request.LocatedRecordCountsJson = JsonSerializer.Serialize(counts);
        request.State = PrivacyRequestStates.Completed;
        request.DecidedByUserId = requestedByUserId;
        request.DecidedAtUtc = now;
        request.DecisionReason = "Subject access inventory generated for controller review.";
        await db.SaveChangesAsync(cancellationToken);

        return new SubjectAccessArtifact(request.Id, bytes, sha256, counts);
    }

    public async Task<ErasureDecision> ExecuteApprovedErasureAsync(
        int tenantId,
        int subjectUserId,
        int approvedByUserId,
        string decisionReason,
        CancellationToken cancellationToken = default)
    {
        if (subjectUserId == approvedByUserId)
            throw new BusinessRuleException("An Owner cannot approve erasure of their own active identity.");
        var normalizedReason = decisionReason?.Trim();
        if (normalizedReason?.EnumerateRunes().Count() is not (>= 20 and <= 2000))
            throw new BusinessRuleException("The erasure decision reason must contain 20 to 2,000 characters.");

        var subject = await db.UserAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(user => user.TenantId == tenantId && user.Id == subjectUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The subject user was not found in this tenant.");
        var approvingOwner = await db.UserAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(user => user.TenantId == tenantId
                && user.Id == approvedByUserId
                && user.IsActive
                && user.Role == "Owner", cancellationToken);
        if (!approvingOwner)
            throw new UnauthorizedAccessException("Only an active same-tenant Owner may approve erasure.");
        if (subject.Role == "Owner")
        {
            var otherOwners = await db.UserAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(user => user.TenantId == tenantId
                    && user.Id != subjectUserId
                    && user.IsActive
                    && user.Role == "Owner", cancellationToken);
            if (otherOwners == 0)
                throw new BusinessRuleException("The last active Owner cannot be erased or offboarded.");
        }

        var now = UtcNow();
        var legacyEmail = subject.Email.Trim().ToLowerInvariant();
        var actorKeys = new[] { $"user:{subjectUserId}", legacyEmail };
        var retainedAudit = await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.UserId != null && actorKeys.Contains(item.UserId))
            .Select(item => new { item.Id, item.CompanyId, item.PeriodId, item.Action, item.Timestamp })
            .ToListAsync(cancellationToken);
        var retainedProfessionalEvidence = await LocateProfessionalEvidenceAsync(
            tenantId,
            actorKeys,
            cancellationToken);
        var relatedPeriodIds = retainedAudit
            .Where(item => item.PeriodId is not null)
            .Select(item => item.PeriodId!.Value)
            .Concat(retainedProfessionalEvidence.Where(item => item.PeriodId is not null).Select(item => item.PeriodId!.Value))
            .Distinct()
            .ToArray();
        var relatedCompanyIds = retainedAudit
            .Where(item => item.CompanyId is not null)
            .Select(item => item.CompanyId!.Value)
            .Concat(retainedProfessionalEvidence.Where(item => item.CompanyId is not null).Select(item => item.CompanyId!.Value))
            .Distinct()
            .ToArray();
        var latestPeriodEnd = await db.AccountingPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(period => period.Company.TenantId == tenantId
                && (relatedPeriodIds.Contains(period.Id) || relatedCompanyIds.Contains(period.CompanyId)))
            .Select(period => (DateOnly?)period.PeriodEnd)
            .MaxAsync(cancellationToken);
        var retentionYear = Math.Max(latestPeriodEnd?.Year ?? now.Year, now.Year);
        var retainUntil = new DateTime(
            retentionYear + config.StatutoryRecordMinimumYears + 1,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var hasStatutoryEvidence = retainedAudit.Count > 0 || retainedProfessionalEvidence.Count > 0;

        var request = new PrivacySubjectRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubjectUserId = subjectUserId,
            RequestKind = PrivacyRequestKinds.ErasureReview,
            State = !hasStatutoryEvidence
                ? PrivacyRequestStates.Completed
                : PrivacyRequestStates.PartiallyCompletedStatutoryOverride,
            RequestedByUserId = approvedByUserId,
            RequestedAtUtc = now,
            DecidedByUserId = approvedByUserId,
            DecidedAtUtc = now,
            DecisionReason = normalizedReason,
            StatutoryRetentionOverrideApplied = hasStatutoryEvidence,
            StatutoryRetentionLegalBasis = hasStatutoryEvidence ? CompaniesActRetentionBasis : null,
            StatutoryRetainUntilUtc = hasStatutoryEvidence ? retainUntil : null,
            StatutoryRetentionInventoryJson = hasStatutoryEvidence
                ? JsonSerializer.Serialize(new
                {
                    auditRecordIds = retainedAudit.Select(item => item.Id).Order().ToArray(),
                    professionalEvidenceReferences = retainedProfessionalEvidence,
                    companyIds = relatedCompanyIds.Order().ToArray(),
                    periodIds = relatedPeriodIds.Order().ToArray(),
                    preservation = "Hash-chained audit rows are retained unchanged."
                })
                : null,
            LocatedRecordCountsJson = JsonSerializer.Serialize(new SortedDictionary<string, int>
            {
                ["retainedAuditEvents"] = retainedAudit.Count,
                ["retainedProfessionalEvidenceReferences"] = retainedProfessionalEvidence.Count
            }),
            MetadataExpiresAtUtc = hasStatutoryEvidence
                ? retainUntil.AddYears(config.SubjectRequestMetadataRetentionYears)
                : now.AddYears(config.SubjectRequestMetadataRetentionYears)
        };
        db.Set<PrivacySubjectRequest>().Add(request);

        var accesses = await db.UserCompanyAccesses
            .IgnoreQueryFilters()
            .Where(access => access.UserId == subjectUserId && access.User.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var tokens = await db.Set<UserActionToken>()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.UserId == subjectUserId)
            .ToListAsync(cancellationToken);
        var challenges = await db.Set<UserMfaChallenge>()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.UserId == subjectUserId)
            .ToListAsync(cancellationToken);
        var recoveryCodes = await db.Set<UserMfaRecoveryCode>()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.UserId == subjectUserId)
            .ToListAsync(cancellationToken);
        var credential = await db.Set<UserMfaCredential>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.UserId == subjectUserId, cancellationToken);
        var securityEvents = await db.Set<LoginSecurityEvent>()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.UserId == subjectUserId)
            .ToListAsync(cancellationToken);

        db.UserCompanyAccesses.RemoveRange(accesses);
        db.Set<UserActionToken>().RemoveRange(tokens);
        db.Set<UserMfaChallenge>().RemoveRange(challenges);
        db.Set<UserMfaRecoveryCode>().RemoveRange(recoveryCodes);
        if (credential is not null)
            db.Set<UserMfaCredential>().Remove(credential);
        db.Set<LoginSecurityEvent>().RemoveRange(securityEvents);

        var placeholderToken = Fingerprint($"erasure:{request.Id:N}:{subjectUserId}")[..24];
        subject.Email = $"erased-{placeholderToken}@privacy.invalid";
        subject.DisplayName = $"Erased user {placeholderToken[..8]}";
        subject.IsActive = false;
        subject.MustChangePassword = false;
        subject.PasswordHash = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        subject.PasswordSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        subject.PasswordAlgorithm = "DISABLED-PRIVACY-ERASURE-V1";
        subject.SessionVersion = checked(subject.SessionVersion + 1);
        subject.FailedLoginCount = 0;
        subject.LastFailedLoginAt = null;
        subject.LockedUntilUtc = null;
        subject.DeactivatedAtUtc ??= now;
        subject.OffboardedAtUtc ??= now;
        subject.UpdatedAt = now;

        db.Set<UserLifecycleEvent>().Add(new UserLifecycleEvent
        {
            TenantId = tenantId,
            TargetUserId = subjectUserId,
            ActorUserId = approvedByUserId,
            EventType = "privacy-erasure-executed",
            DetailsJson = JsonSerializer.Serialize(new
            {
                requestId = request.Id,
                statutoryRetentionOverride = request.StatutoryRetentionOverrideApplied,
                statutoryRetainUntilUtc = request.StatutoryRetainUntilUtc,
                removedCompanyAssignmentCount = accesses.Count,
                removedIdentityArtifactCount = tokens.Count + challenges.Count + recoveryCodes.Count + (credential is null ? 0 : 1),
                removedLoginSecurityEventCount = securityEvents.Count,
                sessionVersion = subject.SessionVersion
            }),
            OccurredAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return new ErasureDecision(
            request.Id,
            request.State,
            request.StatutoryRetentionOverrideApplied,
            request.StatutoryRetainUntilUtc,
            retainedAudit.Count,
            retainedProfessionalEvidence.Count,
            accesses.Count,
            securityEvents.Count);
    }

    public Task<RetentionRunResult> RunRetentionAsync(CancellationToken cancellationToken = default) =>
        RunRetentionForTenantAsync(null, cancellationToken);

    public async Task<RetentionRunResult> RunRetentionForTenantAsync(
        int? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId is <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        var now = UtcNow();
        var identityCutoff = now.AddHours(-config.TerminalIdentityArtifactRetentionHours);
        var recoveryCutoff = now.AddDays(-config.UsedRecoveryCodeRetentionDays);

        var loginEvents = db.Set<LoginSecurityEvent>().IgnoreQueryFilters()
            .Where(item => item.ExpiresAtUtc <= now
                && (tenantId == null || item.TenantId == tenantId));
        var actionTokens = db.Set<UserActionToken>().IgnoreQueryFilters()
            .Where(item => (tenantId == null || item.TenantId == tenantId)
                && (item.ExpiresAtUtc <= identityCutoff
                    || item.ConsumedAtUtc <= identityCutoff
                    || item.RevokedAtUtc <= identityCutoff));
        var mfaChallenges = db.Set<UserMfaChallenge>().IgnoreQueryFilters()
            .Where(item => (tenantId == null || item.TenantId == tenantId)
                && (item.ExpiresAtUtc <= identityCutoff
                    || item.ConsumedAtUtc <= identityCutoff
                    || item.RevokedAtUtc <= identityCutoff));
        var recoveryCodes = db.Set<UserMfaRecoveryCode>().IgnoreQueryFilters()
            .Where(item => (tenantId == null || item.TenantId == tenantId)
                && item.UsedAtUtc <= recoveryCutoff);
        var completedRequests = db.Set<PrivacySubjectRequest>().IgnoreQueryFilters()
            .Where(item => (tenantId == null || item.TenantId == tenantId)
                && item.State != PrivacyRequestStates.Requested
                && item.MetadataExpiresAtUtc <= now
                && (item.StatutoryRetainUntilUtc == null || item.StatutoryRetainUntilUtc <= now));

        var deletedLoginEvents = await DeleteAsync(loginEvents, cancellationToken);
        var deletedActionTokens = await DeleteAsync(actionTokens, cancellationToken);
        var deletedMfaChallenges = await DeleteAsync(mfaChallenges, cancellationToken);
        var deletedRecoveryCodes = await DeleteAsync(recoveryCodes, cancellationToken);
        var deletedRequestMetadata = await DeleteAsync(completedRequests, cancellationToken);

        var staleFailureUsers = await db.UserAccounts
            .IgnoreQueryFilters()
            .Where(user => (tenantId == null || user.TenantId == tenantId)
                && user.LastFailedLoginAt != null
                && user.LastFailedLoginAt <= now.AddDays(-config.LoginSecurityEventRetentionDays)
                && (user.LockedUntilUtc == null || user.LockedUntilUtc <= now))
            .ToListAsync(cancellationToken);
        foreach (var user in staleFailureUsers)
        {
            user.FailedLoginCount = 0;
            user.LastFailedLoginAt = null;
            user.LockedUntilUtc = null;
        }
        if (staleFailureUsers.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return new RetentionRunResult(
            now,
            deletedLoginEvents,
            deletedActionTokens,
            deletedMfaChallenges,
            deletedRecoveryCodes,
            deletedRequestMetadata,
            staleFailureUsers.Count);
    }

    public async Task<PrivacyIncidentExercise> RecordIncidentExerciseAsync(
        PrivacyIncidentExercise input,
        int tenantId,
        int reviewedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (input.TenantId != tenantId || input.ReviewedByUserId != reviewedByUserId)
            throw new UnauthorizedAccessException("Incident exercise tenant and reviewer are server-controlled.");
        if (!input.UsedSyntheticDataOnly)
            throw new BusinessRuleException("Repository privacy exercises must use synthetic data only.");
        if (!IsSha256(input.ScenarioSha256) || !IsSha256(input.EvidenceManifestSha256))
            throw new BusinessRuleException("Incident exercise scenario and evidence manifests require SHA-256 identity.");
        if (string.IsNullOrWhiteSpace(input.ReleaseCandidate)
            || string.IsNullOrWhiteSpace(input.EnvironmentName)
            || string.IsNullOrWhiteSpace(input.NotificationDecision)
            || !string.Equals(input.ReviewDecision, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("Incident exercise candidate, environment, notification and accepted review decisions are required.");
        }
        var times = new[]
        {
            input.DetectedAtUtc,
            input.NotificationRoutedAtUtc,
            input.ContainedAtUtc,
            input.EvidencePreservedAtUtc,
            input.RecoveryVerifiedAtUtc,
            input.ReviewedAtUtc
        };
        if (times.Any(time => time.Kind != DateTimeKind.Utc)
            || !times.SequenceEqual(times.OrderBy(time => time)))
        {
            throw new BusinessRuleException("Incident exercise milestones must be ordered UTC timestamps.");
        }
        if (!input.TenantIsolationVerified || !input.AuditIntegrityVerified || !input.FinancialIntegrityVerified)
            throw new BusinessRuleException("Incident exercise recovery must verify tenant, audit and financial integrity.");

        input.Id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id;
        db.Set<PrivacyIncidentExercise>().Add(input);
        await db.SaveChangesAsync(cancellationToken);
        return input;
    }

    private async Task<List<SubjectEvidenceReference>> LocateProfessionalEvidenceAsync(
        int tenantId,
        string[] actorKeys,
        CancellationToken cancellationToken)
    {
        var evidence = new List<SubjectEvidenceReference>();
        evidence.AddRange(await db.FilingAuthorityEngagements.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && (actorKeys.Contains(item.ReviewedByUserId) || actorKeys.Contains(item.CreatedByUserId)))
            .Select(item => new SubjectEvidenceReference(nameof(FilingAuthorityEngagement), item.Id.ToString(),
                "ReviewedByUserId|CreatedByUserId", item.CompanyId, null, item.CreatedAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.ExternalFilingHandoffSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && actorKeys.Contains(item.PreparedByUserId))
            .Select(item => new SubjectEvidenceReference(nameof(ExternalFilingHandoffSnapshot), item.Id.ToString(),
                nameof(ExternalFilingHandoffSnapshot.PreparedByUserId), item.CompanyId, item.PeriodId, item.PreparedAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.ExternalFilingOutcomeEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && actorKeys.Contains(item.RecordedByUserId))
            .Select(item => new SubjectEvidenceReference(nameof(ExternalFilingOutcomeEvent), item.Id.ToString(),
                nameof(ExternalFilingOutcomeEvent.RecordedByUserId), item.CompanyId, item.PeriodId, item.RecordedAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.AnnualReturnDateRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Company.TenantId == tenantId && actorKeys.Contains(item.RecordedByUserId))
            .Select(item => new SubjectEvidenceReference(nameof(AnnualReturnDateRecord), item.Id.ToString(),
                nameof(AnnualReturnDateRecord.RecordedByUserId), item.CompanyId, null, item.RecordedAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.CompanyQuarantineEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && actorKeys.Contains(item.ActorUserId))
            .Select(item => new SubjectEvidenceReference(nameof(CompanyQuarantineEvent), item.Id.ToString(),
                nameof(CompanyQuarantineEvent.ActorUserId), item.CompanyId, null, item.OccurredAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.CompanyOnboardingRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && actorKeys.Contains(item.CreatedByUserId))
            .Select(item => new SubjectEvidenceReference(nameof(CompanyOnboardingRequest), item.Id.ToString(),
                nameof(CompanyOnboardingRequest.CreatedByUserId), item.CompanyId, item.PeriodId, item.StartedAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.IdempotencyRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && actorKeys.Contains(item.CreatedByUserId))
            .Select(item => new SubjectEvidenceReference(nameof(IdempotencyRecord), item.Id.ToString(),
                nameof(IdempotencyRecord.CreatedByUserId), null, null, item.StartedAtUtc))
            .ToListAsync(cancellationToken));
        evidence.AddRange(await db.AuditIntegrityCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && item.CreatedByUserId != null
                && actorKeys.Contains(item.CreatedByUserId))
            .Select(item => new SubjectEvidenceReference(nameof(AuditIntegrityCheckpoint), item.Id.ToString(),
                nameof(AuditIntegrityCheckpoint.CreatedByUserId), item.CompanyId, null, item.CreatedAtUtc))
            .ToListAsync(cancellationToken));
        return evidence
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.RecordType, StringComparer.Ordinal)
            .ThenBy(item => item.RecordId, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<int> DeleteAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (db.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);
        var rows = await query.ToListAsync(cancellationToken);
        db.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private string Fingerprint(string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(identifierHmacKey, Encoding.UTF8.GetBytes(value)));

    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string RequiredCode(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || !CodePattern().IsMatch(normalized))
            throw new ArgumentException("A fixed lowercase event code is required.", parameterName);
        return normalized;
    }

    private static string? SafeCorrelationId(string? value)
    {
        var candidate = value?.Trim();
        return candidate is { Length: > 0 and <= 128 } && CorrelationPattern().IsMatch(candidate)
            ? candidate
            : null;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();

    public sealed record SubjectAccessArtifact(
        Guid RequestId,
        byte[] Bytes,
        string Sha256,
        IReadOnlyDictionary<string, int> LocatedRecordCounts);

    public sealed record ErasureDecision(
        Guid RequestId,
        string State,
        bool StatutoryRetentionOverrideApplied,
        DateTime? StatutoryRetainUntilUtc,
        int RetainedAuditEventCount,
        int RetainedProfessionalEvidenceReferenceCount,
        int RemovedCompanyAssignmentCount,
        int RemovedLoginSecurityEventCount);

    public sealed record RetentionRunResult(
        DateTime CompletedAtUtc,
        int DeletedLoginSecurityEvents,
        int DeletedActionTokens,
        int DeletedMfaChallenges,
        int DeletedUsedRecoveryCodes,
        int DeletedSubjectRequestMetadata,
        int ClearedStaleLoginFailureStates);

    public sealed record SubjectEvidenceReference(
        string RecordType,
        string RecordId,
        string MatchedActorFields,
        int? CompanyId,
        int? PeriodId,
        DateTime OccurredAtUtc);
}

public sealed class PrivacyRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PrivacyGovernanceConfig> options,
    TimeProvider timeProvider,
    ILogger<PrivacyRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(options.Value.RetentionWorkerIntervalHours),
            timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                IReadOnlyList<int> tenantIds;
                long anonymousDeleted;
                await using (var discovery = scopeFactory.CreateAsyncScope())
                {
                    var resolver = discovery.ServiceProvider.GetRequiredService<DatabaseTenantBootstrapResolver>();
                    tenantIds = await resolver.ListTenantIdsForJobsAsync(stoppingToken);
                    anonymousDeleted = await resolver.DeleteExpiredAnonymousLoginEventsAsync(stoppingToken);
                }

                var results = new List<PrivacyGovernanceService.RetentionRunResult>();
                foreach (var tenantId in tenantIds)
                {
                    await using var tenantScope = scopeFactory.CreateAsyncScope();
                    tenantScope.ServiceProvider.GetRequiredService<DatabaseTenantContext>().SetResolvedTenant(tenantId);
                    results.Add(await tenantScope.ServiceProvider
                        .GetRequiredService<PrivacyGovernanceService>()
                        .RunRetentionForTenantAsync(tenantId, stoppingToken));
                }
                logger.LogInformation(
                    "Privacy retention completed at {CompletedAtUtc}: login={LoginCount}, identity={IdentityCount}, requests={RequestCount}",
                    timeProvider.GetUtcNow().UtcDateTime,
                    results.Sum(result => result.DeletedLoginSecurityEvents) + anonymousDeleted,
                    results.Sum(result => result.DeletedActionTokens + result.DeletedMfaChallenges + result.DeletedUsedRecoveryCodes),
                    results.Sum(result => result.DeletedSubjectRequestMetadata));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Privacy retention cleanup failed");
            }
        }
    }
}
