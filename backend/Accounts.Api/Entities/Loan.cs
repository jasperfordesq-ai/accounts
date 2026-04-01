namespace Accounts.Api.Entities;

public class Loan
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Lender { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal Balance { get; set; }
    public decimal InterestRate { get; set; }
    public bool IsDirectorLoan { get; set; }
    public decimal DueWithinYear { get; set; }
    public decimal DueAfterYear { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
}
