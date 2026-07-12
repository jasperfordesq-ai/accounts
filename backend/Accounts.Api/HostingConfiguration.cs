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

    public static FileBackedConfigurationProvenance AddFileBackedEnvironmentVariables(
        ConfigurationManager configuration)
    {
        var loadedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = LoadFromEnvironment(loadedKeys);
        if (values.Count > 0)
            configuration.AddInMemoryCollection(values);
        return new FileBackedConfigurationProvenance(loadedKeys);
    }

    private static IReadOnlyDictionary<string, string?> LoadFromEnvironment(
        ISet<string> loadedKeys)
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
            loadedKeys.Add(configKey);
        }

        return values;
    }
}

public sealed class FileBackedConfigurationProvenance(IEnumerable<string> loadedKeys)
{
    private readonly HashSet<string> keys = new(loadedKeys, StringComparer.OrdinalIgnoreCase);

    public bool IsFileBacked(string configKey) => keys.Contains(configKey);
}
