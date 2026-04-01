namespace Accounts.Api.Entities;

public class Company
{
    public int Id { get; set; }
    public required string LegalName { get; set; }
    public string? TradingName { get; set; }
    public string? CroNumber { get; set; }
    public string? TaxReference { get; set; }
    public CompanyType CompanyType { get; set; }
    public DateOnly IncorporationDate { get; set; }
    public int FinancialYearStartMonth { get; set; } = 1;
    public int ArdMonth { get; set; }

    // Registered office
    public string? RegisteredOfficeAddress1 { get; set; }
    public string? RegisteredOfficeAddress2 { get; set; }
    public string? RegisteredOfficeCity { get; set; }
    public string? RegisteredOfficeCounty { get; set; }
    public string? RegisteredOfficeEircode { get; set; }

    // Flags
    public bool IsGroupMember { get; set; }
    public bool IsHolding { get; set; }
    public bool IsInvestment { get; set; }
    public bool IsSubsidiary { get; set; }
    public bool IsDormant { get; set; }
    public bool IsTrading { get; set; }
    public bool IsVatRegistered { get; set; }
    public bool IsEmployer { get; set; }
    public bool HasStock { get; set; }
    public bool OwnsAssets { get; set; }
    public bool HasBorrowings { get; set; }
    public bool HasDirectorLoans { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<CompanyOfficer> Officers { get; set; } = [];
    public List<AccountingPeriod> Periods { get; set; } = [];
    public List<BankAccount> BankAccounts { get; set; } = [];
    public List<FixedAsset> FixedAssets { get; set; } = [];
    public List<Loan> Loans { get; set; } = [];
    public List<AccountCategory> Categories { get; set; } = [];
    public List<TransactionRule> TransactionRules { get; set; } = [];
    public List<ShareCapital> ShareCapitals { get; set; } = [];
}
