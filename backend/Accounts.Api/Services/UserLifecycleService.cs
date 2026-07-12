using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Text.Json;

namespace Accounts.Api.Services;

public sealed class UserLifecycleService(
    AccountsDbContext db,
    MfaSecurityService mfaSecurity,
    AuditService auditService,
    TimeProvider timeProvider,
    DatabaseTenantBootstrapResolver? tenantBootstrap = null,
    IPasswordSafetyService? passwordSafety = null)
{
    public static readonly IReadOnlySet<string> SupportedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Owner", "Accountant", "Reviewer", "Client"
    };

    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<UserAdministrationSummary>> ListAsync(AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        RequireOwner(actor);
        return await db.UserAccounts
            .AsNoTracking()
            .Include(user => user.CompanyAccesses)
            .Include(user => user.MfaCredential)
            .Where(user => user.TenantId == actor.TenantId)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => UserAdministrationSummary.From(user))
            .ToListAsync(cancellationToken);
    }

    public Task<UserProvisioningResult> InviteAsync(
        AuthenticatedUser actor,
        InviteUserInput input,
        CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var email = NormalizeEmail(input.Email);
            var displayName = NormalizeDisplayName(input.DisplayName);
            var role = NormalizeRole(input.Role);
            await EnsureEmailAvailableAsync(actor.TenantId, email, cancellationToken);
            var companyIds = await ValidateCompanyIdsAsync(actor.TenantId, input.CompanyIds, cancellationToken);
            var now = UtcNow();
            var placeholder = PasswordHasher.HashPassword(mfaSecurity.CreateOpaqueToken());
            var user = new UserAccount
            {
                TenantId = actor.TenantId,
                Email = email,
                DisplayName = displayName,
                Role = role,
                PasswordHash = placeholder.Hash,
                PasswordSalt = placeholder.Salt,
                PasswordAlgorithm = "INVITE-PENDING-v1",
                PasswordStrengthScore = 0,
                IsActive = false,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            AddCompanyAssignments(user, companyIds, now);

            var rawToken = mfaSecurity.CreateOpaqueToken();
            var token = new UserActionToken
            {
                TenantId = actor.TenantId,
                UserId = user.Id,
                Purpose = UserActionPurposes.Invitation,
                TokenHash = mfaSecurity.HashOpaqueToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(3),
                CreatedByUserId = actor.UserId
            };
            db.UserActionTokens.Add(token);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.Invited, new
            {
                role,
                active = false,
                companyIds,
                expiresAtUtc = token.ExpiresAtUtc
            }, cancellationToken);
            return new UserProvisioningResult(UserAdministrationSummary.From(user), rawToken, token.ExpiresAtUtc);
        }, cancellationToken);

    public Task<UserAdministrationSummary> CreateAsync(
        AuthenticatedUser actor,
        CreateUserInput input,
        CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var email = NormalizeEmail(input.Email);
            var displayName = NormalizeDisplayName(input.DisplayName);
            var role = NormalizeRole(input.Role);
            ValidateStrongPassword(input.TemporaryPassword);
            await EnsurePasswordSafeAsync(input.TemporaryPassword!, cancellationToken);
            await EnsureEmailAvailableAsync(actor.TenantId, email, cancellationToken);
            var companyIds = await ValidateCompanyIdsAsync(actor.TenantId, input.CompanyIds, cancellationToken);
            var now = UtcNow();
            var password = PasswordHasher.HashPassword(input.TemporaryPassword!);
            var user = new UserAccount
            {
                TenantId = actor.TenantId,
                Email = email,
                DisplayName = displayName,
                Role = role,
                PasswordHash = password.Hash,
                PasswordSalt = password.Salt,
                PasswordAlgorithm = AuthService.PasswordAlgorithm,
                PasswordStrengthScore = 5,
                IsActive = true,
                MustChangePassword = true,
                PasswordLastChangedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            AddCompanyAssignments(user, companyIds, now);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.Created, new
            {
                role,
                active = true,
                mustChangePassword = true,
                companyIds
            }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    public Task AcceptInvitationAsync(AcceptActionTokenInput input, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            ValidateStrongPassword(input.NewPassword);
            await EnsurePasswordSafeAsync(input.NewPassword!, cancellationToken);
            var now = UtcNow();
            var token = await FindUsableActionTokenAsync(input.Token, UserActionPurposes.Invitation, now, cancellationToken);
            var user = await db.UserAccounts
                .Include(candidate => candidate.CompanyAccesses)
                .SingleAsync(candidate => candidate.Id == token.UserId, cancellationToken);
            if (user.OffboardedAtUtc is not null || user.PasswordAlgorithm != "INVITE-PENDING-v1")
                throw new BusinessRuleException("This invitation is no longer available.");

            SetPassword(user, input.NewPassword!, now);
            user.IsActive = true;
            user.InviteAcceptedAtUtc = now;
            user.UpdatedAt = now;
            user.SessionVersion++;
            token.ConsumedAtUtc = now;
            await CommitSystemEvidenceAsync(user, token.CreatedByUserId, token.CreatedByActorKind, UserLifecycleEventTypes.InvitationAccepted, new
            {
                role = user.Role,
                active = true,
                companyIds = user.CompanyAccesses.Select(access => access.CompanyId).Order().ToArray()
            }, cancellationToken);
        }, cancellationToken);

    public Task<UserProvisioningResult> BeginPasswordResetAsync(
        AuthenticatedUser actor,
        int targetUserId,
        CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken);
            if (user.OffboardedAtUtc is not null)
                throw new BusinessRuleException("An offboarded account cannot be recovered.");
            var now = UtcNow();
            await RevokeOutstandingTokensAsync(user.Id, UserActionPurposes.PasswordReset, now, cancellationToken);
            await RevokeSecurityStateAsync(user, now, cancellationToken);
            user.MustChangePassword = true;
            var rawToken = mfaSecurity.CreateOpaqueToken();
            var token = new UserActionToken
            {
                TenantId = actor.TenantId,
                UserId = user.Id,
                Purpose = UserActionPurposes.PasswordReset,
                TokenHash = mfaSecurity.HashOpaqueToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                CreatedByUserId = actor.UserId
            };
            db.UserActionTokens.Add(token);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.PasswordResetStarted, new
            {
                active = user.IsActive,
                locked = user.LockedUntilUtc > now,
                expiresAtUtc = token.ExpiresAtUtc,
                sessionsRevoked = true
            }, cancellationToken);
            return new UserProvisioningResult(UserAdministrationSummary.From(user), rawToken, token.ExpiresAtUtc);
        }, cancellationToken);

    public Task CompletePasswordResetAsync(AcceptActionTokenInput input, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            ValidateStrongPassword(input.NewPassword);
            await EnsurePasswordSafeAsync(input.NewPassword!, cancellationToken);
            var now = UtcNow();
            var token = await FindUsableActionTokenAsync(input.Token, UserActionPurposes.PasswordReset, now, cancellationToken);
            var user = await db.UserAccounts.SingleAsync(candidate => candidate.Id == token.UserId, cancellationToken);
            if (!user.IsActive || user.OffboardedAtUtc is not null)
                throw new BusinessRuleException("This account cannot complete password recovery.");

            // Deliberately retain lockout state: possession of a reset token must not
            // bypass lockout, role, tenant, active-state or MFA controls.
            SetPassword(user, input.NewPassword!, now);
            user.MustChangePassword = false;
            user.SessionVersion++;
            token.ConsumedAtUtc = now;
            await CommitSystemEvidenceAsync(user, token.CreatedByUserId, token.CreatedByActorKind, UserLifecycleEventTypes.PasswordResetCompleted, new
            {
                active = true,
                locked = user.LockedUntilUtc > now,
                mfaStillRequired = AuthService.RequiresPrivilegedMfa(user.Role),
                sessionsRevoked = true
            }, cancellationToken);
        }, cancellationToken);

    public Task<UserAdministrationSummary> SetActiveAsync(AuthenticatedUser actor, int targetUserId, bool active, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            if (!active && actor.UserId == targetUserId)
                throw new BusinessRuleException("An Owner cannot deactivate their own account.");
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken);
            if (user.OffboardedAtUtc is not null && active)
                throw new BusinessRuleException("Offboarding is final; create a new invitation instead.");
            if (!active && user.Role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                await EnsureAnotherActiveOwnerAsync(actor.TenantId, user.Id, cancellationToken);
            var now = UtcNow();
            user.IsActive = active;
            user.DeactivatedAtUtc = active ? null : now;
            await RevokeSecurityStateAsync(user, now, cancellationToken);
            await CommitEvidenceAsync(actor, user, active ? UserLifecycleEventTypes.Activated : UserLifecycleEventTypes.Deactivated, new
            {
                active,
                sessionsRevoked = true
            }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    public Task<UserAdministrationSummary> UnlockAsync(AuthenticatedUser actor, int targetUserId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken);
            var wasLocked = user.LockedUntilUtc is not null || user.FailedLoginCount > 0;
            user.LockedUntilUtc = null;
            user.LastFailedLoginAt = null;
            user.FailedLoginCount = 0;
            await RevokeSecurityStateAsync(user, UtcNow(), cancellationToken);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.Unlocked, new
            {
                wasLocked,
                sessionsRevoked = true
            }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    public Task<UserAdministrationSummary> ChangeRoleAsync(AuthenticatedUser actor, int targetUserId, string? requestedRole, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var role = NormalizeRole(requestedRole);
            if (actor.UserId == targetUserId && !role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("An Owner cannot remove their own Owner role.");
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken);
            if (user.Role.Equals("Owner", StringComparison.OrdinalIgnoreCase) && !role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                await EnsureAnotherActiveOwnerAsync(actor.TenantId, user.Id, cancellationToken);
            var previousRole = user.Role;
            user.Role = role;
            await RevokeSecurityStateAsync(user, UtcNow(), cancellationToken);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.RoleChanged, new
            {
                previousRole,
                role,
                privilegedMfaRequired = AuthService.RequiresPrivilegedMfa(role),
                sessionsRevoked = true
            }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    public Task<UserAdministrationSummary> SetCompanyAssignmentsAsync(AuthenticatedUser actor, int targetUserId, IReadOnlyList<int>? requestedCompanyIds, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken, includeAssignments: true);
            var companyIds = await ValidateCompanyIdsAsync(actor.TenantId, requestedCompanyIds, cancellationToken);
            var previous = user.CompanyAccesses.Select(access => access.CompanyId).Order().ToArray();
            db.UserCompanyAccesses.RemoveRange(user.CompanyAccesses);
            user.CompanyAccesses.Clear();
            AddCompanyAssignments(user, companyIds, UtcNow());
            await RevokeSecurityStateAsync(user, UtcNow(), cancellationToken);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.CompanyAssignmentsChanged, new
            {
                previousCompanyIds = previous,
                companyIds,
                sessionsRevoked = true
            }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    public Task<UserAdministrationSummary> RevokeSessionsAsync(AuthenticatedUser actor, int targetUserId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken);
            await RevokeSecurityStateAsync(user, UtcNow(), cancellationToken);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.SessionsRevoked, new { sessionsRevoked = true }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    public Task<UserAdministrationSummary> OffboardAsync(AuthenticatedUser actor, int targetUserId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            RequireOwner(actor);
            if (actor.UserId == targetUserId)
                throw new BusinessRuleException("An Owner cannot offboard their own account.");
            var user = await RequireTargetAsync(actor, targetUserId, cancellationToken, includeAssignments: true);
            if (user.Role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                await EnsureAnotherActiveOwnerAsync(actor.TenantId, user.Id, cancellationToken);
            var now = UtcNow();
            user.IsActive = false;
            user.DeactivatedAtUtc = now;
            user.OffboardedAtUtc = now;
            db.UserCompanyAccesses.RemoveRange(user.CompanyAccesses);
            user.CompanyAccesses.Clear();
            await RevokeOutstandingTokensAsync(user.Id, null, now, cancellationToken);
            await RevokeSecurityStateAsync(user, now, cancellationToken);
            await CommitEvidenceAsync(actor, user, UserLifecycleEventTypes.Offboarded, new
            {
                active = false,
                companyIds = Array.Empty<int>(),
                tokensRevoked = true,
                sessionsRevoked = true
            }, cancellationToken);
            return UserAdministrationSummary.From(user);
        }, cancellationToken);

    private async Task CommitEvidenceAsync(AuthenticatedUser actor, UserAccount target, string eventType, object details, CancellationToken cancellationToken)
    {
        AddLifecycleEvent(actor.TenantId, target.Id, actor.UserId, IdentityActorKinds.User, eventType, details);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.LogAsync(
            companyId: null,
            periodId: null,
            entityType: nameof(UserAccount),
            entityId: target.Id,
            action: eventType,
            newValue: details,
            userId: $"user:{actor.UserId}",
            tenantId: actor.TenantId,
            actorDisplayName: "Authenticated firm user",
            durableAudit: true,
            cancellationToken: cancellationToken);
    }

    private async Task CommitSystemEvidenceAsync(UserAccount target, int? actorUserId, string actorKind, string eventType, object details, CancellationToken cancellationToken)
    {
        AddLifecycleEvent(target.TenantId, target.Id, actorUserId, actorKind, eventType, details);
        await db.SaveChangesAsync(cancellationToken);
        var hostOperator = actorKind == IdentityActorKinds.PrivateServerHostOperator;
        await auditService.LogAsync(
            companyId: null,
            periodId: null,
            entityType: nameof(UserAccount),
            entityId: target.Id,
            action: eventType,
            newValue: details,
            userId: hostOperator ? "operator:private-host" : $"user:{actorUserId}",
            tenantId: target.TenantId,
            actorDisplayName: hostOperator ? "Private Server host operator" : "Identity recovery workflow",
            durableAudit: true,
            cancellationToken: cancellationToken);
    }

    private void AddLifecycleEvent(int tenantId, int targetUserId, int? actorUserId, string actorKind, string eventType, object details) =>
        db.UserLifecycleEvents.Add(new UserLifecycleEvent
        {
            TenantId = tenantId,
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            ActorKind = actorKind,
            EventType = eventType,
            DetailsJson = JsonSerializer.Serialize(details, DetailsJson),
            OccurredAtUtc = UtcNow()
        });

    private async Task<UserActionToken> FindUsableActionTokenAsync(string? rawToken, string purpose, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new BusinessRuleException("The action token is invalid or expired.");
        var hash = mfaSecurity.HashOpaqueToken(rawToken);
        if (tenantBootstrap is not null)
            await tenantBootstrap.ResolveActionTokenTenantAsync(hash, purpose, cancellationToken);
        var token = await db.UserActionTokens.SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (token is null
            || token.Purpose != purpose
            || token.ConsumedAtUtc is not null
            || token.RevokedAtUtc is not null
            || token.ExpiresAtUtc <= now)
            throw new BusinessRuleException("The action token is invalid or expired.");
        return token;
    }

    private async Task<UserAccount> RequireTargetAsync(AuthenticatedUser actor, int targetUserId, CancellationToken cancellationToken, bool includeAssignments = false)
    {
        IQueryable<UserAccount> query = db.UserAccounts;
        if (includeAssignments) query = query.Include(user => user.CompanyAccesses);
        var target = await query.SingleOrDefaultAsync(user => user.Id == targetUserId && user.TenantId == actor.TenantId, cancellationToken);
        return target ?? throw new KeyNotFoundException("User account was not found.");
    }

    private async Task EnsureEmailAvailableAsync(int tenantId, string email, CancellationToken cancellationToken)
    {
        var exists = await db.UserAccounts.AnyAsync(
            user => user.TenantId == tenantId && user.Email == email,
            cancellationToken);
        if (exists)
            throw new BusinessRuleException("The user account could not be created with the supplied details.");
    }

    private async Task<int[]> ValidateCompanyIdsAsync(int tenantId, IReadOnlyList<int>? requested, CancellationToken cancellationToken)
    {
        var ids = (requested ?? []).Where(id => id > 0).Distinct().Order().ToArray();
        if (ids.Length == 0) return [];
        var valid = await db.Companies.IgnoreQueryFilters()
            .Where(company => ids.Contains(company.Id) && company.TenantId == tenantId && !company.IsQuarantined)
            .Select(company => company.Id)
            .OrderBy(id => id)
            .ToArrayAsync(cancellationToken);
        if (!ids.SequenceEqual(valid))
            throw new BusinessRuleException("Every company assignment must belong to the current tenant and be active.");
        return ids;
    }

    private void AddCompanyAssignments(UserAccount user, IReadOnlyList<int> companyIds, DateTime now)
    {
        foreach (var companyId in companyIds)
        {
            var access = new UserCompanyAccess { UserId = user.Id, CompanyId = companyId, CreatedAt = now };
            user.CompanyAccesses.Add(access);
            db.UserCompanyAccesses.Add(access);
        }
    }

    private async Task EnsureAnotherActiveOwnerAsync(int tenantId, int excludedUserId, CancellationToken cancellationToken)
    {
        if (!await db.UserAccounts.AnyAsync(user => user.TenantId == tenantId
                && user.Id != excludedUserId
                && user.IsActive
                && user.OffboardedAtUtc == null
                && user.Role == "Owner", cancellationToken))
            throw new BusinessRuleException("The tenant must retain at least one active Owner.");
    }

    private async Task RevokeOutstandingTokensAsync(int userId, string? purpose, DateTime now, CancellationToken cancellationToken)
    {
        var tokens = await db.UserActionTokens
            .Where(token => token.UserId == userId
                && token.ConsumedAtUtc == null
                && token.RevokedAtUtc == null
                && (purpose == null || token.Purpose == purpose))
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.RevokedAtUtc = now;
    }

    private async Task RevokeSecurityStateAsync(
        UserAccount user,
        DateTime now,
        CancellationToken cancellationToken)
    {
        user.SessionVersion++;
        user.UpdatedAt = now;
        if (db.Database.IsRelational())
        {
            await db.UserMfaChallenges
                .Where(challenge => challenge.UserId == user.Id
                    && challenge.ConsumedAtUtc == null
                    && challenge.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(challenge => challenge.RevokedAtUtc, now),
                    cancellationToken);
            return;
        }

        var challenges = await db.UserMfaChallenges
            .Where(challenge => challenge.UserId == user.Id
                && challenge.ConsumedAtUtc == null
                && challenge.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in challenges)
            challenge.RevokedAtUtc = now;
    }

    private static void SetPassword(UserAccount user, string password, DateTime now)
    {
        var hashed = PasswordHasher.HashPassword(password);
        user.PasswordHash = hashed.Hash;
        user.PasswordSalt = hashed.Salt;
        user.PasswordAlgorithm = AuthService.PasswordAlgorithm;
        user.PasswordStrengthScore = 5;
        user.PasswordLastChangedAt = now;
    }

    private async Task EnsurePasswordSafeAsync(string password, CancellationToken cancellationToken)
    {
        if (passwordSafety is null) return;
        var result = await passwordSafety.CheckAsync(password, cancellationToken);
        if (result.Status == PasswordSafetyStatus.Breached)
            throw new BusinessRuleException("This password appears in a known breach and cannot be used.");
        if (result.Status == PasswordSafetyStatus.Unavailable)
            throw new BusinessRuleException("Password safety validation is temporarily unavailable. Try again before setting the password.");
    }

    private DateTime UtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }

    private static void RequireOwner(AuthenticatedUser actor)
    {
        if (!actor.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Owner access is required for user administration.");
    }

    private static string NormalizeRole(string? role)
    {
        var match = SupportedRoles.SingleOrDefault(candidate => candidate.Equals(role?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new BusinessRuleException("Role must be Owner, Accountant, Reviewer, or Client.");
    }

    private static string NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length is < 3 or > 320 || !normalized.Contains('@'))
            throw new BusinessRuleException("A valid email address is required.");
        return normalized;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        if (normalized is null || normalized.Length is < 2 or > 200)
            throw new BusinessRuleException("Display name must contain between 2 and 200 characters.");
        return normalized;
    }

    public static void ValidateStrongPassword(string? password)
    {
        if (password is null
            || password.Length < 20
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character)))
            throw new BusinessRuleException("Password must be at least 20 characters and include upper case, lower case, number, and symbol characters.");
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await operation();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private Task InTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) =>
        InTransactionAsync(async () => { await operation(); return true; }, cancellationToken);
}

public static class UserActionPurposes
{
    public const string Invitation = "Invitation";
    public const string PasswordReset = "PasswordReset";
}

public static class UserLifecycleEventTypes
{
    public const string Invited = "UserInvited";
    public const string InvitationAccepted = "UserInvitationAccepted";
    public const string Created = "UserCreated";
    public const string Activated = "UserActivated";
    public const string Deactivated = "UserDeactivated";
    public const string Unlocked = "UserUnlocked";
    public const string PasswordResetStarted = "UserPasswordResetStarted";
    public const string PasswordResetCompleted = "UserPasswordResetCompleted";
    public const string PrivateOwnerRecoveryStarted = "PrivateOwnerRecoveryStarted";
    public const string RoleChanged = "UserRoleChanged";
    public const string CompanyAssignmentsChanged = "UserCompanyAssignmentsChanged";
    public const string SessionsRevoked = "UserSessionsRevoked";
    public const string Offboarded = "UserOffboarded";
    public const string MfaEnrolled = "UserMfaEnrolled";
    public const string MfaChallengeCompleted = "UserMfaChallengeCompleted";
    public const string MfaRecoveryCodeUsed = "UserMfaRecoveryCodeUsed";
    public const string MfaRecoveryCodesRegenerated = "UserMfaRecoveryCodesRegenerated";
    public const string RecentAuthenticationCompleted = "UserRecentAuthenticationCompleted";
}

public sealed record InviteUserInput(string? Email, string? DisplayName, string? Role, IReadOnlyList<int>? CompanyIds);
public sealed record CreateUserInput(string? Email, string? DisplayName, string? Role, string? TemporaryPassword, IReadOnlyList<int>? CompanyIds);
public sealed record AcceptActionTokenInput(string? Token, string? NewPassword);
public sealed record UserProvisioningResult(UserAdministrationSummary User, string ActionToken, DateTime ExpiresAtUtc);

public sealed record UserAdministrationSummary(
    int UserId,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    bool IsLocked,
    DateTime? LockedUntilUtc,
    bool MfaEnabled,
    IReadOnlyList<int> CompanyIds,
    DateTime? InviteAcceptedAtUtc,
    DateTime? DeactivatedAtUtc,
    DateTime? OffboardedAtUtc,
    int SessionVersion)
{
    public static UserAdministrationSummary From(UserAccount user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.IsActive,
        user.MustChangePassword,
        user.LockedUntilUtc > DateTime.UtcNow,
        user.LockedUntilUtc,
        user.MfaCredential?.EnabledAtUtc is not null,
        user.CompanyAccesses.Select(access => access.CompanyId).Distinct().Order().ToArray(),
        user.InviteAcceptedAtUtc,
        user.DeactivatedAtUtc,
        user.OffboardedAtUtc,
        user.SessionVersion);
}
