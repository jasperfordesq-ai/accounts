namespace Accounts.Api.Entities;

public class Creditor
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public CreditorType Type { get; set; }
    public bool DueWithinYear { get; set; } = true;
    public string? Notes { get; set; }

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
}
