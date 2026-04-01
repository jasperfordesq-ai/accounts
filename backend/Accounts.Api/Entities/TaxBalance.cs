namespace Accounts.Api.Entities;

public class TaxBalance
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public TaxType TaxType { get; set; }
    public decimal Liability { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance { get; set; }

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
}
