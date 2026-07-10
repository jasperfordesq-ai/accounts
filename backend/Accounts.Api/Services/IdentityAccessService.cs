using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Accounts.Api.Services;

public sealed class IdentityAccessService(
    AccountsDbContext db,
    AuthService authService,
    MfaSecurityService mfaSecurity,
    AuditService auditService,
    IOptions<AuthSessionConfig> options,
    TimeProvider timeProvider,
    DatabaseTenantBootstrapResolver? tenantBootstrap = null)
{
    public const int MaximumChallengeAttempts = 5;
    private readonly AuthSessionConfig config = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdentityLoginResult> BeginLoginAsync(string? email, string? password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (tenantBootstrap is not null && !string.IsNullOrWhiteSpace(normalizedEmail))
            await tenantBootstrap.ResolveLoginTenantAsync(normalizedEmail, cancellationToken);
        var firstFactor = await authService.LoginAsync(email, password);
        if (!firstFactor.Succeeded || firstFactor.User is null)
            return IdentityLoginResult.Failed(firstFactor);

        if (!config.RequirePrivilegedMfa || !AuthService.RequiresPrivilegedMfa(firstFactor.User.Role))
            return IdentityLoginResult.Authenticated(firstFactor, firstFactor.User);

        var user = await db.UserAccounts
            .Include(candidate => candidate.MfaCredential)
            .SingleAsync(candidate => candidate.Id == firstFactor.User.UserId, cancellationToken);
        var now = UtcNow();
        if (user.LockedUntilUtc > now || !user.IsActive || user.SessionVersion != firstFactor.User.SessionVersion)
            return IdentityLoginResult.Failed(LoginResult.Failed("Invalid email or password.", user: user, accountLocked: user.LockedUntilUtc > now));

        var enrollment = user.MfaCredential?.EnabledAtUtc is null;
        string? enrollmentSecret = null;
        if (user.MfaCredential is null)
        {
            enrollmentSecret = mfaSecurity.GenerateSecret();
            user.MfaCredential = new UserMfaCredential
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                EncryptedSecret = mfaSecurity.EncryptSecret(enrollmentSecret),
                SecretVersion = mfaSecurity.ActiveSecretVersion,
                CreatedAtUtc = now
            };
            db.UserMfaCredentials.Add(user.MfaCredential);
        }
        else if (enrollment)
        {
            enrollmentSecret = DecryptAndRewrap(user.MfaCredential);
        }

        var outstanding = await db.UserMfaChallenges
            .Where(challenge => challenge.UserId == user.Id
                && challenge.ConsumedAtUtc == null
                && challenge.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in outstanding) challenge.RevokedAtUtc = now;

        var rawChallenge = mfaSecurity.CreateOpaqueToken();
        var expires = now.AddMinutes(Math.Clamp(config.MfaChallengeMinutes, 2, 15));
        db.UserMfaChallenges.Add(new UserMfaChallenge
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            Purpose = enrollment ? MfaChallengePurposes.Enrollment : MfaChallengePurposes.Login,
            TokenHash = mfaSecurity.HashOpaqueToken(rawChallenge),
            SessionVersion = user.SessionVersion,
            CreatedAtUtc = now,
            ExpiresAtUtc = expires
        });
        await db.SaveChangesAsync(cancellationToken);

        return IdentityLoginResult.Challenge(
            firstFactor,
            new MfaChallengeResponse(
                rawChallenge,
                enrollment,
                expires,
                enrollmentSecret,
                enrollmentSecret is null ? null : MfaSecurityService.BuildOtpAuthUri("Irish Accounts", user.Email, enrollmentSecret)));
    }

    public async Task<MfaCompletionResult> CompleteChallengeAsync(
        string? rawChallenge,
        string? totpCode,
        string? recoveryCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawChallenge))
            return MfaCompletionResult.Failed("The MFA challenge is invalid or expired.");
        var tokenHash = mfaSecurity.HashOpaqueToken(rawChallenge);
        if (tenantBootstrap is not null)
            await tenantBootstrap.ResolveMfaChallengeTenantAsync(tokenHash, cancellationToken);
        var challenge = await db.UserMfaChallenges
            .Include(candidate => candidate.User).ThenInclude(user => user.Tenant)
            .Include(candidate => candidate.User).ThenInclude(user => user.CompanyAccesses)
            .Include(candidate => candidate.User).ThenInclude(user => user.MfaCredential)
            .Include(candidate => candidate.User).ThenInclude(user => user.MfaRecoveryCodes)
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        var now = UtcNow();
        if (challenge is null
            || challenge.ConsumedAtUtc is not null
            || challenge.RevokedAtUtc is not null
            || challenge.ExpiresAtUtc <= now
            || challenge.FailedAttempts >= MaximumChallengeAttempts
            || !challenge.User.IsActive
            || challenge.User.OffboardedAtUtc is not null
            || challenge.User.LockedUntilUtc > now
            || challenge.User.SessionVersion != challenge.SessionVersion
            || !AuthService.RequiresPrivilegedMfa(challenge.User.Role))
            return MfaCompletionResult.Failed("The MFA challenge is invalid or expired.");

        var credential = challenge.User.MfaCredential;
        if (credential is null)
            return MfaCompletionResult.Failed("The MFA challenge is invalid or expired.");
        var secret = DecryptAndRewrap(credential);
        var enrollment = challenge.Purpose == MfaChallengePurposes.Enrollment;
        var isTotp = mfaSecurity.TryVerifyTotp(
            secret,
            totpCode,
            new DateTimeOffset(now),
            credential.LastAcceptedTotpCounter,
            out var acceptedCounter);
        UserMfaRecoveryCode? usedRecovery = null;
        if (!isTotp && !enrollment && !string.IsNullOrWhiteSpace(recoveryCode))
        {
            var hash = mfaSecurity.HashRecoveryCode(challenge.UserId, recoveryCode);
            usedRecovery = challenge.User.MfaRecoveryCodes.SingleOrDefault(code => code.CodeHash == hash && code.UsedAtUtc == null);
        }

        if (!isTotp && usedRecovery is null)
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= MaximumChallengeAttempts) challenge.RevokedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            await AuditMfaEventAsync(challenge.User, AuditEventCodes.AuthMfaChallengeFailed, new
            {
                purpose = challenge.Purpose,
                attempts = challenge.FailedAttempts,
                exhausted = challenge.RevokedAtUtc is not null
            }, cancellationToken);
            return MfaCompletionResult.Failed("The authenticator or recovery code was not accepted.");
        }

        IReadOnlyList<string> recoveryCodes = [];
        string eventType;
        if (enrollment)
        {
            credential.EnabledAtUtc = now;
            recoveryCodes = mfaSecurity.GenerateRecoveryCodes();
            foreach (var existing in challenge.User.MfaRecoveryCodes) db.UserMfaRecoveryCodes.Remove(existing);
            foreach (var code in recoveryCodes)
            {
                db.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCode
                {
                    TenantId = challenge.TenantId,
                    UserId = challenge.UserId,
                    CodeHash = mfaSecurity.HashRecoveryCode(challenge.UserId, code),
                    CreatedAtUtc = now
                });
            }
            credential.RecoveryCodesGeneratedAtUtc = now;
            eventType = UserLifecycleEventTypes.MfaEnrolled;
        }
        else if (usedRecovery is not null)
        {
            usedRecovery.UsedAtUtc = now;
            eventType = UserLifecycleEventTypes.MfaRecoveryCodeUsed;
        }
        else
        {
            eventType = UserLifecycleEventTypes.MfaChallengeCompleted;
        }

        credential.LastVerifiedAtUtc = now;
        if (isTotp) credential.LastAcceptedTotpCounter = acceptedCounter;
        challenge.ConsumedAtUtc = now;
        AddLifecycleEvent(challenge.User, challenge.User.Id, eventType, new
        {
            method = isTotp ? "totp" : "recovery",
            enrollment,
            recoveryCodesRemaining = challenge.User.MfaRecoveryCodes.Count(code => code.UsedAtUtc == null) - (usedRecovery is null ? 0 : 1)
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MfaCompletionResult.Failed("The authenticator or recovery code was not accepted.");
        }
        await AuditMfaEventAsync(challenge.User, eventType, new { method = isTotp ? "totp" : "recovery", enrollment }, cancellationToken);

        var principal = await authService.GetPrincipalAsync(challenge.UserId)
            ?? throw new InvalidOperationException("MFA completion principal could not be loaded.");
        principal = principal with
        {
            MfaVerifiedAtUtc = new DateTimeOffset(now),
            MfaMethod = isTotp ? "totp" : "recovery"
        };
        return MfaCompletionResult.Success(principal, recoveryCodes);
    }

    public async Task<ReauthenticationResult> ReauthenticateAsync(
        AuthenticatedUser currentUser,
        string? password,
        string? totpCode,
        CancellationToken cancellationToken = default)
    {
        if (!AuthService.RequiresPrivilegedMfa(currentUser.Role)
            || !await authService.VerifyCurrentPasswordAsync(currentUser.UserId, password))
            return ReauthenticationResult.Failed("Password or authenticator code was not accepted.");

        var credential = await db.UserMfaCredentials.SingleOrDefaultAsync(
            candidate => candidate.UserId == currentUser.UserId && candidate.EnabledAtUtc != null,
            cancellationToken);
        if (credential is null)
            return ReauthenticationResult.Failed("Password or authenticator code was not accepted.");
        var now = UtcNow();
        var secret = DecryptAndRewrap(credential);
        if (!mfaSecurity.TryVerifyTotp(
                secret,
                totpCode,
                new DateTimeOffset(now),
                credential.LastAcceptedTotpCounter,
                out var acceptedCounter))
            return ReauthenticationResult.Failed("Password or authenticator code was not accepted.");

        credential.LastVerifiedAtUtc = now;
        credential.LastAcceptedTotpCounter = acceptedCounter;
        var user = await db.UserAccounts.Include(candidate => candidate.Tenant).SingleAsync(candidate => candidate.Id == currentUser.UserId, cancellationToken);
        AddLifecycleEvent(user, user.Id, UserLifecycleEventTypes.RecentAuthenticationCompleted, new { method = "totp" });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ReauthenticationResult.Failed("Password or authenticator code was not accepted.");
        }
        await AuditMfaEventAsync(user, UserLifecycleEventTypes.RecentAuthenticationCompleted, new { method = "totp" }, cancellationToken);

        var principal = await authService.GetPrincipalAsync(currentUser.UserId)
            ?? throw new InvalidOperationException("Reauthenticated principal could not be loaded.");
        principal = principal with
        {
            CsrfToken = currentUser.CsrfToken,
            SessionIssuedAtUtc = currentUser.SessionIssuedAtUtc,
            LastActivityAtUtc = currentUser.LastActivityAtUtc,
            MfaVerifiedAtUtc = new DateTimeOffset(now),
            MfaMethod = "totp"
        };
        return ReauthenticationResult.Success(principal);
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(AuthenticatedUser currentUser, CancellationToken cancellationToken = default)
    {
        var credential = await db.UserMfaCredentials.SingleOrDefaultAsync(
            candidate => candidate.UserId == currentUser.UserId && candidate.EnabledAtUtc != null,
            cancellationToken) ?? throw new BusinessRuleException("MFA enrollment is required.");
        var user = await db.UserAccounts.Include(candidate => candidate.MfaRecoveryCodes).SingleAsync(candidate => candidate.Id == currentUser.UserId, cancellationToken);
        db.UserMfaRecoveryCodes.RemoveRange(user.MfaRecoveryCodes);
        var now = UtcNow();
        var rawCodes = mfaSecurity.GenerateRecoveryCodes();
        foreach (var code in rawCodes)
        {
            db.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCode
            {
                TenantId = currentUser.TenantId,
                UserId = currentUser.UserId,
                CodeHash = mfaSecurity.HashRecoveryCode(currentUser.UserId, code),
                CreatedAtUtc = now
            });
        }
        credential.RecoveryCodesGeneratedAtUtc = now;
        AddLifecycleEvent(user, currentUser.UserId, UserLifecycleEventTypes.MfaRecoveryCodesRegenerated, new { count = rawCodes.Count });
        await db.SaveChangesAsync(cancellationToken);
        await AuditMfaEventAsync(user, UserLifecycleEventTypes.MfaRecoveryCodesRegenerated, new { count = rawCodes.Count }, cancellationToken);
        return rawCodes;
    }

    private void AddLifecycleEvent(UserAccount target, int actorUserId, string eventType, object details) =>
        db.UserLifecycleEvents.Add(new UserLifecycleEvent
        {
            TenantId = target.TenantId,
            TargetUserId = target.Id,
            ActorUserId = actorUserId,
            EventType = eventType,
            DetailsJson = JsonSerializer.Serialize(details, JsonOptions),
            OccurredAtUtc = UtcNow()
        });

    private string DecryptAndRewrap(UserMfaCredential credential)
    {
        var decrypted = mfaSecurity.DecryptSecretWithRotation(credential.EncryptedSecret);
        if (decrypted.NeedsRewrap)
        {
            credential.EncryptedSecret = mfaSecurity.EncryptSecret(decrypted.Secret);
            credential.SecretVersion = mfaSecurity.ActiveSecretVersion;
        }
        return decrypted.Secret;
    }

    private Task AuditMfaEventAsync(UserAccount user, string eventType, object details, CancellationToken cancellationToken) =>
        auditService.LogAsync(
            companyId: null,
            periodId: null,
            entityType: nameof(UserAccount),
            entityId: user.Id,
            action: eventType,
            newValue: details,
            userId: $"user:{user.Id}",
            tenantId: user.TenantId,
            actorDisplayName: "Authenticated identity workflow",
            durableAudit: true,
            cancellationToken: cancellationToken);

    private DateTime UtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }
}

