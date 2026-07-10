using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public sealed class CorporationTaxPaymentRecord
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public CorporationTaxPaymentKind Kind { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public string? ExternalPaymentReference { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
