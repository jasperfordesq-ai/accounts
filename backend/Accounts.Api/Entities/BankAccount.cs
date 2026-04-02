using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class BankAccount
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Name { get; set; }
    public string? Iban { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningBalanceDate { get; set; }

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    public List<ImportedTransaction> Transactions { get; set; } = [];
    public List<ImportBatch> ImportBatches { get; set; } = [];
}
