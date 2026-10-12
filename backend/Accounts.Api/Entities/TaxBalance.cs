using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class TaxBalance
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public TaxType TaxType { get; set; }
    public decimal Liability { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance { get; set; }

    // For a company holding a charitable tax exemption: an Owner or Reviewer's confirmation that the
    // entered corporation-tax liability is due (for example on income outside the exemption). It lets
    // the liability be provided for; saving a different liability clears it.
    public string? ConfirmedDueBy { get; set; }
    public DateTime? ConfirmedDueAt { get; set; }
    public string? ConfirmedDueReason { get; set; }
    // The liability the confirmation was given for; it counts only while this equals Liability.
    public decimal? ConfirmedDueLiability { get; set; }
    // The attested taxable profit from activities outside the charitable exemption: the basis of the
    // exemption-aware computation, bound to the confirmation like the liability.
    public decimal? ConfirmedTaxableProfitOutsideExemption { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
