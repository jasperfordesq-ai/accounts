using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class PrivacyGovernanceTests
{
    private static readonly DateTimeOffset Instant =
        new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoginSecurityEvents_AreMinimisedKeyedAndExpiredByTheApprovedSchedule()
    {
        await using var db = CreateDb();
        var tenant = await SeedTenantAsync(db, "privacy-login");
        var user = await SeedUserAsync(db, tenant.Id, "subject@example.ie", "Privacy Subject", "Client");
        var service = Service(db, Instant);

        await service.RecordLoginAttemptAsync(
            " Subject@Example.IE ",
            tenant.Id,
            user.Id,
            "rejected",
            "invalid-credentials",
            "subject@example.ie:NeverRetainThisSecret");

        var retained = await db.Set<LoginSecurityEvent>().SingleAsync();
        Assert.Equal(64, retained.IdentifierFingerprint.Length);
        Assert.Equal(Instant.UtcDateTime, retained.OccurredAtUtc);
        Assert.Equal(Instant.AddDays(30).UtcDateTime, retained.ExpiresAtUtc);
        Assert.Null(retained.CorrelationId);
        var retainedJson = JsonSerializer.Serialize(retained);
        Assert.DoesNotContain("subject@example.ie", retainedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NeverRetainThisSecret", retainedJson, StringComparison.Ordinal);

        var result = await Service(db, Instant.AddDays(31)).RunRetentionAsync();

        Assert.Equal(1, result.DeletedLoginSecurityEvents);
        Assert.Empty(await db.Set<LoginSecurityEvent>().ToListAsync());
    }

    [Fact]
    public async Task SubjectAccessExport_LocatesApplicableDataWithoutCredentialsOrOtherTenantData()
    {
        await using var db = CreateDb();
        var tenant = await SeedTenantAsync(db, "privacy-export");
        var otherTenant = await SeedTenantAsync(db, "privacy-export-other");
        var owner = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Owner", "Owner");
        var subject = await SeedUserAsync(db, tenant.Id, "subject@example.ie", "Subject", "Accountant");
        var other = await SeedUserAsync(db, otherTenant.Id, "other@example.ie", "Other", "Owner");
        var company = await SeedCompanyAndPeriodAsync(db, tenant.Id, "Subject Company");
        db.UserCompanyAccesses.Add(new UserCompanyAccess { UserId = subject.Id, CompanyId = company.CompanyId });
        db.Set<UserLifecycleEvent>().Add(new UserLifecycleEvent
        {
            TenantId = tenant.Id,
            TargetUserId = subject.Id,
            ActorUserId = owner.Id,
            EventType = "company-access-assigned",
            DetailsJson = JsonSerializer.Serialize(new { companyId = company.CompanyId, role = "Accountant" }),
            OccurredAtUtc = Instant.UtcDateTime
        });
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            TenantId = tenant.Id,
            IdempotencyKey = "privacy-export-001",
            Operation = "privacy-subject-export-fixture",
            RequestFingerprintSha256 = new string('a', 64),
            Status = "InProgress",
            CreatedByUserId = $"user:{subject.Id}",
            CreatedByDisplayName = subject.DisplayName,
            StartedAtUtc = Instant.UtcDateTime,
            ExpiresAtUtc = Instant.AddDays(30).UtcDateTime
        });
        await db.SaveChangesAsync();
        await Service(db, Instant).RecordLoginAttemptAsync(
            subject.Email,
            tenant.Id,
            subject.Id,
            "accepted",
            "password-verified",
            "privacy-export-001");
        await new AuditService(db).LogAsync(
            company.CompanyId,
            company.PeriodId,
            "ReviewEvidence",
            1,
            "ReviewAccepted",
            newValue: new { Decision = "accepted" },
            userId: $"user:{subject.Id}",
            tenantId: tenant.Id,
            requestId: "privacy-export-001",
            actorDisplayName: subject.DisplayName);

        var artifact = await Service(db, Instant).BuildSubjectAccessExportAsync(
            tenant.Id,
            subject.Id,
            owner.Id);

        var json = Encoding.UTF8.GetString(artifact.Bytes);
        Assert.Contains("privacy-subject-access-v1", json);
        Assert.Contains("subject@example.ie", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReviewAccepted", json);
        Assert.DoesNotContain(subject.PasswordHash, json, StringComparison.Ordinal);
        Assert.DoesNotContain(subject.PasswordSalt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(other.Email, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, artifact.Sha256.Length);
        Assert.Equal(1, artifact.LocatedRecordCounts["auditEvents"]);
        Assert.Equal(1, artifact.LocatedRecordCounts["companyAssignments"]);
        Assert.Equal(1, artifact.LocatedRecordCounts["professionalEvidenceReferences"]);
        Assert.Contains("IdempotencyRecord", json, StringComparison.Ordinal);
        var request = await db.Set<PrivacySubjectRequest>().SingleAsync();
        Assert.Equal(PrivacyRequestStates.Completed, request.State);
        Assert.Equal(artifact.Sha256, request.ExportSha256);
        Assert.Equal(artifact.Bytes.LongLength, request.ExportByteCount);
    }

    [Fact]
    public async Task ApprovedErasure_RevokesIdentityAndPreservesHashChainedStatutoryEvidenceWithOverride()
    {
        await using var db = CreateDb();
        var tenant = await SeedTenantAsync(db, "privacy-erasure");
        var owner = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Owner", "Owner");
        var subject = await SeedUserAsync(db, tenant.Id, "subject@example.ie", "Subject", "Accountant");
        var company = await SeedCompanyAndPeriodAsync(db, tenant.Id, "Erasure Company");
        db.UserCompanyAccesses.Add(new UserCompanyAccess { UserId = subject.Id, CompanyId = company.CompanyId });
        db.Set<UserActionToken>().Add(new UserActionToken
        {
            TenantId = tenant.Id,
            UserId = subject.Id,
            Purpose = "password-reset",
            TokenHash = new string('a', 64),
            ExpiresAtUtc = Instant.AddHours(1).UtcDateTime,
            CreatedByUserId = owner.Id
        });
        db.Set<UserMfaCredential>().Add(new UserMfaCredential
        {
            TenantId = tenant.Id,
            UserId = subject.Id,
            EncryptedSecret = "synthetic-encrypted-secret"
        });
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            TenantId = tenant.Id,
            IdempotencyKey = "privacy-erasure-001",
            Operation = "privacy-erasure-professional-evidence",
            RequestFingerprintSha256 = new string('c', 64),
            Status = "InProgress",
            CreatedByUserId = subject.Email.ToLowerInvariant(),
            CreatedByDisplayName = subject.DisplayName,
            StartedAtUtc = Instant.UtcDateTime,
            ExpiresAtUtc = Instant.AddDays(30).UtcDateTime
        });
        await db.SaveChangesAsync();
        await Service(db, Instant).RecordLoginAttemptAsync(
            subject.Email,
            tenant.Id,
            subject.Id,
            "rejected",
            "invalid-credentials",
            "privacy-erasure-001");
        await new AuditService(db).LogAsync(
            company.CompanyId,
            company.PeriodId,
            "QualifiedAccountantReview",
            1,
            "EvidenceAccepted",
            newValue: new { Decision = "accepted", ArtifactSha256 = new string('b', 64) },
            userId: subject.Email.ToLowerInvariant(),
            tenantId: tenant.Id,
            requestId: "privacy-erasure-001",
            actorDisplayName: subject.DisplayName);
        var auditBefore = await db.AuditLogs.AsNoTracking().SingleAsync();
        var priorSessionVersion = subject.SessionVersion;

        var decision = await Service(db, Instant).ExecuteApprovedErasureAsync(
            tenant.Id,
            subject.Id,
            owner.Id,
            "Approved after identity verification; statutory accounting evidence must remain intact.");

        var erased = await db.UserAccounts.IgnoreQueryFilters().SingleAsync(user => user.Id == subject.Id);
        Assert.False(erased.IsActive);
        Assert.StartsWith("erased-", erased.Email);
        Assert.EndsWith("@privacy.invalid", erased.Email);
        Assert.DoesNotContain("subject", erased.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(priorSessionVersion + 1, erased.SessionVersion);
        Assert.Equal("DISABLED-PRIVACY-ERASURE-V1", erased.PasswordAlgorithm);
        Assert.Empty(await db.UserCompanyAccesses.IgnoreQueryFilters().Where(item => item.UserId == subject.Id).ToListAsync());
        Assert.Empty(await db.Set<UserActionToken>().IgnoreQueryFilters().Where(item => item.UserId == subject.Id).ToListAsync());
        Assert.Empty(await db.Set<UserMfaCredential>().IgnoreQueryFilters().Where(item => item.UserId == subject.Id).ToListAsync());
        Assert.Empty(await db.Set<LoginSecurityEvent>().IgnoreQueryFilters().Where(item => item.UserId == subject.Id).ToListAsync());

        var auditAfter = await db.AuditLogs.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(auditBefore.IntegrityHash, auditAfter.IntegrityHash);
        Assert.Equal(auditBefore.UserId, auditAfter.UserId);
        Assert.True(decision.StatutoryRetentionOverrideApplied);
        Assert.Equal(1, decision.RetainedAuditEventCount);
        Assert.Equal(1, decision.RetainedProfessionalEvidenceReferenceCount);
        Assert.Equal(new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc), decision.StatutoryRetainUntilUtc);
        var request = await db.Set<PrivacySubjectRequest>().SingleAsync();
        Assert.Equal(PrivacyRequestStates.PartiallyCompletedStatutoryOverride, request.State);
        Assert.Equal(PrivacyGovernanceService.CompaniesActRetentionBasis, request.StatutoryRetentionLegalBasis);
        Assert.Contains(auditAfter.Id.ToString(), request.StatutoryRetentionInventoryJson);
        Assert.Contains(nameof(IdempotencyRecord), request.StatutoryRetentionInventoryJson);
        Assert.Contains(await db.Set<UserLifecycleEvent>().ToListAsync(), item =>
            item.EventType == "privacy-erasure-executed" && item.TargetUserId == subject.Id);
    }

    [Fact]
    public async Task ApprovedErasure_CannotCrossTenantOrRemoveLastActiveOwner()
    {
        await using var db = CreateDb();
        var tenant = await SeedTenantAsync(db, "privacy-owner");
        var otherTenant = await SeedTenantAsync(db, "privacy-owner-other");
        var owner = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Owner", "Owner");
        var otherOwner = await SeedUserAsync(db, otherTenant.Id, "other@example.ie", "Other", "Owner");
        var service = Service(db, Instant);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExecuteApprovedErasureAsync(
            tenant.Id,
            owner.Id,
            otherOwner.Id,
            "A deliberately invalid cross-tenant erasure decision must always fail closed."));
        Assert.True((await db.UserAccounts.IgnoreQueryFilters().SingleAsync(user => user.Id == owner.Id)).IsActive);
    }

    [Fact]
    public async Task IncidentExercise_RequiresSyntheticDataOrderedMilestonesAndIntegrityRecovery()
    {
        await using var db = CreateDb();
        var tenant = await SeedTenantAsync(db, "privacy-incident");
        var owner = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Owner", "Owner");
        var service = Service(db, Instant);
        var valid = Exercise(tenant.Id, owner.Id);

        var retained = await service.RecordIncidentExerciseAsync(valid, tenant.Id, owner.Id);

        Assert.NotEqual(Guid.Empty, retained.Id);
        Assert.Single(await db.Set<PrivacyIncidentExercise>().ToListAsync());
        var unsafeExercise = Exercise(tenant.Id, owner.Id);
        unsafeExercise.UsedSyntheticDataOnly = false;
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RecordIncidentExerciseAsync(unsafeExercise, tenant.Id, owner.Id));
    }

    private static PrivacyIncidentExercise Exercise(int tenantId, int ownerId) => new()
    {
        TenantId = tenantId,
        ExerciseKind = "synthetic-tabletop",
        ReleaseCandidate = new string('c', 40),
        EnvironmentName = "production-like-ci",
        ScenarioSha256 = new string('d', 64),
        DetectedAtUtc = Instant.UtcDateTime,
        NotificationRoutedAtUtc = Instant.AddMinutes(2).UtcDateTime,
        ContainedAtUtc = Instant.AddMinutes(5).UtcDateTime,
        EvidencePreservedAtUtc = Instant.AddMinutes(7).UtcDateTime,
        RecoveryVerifiedAtUtc = Instant.AddMinutes(20).UtcDateTime,
        ReviewedAtUtc = Instant.AddMinutes(30).UtcDateTime,
        ReviewedByUserId = ownerId,
        NotificationDecision = "named-controller-review-required-no-synthetic-notification",
        EvidenceManifestSha256 = new string('e', 64),
        ReviewDecision = "accepted",
        UsedSyntheticDataOnly = true,
        TenantIsolationVerified = true,
        AuditIntegrityVerified = true,
        FinancialIntegrityVerified = true
    };

    private static PrivacyGovernanceService Service(AccountsDbContext db, DateTimeOffset now) => new(
        db,
        Options.Create(new PrivacyGovernanceConfig()),
        Options.Create(new AuthSessionConfig { SigningKey = StrongSigningKey() }),
        new FixedTimeProvider(now));

    private static AccountsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<Tenant> SeedTenantAsync(AccountsDbContext db, string slug)
    {
        var tenant = new Tenant { Name = slug, Slug = $"{slug}-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<UserAccount> SeedUserAsync(
        AccountsDbContext db,
        int tenantId,
        string email,
        string displayName,
        string role)
    {
        var user = new UserAccount
        {
            TenantId = tenantId,
            Email = email,
            DisplayName = displayName,
            Role = role,
            PasswordHash = $"hash-{Guid.NewGuid():N}",
            PasswordSalt = $"salt-{Guid.NewGuid():N}"
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(int CompanyId, int PeriodId)> SeedCompanyAndPeriodAsync(
        AccountsDbContext db,
        int tenantId,
        string legalName)
    {
        var company = new Company
        {
            TenantId = tenantId,
            LegalName = legalName,
            IncorporationDate = new DateOnly(2020, 1, 1),
            FinancialYearStartMonth = 1
        };
        var period = new AccountingPeriod
        {
            Company = company,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31)
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return (company.Id, period.Id);
    }

    private static string StrongSigningKey() => Convert.ToBase64String(
        Enumerable.Range(1, 64).Select(value => (byte)value).ToArray());

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
