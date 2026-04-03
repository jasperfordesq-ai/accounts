using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class CroFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string? PdfPath { get; set; }
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;
    public FilingStatus FilingStatus { get; set; } = FilingStatus.NotStarted;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? CroSubmissionReference { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CorrectionDeadline { get; set; } // 14 days from rejection
    public bool AccountsPdfGenerated { get; set; }
    public bool SignaturePageGenerated { get; set; }
    public bool PaymentCompleted { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
