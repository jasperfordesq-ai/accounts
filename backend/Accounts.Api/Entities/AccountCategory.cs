namespace Accounts.Api.Entities;

public class AccountCategory
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public AccountCategoryType Type { get; set; }
    public TaxTreatment TaxTreatment { get; set; } = TaxTreatment.Deductible;
    public bool IsSystem { get; set; }
    public int? ParentId { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public AccountCategory? Parent { get; set; }
    public List<AccountCategory> Children { get; set; } = [];
    public List<ImportedTransaction> Transactions { get; set; } = [];
    public List<TransactionRule> Rules { get; set; } = [];
}
