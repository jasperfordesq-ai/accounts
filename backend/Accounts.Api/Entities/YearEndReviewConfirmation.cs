using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class YearEndReviewConfirmation
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public required string SectionKey { get; set; }
    public bool Confirmed { get; set; } = true;
    public string? ConfirmedBy { get; set; }
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
