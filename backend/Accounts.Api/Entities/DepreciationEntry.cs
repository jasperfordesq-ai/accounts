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
    public FixedAsset Asset { get; set; } = null!;
    public AccountingPeriod Period { get; set; } = null!;
}
