namespace Accounts.Api.Services;

/// <summary>
/// Resolves the immutable application/release identity used when a filing artifact is generated.
/// Production deliberately has no fallback: a candidate that cannot identify itself cannot release
/// filing artifacts. Development keeps a stable local identity for focused workflow testing.
/// </summary>
public sealed class FilingReleaseIdentityProvider(IConfiguration configuration, IHostEnvironment environment)
{
    public string GetRequiredCandidate()
    {
        var candidate = configuration["FilingRelease:Candidate"]
            ?? Environment.GetEnvironmentVariable("ACCOUNTS_RELEASE_CANDIDATE")
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA");
        candidate = candidate?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
                return "local-development";

            throw new FilingReleaseBlockedException(
                "Final filing release is disabled because FilingRelease:Candidate is not configured for this deployment.");
        }

        if (candidate.Length > 200)
            throw new FilingReleaseBlockedException("The filing release candidate identity is malformed.");

        return candidate;
    }
}
