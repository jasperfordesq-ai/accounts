using System.Text.Json.Serialization;
namespace Accounts.Api.Entities;

public class ContingentLiability
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public string Description { get; set; } = "";
    public string Nature { get; set; } = ""; // Guarantee, Legal Claim, Warranty, Other
    public decimal? EstimatedAmount { get; set; }
    public string Likelihood { get; set; } = "Possible"; // Probable, Possible, Remote
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public AccountingPeriod Period { get; set; } = null!;
}
