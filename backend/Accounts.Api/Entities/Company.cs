using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class Company
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public required string LegalName { get; set; }
    public string? TradingName { get; set; }
    public string? CroNumber { get; set; }
    public string? TaxReference { get; set; }
    public CompanyType CompanyType { get; set; }
    public DateOnly IncorporationDate { get; set; }
    public int FinancialYearStartMonth { get; set; } = 1;
    /// <summary>
    /// Exact current Annual Return Date (ARD) shown by the CRO. Legacy month-only records are
    /// migrated to null and must be confirmed against CORE before deadline calculation can run.
    /// </summary>
    public DateOnly? AnnualReturnDate { get; set; }

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

    // Fifth Schedule — Ineligible entity flags (Companies Act 2014)
    public bool IsListedSecurities { get; set; }
    public bool IsCreditInstitution { get; set; }
    public bool IsInsuranceUndertaking { get; set; }
    public bool IsPensionFund { get; set; }
    public bool IsFifthScheduleEntity { get; set; }
    public bool IsOtherIneligibleEntity { get; set; }
    public bool IsFinancialHoldingUndertaking { get; set; }
    public bool PreparesGroupFinancialStatements { get; set; }
    public bool IncludedInHigherConsolidatedFinancialStatements { get; set; }
    public bool IsCharitableOrganisation { get; set; }

    // Recoverable quarantine. Detailed immutable evidence is retained separately.
    public bool IsQuarantined { get; set; }
    public DateTime? QuarantinedAtUtc { get; set; }
    public string? QuarantinedByUserId { get; set; }
    public string? QuarantinedByDisplayName { get; set; }
    public string? QuarantineReason { get; set; }
    public string? QuarantineEvidenceSha256 { get; set; }

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
    public List<FilingDeadline> FilingDeadlines { get; set; } = [];
    public List<FilingHistory> FilingHistories { get; set; } = [];
    [JsonIgnore]
    public List<AnnualReturnDateRecord> AnnualReturnDateHistory { get; set; } = [];
    public List<UserCompanyAccess> UserAccesses { get; set; } = [];
    public CharityInfo? CharityInfo { get; set; }
    public Tenant? Tenant { get; set; }
}
