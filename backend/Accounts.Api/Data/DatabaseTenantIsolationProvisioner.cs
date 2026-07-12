using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Data;

/// <summary>
/// Runs only under the controlled migrate-only process. The API credential must already exist,
/// but this step strips privileged attributes, grants only the RLS group role and installs the
/// independently supplied context-signing key in the database's protected key table.
/// </summary>
public sealed class DatabaseTenantIsolationProvisioner(
    AccountsDbContext db,
    IOptions<DatabaseTenantIsolationConfig> configuredOptions)
{
    private readonly DatabaseTenantIsolationConfig options = configuredOptions.Value;

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Required) return;
        if (!DatabaseTenantIsolationConfig.IsValid(options))
            throw new InvalidOperationException("Database tenant isolation configuration is invalid.");

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var currentRole = await ScalarStringAsync(connection, "SELECT current_user", cancellationToken);
            if (string.Equals(currentRole, options.ApplicationLoginRole, StringComparison.Ordinal))
                throw new InvalidOperationException("The migrate-only process cannot use the API application login role.");

            if (!await CurrentRoleHasMembershipAsync(
                    connection,
                    TenantIsolationPolicyCatalog.AdministratorGroupRole,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    $"The migrate-only role is not a member of '{TenantIsolationPolicyCatalog.AdministratorGroupRole}'.");
            }

            var loginExists = await RoleExistsAsync(connection, options.ApplicationLoginRole, cancellationToken);
            if (!loginExists)
                throw new InvalidOperationException($"The PostgreSQL application login role '{options.ApplicationLoginRole}' must be created before migrate-only runs.");

            await using (var roleCommand = connection.CreateCommand())
            {
                var schemaName = await ScalarStringAsync(connection, "SELECT current_schema()", cancellationToken)
                    ?? throw new InvalidOperationException("The PostgreSQL current schema could not be resolved.");
                roleCommand.CommandText = $"""
                    ALTER ROLE {QuoteIdentifier(options.ApplicationLoginRole)}
                        LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
                    REVOKE {QuoteIdentifier(TenantIsolationPolicyCatalog.AdministratorGroupRole)}
                        FROM {QuoteIdentifier(options.ApplicationLoginRole)};
                    GRANT {QuoteIdentifier(options.ApplicationGroupRole)} TO {QuoteIdentifier(options.ApplicationLoginRole)};
                    REVOKE ALL PRIVILEGES ON TABLE "__EFMigrationsHistory" FROM PUBLIC;
                    REVOKE ALL PRIVILEGES ON TABLE "__EFMigrationsHistory"
                        FROM {QuoteIdentifier(options.ApplicationLoginRole)};
                    REVOKE ALL PRIVILEGES ON TABLE "__EFMigrationsHistory"
                        FROM {QuoteIdentifier(options.ApplicationGroupRole)};
                    GRANT SELECT ON TABLE "__EFMigrationsHistory"
                        TO {QuoteIdentifier(options.ApplicationGroupRole)};
                    {BuildPrivilegeRepairSql(schemaName, options.ApplicationLoginRole)}
                    """;
                await roleCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var key = DatabaseTenantIsolationConfig.DecodeContextSigningKey(options.ContextSigningKey);
            await using var keyCommand = connection.CreateCommand();
            keyCommand.CommandText = """
                INSERT INTO tenant_rls_context_keys ("KeyId", "KeyMaterial", "UpdatedAtUtc")
                VALUES (1, @key_material, CURRENT_TIMESTAMP)
                ON CONFLICT ("KeyId") DO UPDATE
                SET "KeyMaterial" = EXCLUDED."KeyMaterial",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
                """;
            AddParameter(keyCommand, "key_material", key);
            await keyCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> RoleExistsAsync(
        DbConnection connection,
        string roleName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = @role_name)";
        AddParameter(command, "role_name", roleName);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<string?> ScalarStringAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<bool> CurrentRoleHasMembershipAsync(
        DbConnection connection,
        string roleName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_has_role(current_user, @role_name, 'MEMBER')";
        AddParameter(command, "role_name", roleName);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string BuildPrivilegeRepairSql(string schemaName, string applicationLoginRole)
    {
        var schema = QuoteIdentifier(schemaName);
        var applicationGroup = QuoteIdentifier(TenantIsolationPolicyCatalog.ApplicationGroupRole);
        var administratorGroup = QuoteIdentifier(TenantIsolationPolicyCatalog.AdministratorGroupRole);
        var applicationLogin = QuoteIdentifier(applicationLoginRole);
        var sql = new StringBuilder(32_000);
        sql.Append("REVOKE CREATE ON SCHEMA ").Append(schema).Append(" FROM PUBLIC, ")
            .Append(applicationGroup).Append(", ").Append(applicationLogin).AppendLine(";")
            .Append("GRANT USAGE ON SCHEMA ").Append(schema).Append(" TO ")
            .Append(applicationGroup).Append(", ").Append(administratorGroup).AppendLine(";");

        foreach (var policy in TenantIsolationPolicyCatalog.Policies)
        {
            var table = QuoteIdentifier(policy.TableName);
            sql.Append("REVOKE ALL PRIVILEGES ON TABLE ").Append(table).Append(" FROM PUBLIC, ")
                .Append(applicationLogin).AppendLine(";")
                .Append("GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE ").Append(table).Append(" TO ")
                .Append(applicationGroup).Append(", ").Append(administratorGroup).AppendLine(";");
        }

        sql.Append("REVOKE ALL PRIVILEGES ON TABLE tenant_rls_context_keys FROM PUBLIC, ")
            .Append(applicationGroup).Append(", ").Append(applicationLogin).AppendLine(";")
            .Append("GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE tenant_rls_context_keys TO ")
            .Append(administratorGroup).AppendLine(";");

        foreach (var function in new[]
        {
            "accounts_current_tenant_id()",
            "accounts_resolve_login_tenant(text, text)",
            "accounts_resolve_action_token_tenant(text, text)",
            "accounts_resolve_mfa_challenge_tenant(text)",
            "accounts_list_tenant_ids_for_jobs()",
            "accounts_delete_expired_anonymous_login_events()"
        })
        {
            sql.Append("REVOKE ALL ON FUNCTION ").Append(function).Append(" FROM PUBLIC, ")
                .Append(applicationLogin).AppendLine(";")
                .Append("GRANT EXECUTE ON FUNCTION ").Append(function).Append(" TO ")
                .Append(applicationGroup);
            if (function == "accounts_current_tenant_id()") sql.Append(", ").Append(administratorGroup);
            sql.AppendLine(";");
        }

        sql.Append($$"""
            DO $sequence_privileges$
            DECLARE
                sequence_name text;
            BEGIN
                FOR sequence_name IN
                    SELECT sequence_entry.relname
                    FROM pg_catalog.pg_class AS sequence_entry
                    JOIN pg_catalog.pg_namespace AS namespace_entry
                      ON namespace_entry.oid = sequence_entry.relnamespace
                    WHERE namespace_entry.nspname = current_schema()
                      AND sequence_entry.relkind = 'S'
                LOOP
                    EXECUTE format(
                        'REVOKE ALL PRIVILEGES ON SEQUENCE %I.%I FROM PUBLIC, %I',
                        current_schema(), sequence_name, {{QuoteLiteral(applicationLoginRole)}});
                    EXECUTE format(
                        'GRANT USAGE, SELECT ON SEQUENCE %I.%I TO %I, %I',
                        current_schema(), sequence_name,
                        {{QuoteLiteral(TenantIsolationPolicyCatalog.ApplicationGroupRole)}},
                        {{QuoteLiteral(TenantIsolationPolicyCatalog.AdministratorGroupRole)}});
                END LOOP;
            END
            $sequence_privileges$;
            """);
        return sql.ToString();
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string QuoteLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
