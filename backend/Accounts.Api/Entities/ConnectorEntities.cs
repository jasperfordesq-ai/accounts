using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// One row per issued connector refresh token. Rows sharing <see cref="FamilyId"/> form one
/// connected agent; rotation inserts a successor and revokes the predecessor. Posture
/// (level, scope, owner) is immutable per family and protected by a database trigger.
/// </summary>
public class ConnectorSession
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public Guid FamilyId { get; set; }
    public DateTime FamilyCreatedAtUtc { get; set; }
    public string ClientKind { get; set; } = ConnectorClientKinds.McpConnector;
    public required string AccessLevel { get; set; }
    public required string DataScope { get; set; }
    public required string RefreshTokenHash { get; set; }
    public long PasswordLastChangedAtTicks { get; set; }
    public DateTime? MfaVerifiedAtUtc { get; set; }
    public string? MfaMethod { get; set; }
    public string? DeviceLabel { get; set; }
    public string? ClientVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
    public Guid? RotatedFromSessionId { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}

/// <summary>A pending or granted human confirmation for one exact connector request.</summary>
public class ConnectorActionApproval
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public Guid SessionFamilyId { get; set; }
    public required string RequestDigest { get; set; }
    public required string Summary { get; set; }
    public required string Method { get; set; }
    public required string RoutePattern { get; set; }
    public string? ResourceId { get; set; }
    /// <summary>Company the request addressed, for reporting only; the row is owned by the user.</summary>
    public int? TargetCompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}

/// <summary>Append-only record of connector writes, refusals and personal-data reads.</summary>
public class ConnectorActivityEvent
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid SessionFamilyId { get; set; }
    public required string AccessLevel { get; set; }
    public required string DataScope { get; set; }
    public required string Method { get; set; }
    public required string RoutePattern { get; set; }
    public string? ResourceId { get; set; }
    /// <summary>Company the request addressed, for reporting only; the row is owned by the user.</summary>
    public int? TargetCompanyId { get; set; }
    public int StatusCode { get; set; }
    public string? RequestId { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;
}

public static class ConnectorClientKinds
{
    public const string McpConnector = "McpConnector";
}

public static class ConnectorAccessLevels
{
    public const string Read = "Read";
    public const string Write = "Write";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Read, Write, Admin];

    public static string? Normalize(string? value) =>
        All.FirstOrDefault(level => level.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ConnectorDataScopes
{
    public const string Withheld = "Withheld";
    public const string Full = "Full";

    public static readonly IReadOnlyList<string> All = [Withheld, Full];

    public static string? Normalize(string? value) =>
        All.FirstOrDefault(scope => scope.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ConnectorRevocationReasons
{
    public const string Rotated = "Rotated";
    public const string Logout = "Logout";
    public const string ReuseDetected = "ReuseDetected";
    public const string SecurityStateRevoked = "SecurityStateRevoked";
    public const string UserRevoked = "UserRevoked";
}
