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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;
}