public static class MfaChallengePurposes
{
    public const string Enrollment = "MfaEnrollment";
    public const string Login = "MfaLogin";
}

public sealed record MfaChallengeResponse(
    string ChallengeToken,
    bool RequiresEnrollment,
    DateTime ExpiresAtUtc,
    string? EnrollmentSecret,
    string? OtpAuthUri);

public sealed record IdentityLoginResult(
    bool Succeeded,
    AuthenticatedUser? User,
    MfaChallengeResponse? MfaChallenge,
    LoginResult FirstFactor)
{
    public static IdentityLoginResult Failed(LoginResult firstFactor) => new(false, null, null, firstFactor);
    public static IdentityLoginResult Authenticated(LoginResult firstFactor, AuthenticatedUser user) => new(true, user, null, firstFactor);
    public static IdentityLoginResult Challenge(LoginResult firstFactor, MfaChallengeResponse challenge) => new(true, null, challenge, firstFactor);
}

public sealed record MfaCompletionResult(bool Succeeded, AuthenticatedUser? User, IReadOnlyList<string> RecoveryCodes, string? FailureReason)
{
    public static MfaCompletionResult Success(AuthenticatedUser user, IReadOnlyList<string> recoveryCodes) => new(true, user, recoveryCodes, null);
    public static MfaCompletionResult Failed(string reason) => new(false, null, [], reason);
}

public sealed record ReauthenticationResult(bool Succeeded, AuthenticatedUser? User, string? FailureReason)
{
    public static ReauthenticationResult Success(AuthenticatedUser user) => new(true, user, null);
    public static ReauthenticationResult Failed(string reason) => new(false, null, reason);
}
