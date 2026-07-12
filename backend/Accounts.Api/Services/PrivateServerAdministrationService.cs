using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed partial class PrivateInitializationService(
    AccountsDbContext db,
    FileBackedConfigurationProvenance fileBackedConfiguration,
    IOptions<DeploymentConfig> deploymentOptions,
    IOptions<PrivateInitializationConfig> initializationOptions,
    IPasswordSafetyService passwordSafety,
    TimeProvider timeProvider)
{
    public async Task<PrivateInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        PrivateServerAdministrationSupport.RequirePrivateServer(deploymentOptions.Value.Mode);
        var config = initializationOptions.Value;
        if (!fileBackedConfiguration.IsFileBacked("PrivateInitialization:OwnerInitialPassword"))
        {
            throw new InvalidOperationException(
                "PrivateInitialization:OwnerInitialPassword must be supplied through PrivateInitialization__OwnerInitialPassword_FILE.");
        }

        var tenantName = Required(config.TenantName, "PrivateInitialization:TenantName", 2, 200);
        var tenantSlug = NormalizeSlug(config.TenantSlug);
        var ownerEmail = NormalizeEmail(config.OwnerEmail);
        var ownerDisplayName = Required(
            config.OwnerDisplayName,
            "PrivateInitialization:OwnerDisplayName",
            2,
            200);
        var password = config.OwnerInitialPassword;
        if (BootstrapOwnerPasswordPolicy.Validate(password) is { } passwordFailure)
            throw new InvalidOperationException(passwordFailure.Replace("BootstrapOwner", "PrivateInitialization", StringComparison.Ordinal) + ".");
        var safety = await passwordSafety.CheckAsync(password, cancellationToken);
        if (safety.Status == PasswordSafetyStatus.Breached)
            throw new InvalidOperationException("PrivateInitialization:OwnerInitialPassword appears in a known breach.");
        if (safety.Status == PasswordSafetyStatus.Unavailable)
            throw new InvalidOperationException("Private Server Owner password safety validation is unavailable.");

        await using var transaction = await PrivateServerAdministrationSupport.BeginSerializableTransactionAsync(db, cancellationToken);
        try
        {
            await RequireEmptyIdentityDatabaseAsync(db, cancellationToken);
            var now = PrivateServerAdministrationSupport.UtcNow(timeProvider);
            var tenant = new Tenant
            {
                Name = tenantName,
                Slug = tenantSlug,
                IsMainDemoTenant = false,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);

            var passwordHash = PasswordHasher.HashPassword(password);
            var owner = new UserAccount
            {
                TenantId = tenant.Id,
                Tenant = tenant,
                Email = ownerEmail,
                DisplayName = ownerDisplayName,
                Role = "Owner",
                PasswordHash = passwordHash.Hash,
                PasswordSalt = passwordHash.Salt,
                PasswordAlgorithm = AuthService.PasswordAlgorithm,
                PasswordStrengthScore = 5,
                IsActive = true,
                MustChangePassword = true,
                PasswordLastChangedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(owner);
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new PrivateInitializationResult(tenant.Id, tenant.Slug, owner.Id, owner.Email);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task RequireEmptyIdentityDatabaseAsync(
        AccountsDbContext db,
        CancellationToken cancellationToken)
    {
        var hasTenant = await db.Tenants.IgnoreQueryFilters().AnyAsync(cancellationToken);
        var hasUser = await db.UserAccounts.IgnoreQueryFilters().AnyAsync(cancellationToken);
        var hasCompany = await db.Companies.IgnoreQueryFilters().AnyAsync(cancellationToken);
        if (hasTenant || hasUser || hasCompany)
        {
            throw new InvalidOperationException(
                "Private Server initialization requires an empty database with no tenant, user, or company rows. Existing data was not changed.");
        }
    }

    private static string NormalizeSlug(string? value)
    {
        var slug = value?.Trim().ToLowerInvariant() ?? "";
        if (slug.Length is < 3 or > 120 || !SlugPattern().IsMatch(slug))
            throw new InvalidOperationException("PrivateInitialization:TenantSlug must contain 3-120 lowercase letters, numbers, or internal hyphens.");
        return slug;
    }

    private static string NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant() ?? "";
        if (email.Length is < 3 or > 320
            || !email.Contains('@')
            || email.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("PrivateInitialization:OwnerEmail must be a valid email address.");
        }
        return email;
    }

    private static string Required(string? value, string key, int minimumLength, int maximumLength)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length < minimumLength || normalized.Length > maximumLength)
            throw new InvalidOperationException($"{key} must contain between {minimumLength} and {maximumLength} characters.");
        return normalized;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])$")]
    private static partial Regex SlugPattern();
}

