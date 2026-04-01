namespace Accounts.Api.Entities;

public class Report
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public ReportType Type { get; set; }
    public string? DataJson { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
}
