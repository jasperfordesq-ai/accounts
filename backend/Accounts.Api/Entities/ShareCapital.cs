using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class ShareCapital
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string ShareClass { get; set; } = "Ordinary";
    public decimal NominalValue { get; set; } = 1m;
    public int NumberIssued { get; set; } = 1;
    public decimal TotalValue { get; set; } = 1m;
    public bool IsFullyPaid { get; set; } = true;

    [JsonIgnore]
    public Company Company { get; set; } = null!;
}
