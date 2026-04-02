using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class Dividend
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? DateDeclared { get; set; }
    public DateOnly? DatePaid { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
