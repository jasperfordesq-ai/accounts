using System.Text.Json.Serialization;
namespace Accounts.Api.Entities;

public class RelatedPartyTransaction
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public string PartyName { get; set; } = "";
    public string Relationship { get; set; } = ""; // Director, Connected Person, Group Company
    public string TransactionType { get; set; } = ""; // Sale, Purchase, Loan, Management Fee
    public decimal Amount { get; set; }
    public decimal? BalanceOwed { get; set; }
    public string? Terms { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public AccountingPeriod Period { get; set; } = null!;
}
