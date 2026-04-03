using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class FilingDeadline
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int PeriodId { get; set; }
    public DeadlineType DeadlineType { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? FiledDate { get; set; }
    public bool IsLate { get; set; }
    public decimal PenaltyAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
