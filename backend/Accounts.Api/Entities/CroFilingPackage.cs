using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class CroFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string? PdfPath { get; set; }
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
