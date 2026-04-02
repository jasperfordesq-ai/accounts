using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class PayrollSummary
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public decimal GrossWages { get; set; }
    public decimal EmployerPrsi { get; set; }
    public decimal PensionContributions { get; set; }
    public int StaffCount { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
