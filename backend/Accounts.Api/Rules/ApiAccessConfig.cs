namespace Accounts.Api.Rules;

public class ApiAccessConfig
{
    public bool Enabled { get; set; }
    public bool RequireInProduction { get; set; } = true;
    public string HeaderName { get; set; } = "X-Accounts-Api-Key";
    public List<ApiAccessKeyConfig> Keys { get; set; } = [];
}

public class ApiAccessKeyConfig
{
    public string Name { get; set; } = "Unnamed key";
    public string Role { get; set; } = "Admin";
    public string? KeyHash { get; set; }
    public string? DevelopmentKey { get; set; }
    public List<int> AllowedCompanyIds { get; set; } = [];
}
