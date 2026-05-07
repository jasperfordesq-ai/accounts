using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class OpeningBalance
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int AccountCategoryId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? SourceNote { get; set; }
    public string? EnteredBy { get; set; }
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
    public bool Reviewed { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    public AccountCategory AccountCategory { get; set; } = null!;
}
