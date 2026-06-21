using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

public static class DatabaseConnectionConfig
{
    private const string DevelopmentConnectionString = "Host=localhost;Port=5433;Database=accounts;Username=accounts;Password=accounts_dev";

    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        if (environment.IsDevelopment())
            return DevelopmentConnectionString;

        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required outside Development.");
    }
}

public static class CorsOriginConfig
{
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://localhost:5174"
    ];

    public static string[] Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredOrigins = configuration
            .GetSection("AllowedOrigins")
            .Get<string[]>()
            ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (configuredOrigins.Length > 0)
            return configuredOrigins;

        return environment.IsDevelopment() ? DevelopmentOrigins : [];
    }
}

public static class FileBackedConfiguration
{
    private const string FileSuffix = "_FILE";

    public static void AddFileBackedEnvironmentVariables(ConfigurationManager configuration)
    {
        var values = LoadFromEnvironment();
        if (values.Count > 0)
            configuration.AddInMemoryCollection(values);
    }

    public static IReadOnlyDictionary<string, string?> LoadFromEnvironment()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var environmentName = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(environmentName)
                || !environmentName.EndsWith(FileSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var filePath = entry.Value?.ToString();
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException($"{environmentName} must point to a readable secret file.");

            if (!File.Exists(filePath))
                throw new InvalidOperationException($"{environmentName} points to a secret file that does not exist.");

            var configKey = environmentName[..^FileSuffix.Length].Replace("__", ":");
            values[configKey] = File.ReadAllText(filePath).TrimEnd('\r', '\n');
        }

        return values;
    }
}
