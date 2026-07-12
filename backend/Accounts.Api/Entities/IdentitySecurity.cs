using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// A one-time, server-hashed identity action token. Raw invite and password-reset
/// tokens are returned only to the initiating workflow and are never persisted.
/// </summary>
public class UserActionToken
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public required string Purpose { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public string CreatedByActorKind { get; set; } = IdentityActorKinds.User;

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}

/// <summary>
/// TOTP credential material. The shared secret is envelope-encrypted with a
/// purpose-derived deployment key; it is never logged or returned after enrollment.
/// </summary>
public class UserMfaCredential
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public required string EncryptedSecret { get; set; }
    public int SecretVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EnabledAtUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public long LastAcceptedTotpCounter { get; set; } = -1;
    public DateTime? RecoveryCodesGeneratedAtUtc { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;

    [JsonIgnore]
    public List<UserMfaRecoveryCode> RecoveryCodes { get; set; } = [];
}

/// <summary>One-time MFA recovery code. Only a keyed hash is retained.</summary>
public class UserMfaRecoveryCode
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}

/// <summary>
/// Short-lived first-factor/MFA challenge. The browser receives an opaque token;
/// only its keyed hash and bounded attempt state are stored.
/// </summary>
public class UserMfaChallenge
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public required string Purpose { get; set; }
    public required string TokenHash { get; set; }
    public int SessionVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int FailedAttempts { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}

/// <summary>
/// Immutable tenant lifecycle ledger. This deliberately carries identifiers and
/// state classifications only; email addresses, names, credentials and tokens are
/// excluded. The tamper-evident AuditLog carries the corresponding signed audit.
/// </summary>
public class UserLifecycleEvent
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int TargetUserId { get; set; }
    public int? ActorUserId { get; set; }
    public string ActorKind { get; set; } = IdentityActorKinds.User;
    public required string EventType { get; set; }
    public required string DetailsJson { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public static class IdentityActorKinds
{
    public const string User = "User";
    public const string PrivateServerHostOperator = "PrivateServerHostOperator";
}
