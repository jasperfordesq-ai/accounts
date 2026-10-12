using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// Append-only archive of historic size figures keyed in from filed accounts, written whenever an
/// anchor is retired or its evidence is replaced. It keeps the PDF bytes, hash, figures, source,
/// attestation and retirement, so evidence is never lost when the live row is re-recorded.
/// </summary>
public class HistoricAnchorEvidenceRecord
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public HistoricAnchorEvidenceEvent Event { get; set; }
    public DateTime ArchivedAt { get; set; }
    public string ArchivedBy { get; set; } = "";
    // The principals (audit user ids), so subject access can find the records that name them.
    public string? ArchivedByUserId { get; set; }

    public decimal Turnover { get; set; }
    public decimal BalanceSheetTotal { get; set; }
    public int AvgEmployees { get; set; }
    public DateOnly? ThresholdElectionEffectiveFrom { get; set; }
    public string SourceReference { get; set; } = "";
    [JsonIgnore]
    public byte[] EvidenceArtifact { get; set; } = [];
    public string EvidenceSha256 { get; set; } = "";
    public string AttestedBy { get; set; } = "";
    public string AttestedRole { get; set; } = "";
    public DateTime AttestedAt { get; set; }
    public CompanySizeClass? AttestedEffectiveClass { get; set; }
    public DateTime? RetiredAt { get; set; }
    public string? RetiredBy { get; set; }
    public string? RetiredByUserId { get; set; }
    public string? RetiredReason { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}

/// <summary>Why historic evidence was archived.</summary>
public enum HistoricAnchorEvidenceEvent
{
    Retired,
    Replaced
}
