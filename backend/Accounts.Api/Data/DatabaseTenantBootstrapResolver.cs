using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Data;

/// <summary>
/// Narrow pre-authentication resolvers. Their database functions expose only a tenant identifier,
/// never password, MFA or token material. After resolution, ordinary EF
/// reads and writes remain subject to the same signed tenant RLS context as authenticated traffic.
/// </summary>
public sealed class DatabaseTenantBootstrapResolver(
    AccountsDbContext db,
    DatabaseTenantContext tenantContext,
    TenantRlsConnectionInterceptor rlsInterceptor)
{
    public void UseVerifiedSessionTenant(int tenantId) => tenantContext.SetResolvedTenant(tenantId);

    public Task<int?> ResolveLoginTenantAsync(
        string normalizedTenantSlug,
        string normalizedEmail,
        CancellationToken cancellationToken = default) =>
        ResolveAndSetAsync(
            "SELECT accounts_resolve_login_tenant(@tenant_slug, @lookup_value)",
            normalizedEmail,
            null,
            normalizedTenantSlug,
            cancellationToken);

    public Task<int?> ResolveActionTokenTenantAsync(
        string tokenHash,
        string purpose,
        CancellationToken cancellationToken = default) =>
        ResolveAndSetAsync(
            "SELECT accounts_resolve_action_token_tenant(@lookup_value, @lookup_purpose)",
            tokenHash,
            purpose,
            null,
            cancellationToken);

    public Task<int?> ResolveMfaChallengeTenantAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        ResolveAndSetAsync(
            "SELECT accounts_resolve_mfa_challenge_tenant(@lookup_value)",
            tokenHash,
            null,
            null,
            cancellationToken);

    public async Task<IReadOnlyList<int>> ListTenantIdsForJobsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
            return await db.Tenants.IgnoreQueryFilters()
                .OrderBy(tenant => tenant.Id)
                .Select(tenant => tenant.Id)
                .ToArrayAsync(cancellationToken);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM accounts_list_tenant_ids_for_jobs()";
            var tenantIds = new List<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0)) continue;
                var tenantId = reader.GetInt32(0);
                if (tenantId > 0) tenantIds.Add(tenantId);
            }
            return tenantIds;
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<long> DeleteExpiredAnonymousLoginEventsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational()) return 0;
        var result = await ExecuteScalarWithoutLookupAsync(
            "SELECT accounts_delete_expired_anonymous_login_events()",
            cancellationToken);
        return result switch
        {
            long value when value >= 0 => value,
            int value when value >= 0 => value,
            _ => throw new InvalidOperationException("Anonymous login-retention cleanup returned an invalid result.")
        };
    }

    private async Task<int?> ResolveAndSetAsync(
        string commandText,
        string lookupValue,
        string? purpose,
        string? tenantSlug,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return null;

        var result = await ExecuteScalarAsync(commandText, lookupValue, purpose, tenantSlug, cancellationToken);
        var tenantId = result switch
        {
            int value => value,
            long value when value is > 0 and <= int.MaxValue => (int)value,
            _ => (int?)null
        };
        if (tenantId is not > 0) return null;

        tenantContext.SetResolvedTenant(tenantId.Value);
        var connection = db.Database.GetDbConnection();
        if (connection.State == ConnectionState.Open)
            await rlsInterceptor.ApplyToOpenConnectionAsync(connection, cancellationToken);
        return tenantId;
    }

    private async Task<object?> ExecuteScalarAsync(
        string commandText,
        string lookupValue,
        string? purpose,
        string? tenantSlug,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            AddParameter(command, "lookup_value", lookupValue);
            if (purpose is not null) AddParameter(command, "lookup_purpose", purpose);
            if (tenantSlug is not null) AddParameter(command, "tenant_slug", tenantSlug);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is DBNull ? null : result;
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<object?> ExecuteScalarWithoutLookupAsync(
        string commandText,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is DBNull ? null : result;
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
