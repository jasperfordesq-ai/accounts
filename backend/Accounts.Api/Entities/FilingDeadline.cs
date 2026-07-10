using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class FilingDeadline
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int PeriodId { get; set; }
    public DeadlineType DeadlineType { get; set; }
    /// <summary>The unmodified result of the statutory calculation.</summary>
    public DateOnly CalculatedDueDate { get; set; }
    /// <summary>The operational due date. This equals CalculatedDueDate unless an active evidence-backed override exists.</summary>
    public DateOnly DueDate { get; set; }
    public DateOnly? AnnualReturnDate { get; set; }
    public int? AnnualReturnDateRecordId { get; set; }
    public DateOnly? ReturnMadeUpToDate { get; set; }
    public DateOnly? FinancialStatementsLatestMadeUpToDate { get; set; }
    public DateOnly? DeliveryDueDate { get; set; }
    public bool? MadeUpToDateBroughtForwardForAccountsAge { get; set; }
    public string? CalculationRuleVersion { get; set; }
    public string? CalculationSourceUrl { get; set; }
    public string? CalculationFingerprintSha256 { get; set; }
    public string? ManualOverrideStatus { get; set; }
    public DateOnly? ManualOverrideDueDate { get; set; }
    public string? ManualOverrideReason { get; set; }
    public string? ManualOverrideEvidenceReference { get; set; }
    public string? ManualOverrideEvidenceSha256 { get; set; }
    public string? ManualOverrideByUserId { get; set; }
    public string? ManualOverrideByDisplayName { get; set; }
    public DateTime? ManualOverrideAtUtc { get; set; }
    public string? ManualOverrideCalculationFingerprintSha256 { get; set; }
    public DateOnly? FiledDate { get; set; }
    public string? FilingReference { get; set; }
    public bool IsLate { get; set; }
    public decimal PenaltyAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
