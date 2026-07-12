using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Data;

public sealed class DatabaseTenantIsolationVerifier(
    AccountsDbContext db,
    IOptions<DatabaseTenantIsolationConfig> configuredOptions)
{
    private readonly DatabaseTenantIsolationConfig options = configuredOptions.Value;

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Required) return;
        if (!DatabaseTenantIsolationConfig.IsValid(options))
            throw new InvalidOperationException("Database tenant isolation configuration is invalid.");

        var failures = new List<string>();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var role = await ReadRoleAsync(connection, cancellationToken);
            if (role is null)
            {
                failures.Add("The current PostgreSQL login role could not be inspected.");
            }
            else
            {
                if (!string.Equals(role.Name, options.ApplicationLoginRole, StringComparison.Ordinal))
                    failures.Add($"The API connected as '{role.Name}' instead of the configured application login role.");
                if (!role.IsApplicationMember)
                    failures.Add($"The API login is not a member of '{options.ApplicationGroupRole}'.");
                if (role.IsAdministratorMember)
                    failures.Add($"The API login is a member of privileged role '{TenantIsolationPolicyCatalog.AdministratorGroupRole}'.");
                if (role.IsSuperuser || role.BypassesRls || role.CanCreateDatabase || role.CanCreateRole || role.CanReplicate)
                    failures.Add("The API login has privileged PostgreSQL role attributes.");
            }

            if (await CurrentRoleCanCreateInSchemaAsync(connection, cancellationToken))
                failures.Add("The API login can create objects in the application schema.");
            if (await CurrentRoleCanReadContextKeyAsync(connection, cancellationToken))
                failures.Add("The API login can read the protected tenant-context signing key.");

            var migrationHistoryAccess = await ReadMigrationHistoryAccessAsync(connection, cancellationToken);
            if (!migrationHistoryAccess.CanSelect)
                failures.Add("The API login cannot read EF migration history for readiness checks.");
            if (migrationHistoryAccess.CanWrite)
                failures.Add("The API login can mutate EF migration history.");

            var databaseTables = await ReadTableSecurityAsync(connection, cancellationToken);
            var missingTables = TenantIsolationPolicyCatalog.TableNames.Except(databaseTables.Keys, StringComparer.Ordinal).Order().ToArray();
            if (missingTables.Length > 0)
                failures.Add($"Tenant RLS catalog tables are missing: {string.Join(", ", missingTables)}.");

            foreach (var expected in TenantIsolationPolicyCatalog.Policies)
            {
                if (!databaseTables.TryGetValue(expected.TableName, out var table)) continue;
                if (!table.RlsEnabled || !table.RlsForced)
                    failures.Add($"Table '{expected.TableName}' does not have ENABLE and FORCE ROW LEVEL SECURITY.");
                if (table.OwnedByCurrentLogin)
                    failures.Add($"The API login owns tenant table '{expected.TableName}'.");
            }

            var policies = await ReadPoliciesAsync(connection, cancellationToken);
            foreach (var expected in TenantIsolationPolicyCatalog.Policies)
            {
                var actual = policies.TryGetValue(expected.TableName, out var names)
                    ? names
                    : new HashSet<string>(StringComparer.Ordinal);
                foreach (var policyName in TenantIsolationPolicyCatalog.ExpectedApplicationPolicyNames(expected))
                {
                    if (!actual.Contains(policyName))
                        failures.Add($"Table '{expected.TableName}' is missing application policy '{policyName}'.");
                }
                if (!actual.Contains(TenantIsolationPolicyCatalog.AdministratorPolicyName))
                    failures.Add($"Table '{expected.TableName}' is missing the controlled migration policy.");

                var expectedNames = TenantIsolationPolicyCatalog.ExpectedApplicationPolicyNames(expected)
                    .Append(TenantIsolationPolicyCatalog.AdministratorPolicyName)
                    .ToHashSet(StringComparer.Ordinal);
                var unexpected = actual.Except(expectedNames, StringComparer.Ordinal).Order().ToArray();
                if (unexpected.Length > 0)
                    failures.Add($"Table '{expected.TableName}' has unexpected RLS policies: {string.Join(", ", unexpected)}.");
            }

            failures.AddRange(await ReadFunctionSecurityFailuresAsync(connection, options.ApplicationLoginRole, cancellationToken));

            var key = DatabaseTenantIsolationConfig.DecodeContextSigningKey(options.ContextSigningKey);
            const int probeTenantId = int.MaxValue;
            await TenantRlsConnectionInterceptor.ApplyAsync(connection, probeTenantId, key, cancellationToken);
            await using (var probe = connection.CreateCommand())
            {
                probe.CommandText = "SELECT accounts_current_tenant_id()";
                var result = await probe.ExecuteScalarAsync(cancellationToken);
                if (result is not int value || value != probeTenantId)
                    failures.Add("The database rejected the API tenant-context signing key.");
            }
            await TenantRlsConnectionInterceptor.ApplyAsync(connection, null, key, cancellationToken);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("Database tenant isolation verification failed: " + string.Join(" ", failures));
    }

    private async Task<RoleSecurity?> ReadRoleAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT role.rolname,
                   role.rolsuper,
                   role.rolbypassrls,
                   role.rolcreatedb,
                   role.rolcreaterole,
                   role.rolreplication,
                   pg_has_role(current_user, @application_role, 'MEMBER'),
                   pg_has_role(current_user, @administrator_role, 'MEMBER')
            FROM pg_catalog.pg_roles AS role
            WHERE role.rolname = current_user
            """;
        AddParameter(command, "application_role", options.ApplicationGroupRole);
        AddParameter(command, "administrator_role", TenantIsolationPolicyCatalog.AdministratorGroupRole);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new RoleSecurity(
            reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2),
            reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7));
    }

    private static async Task<bool> CurrentRoleCanCreateInSchemaAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT has_schema_privilege(current_user, current_schema(), 'CREATE')";
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<bool> CurrentRoleCanReadContextKeyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT has_table_privilege(current_user, 'tenant_rls_context_keys', 'SELECT')";
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<MigrationHistoryAccess> ReadMigrationHistoryAccessAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT has_table_privilege(current_user, '"__EFMigrationsHistory"', 'SELECT'),
                   has_table_privilege(current_user, '"__EFMigrationsHistory"', 'INSERT')
                   OR has_table_privilege(current_user, '"__EFMigrationsHistory"', 'UPDATE')
                   OR has_table_privilege(current_user, '"__EFMigrationsHistory"', 'DELETE')
                   OR has_table_privilege(current_user, '"__EFMigrationsHistory"', 'TRUNCATE')
                   OR has_table_privilege(current_user, '"__EFMigrationsHistory"', 'REFERENCES')
                   OR has_table_privilege(current_user, '"__EFMigrationsHistory"', 'TRIGGER')
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new MigrationHistoryAccess(false, true);
        return new MigrationHistoryAccess(reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task<IReadOnlyList<string>> ReadFunctionSecurityFailuresAsync(
        DbConnection connection,
        string applicationLoginRole,
        CancellationToken cancellationToken)
    {
        var expectedFunctions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accounts_current_tenant_id"] = "",
            ["accounts_resolve_login_tenant"] = "text, text",
            ["accounts_resolve_action_token_tenant"] = "text, text",
            ["accounts_resolve_mfa_challenge_tenant"] = "text",
            ["accounts_list_tenant_ids_for_jobs"] = "",
            ["accounts_delete_expired_anonymous_login_events"] = ""
        };
        const string forbiddenGlobalEmailFunction = "accounts_email_exists";
        var found = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT procedure_entry.proname,
                   pg_catalog.oidvectortypes(procedure_entry.proargtypes),
                   procedure_entry.prosecdef,
                   owner_role.rolname,
                   EXISTS (
                       SELECT 1
                       FROM aclexplode(COALESCE(
                           procedure_entry.proacl,
                           acldefault('f', procedure_entry.proowner))) AS privilege
                       WHERE privilege.grantee = 0
                         AND privilege.privilege_type = 'EXECUTE') AS public_execute
            FROM pg_catalog.pg_proc AS procedure_entry
            JOIN pg_catalog.pg_namespace AS namespace_entry
              ON namespace_entry.oid = procedure_entry.pronamespace
            JOIN pg_catalog.pg_roles AS owner_role
              ON owner_role.oid = procedure_entry.proowner
            WHERE namespace_entry.nspname = current_schema()
              AND procedure_entry.proname = ANY (@function_names)
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "function_names";
        parameter.Value = expectedFunctions.Keys.Append(forbiddenGlobalEmailFunction).ToArray();
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var arguments = reader.GetString(1);
            var signature = $"{name}({arguments})";
            if (string.Equals(name, forbiddenGlobalEmailFunction, StringComparison.Ordinal))
            {
                failures.Add($"Forbidden global email-existence function '{signature}' is present.");
                continue;
            }
            if (!expectedFunctions.TryGetValue(name, out var expectedArguments)
                || !string.Equals(arguments, expectedArguments, StringComparison.Ordinal))
            {
                failures.Add($"Unexpected database tenant function overload '{signature}' is present.");
                continue;
            }

            found.Add(name);
            if (!reader.GetBoolean(2)) failures.Add($"Database tenant function '{signature}' is not SECURITY DEFINER.");
            if (string.Equals(reader.GetString(3), applicationLoginRole, StringComparison.Ordinal))
                failures.Add($"The API login owns database tenant function '{signature}'.");
            if (reader.GetBoolean(4)) failures.Add($"Database tenant function '{signature}' is executable by PUBLIC.");
        }

        foreach (var missing in expectedFunctions.Keys.Except(found, StringComparer.Ordinal).Order())
            failures.Add($"Database tenant function '{missing}({expectedFunctions[missing]})' is missing.");
        return failures;
    }

    private static async Task<Dictionary<string, TableSecurity>> ReadTableSecurityAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_entry.relname,
                   table_entry.relrowsecurity,
                   table_entry.relforcerowsecurity,
                   table_entry.relowner = (SELECT usesysid FROM pg_catalog.pg_user WHERE usename = current_user)
            FROM pg_catalog.pg_class AS table_entry
            JOIN pg_catalog.pg_namespace AS namespace_entry ON namespace_entry.oid = table_entry.relnamespace
            WHERE namespace_entry.nspname = current_schema()
              AND table_entry.relkind IN ('r', 'p')
            """;
        var result = new Dictionary<string, TableSecurity>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = new TableSecurity(reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3));
        return result;
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadPoliciesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tablename, policyname
            FROM pg_catalog.pg_policies
            WHERE schemaname = current_schema()
            """;
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            if (!result.TryGetValue(table, out var names)) result[table] = names = new(StringComparer.Ordinal);
            names.Add(reader.GetString(1));
        }
        return result;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record RoleSecurity(
        string Name,
        bool IsSuperuser,
        bool BypassesRls,
        bool CanCreateDatabase,
        bool CanCreateRole,
        bool CanReplicate,
        bool IsApplicationMember,
        bool IsAdministratorMember);

    private sealed record TableSecurity(bool RlsEnabled, bool RlsForced, bool OwnedByCurrentLogin);

    private sealed record MigrationHistoryAccess(bool CanSelect, bool CanWrite);
}
