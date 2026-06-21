using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class AccountingPeriod
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public PeriodStatus Status { get; set; } = PeriodStatus.Draft;
    public bool IsFirstYear { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenedBy { get; set; }
    public string? ReopenReason { get; set; }

    // Member audit notice (s.334 Companies Act 2014)
    public bool MemberAuditNoticeReceived { get; set; }
    public DateOnly? MemberAuditNoticeDate { get; set; }

    public bool GoingConcernConfirmed { get; set; } = true;
    public string? GoingConcernNote { get; set; }

    // accounting-retained-earnings-snapshot: closing reserves captured when the period is finalised/
    // filed, so a later period reads a fixed opening-reserves figure instead of recursively recomputing
    // every prior year's P&L (O(n^2) and subject to retroactive drift). Null until finalised.
    public decimal? ClosingRetainedEarnings { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    public SizeClassification? SizeClassification { get; set; }
    public FilingRegime? FilingRegime { get; set; }
    public List<ImportedTransaction> Transactions { get; set; } = [];
    public List<Debtor> Debtors { get; set; } = [];
    public List<Creditor> Creditors { get; set; } = [];
    public List<DepreciationEntry> DepreciationEntries { get; set; } = [];
    public List<Inventory> Inventories { get; set; } = [];
    public List<DirectorLoan> DirectorLoans { get; set; } = [];
    public PayrollSummary? PayrollSummary { get; set; }
    public List<TaxBalance> TaxBalances { get; set; } = [];
    public List<Dividend> Dividends { get; set; } = [];
    public List<OpeningBalance> OpeningBalances { get; set; } = [];
    public List<Adjustment> Adjustments { get; set; } = [];
    public List<Report> Reports { get; set; } = [];
    public List<NotesDisclosure> NotesDisclosures { get; set; } = [];
    public List<YearEndReviewConfirmation> YearEndReviewConfirmations { get; set; } = [];
    public CroFilingPackage? CroFilingPackage { get; set; }
    public RevenueFilingPackage? RevenueFilingPackage { get; set; }
    public CharityFilingPackage? CharityFilingPackage { get; set; }
    public List<FilingDeadline> FilingDeadlines { get; set; } = [];
    public List<PostBalanceSheetEvent> PostBalanceSheetEvents { get; set; } = [];
    public List<RelatedPartyTransaction> RelatedPartyTransactions { get; set; } = [];
    public List<ContingentLiability> ContingentLiabilities { get; set; } = [];
    public List<FundBalance> FundBalances { get; set; } = [];
}
