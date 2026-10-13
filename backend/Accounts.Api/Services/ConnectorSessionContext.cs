using Accounts.Api.Entities;

namespace Accounts.Api.Services;

/// <summary>Posture of the connector session that authenticated the current request.</summary>
public sealed record ConnectorSessionContext(
    Guid SessionId,
    Guid FamilyId,
    string AccessLevel,
    string DataScope,
    string? ClientVersion,
    DateTime AccessTokenExpiresAtUtc = default)
{
    public const string ItemKey = "ConnectorSession";

    public static ConnectorSessionContext? Get(HttpContext context) =>
        context.Items[ItemKey] as ConnectorSessionContext;

    public bool IsReadOnly => AccessLevel == ConnectorAccessLevels.Read;
    public bool IsAdmin => AccessLevel == ConnectorAccessLevels.Admin;
    public bool HasFullDataScope => DataScope == ConnectorDataScopes.Full;
}

/// <summary>Connector attribution added to API audit payloads.</summary>
public static class ConnectorAudit
{
    public const string ReasonHeader = "X-FilingBridge-Reason";

    public static object? ClientAttribution(HttpContext context)
    {
        var connector = ConnectorSessionContext.Get(context);
        if (connector is null) return null;
        return new
        {
            kind = ConnectorClientKinds.McpConnector,
            sessionFamilyId = connector.FamilyId,
            accessLevel = connector.AccessLevel,
            dataScope = connector.DataScope,
            clientVersion = connector.ClientVersion,
            reason = Reason(context)
        };
    }

    public static string? Reason(HttpContext context)
    {
        var raw = context.Request.Headers[ReasonHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string(raw.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length == 0) return null;
        return cleaned.Length > 500 ? cleaned[..500] : cleaned;
    }
}
