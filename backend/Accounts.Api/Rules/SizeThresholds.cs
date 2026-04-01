namespace Accounts.Api.Rules;

public class SizeThresholdConfig
{
    public string EffectiveFrom { get; set; } = "2024-01-01";
    public ThresholdSet Micro { get; set; } = new() { Turnover = 900_000m, BalanceSheet = 450_000m, Employees = 10 };
    public ThresholdSet Small { get; set; } = new() { Turnover = 15_000_000m, BalanceSheet = 7_500_000m, Employees = 50 };
    public ThresholdSet Medium { get; set; } = new() { Turnover = 50_000_000m, BalanceSheet = 25_000_000m, Employees = 250 };
}

public class ThresholdSet
{
    public decimal Turnover { get; set; }
    public decimal BalanceSheet { get; set; }
    public int Employees { get; set; }
}
