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

    // crypto-tls-to-db: outside development the DB connection must require TLS. Set this true only to
    // deliberately allow an unencrypted connection (e.g. a private encrypted network link).
    public bool AllowInsecureDatabaseConnection { get; set; }
}
