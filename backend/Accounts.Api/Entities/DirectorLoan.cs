using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class DirectorLoan
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int DirectorId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Advances { get; set; }
    public decimal Repayments { get; set; }
    public decimal ClosingBalance { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public CompanyOfficer Director { get; set; } = null!;
}