public sealed partial class PrivateOwnerRecoveryService(
    AccountsDbContext db,
    IOptions<DeploymentConfig> deploymentOptions,
    IOptions<PrivateOwnerRecoveryConfig> recoveryOptions,
    MfaSecurityService mfaSecurity,
    AuditService auditService,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerDefaults.Web);

    public async Task<PrivateOwnerRecoveryResult> BeginAsync(
        CancellationToken cancellationToken = default)
    {
        var deployment = deploymentOptions.Value;
        PrivateServerAdministrationSupport.RequirePrivateServer(deployment.Mode);
        if (!Guid.TryParse(deployment.InstallationId, out var installationId))
            throw new InvalidOperationException("Deployment:InstallationId is invalid.");

        var recovery = recoveryOptions.Value;
        if (!Guid.TryParse(recovery.ConfirmInstallationId, out var confirmedInstallationId)
            || confirmedInstallationId != installationId)
        {
            throw new InvalidOperationException("Private Server recovery installation confirmation did not match.");
        }
        if (!string.Equals(
                recovery.ConfirmationPhrase,
                PrivateOwnerRecoveryConfig.RequiredConfirmationPhrase,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Private Server recovery requires the exact confirmation phrase '{PrivateOwnerRecoveryConfig.RequiredConfirmationPhrase}'.");
        }

        var tenantSlug = recovery.TenantSlug?.Trim().ToLowerInvariant() ?? "";
        if (tenantSlug.Length is < 3 or > 120
            || tenantSlug.StartsWith('-')
            || tenantSlug.EndsWith('-')
            || tenantSlug.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
        {
            throw new InvalidOperationException(
                "Private Server recovery requires the exact configured tenant slug.");
        }

        var ownerEmail = recovery.OwnerEmail?.Trim().ToLowerInvariant() ?? "";
        var confirmedEmail = recovery.ConfirmOwnerEmail?.Trim().ToLowerInvariant() ?? "";
        if (ownerEmail.Length is < 3 or > 320
            || !ownerEmail.Contains('@')
            || !string.Equals(ownerEmail, confirmedEmail, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Private Server recovery Owner email confirmation did not match.");
        }

        await using var transaction = await PrivateServerAdministrationSupport.BeginSerializableTransactionAsync(db, cancellationToken);
        try
        {
            var owner = await db.UserAccounts
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(user =>
                    user.Tenant.Slug == tenantSlug
                    && user.Email == ownerEmail
                    && user.Role == "Owner"
                    && user.IsActive
                    && user.OffboardedAtUtc == null,
                    cancellationToken)
                ?? throw new InvalidOperationException("Exactly one matching active Owner account is required for Private Server recovery.");
            var now = PrivateServerAdministrationSupport.UtcNow(timeProvider);

            var outstandingTokens = await db.UserActionTokens.IgnoreQueryFilters()
                .Where(token => token.UserId == owner.Id
                    && token.ConsumedAtUtc == null
                    && token.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var token in outstandingTokens) token.RevokedAtUtc = now;

            var outstandingChallenges = await db.UserMfaChallenges.IgnoreQueryFilters()
                .Where(challenge => challenge.UserId == owner.Id
                    && challenge.ConsumedAtUtc == null
                    && challenge.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var challenge in outstandingChallenges) challenge.RevokedAtUtc = now;

            var credentials = await db.UserMfaCredentials.IgnoreQueryFilters()
                .Where(credential => credential.UserId == owner.Id)
                .ToListAsync(cancellationToken);
            var recoveryCodes = await db.UserMfaRecoveryCodes.IgnoreQueryFilters()
                .Where(code => code.UserId == owner.Id)
                .ToListAsync(cancellationToken);
            db.UserMfaRecoveryCodes.RemoveRange(recoveryCodes);
            db.UserMfaCredentials.RemoveRange(credentials);

            owner.SessionVersion++;
            owner.MustChangePassword = true;
            owner.FailedLoginCount = 0;
            owner.LastFailedLoginAt = null;
            owner.LockedUntilUtc = null;
            owner.UpdatedAt = now;

            var rawToken = mfaSecurity.CreateOpaqueToken();
            var expiresAt = now.AddHours(1);
            db.UserActionTokens.Add(new UserActionToken
            {
                TenantId = owner.TenantId,
                UserId = owner.Id,
                Purpose = UserActionPurposes.PasswordReset,
                TokenHash = mfaSecurity.HashOpaqueToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                CreatedByUserId = null,
                CreatedByActorKind = IdentityActorKinds.PrivateServerHostOperator
            });
            var evidence = new
            {
                actorKind = IdentityActorKinds.PrivateServerHostOperator,
                hostInstallationConfirmed = true,
                tenantQualified = true,
                sessionsRevoked = true,
                outstandingTokensRevoked = outstandingTokens.Count,
                outstandingMfaChallengesRevoked = outstandingChallenges.Count,
                mfaCredentialsRemoved = credentials.Count,
                mfaRecoveryCodesRemoved = recoveryCodes.Count,
                mfaReenrollmentRequired = true,
                resetTokenExpiresAtUtc = expiresAt
            };
            db.UserLifecycleEvents.Add(new UserLifecycleEvent
            {
                TenantId = owner.TenantId,
                TargetUserId = owner.Id,
                ActorUserId = null,
                ActorKind = IdentityActorKinds.PrivateServerHostOperator,
                EventType = UserLifecycleEventTypes.PrivateOwnerRecoveryStarted,
                DetailsJson = JsonSerializer.Serialize(evidence, EvidenceJson),
                OccurredAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await auditService.LogAsync(
                companyId: null,
                periodId: null,
                entityType: nameof(UserAccount),
                entityId: owner.Id,
                action: UserLifecycleEventTypes.PrivateOwnerRecoveryStarted,
                newValue: evidence,
                userId: "operator:private-host",
                tenantId: owner.TenantId,
                actorDisplayName: "Private Server host operator",
                durableAudit: true,
                cancellationToken: cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new PrivateOwnerRecoveryResult(owner.Id, owner.Email, rawToken, expiresAt);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

public sealed record PrivateInitializationResult(
    int TenantId,
    string TenantSlug,
    int OwnerUserId,
    string OwnerEmail);

public sealed record PrivateOwnerRecoveryResult(
    int OwnerUserId,
    string OwnerEmail,
    string ResetToken,
    DateTime ExpiresAtUtc);

file static class PrivateServerAdministrationSupport
{
    public static void RequirePrivateServer(string? mode)
    {
        if (!string.Equals(mode, DeploymentModeContract.PrivateServer, StringComparison.Ordinal))
            throw new InvalidOperationException("This operator command is available only when Deployment:Mode is exactly PrivateServer.");
    }

    public static async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        AccountsDbContext db,
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    public static DateTime UtcNow(TimeProvider timeProvider)
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }
}
