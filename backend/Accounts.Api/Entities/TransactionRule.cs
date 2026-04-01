namespace Accounts.Api.Entities;

public class TransactionRule
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Pattern { get; set; }
    public int CategoryId { get; set; }
    public int Priority { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public AccountCategory Category { get; set; } = null!;
}
