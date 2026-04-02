using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class ImportBatch
{
    public int Id { get; set; }
    public int BankAccountId { get; set; }
    public required string Filename { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int RowCount { get; set; }
    public int MatchedCount { get; set; }

    // Navigation
    [JsonIgnore]
    public BankAccount BankAccount { get; set; } = null!;
    public List<ImportedTransaction> Transactions { get; set; } = [];
}
