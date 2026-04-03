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

    // s.239 / s.307 compliance fields
    public decimal InterestRate { get; set; } = 5m; // Default statutory rate
    public decimal InterestCharged { get; set; }
    public bool IsDocumented { get; set; } = true;
    public string? LoanTerms { get; set; }
    public decimal MaxBalanceDuringYear { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public CompanyOfficer Director { get; set; } = null!;
}
