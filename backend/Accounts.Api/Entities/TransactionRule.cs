using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class TransactionRule
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Pattern { get; set; }
    public int CategoryId { get; set; }
    public int Priority { get; set; }

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountCategory Category { get; set; } = null!;
}
