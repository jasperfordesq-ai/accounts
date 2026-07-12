namespace Accounts.Api.Rules;

public enum DeploymentMode
{
    Development,
    PrivateServer,
    PublicProduction
}

public sealed class DeploymentConfig
{
    public string Mode { get; set; } = "";
    public string InstallationId { get; set; } = "";
}

public static class DeploymentModeContract
{
    public const string Development = nameof(DeploymentMode.Development);
    public const string PrivateServer = nameof(DeploymentMode.PrivateServer);
    public const string PublicProduction = nameof(DeploymentMode.PublicProduction);

    public static bool TryParse(string? configuredMode, out DeploymentMode mode)
    {
        mode = default;
        return configuredMode switch
        {
            Development => Assign(DeploymentMode.Development, out mode),
            PrivateServer => Assign(DeploymentMode.PrivateServer, out mode),
            PublicProduction => Assign(DeploymentMode.PublicProduction, out mode),
            _ => false
        };
    }

    public static IReadOnlyList<string> Validate(
        string? configuredMode,
        string environmentName,
        out DeploymentMode effectiveMode)
    {
        var failures = new List<string>();
        if (!TryParse(configuredMode, out var parsedMode))
        {
            failures.Add(
                "Deployment:Mode must be exactly Development, PrivateServer, or PublicProduction.");
            // Invalid and missing values deliberately receive the strict public policy. They must
            // never acquire one of the Private Server relaxations through fallback behaviour.
            effectiveMode = DeploymentMode.PublicProduction;
            return failures;
        }

        effectiveMode = parsedMode;
        if (parsedMode == DeploymentMode.Development)
        {
            if (!string.Equals(environmentName, Environments.Development, StringComparison.Ordinal))
                failures.Add("Deployment:Mode=Development requires ASPNETCORE_ENVIRONMENT=Development.");
            return failures;
        }

        if (!string.Equals(environmentName, Environments.Production, StringComparison.Ordinal))
        {
            failures.Add(
                $"Deployment:Mode={configuredMode} requires ASPNETCORE_ENVIRONMENT=Production.");
        }

        return failures;
    }

    public static bool IsPrivateServerRuntime(IConfiguration configuration, IHostEnvironment environment) =>
        string.Equals(environment.EnvironmentName, Environments.Production, StringComparison.Ordinal)
        && string.Equals(
            configuration["Deployment:Mode"],
            PrivateServer,
            StringComparison.Ordinal);

    private static bool Assign(DeploymentMode value, out DeploymentMode target)
    {
        target = value;
        return true;
    }
}
