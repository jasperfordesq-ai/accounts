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

    // Immutable source evidence retained for duplicate review. Legacy/manual rows can be null.
    [JsonIgnore]
    public int? SourceRowNumber { get; set; }
    [JsonIgnore]
    public string? SourceRowSha256 { get; set; }
    [JsonIgnore]
    public string? SourceRowJson { get; set; }

    // A candidate remains included unless and until a reviewer explicitly records Discarded.
    [JsonIgnore]
    public DuplicateReviewStatus DuplicateReviewStatus { get; set; }
    [JsonIgnore]
    public DuplicateCandidateKind? DuplicateCandidateKind { get; set; }
    [JsonIgnore]
    public decimal? DuplicateConfidence { get; set; }
    [JsonIgnore]
    public string? DuplicateCandidateReasonsJson { get; set; }
    [JsonIgnore]
    public int? DuplicateMatchedTransactionId { get; set; }
    [JsonIgnore]
    public string? DuplicateMatchedSourceRowSha256 { get; set; }
    [JsonIgnore]
    public string? DuplicateDecisionByUserId { get; set; }
    [JsonIgnore]
    public string? DuplicateDecisionByDisplayName { get; set; }
    [JsonIgnore]
    public DateTime? DuplicateDecisionAtUtc { get; set; }
    [JsonIgnore]
    public string? DuplicateDecisionReason { get; set; }
    [JsonIgnore]
    public int DuplicateDecisionVersion { get; set; }

    // Navigation
    [JsonIgnore]
    public BankAccount BankAccount { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod? Period { get; set; }
    [JsonIgnore]
    public ImportBatch? ImportBatch { get; set; }
    public AccountCategory? Category { get; set; }
}
