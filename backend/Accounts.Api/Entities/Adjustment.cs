namespace Accounts.Api.Entities;

public class Adjustment
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public required string Description { get; set; }
    public int? DebitCategoryId { get; set; }
    public int? CreditCategoryId { get; set; }
    public decimal Amount { get; set; }
    public AdjustmentSource Source { get; set; } = AdjustmentSource.Manual;
    public string? Reason { get; set; }
    public string? LegalBasis { get; set; }
    public decimal ImpactOnProfit { get; set; }
    public decimal ImpactOnAssets { get; set; }
    public string? CreatedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsAuto { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
    public AccountCategory? DebitCategory { get; set; }
    public AccountCategory? CreditCategory { get; set; }
}
