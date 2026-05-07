using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class ProductionSafetyService(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<DatabaseStartupConfig> databaseStartup,
    ApiAccessService apiAccess)
{
    public IReadOnlyList<string> Validate()
    {
        if (!environment.IsProduction())
            return [];

        var failures = new List<string>();
        var dbStartup = databaseStartup.Value;
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];

        if (dbStartup.AutoMigrateOnStartup && !dbStartup.AllowStartupMigrationInProduction)
            failures.Add("DatabaseStartup:AutoMigrateOnStartup is enabled in production. Run migrations through a controlled release step or set AllowStartupMigrationInProduction=true deliberately.");

        if (dbStartup.SeedDemoData && !dbStartup.AllowDemoSeedInProduction)
            failures.Add("DatabaseStartup:SeedDemoData is enabled in production. Disable demo seeding before handling client data.");

        if (!dbStartup.AllowDevelopmentDatabasePasswordInProduction && connectionString.Contains("accounts_dev", StringComparison.OrdinalIgnoreCase))
            failures.Add("DefaultConnection appears to use the development database password in production.");

        if (allowedOrigins.Length == 0)
            failures.Add("AllowedOrigins must be explicitly configured in production.");

        if (allowedOrigins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        failures.Add("AllowedOrigins contains localhost in production. Configure the real application origin.");

        failures.AddRange(apiAccess.ValidateConfiguration());

        return failures;
    }

    public void ThrowIfUnsafe()
    {
        var failures = Validate();
        if (failures.Count > 0)
            throw new InvalidOperationException("Unsafe production configuration: " + string.Join(" ", failures));
    }
}
