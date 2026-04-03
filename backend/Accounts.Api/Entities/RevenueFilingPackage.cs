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
    public FilingStatus FilingStatus { get; set; } = FilingStatus.NotStarted;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IxbrlGenerated { get; set; }
    public bool IxbrlValidated { get; set; }
    public string? IxbrlValidationErrors { get; set; }
    public string? Ct1Reference { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
