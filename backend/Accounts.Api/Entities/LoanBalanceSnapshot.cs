using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class LoanBalanceSnapshot
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public int PeriodId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Drawdowns { get; set; }
    public decimal Repayments { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal DueWithinYear { get; set; }
    public decimal DueAfterYear { get; set; }
    public string? Notes { get; set; }
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
    public string? EnteredBy { get; set; }

    [JsonIgnore]
    public Loan Loan { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
