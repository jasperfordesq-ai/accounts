using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class RevenueFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public string? Ct1DataJson { get; set; }
    public string? IxbrlPath { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
