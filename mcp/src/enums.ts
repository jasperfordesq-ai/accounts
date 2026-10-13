/**
 * Enumerations FilingBridge accepts in request bodies, written out by hand.
 *
 * A test compares every list here with the matching enum in the committed
 * OpenAPI contract, so a value added or renamed in C# turns the suite red
 * rather than being refused by the connector it was never taught.
 */
export const ENUMS = {
  AccountCashFlowClass: ['Operating', 'Investing', 'Tax', 'Dividend', 'Loan', 'ShareIssue', 'OtherFinancing', 'Cash'],
  AccountCategoryType: ['Income', 'Expense', 'Asset', 'Liability', 'Equity'],
  AccountStatementLine: ['TangibleFixedAssets', 'Stocks', 'Debtors', 'Prepayments', 'CashAtBank', 'TradeCreditors', 'Accruals', 'TaxCreditors', 'OtherCreditorsWithinYear', 'LoansAfterYear', 'OtherCreditorsAfterYear', 'ShareCapital', 'Reserves', 'RestrictedFunds', 'DividendsPaid', 'Suspense', 'OpeningBalanceControl', 'Turnover', 'OtherOperatingIncome', 'CostOfSales', 'AdministrativeExpenses', 'Depreciation', 'InterestPayable', 'TaxOnProfit'],
  AnnualReturnDateSource: ['CroRecord', 'BroughtForward', 'ExtendedB73', 'CourtOrder', 'ManualOverride'],
  CapitalAllowanceTreatment: ['Unreviewed', 'NonQualifying', 'PlantAndMachinery12Point5', 'UnsupportedSpecialScheme'],
  ChartOfAccountsVariant: ['GenericTrading', 'CharityClg'],
  CompanySizeClass: ['Micro', 'Small', 'Medium', 'Large'],
  CompanyType: ['Private', 'PrivateUnlimited', 'DesignatedActivityCompany', 'CompanyLimitedByGuarantee', 'PublicLimitedCompany'],
  CorporationTaxLossTreatment: ['Unreviewed', 'NotApplicable', 'CarryForwardSameTrade', 'CurrentPeriodOrCarryBackClaim', 'GroupRelief', 'TerminalLossRelief', 'Other'],
  CorporationTaxPaymentKind: ['PreliminaryFirst', 'PreliminarySecondOrSingle', 'Balance', 'InterestOrSurcharge', 'Other'],
  CreditorType: ['Trade', 'Other', 'Accrual', 'Tax'],
  DeadlineType: ['CRO', 'Charity', 'Revenue'],
  DebtorType: ['Trade', 'Other', 'Prepayment'],
  DepreciationMethod: ['StraightLine', 'ReducingBalance'],
  DirectorLoanArrangementType: ['Loan', 'QuasiLoan', 'CreditTransaction', 'GuaranteeOrSecurity'],
  DirectorLoanComplianceBasis: ['Unassessed', 'Section240BelowTenPercent', 'Section242SummaryApprovalProcedure', 'Section243IntraGroup', 'Section244VouchedExpense', 'Section245OrdinaryBusiness'],
  DirectorLoanCounterpartyType: ['Director', 'ConnectedPerson', 'GroupCompany'],
  DirectorLoanMovementType: ['Advance', 'Repayment'],
  DirectorLoanRelevantAssetsBasis: ['Unassessed', 'LastLaidEntityFinancialStatements', 'CalledUpShareCapitalNoPriorStatements'],
  DirectorLoanRelevantAssetsFallReview: ['Unassessed', 'NoRelevantFall', 'FallRemainedBelowLimit', 'TermsAmendedWithinTwoMonths', 'SapArrangementNotCounted'],
  DirectorLoanReviewDecision: ['Unreviewed', 'Accepted', 'RemediationRequired'],
  DirectorLoanTermsStatus: ['Unassessed', 'NotWritten', 'WrittenComplete', 'WrittenAmbiguousRepayment', 'WrittenAmbiguousInterest', 'WrittenAmbiguousRepaymentAndInterest'],
  DuplicateReviewStatus: ['NotCandidate', 'Pending', 'LegacyLockedUnverified', 'Retained', 'Discarded'],
  ElectedRegime: ['Micro', 'Small', 'SmallAbridged', 'Medium', 'Full'],
  ExternalFilingAuthorityKind: ['CroPresenter', 'CroElectronicFilingAgent', 'RevenueRosAgent'],
  ExternalFilingOutcomeKind: ['ReadyForManualHandoff', 'ExternallySubmittedRecorded', 'CorrectionRequired', 'ExternallyRejected', 'ExternallyAcceptedRecorded', 'SupersededByAmendment'],
  ExternalFilingWorkflow: ['CroB1', 'RevenueCt1Support'],
  FilingStatus: ['NotStarted', 'InProgress', 'ReadyForReview', 'Approved', 'PackageGenerated', 'Submitted', 'Accepted', 'Rejected', 'CorrectionRequired'],
  OfficerRole: ['Director', 'Secretary', 'CompanySecretary'],
  PeriodStatus: ['Draft', 'Review', 'Finalised', 'Filed'],
  TaxTreatment: ['Deductible', 'NonDeductible', 'CapitalAllowance', 'Exempt', 'Other'],
  ValuationMethod: ['Cost', 'NetRealisableValue', 'LowerOfCostAndNrv'],
  /** Entities/Enums.cs TaxType, the {taxType} path segment of tax-balances. */
  TaxType: ['CorporationTax', 'Vat', 'Paye', 'Rct'],
} as const satisfies Record<string, readonly string[]>;

export type EnumName = keyof typeof ENUMS;
