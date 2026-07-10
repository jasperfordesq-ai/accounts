using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class UserAccount
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string Role { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public string PasswordAlgorithm { get; set; } = "PBKDF2-SHA256-210000";
    public int PasswordStrengthScore { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime PasswordLastChangedAt { get; set; } = DateTime.UtcNow;
    public int SessionVersion { get; set; } = 1;
    public int FailedLoginCount { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? InviteAcceptedAtUtc { get; set; }
    public DateTime? DeactivatedAtUtc { get; set; }
    public DateTime? OffboardedAtUtc { get; set; }

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;

    [JsonIgnore]
    public List<UserCompanyAccess> CompanyAccesses { get; set; } = [];

    [JsonIgnore]
    public UserMfaCredential? MfaCredential { get; set; }

    [JsonIgnore]
    public List<UserMfaRecoveryCode> MfaRecoveryCodes { get; set; } = [];

    [JsonIgnore]
    public List<UserMfaChallenge> MfaChallenges { get; set; } = [];

    [JsonIgnore]
    public List<UserActionToken> ActionTokens { get; set; } = [];
}
