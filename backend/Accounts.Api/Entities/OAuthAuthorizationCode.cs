using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// A single-use OAuth 2.1 authorization code (60 seconds) issued after a person signed in on the
/// FilingBridge consent page. Only its keyed hash is stored. Exchanging it at the token endpoint
/// starts an ordinary connector session with the posture recorded here.
/// </summary>
public class OAuthAuthorizationCode
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public required string CodeHash { get; set; }
    public required string ClientIdHash { get; set; }
    public required string ClientName { get; set; }
    public required string RedirectUri { get; set; }
    public required string CodeChallenge { get; set; }
    public required string Resource { get; set; }
    public required string AccessLevel { get; set; }
    public required string DataScope { get; set; }
    public long PasswordLastChangedAtTicks { get; set; }
    public DateTime? MfaVerifiedAtUtc { get; set; }
    public string? MfaMethod { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}
