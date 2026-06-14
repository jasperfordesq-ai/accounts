using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class UserCompanyAccess
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public UserAccount User { get; set; } = null!;

    [JsonIgnore]
    public Company Company { get; set; } = null!;
}
