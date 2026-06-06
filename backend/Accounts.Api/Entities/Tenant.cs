using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class Tenant
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsMainDemoTenant { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public List<Company> Companies { get; set; } = [];

    [JsonIgnore]
    public List<UserAccount> Users { get; set; } = [];
}
