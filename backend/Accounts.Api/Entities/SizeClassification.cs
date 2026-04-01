namespace Accounts.Api.Entities;

public class SizeClassification
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public decimal Turnover { get; set; }
    public decimal BalanceSheetTotal { get; set; }
    public int AvgEmployees { get; set; }
    public CompanySizeClass? PriorYearClass { get; set; }
    public CompanySizeClass CalculatedClass { get; set; }
    public CompanySizeClass? OverrideClass { get; set; }
    public string? OverrideReason { get; set; }
    public string? ExclusionFlagsJson { get; set; }
    public string? QualificationNotes { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
}
