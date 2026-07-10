namespace Accounts.Api.Entities;

public enum CompanyType
{
    Private,
    PrivateUnlimited,
    DesignatedActivityCompany,
    CompanyLimitedByGuarantee,
    PublicLimitedCompany
}

public enum PeriodStatus
{
    Draft,
    Review,
    Finalised,
    Filed
}

/// <summary>
/// A duplicate signal is never itself a disposal decision. Pending and retained rows remain in the
/// ledger; only an explicit, audited Discarded decision excludes the imported row.
/// </summary>
public enum DuplicateReviewStatus
{
    NotCandidate,
    Pending,
    LegacyLockedUnverified,
    Retained,
    Discarded
}

public enum DuplicateCandidateKind
{
    ExactSourceReimport,
    ReferenceAndBalanceMatch,
    ReferenceMatch,
    BalanceMatch,
    SameDateAmountDescription,
    LegacyUnverified
}

public enum CompanySizeClass
{
    Micro,
    Small,
    Medium,
    Large
}

public enum OfficerRole
{
    Director,
    Secretary,
    CompanySecretary
}

public enum AccountCategoryType
{
    Income,
    Expense,
    Asset,
    Liability,
    Equity
}

public enum TaxTreatment
{
    Deductible,
    NonDeductible,
    CapitalAllowance,
    Exempt,
    Other
}

public enum DepreciationMethod
{
    StraightLine,
    ReducingBalance
}

/// <summary>
/// Explicit tax treatment for a fixed asset. Unreviewed and special-scheme assets are deliberately
/// excluded from the automated wear-and-tear computation until their treatment is resolved.
/// </summary>
public enum CapitalAllowanceTreatment
{
    Unreviewed,
    NonQualifying,
    PlantAndMachinery12Point5,
    UnsupportedSpecialScheme
}

/// <summary>
/// Loss claim/election selected for the period. Only same-trade carry-forward is currently within
/// the automated support-data scope; every other claim type is retained but fails closed.
/// </summary>
public enum CorporationTaxLossTreatment
{
    Unreviewed,
    NotApplicable,
    CarryForwardSameTrade,
    CurrentPeriodOrCarryBackClaim,
    GroupRelief,
    TerminalLossRelief,
    Other
}

public enum CorporationTaxPaymentKind
{
    PreliminaryFirst,
    PreliminarySecondOrSingle,
    Balance,
    InterestOrSurcharge,
    Other
}

public enum DebtorType
{
    Trade,
    Other,
    Prepayment
}

public enum CreditorType
{
    Trade,
    Other,
    Accrual,
    Tax
}

public enum TaxType
{
    CorporationTax,
    Vat,
    Paye,
    Rct
}

public enum AdjustmentSource
{
    Auto,
    Manual
}

public enum ReportType
{
    TrialBalance,
    ProfitAndLoss,
    BalanceSheet,
    Notes,
    DirectorsReport,
    LeadSchedule,
    TaxComputation
}

public enum FilingPackageStatus
{
    Draft,
    Generated,
    Submitted,
    Accepted
}

public enum ValuationMethod
{
    Cost,
    NetRealisableValue,
    LowerOfCostAndNrv
}

public enum ElectedRegime
{
    Micro,
    Small,
    SmallAbridged,
    Medium,
    Full
}

public enum DeadlineType
{
    CRO,
    Charity,
    Revenue
}

public enum FilingStatus
{
    NotStarted,
    InProgress,
    ReadyForReview,
    Approved,
    PackageGenerated,
    Submitted,
    Accepted,
    Rejected,
    CorrectionRequired
}
