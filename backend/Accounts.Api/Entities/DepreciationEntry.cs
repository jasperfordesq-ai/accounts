using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class DepreciationEntry
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int PeriodId { get; set; }
    public decimal OpeningNbv { get; set; }
    public decimal Charge { get; set; }
    public decimal ClosingNbv { get; set; }

    // Navigation
    [JsonIgnore]
    public FixedAsset Asset { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
