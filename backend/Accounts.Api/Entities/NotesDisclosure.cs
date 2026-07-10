using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class NotesDisclosure
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int NoteNumber { get; set; }
    public string? Code { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
    public bool IsRequired { get; set; }
    public bool IsIncluded { get; set; } = true;
    public NoteChecklistState ChecklistState { get; set; } = NoteChecklistState.Required;
    public string? ReviewEvidence { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}

public enum NoteChecklistState
{
    Required,
    NotApplicable,
    ExplicitReview
}
