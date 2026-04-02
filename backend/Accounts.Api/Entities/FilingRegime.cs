using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class FilingRegime
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public bool CanUseMicro { get; set; }
    public bool CanFileAbridged { get; set; }
    public bool AuditExempt { get; set; }
    public ElectedRegime ElectedRegime { get; set; }
    public string? RequiredNotesJson { get; set; }
    public string? RequiredStatementsJson { get; set; }
    public DateTime DeterminedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
