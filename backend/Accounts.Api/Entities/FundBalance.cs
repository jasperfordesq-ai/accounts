using System.Text.Json.Serialization;
namespace Accounts.Api.Entities;

public class FundBalance
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public string FundName { get; set; } = "";
    public string FundType { get; set; } = "Unrestricted"; // Unrestricted, Restricted, Endowment, Designated
    public decimal OpeningBalance { get; set; }
    public decimal IncomingResources { get; set; }
    public decimal ResourcesExpended { get; set; }
    public decimal Transfers { get; set; }
    public decimal GainsLosses { get; set; }
    public decimal ClosingBalance { get; set; }
    public string? Notes { get; set; }
    [JsonIgnore] public AccountingPeriod Period { get; set; } = null!;
}
