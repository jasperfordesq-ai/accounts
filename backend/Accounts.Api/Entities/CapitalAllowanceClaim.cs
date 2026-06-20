using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

// A persisted record of the wear-and-tear (capital) allowance actually claimed against a fixed
// asset in a given accounting period (s.284 TCA 1997). Capital allowances run over eight years
// independently of the accounting depreciation life, so the cumulative claim cannot be re-derived
// from depreciation entries — it must be stored. Future periods read prior claims from these rows
// to cap the cumulative allowance at 100% of cost rather than re-estimating from period length.
public class CapitalAllowanceClaim
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int PeriodId { get; set; }

    // The cost basis the claim was computed on, kept for traceability/lead-schedule purposes.
    public decimal Cost { get; set; }

    // The wear-and-tear allowance claimed in this period.
    public decimal Claim { get; set; }

    // Navigation
    [JsonIgnore]
    public FixedAsset Asset { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
