using System.Text.Json.Serialization;
namespace Accounts.Api.Entities;

public class PostBalanceSheetEvent
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public string Description { get; set; } = "";
    public DateOnly EventDate { get; set; }
    public bool IsAdjusting { get; set; } // adjusting vs non-adjusting event
    public decimal? FinancialImpact { get; set; }
    public string? ActionRequired { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public AccountingPeriod Period { get; set; } = null!;
}
