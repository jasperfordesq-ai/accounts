using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class ImportedTransaction
{
    public int Id { get; set; }
    public int BankAccountId { get; set; }
    public int? PeriodId { get; set; }
    public int? ImportBatchId { get; set; }
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public string? Reference { get; set; }
    public int? CategoryId { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public bool IsDuplicate { get; set; }
    public bool ManualOverride { get; set; }

    // Navigation
    [JsonIgnore]
    public BankAccount BankAccount { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod? Period { get; set; }
    [JsonIgnore]
    public ImportBatch? ImportBatch { get; set; }
    public AccountCategory? Category { get; set; }
}
