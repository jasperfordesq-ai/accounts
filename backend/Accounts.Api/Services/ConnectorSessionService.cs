using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed record ConnectorLoginInput(
    string? TenantSlug,
    string? Email,
    string? Password,
    string? AccessLevel,
    string? DataScope,
    string? TotpCode,
    string? RecoveryCode,
    string? DeviceLabel);

public sealed record ConnectorRefreshInput(string? RefreshToken);

public sealed record ConnectorLogoutInput(string? RefreshToken);

public sealed record ConnectorApproveInput(Guid ApprovalId, string? Password, string? TotpCode);

public sealed record ConnectorSessionPosture(
    Guid SessionId,
    Guid FamilyId,
    string ClientKind,
    string AccessLevel,
    string DataScope,
    string Role,
    string TenantSlug,
    int UserId,
    bool MfaVerified,
    DateTime AccessTokenExpiresAtUtc,
    int WriteBudgetPerMinute,
    int ApprovalMinutes);

public sealed record ConnectorLoginResponse(
    AuthResponse User,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    ConnectorSessionPosture Session);

public sealed record ConnectorSessionSummary(
    Guid FamilyId,
    int UserId,
    string AccessLevel,
    string DataScope,
    string? DeviceLabel,
    string? ClientVersion,
    DateTime ConnectedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record ConnectorApprovalView(
    Guid ApprovalId,
    string Summary,
    string Method,
    string RoutePattern,
    string? ResourceId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? ConsumedAtUtc);

public sealed record ConnectorApprovalGrant(Guid ApprovalId, string Summary, DateTime ExpiresAtUtc);

public sealed record ConnectorLoginResult(
    bool Succeeded,
    ConnectorLoginResponse? Response,
    int StatusCode,
    string? Code,
    string? Error)
{
    public static ConnectorLoginResult Success(ConnectorLoginResponse response) => new(true, response, 200, null, null);
    public static ConnectorLoginResult Refused(int statusCode, string code, string error) => new(false, null, statusCode, code, error);
}

public static class ConnectorErrorCodes
{
    public const string InvalidArguments = "INVALID_ARGUMENTS";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string SecondFactorRequired = "SECOND_FACTOR_REQUIRED";
    public const string SecondFactorRejected = "SECOND_FACTOR_REJECTED";
    public const string MfaEnrolmentRequired = "MFA_ENROLMENT_REQUIRED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string DataScopeForbidden = "DATA_SCOPE_FORBIDDEN";
    public const string SessionInvalid = "CONNECTOR_SESSION_INVALID";
    public const string BrowserClientRejected = "BROWSER_CLIENT_REJECTED";
    public const string SessionReadOnly = "SESSION_READ_ONLY";
    public const string SessionLevelTooLow = "SESSION_LEVEL_TOO_LOW";
    public const string DataScopeWithheld = "DATA_SCOPE_WITHHELD";
    public const string NotPermitted = "CONNECTOR_NOT_PERMITTED";
    public const string WriteLimit = "CONNECTOR_WRITE_LIMIT";
    public const string ApprovalRequired = "APPROVAL_REQUIRED";
    public const string ApprovalRefused = "APPROVAL_REFUSED";
    public const string ApprovalNotFound = "APPROVAL_NOT_FOUND";
}

/// <summary>
/// Issues, rotates, validates and revokes MCP connector sessions, and grants/spends per-action
/// approvals. Every credential path reuses the browser's identity services (PBKDF2, lockout, MFA
/// challenge, replay-protected TOTP) so the connector can never be weaker than the web sign-in.
/// </summary>
public sealed class ConnectorSessionService
{
    private readonly AccountsDbContext db;
    private readonly AuthService authService;
    private readonly IdentityAccessService identityAccess;
    private readonly MfaSecurityService mfaSecurity;
    private readonly AuditService auditService;
    private readonly ConnectorConfig config;
    private readonly TimeProvider timeProvider;
    private readonly DatabaseTenantBootstrapResolver? tenantBootstrap;
    private readonly ConnectorTokenCodec? codec;
    private readonly byte[]? refreshHashKey;

    public ConnectorSessionService(
        AccountsDbContext db,
        AuthService authService,
        IdentityAccessService identityAccess,
        MfaSecurityService mfaSecurity,
        AuditService auditService,
        IOptions<ConnectorConfig> options,
        TimeProvider timeProvider,
        DatabaseTenantBootstrapResolver? tenantBootstrap = null)
    {
        this.db = db;
        this.authService = authService;
        this.identityAccess = identityAccess;
        this.mfaSecurity = mfaSecurity;
        this.auditService = auditService;
        config = options.Value;
        this.timeProvider = timeProvider;
        this.tenantBootstrap = tenantBootstrap;
        if (AuthSessionKey.HasStrongKey(config.SigningKey))
        {
            var connectorKey = AuthSessionKey.DecodeRequired(config.SigningKey);
            codec = new ConnectorTokenCodec(connectorKey);
            // Refresh-token hashes are keyed from the connector signing key, which Private Server
            // restore and host recovery rotate. A restored database therefore cannot revive an agent
            // that was disconnected after the backup point.
            refreshHashKey = System.Security.Cryptography.HMACSHA256.HashData(
                connectorKey, System.Text.Encoding.ASCII.GetBytes("accounts:connector:refresh-token-hash:v1"));
        }
    }

    public bool IsAvailable => config.Enabled && codec is not null;

    public ConnectorConfig Config => config;

    public async Task<ConnectorLoginResult> LoginAsync(
        ConnectorLoginInput input,
        string? clientVersion,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return ConnectorLoginResult.Refused(404, ConnectorErrorCodes.SessionInvalid, "The connector is not enabled on this server.");
        var accessLevel = ConnectorAccessLevels.Normalize(input.AccessLevel);
        var dataScope = ConnectorDataScopes.Normalize(input.DataScope ?? ConnectorDataScopes.Withheld);
        if (accessLevel is null || dataScope is null)
            return ConnectorLoginResult.Refused(400, ConnectorErrorCodes.InvalidArguments,
                "accessLevel must be Read, Write or Admin and dataScope must be Withheld or Full.");

        var firstFactor = await identityAccess.BeginLoginAsync(input.TenantSlug, input.Email, input.Password, cancellationToken);
        if (!firstFactor.Succeeded)
            return ConnectorLoginResult.Refused(401, ConnectorErrorCodes.InvalidCredentials, AuthService.InvalidLoginMessage);

        AuthenticatedUser principal;
        if (firstFactor.User is not null)
        {
            principal = firstFactor.User;
        }
        else
        {
            var challenge = firstFactor.MfaChallenge!;
            if (challenge.RequiresEnrollment)
                return ConnectorLoginResult.Refused(403, ConnectorErrorCodes.MfaEnrolmentRequired,
                    "Set up an authenticator in the FilingBridge web app before connecting an agent.");
            if (string.IsNullOrWhiteSpace(input.TotpCode) && string.IsNullOrWhiteSpace(input.RecoveryCode))
                return ConnectorLoginResult.Refused(401, ConnectorErrorCodes.SecondFactorRequired,
                    "An authenticator code or recovery code is required for this role.");
            var completion = await identityAccess.CompleteChallengeAsync(
                challenge.ChallengeToken, input.TotpCode, input.RecoveryCode, cancellationToken);
            if (!completion.Succeeded || completion.User is null)
                return ConnectorLoginResult.Refused(401, ConnectorErrorCodes.SecondFactorRejected,
                    "The authenticator or recovery code was not accepted.");
            principal = completion.User;
        }

        if (principal.MustChangePassword)
            return ConnectorLoginResult.Refused(403, ConnectorErrorCodes.PasswordChangeRequired,
                "Change the temporary password in the FilingBridge web app before connecting an agent.");
        if (dataScope == ConnectorDataScopes.Full
            && !principal.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase))
            return ConnectorLoginResult.Refused(403, ConnectorErrorCodes.DataScopeForbidden,
                "Only an Owner can connect with full personal-data access.");

        var now = UtcNow();
        var familyId = Guid.NewGuid();
        var rawRefresh = ConnectorTokenCodec.CreateRefreshToken(principal.TenantId);
        var session = new ConnectorSession
        {
            Id = Guid.NewGuid(),
            TenantId = principal.TenantId,
            UserId = principal.UserId,
            FamilyId = familyId,
            FamilyCreatedAtUtc = now,
            AccessLevel = accessLevel,
            DataScope = dataScope,
            RefreshTokenHash = HashRefreshToken(rawRefresh),
            PasswordLastChangedAtTicks = principal.PasswordLastChangedAt.Ticks,
            MfaVerifiedAtUtc = principal.MfaVerifiedAtUtc?.UtcDateTime,
            MfaMethod = principal.MfaMethod,
            DeviceLabel = Truncate(input.DeviceLabel, 120),
            ClientVersion = Truncate(clientVersion, 40),
            CreatedAtUtc = now,
            ExpiresAtUtc = RefreshExpiry(now, now)
        };
        db.ConnectorSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(principal, AuditEventCodes.ConnectorSessionIssued, new
        {
            sessionFamilyId = familyId,
            accessLevel,
            dataScope,
            clientVersion = session.ClientVersion
        }, cancellationToken);

        return ConnectorLoginResult.Success(BuildResponse(principal, session, rawRefresh, now));
    }

    public async Task<ConnectorLoginResponse?> RefreshAsync(string? rawRefresh, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return null;
        var tenantId = ConnectorTokenCodec.ReadRefreshTokenTenant(rawRefresh);
        if (tenantId is null) return null;
        tenantBootstrap?.UseVerifiedSessionTenant(tenantId.Value);

        var hash = HashRefreshToken(rawRefresh!);
        var current = await db.ConnectorSessions.SingleOrDefaultAsync(
            session => session.RefreshTokenHash == hash && session.TenantId == tenantId, cancellationToken);
        var now = UtcNow();
        if (current is null) return null;
        if (current.RevokedAtUtc is not null)
        {
            if (current.RevocationReason == ConnectorRevocationReasons.Rotated)
            {
                await RevokeFamilyInternalAsync(current.FamilyId, ConnectorRevocationReasons.ReuseDetected, now, cancellationToken);
                await AuditForUserAsync(current.UserId, current.TenantId, AuditEventCodes.ConnectorRefreshReuseDetected,
                    new { sessionFamilyId = current.FamilyId }, cancellationToken);
            }
            return null;
        }
        if (current.ExpiresAtUtc <= now) return null;

        var principal = await LoadValidPrincipalAsync(current.UserId, current.TenantId, current.PasswordLastChangedAtTicks, now, cancellationToken);
        if (principal is null)
        {
            await RevokeFamilyInternalAsync(current.FamilyId, ConnectorRevocationReasons.SecurityStateRevoked, now, cancellationToken);
            return null;
        }

        if (!await TryRevokeSessionRowAsync(current, ConnectorRevocationReasons.Rotated, now, cancellationToken))
        {
            // Another process spent this refresh token first: treat it as reuse.
            await RevokeFamilyInternalAsync(current.FamilyId, ConnectorRevocationReasons.ReuseDetected, now, cancellationToken);
            return null;
        }

        var rawNext = ConnectorTokenCodec.CreateRefreshToken(current.TenantId);
        var successor = new ConnectorSession
        {
            Id = Guid.NewGuid(),
            TenantId = current.TenantId,
            UserId = current.UserId,
            FamilyId = current.FamilyId,
            FamilyCreatedAtUtc = current.FamilyCreatedAtUtc,
            ClientKind = current.ClientKind,
            AccessLevel = current.AccessLevel,
            DataScope = current.DataScope,
            RefreshTokenHash = HashRefreshToken(rawNext),
            PasswordLastChangedAtTicks = current.PasswordLastChangedAtTicks,
            MfaVerifiedAtUtc = current.MfaVerifiedAtUtc,
            MfaMethod = current.MfaMethod,
            DeviceLabel = current.DeviceLabel,
            ClientVersion = current.ClientVersion,
            CreatedAtUtc = now,
            LastUsedAtUtc = now,
            ExpiresAtUtc = RefreshExpiry(now, current.FamilyCreatedAtUtc),
            RotatedFromSessionId = current.Id
        };
        if (successor.ExpiresAtUtc <= now) return null;
        db.ConnectorSessions.Add(successor);
        await db.SaveChangesAsync(cancellationToken);

        // Rotation is two writes (revoke predecessor, insert successor). A disconnect, reuse
        // detection or security revocation that ran between them saw no active row, so re-check
        // the family now that the successor exists and end it if anything else revoked the family.
        var familyEndedElsewhere = await db.ConnectorSessions.AsNoTracking().AnyAsync(session =>
            session.FamilyId == current.FamilyId
            && session.RevokedAtUtc != null
            && session.RevocationReason != ConnectorRevocationReasons.Rotated, cancellationToken);
        if (familyEndedElsewhere)
        {
            await RevokeFamilyInternalAsync(current.FamilyId, ConnectorRevocationReasons.SecurityStateRevoked, UtcNow(), cancellationToken);
            return null;
        }

        var connectedPrincipal = principal with
        {
            MfaVerifiedAtUtc = successor.MfaVerifiedAtUtc is { } verified ? new DateTimeOffset(verified, TimeSpan.Zero) : null,
            MfaMethod = successor.MfaMethod
        };
        return BuildResponse(connectedPrincipal, successor, rawNext, now);
    }

    public async Task<(AuthenticatedUser User, ConnectorSessionContext Session)?> AuthenticateAsync(
        string? bearer,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return null;
        var now = UtcNow();
        var payload = codec!.ReadAccessToken(bearer, new DateTimeOffset(now, TimeSpan.Zero));
        if (payload is null) return null;
        tenantBootstrap?.UseVerifiedSessionTenant(payload.TenantId);

        var session = await db.ConnectorSessions.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == payload.SessionId, cancellationToken);
        if (session is null
            || session.TenantId != payload.TenantId
            || session.UserId != payload.UserId
            || session.FamilyId != payload.FamilyId
            || session.RevokedAtUtc is not null
            || session.ExpiresAtUtc <= now
            || session.PasswordLastChangedAtTicks != payload.PasswordLastChangedAtTicks)
            return null;

        var principal = await LoadValidPrincipalAsync(session.UserId, session.TenantId, session.PasswordLastChangedAtTicks, now, cancellationToken);
        if (principal is null) return null;

        if (session.LastUsedAtUtc is null || now - session.LastUsedAtUtc.Value > TimeSpan.FromMinutes(1))
            await TouchAsync(session.Id, now, cancellationToken);

        return (
            principal with
            {
                CsrfToken = "",
                MfaVerifiedAtUtc = session.MfaVerifiedAtUtc is { } verified ? new DateTimeOffset(verified, TimeSpan.Zero) : null,
                MfaMethod = session.MfaMethod
            },
            new ConnectorSessionContext(session.Id, session.FamilyId, session.AccessLevel, session.DataScope, session.ClientVersion,
                DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds).UtcDateTime));
    }

    public ConnectorSessionPosture DescribePosture(AuthenticatedUser user, ConnectorSessionContext connector, DateTime accessTokenExpiresAtUtc) =>
        new(
            connector.SessionId,
            connector.FamilyId,
            ConnectorClientKinds.McpConnector,
            connector.AccessLevel,
            connector.DataScope,
            user.Role,
            user.TenantSlug,
            user.UserId,
            user.MfaVerifiedAtUtc is not null,
            accessTokenExpiresAtUtc,
            config.WriteBudgetPerMinute,
            config.ApprovalMinutes);

    public async Task LogoutAsync(
        AuthenticatedUser? user,
        ConnectorSessionContext? connector,
        string? rawRefresh,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        if (connector is not null && user is not null)
        {
            await RevokeFamilyInternalAsync(connector.FamilyId, ConnectorRevocationReasons.Logout, now, cancellationToken);
            await AuditAsync(user, AuditEventCodes.ConnectorSessionRevoked, new { sessionFamilyId = connector.FamilyId, reason = ConnectorRevocationReasons.Logout }, cancellationToken);
        }

        var tenantId = ConnectorTokenCodec.ReadRefreshTokenTenant(rawRefresh);
        if (tenantId is null) return;
        if (user is null) tenantBootstrap?.UseVerifiedSessionTenant(tenantId.Value);
        else if (user.TenantId != tenantId) return;
        var hash = HashRefreshToken(rawRefresh!);
        var session = await db.ConnectorSessions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RefreshTokenHash == hash && candidate.TenantId == tenantId, cancellationToken);
        if (session is null || (user is not null && session.UserId != user.UserId)) return;
        await RevokeFamilyInternalAsync(session.FamilyId, ConnectorRevocationReasons.Logout, now, cancellationToken);
    }

    public async Task<IReadOnlyList<ConnectorSessionSummary>> ListSessionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var rows = await db.ConnectorSessions.AsNoTracking()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null && session.ExpiresAtUtc > now)
            .OrderByDescending(session => session.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(session => new ConnectorSessionSummary(
                session.FamilyId,
                session.UserId,
                session.AccessLevel,
                session.DataScope,
                session.DeviceLabel,
                session.ClientVersion,
                session.FamilyCreatedAtUtc,
                session.LastUsedAtUtc,
                session.ExpiresAtUtc))
            .ToList();
    }

    public async Task<bool> RevokeFamilyAsync(AuthenticatedUser actor, int userId, Guid familyId, CancellationToken cancellationToken = default)
    {
        var exists = await db.ConnectorSessions.AsNoTracking()
            .AnyAsync(session => session.FamilyId == familyId && session.UserId == userId && session.TenantId == actor.TenantId, cancellationToken);
        if (!exists) return false;
        await RevokeFamilyInternalAsync(familyId, ConnectorRevocationReasons.UserRevoked, UtcNow(), cancellationToken);
        await AuditAsync(actor, AuditEventCodes.ConnectorSessionRevoked,
            new { sessionFamilyId = familyId, targetUserId = userId, reason = ConnectorRevocationReasons.UserRevoked }, cancellationToken);
        return true;
    }

    public async Task RevokeAllForUserAsync(int userId, string reason, CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        if (db.Database.IsRelational())
        {
            await db.ConnectorSessions
                .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.RevokedAtUtc, now)
                    .SetProperty(session => session.RevocationReason, reason), cancellationToken);
            return;
        }

        foreach (var session in await db.ConnectorSessions.Where(session => session.UserId == userId && session.RevokedAtUtc == null).ToListAsync(cancellationToken))
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = reason;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConnectorApprovalView?> DescribeApprovalAsync(AuthenticatedUser user, Guid approvalId, CancellationToken cancellationToken = default)
    {
        var approval = await db.ConnectorActionApprovals.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == approvalId && candidate.UserId == user.UserId && candidate.TenantId == user.TenantId,
            cancellationToken);
        return approval is null ? null : ToView(approval);
    }

    public async Task<IReadOnlyList<ConnectorApprovalView>> ListPendingApprovalsAsync(AuthenticatedUser user, CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var rows = await db.ConnectorActionApprovals.AsNoTracking()
            .Where(candidate => candidate.UserId == user.UserId
                && candidate.TenantId == user.TenantId
                && candidate.ConsumedAtUtc == null
                && candidate.ExpiresAtUtc > now)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
        return rows.Select(ToView).ToList();
    }

    /// <summary>
    /// Grants an approval after password + TOTP. Credentials are always verified first, so the
    /// expensive work and the response are the same whether or not the approval exists.
    /// </summary>
    public async Task<ConnectorApprovalGrant?> GrantApprovalAsync(
        AuthenticatedUser user,
        Guid approvalId,
        string? password,
        string? totpCode,
        CancellationToken cancellationToken = default)
    {
        var reauthenticated = await identityAccess.ReauthenticateAsync(user, password, totpCode, cancellationToken);
        var now = UtcNow();
        if (!reauthenticated.Succeeded)
        {
            await AuditAsync(user, AuditEventCodes.ConnectorApprovalRefused, new { }, cancellationToken);
            return null;
        }

        int updated;
        if (db.Database.IsRelational())
        {
            updated = await db.ConnectorActionApprovals
                .Where(candidate => candidate.Id == approvalId
                    && candidate.TenantId == user.TenantId
                    && candidate.UserId == user.UserId
                    && candidate.ApprovedAtUtc == null
                    && candidate.ConsumedAtUtc == null
                    && candidate.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ApprovedAtUtc, now), cancellationToken);
        }
        else
        {
            var tracked = await db.ConnectorActionApprovals.SingleOrDefaultAsync(candidate => candidate.Id == approvalId
                && candidate.TenantId == user.TenantId
                && candidate.UserId == user.UserId
                && candidate.ApprovedAtUtc == null
                && candidate.ConsumedAtUtc == null
                && candidate.ExpiresAtUtc > now, cancellationToken);
            if (tracked is not null)
            {
                tracked.ApprovedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            updated = tracked is null ? 0 : 1;
        }

        if (updated != 1)
        {
            await AuditAsync(user, AuditEventCodes.ConnectorApprovalRefused, new { }, cancellationToken);
            return null;
        }

        var approval = await db.ConnectorActionApprovals.AsNoTracking().SingleAsync(candidate => candidate.Id == approvalId, cancellationToken);
        await AuditAsync(user, AuditEventCodes.ConnectorApprovalGranted,
            new { approvalId, method = approval.Method, routePattern = approval.RoutePattern, resourceId = approval.ResourceId }, cancellationToken);
        return new ConnectorApprovalGrant(approval.Id, approval.Summary, approval.ExpiresAtUtc);
    }

    /// <summary>Spends a granted approval for exactly this request. Returns false on any mismatch.</summary>
    public async Task<bool> TrySpendApprovalAsync(
        AuthenticatedUser user,
        ConnectorSessionContext connector,
        Guid approvalId,
        string digest,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        if (db.Database.IsRelational())
        {
            var updated = await db.ConnectorActionApprovals
                .Where(candidate => candidate.Id == approvalId
                    && candidate.TenantId == user.TenantId
                    && candidate.UserId == user.UserId
                    && candidate.SessionFamilyId == connector.FamilyId
                    && candidate.RequestDigest == digest
                    && candidate.ApprovedAtUtc != null
                    && candidate.ConsumedAtUtc == null
                    && candidate.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConsumedAtUtc, now), cancellationToken);
            return updated == 1;
        }

        var tracked = await db.ConnectorActionApprovals.SingleOrDefaultAsync(candidate => candidate.Id == approvalId
            && candidate.TenantId == user.TenantId
            && candidate.UserId == user.UserId
            && candidate.SessionFamilyId == connector.FamilyId
            && candidate.RequestDigest == digest
            && candidate.ApprovedAtUtc != null
            && candidate.ConsumedAtUtc == null
            && candidate.ExpiresAtUtc > now, cancellationToken);
        if (tracked is null) return false;
        tracked.ConsumedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Finds the open approval for (family, digest) or creates one. Callers must use a fresh
    /// DbContext scope because the request transaction is rolled back after the 428.
    /// </summary>
    public async Task<ConnectorActionApproval> MintApprovalAsync(
        AuthenticatedUser user,
        ConnectorSessionContext connector,
        string digest,
        string method,
        string routePattern,
        string summary,
        string? resourceId,
        int? companyId,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var expires = now.AddMinutes(config.ApprovalMinutes);
        var existing = await db.ConnectorActionApprovals.SingleOrDefaultAsync(candidate =>
            candidate.SessionFamilyId == connector.FamilyId
            && candidate.RequestDigest == digest
            && candidate.ConsumedAtUtc == null, cancellationToken);
        if (existing is not null)
        {
            if (existing.ExpiresAtUtc > now) return existing;
            if (existing.ApprovedAtUtc is null)
            {
                existing.ExpiresAtUtc = expires;
                existing.CreatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                return existing;
            }

            // An approved-but-expired approval is closed so a fresh, unapproved one can be issued.
            existing.ConsumedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        var approval = new ConnectorActionApproval
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.UserId,
            SessionFamilyId = connector.FamilyId,
            RequestDigest = digest,
            Summary = Truncate(summary, 500) ?? method,
            Method = method.ToUpperInvariant(),
            RoutePattern = Truncate(routePattern, 300) ?? "",
            ResourceId = Truncate(resourceId, 200),
            TargetCompanyId = companyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = expires
        };
        db.ConnectorActionApprovals.Add(approval);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(approval).State = EntityState.Detached;
            return await db.ConnectorActionApprovals.AsNoTracking().SingleAsync(candidate =>
                candidate.SessionFamilyId == connector.FamilyId
                && candidate.RequestDigest == digest
                && candidate.ConsumedAtUtc == null, cancellationToken);
        }

        await AuditAsync(user, AuditEventCodes.ConnectorApprovalRequested,
            new { approvalId = approval.Id, method = approval.Method, routePattern = approval.RoutePattern, resourceId }, cancellationToken);
        return approval;
    }

    private ConnectorLoginResponse BuildResponse(AuthenticatedUser principal, ConnectorSession session, string rawRefresh, DateTime now)
    {
        var accessExpires = now.AddMinutes(config.AccessTokenMinutes);
        if (accessExpires > session.ExpiresAtUtc) accessExpires = session.ExpiresAtUtc;
        var accessToken = codec!.CreateAccessToken(new ConnectorAccessPayload(
            session.Id,
            session.FamilyId,
            session.UserId,
            session.TenantId,
            new DateTimeOffset(now, TimeSpan.Zero).ToUnixTimeSeconds(),
            new DateTimeOffset(accessExpires, TimeSpan.Zero).ToUnixTimeSeconds(),
            session.PasswordLastChangedAtTicks));
        var connector = new ConnectorSessionContext(session.Id, session.FamilyId, session.AccessLevel, session.DataScope, session.ClientVersion);
        return new ConnectorLoginResponse(
            AuthResponse.From(principal),
            accessToken,
            accessExpires,
            rawRefresh,
            session.ExpiresAtUtc,
            DescribePosture(principal, connector, accessExpires));
    }

    private async Task<AuthenticatedUser?> LoadValidPrincipalAsync(
        int userId,
        int tenantId,
        long passwordLastChangedAtTicks,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var state = await db.UserAccounts.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.TenantId, user.IsActive, user.OffboardedAtUtc, user.LockedUntilUtc, user.PasswordLastChangedAt, user.MustChangePassword })
            .SingleOrDefaultAsync(cancellationToken);
        if (state is null
            || state.TenantId != tenantId
            || !state.IsActive
            || state.OffboardedAtUtc is not null
            || state.LockedUntilUtc > now
            || state.MustChangePassword
            || state.PasswordLastChangedAt.Ticks != passwordLastChangedAtTicks)
            return null;
        return await authService.GetPrincipalAsync(userId);
    }

    private async Task<bool> TryRevokeSessionRowAsync(ConnectorSession session, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.ConnectorSessions
                .Where(candidate => candidate.Id == session.Id && candidate.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.RevokedAtUtc, now)
                    .SetProperty(candidate => candidate.RevocationReason, reason), cancellationToken);
            return updated == 1;
        }

        if (session.RevokedAtUtc is not null) return false;
        session.RevokedAtUtc = now;
        session.RevocationReason = reason;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task RevokeFamilyInternalAsync(Guid familyId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.ConnectorSessions
                .Where(session => session.FamilyId == familyId && session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.RevokedAtUtc, now)
                    .SetProperty(session => session.RevocationReason, reason), cancellationToken);
            return;
        }

        foreach (var session in await db.ConnectorSessions.Where(session => session.FamilyId == familyId && session.RevokedAtUtc == null).ToListAsync(cancellationToken))
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = reason;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task TouchAsync(Guid sessionId, DateTime now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.ConnectorSessions
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.LastUsedAtUtc, now), cancellationToken);
            return;
        }

        var tracked = await db.ConnectorSessions.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
        if (tracked is null) return;
        tracked.LastUsedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private DateTime RefreshExpiry(DateTime now, DateTime familyCreatedAt)
    {
        var idle = now.AddDays(config.RefreshIdleDays);
        var absolute = familyCreatedAt.AddDays(config.FamilyAbsoluteDays);
        return idle < absolute ? idle : absolute;
    }

    private string HashRefreshToken(string raw) =>
        Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(
            refreshHashKey ?? throw new InvalidOperationException("The connector is not configured."),
            System.Text.Encoding.UTF8.GetBytes(raw.Trim())));

    private Task AuditAsync(AuthenticatedUser user, string action, object details, CancellationToken cancellationToken) =>
        auditService.LogAsync(
            companyId: null,
            periodId: null,
            entityType: "ConnectorSession",
            entityId: user.UserId,
            action: action,
            newValue: details,
            userId: AuthenticatedIdentity.AuditUserId(user),
            tenantId: user.TenantId,
            actorDisplayName: "MCP connector",
            durableAudit: true,
            cancellationToken: cancellationToken);

    private Task AuditForUserAsync(int userId, int tenantId, string action, object details, CancellationToken cancellationToken) =>
        auditService.LogAsync(
            companyId: null,
            periodId: null,
            entityType: "ConnectorSession",
            entityId: userId,
            action: action,
            newValue: details,
            userId: $"user:{userId}",
            tenantId: tenantId,
            actorDisplayName: "MCP connector",
            durableAudit: true,
            cancellationToken: cancellationToken);

    private static ConnectorApprovalView ToView(ConnectorActionApproval approval) => new(
        approval.Id,
        approval.Summary,
        approval.Method,
        approval.RoutePattern,
        approval.ResourceId,
        approval.CreatedAtUtc,
        approval.ExpiresAtUtc,
        approval.ApprovedAtUtc,
        approval.ConsumedAtUtc);

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (trimmed.Length == 0) return null;
        return trimmed.Length > length ? trimmed[..length] : trimmed;
    }

    private DateTime UtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }
}
