namespace Accounts.Api.Rules;

public class DatabaseStartupConfig
{
    public bool AutoMigrateOnStartup { get; set; } = true;
    public bool SeedDemoData { get; set; } = true;
    public bool AllowStartupMigrationInProduction { get; set; }
    public bool AllowDemoSeedInProduction { get; set; }
    public bool AllowDevelopmentDatabasePasswordInProduction { get; set; }
}
