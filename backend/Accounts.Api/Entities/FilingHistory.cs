using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class FilingHistory
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? PeriodId { get; set; }
    public DeadlineType DeadlineType { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly FiledDate { get; set; }
    public int DaysLate { get; set; }
    public decimal PenaltyAmount { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod? Period { get; set; }
}
