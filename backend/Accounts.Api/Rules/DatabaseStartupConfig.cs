namespace Accounts.Api.Rules;

public class DatabaseStartupConfig
{
    public bool AutoMigrateOnStartup { get; set; }
    public bool SeedDemoData { get; set; }
    public bool SeedDemoUsers { get; set; } = true;
    public bool SeedSampleCompanies { get; set; } = true;
    public bool AllowStartupMigrationInProduction { get; set; }
    public bool AllowDemoSeedInProduction { get; set; }
    public bool AllowDevelopmentDatabasePasswordInProduction { get; set; }

    // Retained for configuration compatibility only. ProductionSafetyService rejects this legacy
    // escape hatch outside development; production must use certificate-verified PostgreSQL TLS.
    public bool AllowInsecureDatabaseConnection { get; set; }
}
