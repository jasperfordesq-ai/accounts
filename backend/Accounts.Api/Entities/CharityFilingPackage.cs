using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class CharityFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;
    public FilingStatus FilingStatus { get; set; } = FilingStatus.NotStarted;
    public bool SofaGenerated { get; set; }
    public bool TrusteesReportGenerated { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? AcceptedBy { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? AnnualReturnReference { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CorrectionDeadline { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
