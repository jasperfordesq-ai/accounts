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
