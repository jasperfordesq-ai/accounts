namespace Accounts.Api.Entities;

public class NotesDisclosure
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int NoteNumber { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
    public bool IsRequired { get; set; }
    public bool IsIncluded { get; set; } = true;

    // Navigation
    public AccountingPeriod Period { get; set; } = null!;
}
