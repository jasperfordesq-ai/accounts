using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// Immutable-at-a-point support ledger for a period's trading-loss movement. The calculation hash
/// makes later source-data drift visible and forces the scope workflow to be reviewed again.
/// </summary>
public class CorporationTaxLossRecord
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public decimal OpeningTradingLoss { get; set; }
    public decimal CurrentPeriodTradingLoss { get; set; }
    public decimal TradingLossUsed { get; set; }
    public decimal ClosingTradingLoss { get; set; }
    public CorporationTaxLossTreatment Treatment { get; set; }
    public required string CalculationSha256 { get; set; }
    public required string RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
