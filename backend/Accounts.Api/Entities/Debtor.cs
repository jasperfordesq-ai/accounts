using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class Debtor
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public DebtorType Type { get; set; }
    public string? Notes { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
