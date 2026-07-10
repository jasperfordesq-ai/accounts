using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public enum DirectorLoanCounterpartyType
{
    Director,
    ConnectedPerson,
    GroupCompany
}

public enum DirectorLoanArrangementType
{
    Loan,
    QuasiLoan,
    CreditTransaction,
    GuaranteeOrSecurity
}

public enum DirectorLoanTermsStatus
{
    Unassessed,
    NotWritten,
    WrittenComplete,
    WrittenAmbiguousRepayment,
    WrittenAmbiguousInterest,
    WrittenAmbiguousRepaymentAndInterest
}

public enum DirectorLoanComplianceBasis
{
    Unassessed,
    Section240BelowTenPercent,
    Section242SummaryApprovalProcedure,
    Section243IntraGroup,
    Section244VouchedExpense,
    Section245OrdinaryBusiness
}

public enum DirectorLoanRelevantAssetsBasis
{
    Unassessed,
    LastLaidEntityFinancialStatements,
    CalledUpShareCapitalNoPriorStatements
}

public enum DirectorLoanRelevantAssetsFallReview
{
    Unassessed,
    NoRelevantFall,
    FallRemainedBelowLimit,
    TermsAmendedWithinTwoMonths,
    SapArrangementNotCounted
}

public enum DirectorLoanReviewDecision
{
    Unreviewed,
    Accepted,
    RemediationRequired
}

public enum DirectorLoanMovementType
{
    Advance,
    Repayment
}

public class DirectorLoan
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int? DirectorId { get; set; }
    public DirectorLoanCounterpartyType CounterpartyType { get; set; } = DirectorLoanCounterpartyType.Director;
    public string? CounterpartyName { get; set; }
    public DirectorLoanArrangementType ArrangementType { get; set; } = DirectorLoanArrangementType.Loan;
    public DateOnly? ArrangementDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Advances { get; set; }
    public decimal Repayments { get; set; }
    public decimal ClosingBalance { get; set; }

    // Sections 236 and 307: written terms, interest and disclosure particulars.
    public DirectorLoanTermsStatus TermsStatus { get; set; } = DirectorLoanTermsStatus.Unassessed;
    public decimal InterestRate { get; set; }
    public decimal InterestCharged { get; set; }
    public decimal AllowanceMade { get; set; }
    public string? Section236PresumptionEvidenceReference { get; set; }
    // Retained only for compatibility with older rows. Compliance is driven by TermsStatus, not this flag.
    public bool IsDocumented { get; set; } = true;
    public string? LoanTerms { get; set; }
    public decimal MaxBalanceDuringYear { get; set; }

    // Sections 239 to 245: the claimed legal basis and evidence needed to substantiate it.
    public DirectorLoanComplianceBasis ComplianceBasis { get; set; } = DirectorLoanComplianceBasis.Unassessed;
    public DirectorLoanRelevantAssetsBasis RelevantAssetsBasis { get; set; } = DirectorLoanRelevantAssetsBasis.Unassessed;
    public decimal? RelevantAssetsAmount { get; set; }
    public DateOnly? RelevantAssetsAsOfDate { get; set; }
    public string? RelevantAssetsReference { get; set; }
    public bool NoPriorFinancialStatementsConfirmed { get; set; }
    public DirectorLoanRelevantAssetsFallReview RelevantAssetsFallReview { get; set; } = DirectorLoanRelevantAssetsFallReview.Unassessed;
    public DateOnly? RelevantAssetsReductionAwarenessDate { get; set; }
    public DateOnly? TermsAmendedDate { get; set; }
    public string? TermsAmendmentEvidenceReference { get; set; }
    public string? ExceptionEvidenceReference { get; set; }

    // Sections 202, 203 and 242: Summary Approval Procedure evidence.
    public DateOnly? SapDeclarationDate { get; set; }
    public DateOnly? SapResolutionDate { get; set; }
    public DateOnly? SapActivityStartDate { get; set; }
    public DateOnly? SapCroFilingDate { get; set; }
    public string? SapDeclarationReference { get; set; }
    public string? SapResolutionReference { get; set; }
    public string? SapCroFilingReference { get; set; }
    public bool SapDeclarationCoversSection203Matters { get; set; }

    // Sections 244 and 245 exception-specific evidence.
    public DateOnly? ExpenseIncurredDate { get; set; }
    public DateOnly? ExpenseDischargedDate { get; set; }
    public bool OrdinaryCourseConfirmed { get; set; }
    public bool NoMoreFavourableTermsConfirmed { get; set; }

    // Explicit arrangement-level review. This supplements, and never replaces, release-level
    // qualified-accountant acceptance.
    public DirectorLoanReviewDecision ReviewDecision { get; set; } = DirectorLoanReviewDecision.Unreviewed;
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewerRole { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public CompanyOfficer? Director { get; set; }
    public List<DirectorLoanMovement> BalanceMovements { get; set; } = [];
}

public class DirectorLoanMovement
{
    public int Id { get; set; }
    public int DirectorLoanId { get; set; }
    public DateOnly MovementDate { get; set; }
    public DirectorLoanMovementType MovementType { get; set; }
    public decimal Amount { get; set; }
    public string? EvidenceReference { get; set; }

    [JsonIgnore]
    public DirectorLoan DirectorLoan { get; set; } = null!;
}
