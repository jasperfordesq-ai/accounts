namespace Accounts.Api.Entities;

public class Inventory
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public required string Description { get; set; }
    public decimal Value { get; set; }
    public ValuationMethod ValuationMethod { get; set; } = ValuationMethod.LowerOfCostAndNrv;

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
}
