using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Accounts.Api.Data;

/// <summary>
/// Applies a fail-closed tenant GUC every time an EF connection leaves the Npgsql pool and clears
/// it before return. PostgreSQL policies treat a missing or empty value as no tenant, so background
/// work and anonymous requests cannot see tenant rows accidentally.
/// </summary>
public sealed class TenantRlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly DatabaseTenantContext tenantContext;
    private readonly byte[] signingKey;

    public TenantRlsConnectionInterceptor(
        DatabaseTenantContext tenantContext,
        IOptions<DatabaseTenantIsolationConfig> options)
    {
        this.tenantContext = tenantContext;
        signingKey = DatabaseTenantIsolationConfig.DecodeContextSigningKey(options.Value.ContextSigningKey);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection, tenantContext.TenantId, signingKey);

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, tenantContext.TenantId, signingKey, cancellationToken);

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        Clear(connection);
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await ClearAsync(connection, CancellationToken.None);
        return result;
    }

    public void ApplyToOpenConnection(DbConnection connection) =>
        Apply(connection, tenantContext.TenantId, signingKey);

    public Task ApplyToOpenConnectionAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, tenantContext.TenantId, signingKey, cancellationToken);

    internal static void Apply(DbConnection connection, int? tenantId, ReadOnlySpan<byte> signingKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT set_config('{TenantIsolationPolicyCatalog.TenantSettingName}', @tenant_id, false),
                   set_config('{DatabaseTenantIsolationConfig.TenantSignatureSettingName}', @tenant_signature, false)
            """;
        AddContextParameters(command, connection, tenantId, signingKey);
        command.ExecuteNonQuery();
    }

    internal static async Task ApplyAsync(
        DbConnection connection,
        int? tenantId,
        ReadOnlyMemory<byte> signingKey,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT set_config('{TenantIsolationPolicyCatalog.TenantSettingName}', @tenant_id, false),
                   set_config('{DatabaseTenantIsolationConfig.TenantSignatureSettingName}', @tenant_signature, false)
            """;
        AddContextParameters(command, connection, tenantId, signingKey.Span);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Clear(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            RESET {TenantIsolationPolicyCatalog.TenantSettingName};
            RESET {DatabaseTenantIsolationConfig.TenantSignatureSettingName};
            """;
        command.ExecuteNonQuery();
    }

    private static async Task ClearAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            RESET {TenantIsolationPolicyCatalog.TenantSettingName};
            RESET {DatabaseTenantIsolationConfig.TenantSignatureSettingName};
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddContextParameters(
        DbCommand command,
        DbConnection connection,
        int? tenantId,
        ReadOnlySpan<byte> signingKey)
    {
        var tenantValue = tenantId is > 0
            ? tenantId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "";
        AddParameter(command, "tenant_id", tenantValue);
        AddParameter(
            command,
            "tenant_signature",
            tenantId is > 0 && signingKey.Length >= DatabaseTenantIsolationConfig.MinimumSigningKeyBytes
                ? Sign(tenantValue, BackendProcessId(connection), signingKey)
                : "");
    }

    internal static string Sign(string tenantId, int backendProcessId, ReadOnlySpan<byte> signingKey)
    {
        var payload = Encoding.UTF8.GetBytes($"{tenantId}:{backendProcessId}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(signingKey, payload));
    }

    private static int BackendProcessId(DbConnection connection) =>
        connection is NpgsqlConnection npgsql
            ? npgsql.ProcessID
            : throw new InvalidOperationException("Database tenant isolation requires an Npgsql connection.");

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
