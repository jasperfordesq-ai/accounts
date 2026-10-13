/**
 * The request bodies FilingBridge accepts, one declaration per input record.
 *
 * Each list is checked at compile time against the OpenAPI-generated types
 * (bodyFields() only accepts names the contract declares) and at test time
 * against the contract itself. Descriptions are drawn from the C# input
 * records and the validation the endpoints apply. `personal: true` marks a
 * field the personal-data gate withholds; a Withheld session may not write it.
 */
import type { components } from './generated/openapi-types.js';
import type { FieldSpec } from './tool-body.js';
import { ENUMS } from './enums.js';

type Schemas = components['schemas'];

/** Narrows a field list to names the contract declares for that schema. */
export function bodyFields<K extends keyof Schemas>(
  _schema: K,
  fields: readonly FieldSpec<Extract<keyof Schemas[K], string>>[],
): readonly FieldSpec[] {
  return fields;
}

export const COMPANY_INPUT_FIELDS = bodyFields('CompanyInput', [
  { kind: 'string', max: 200, name: 'legalName', required: true, describe: "Registered legal name, as on the CRO register. Required, 200 characters or fewer." },
  { kind: 'string', max: 200, name: 'tradingName', nullable: true, describe: "Business name the company trades under, if different." },
  { kind: 'string', max: 20, name: 'croNumber', nullable: true, describe: "CRO company number, 20 characters or fewer." },
  { kind: 'string', max: 20, name: 'taxReference', nullable: true, describe: "Revenue tax reference number." },
  { kind: 'enum', values: ENUMS.CompanyType, name: 'companyType', required: true, describe: "Legal form under the Companies Act 2014: Private (LTD), DesignatedActivityCompany (DAC), CompanyLimitedByGuarantee (CLG), PrivateUnlimited or PublicLimitedCompany." },
  { kind: 'date', name: 'incorporationDate', required: true, describe: "Date of incorporation. Required." },
  { kind: 'integer', min: 1, max: 12, name: 'financialYearStartMonth', describe: "Month (1 to 12) in which the financial year starts." },
  { kind: 'date', name: 'annualReturnDate', required: true, describe: "Exact Annual Return Date (ARD). Required, not before incorporation. On an update it must equal the current ARD: change it with company_annual_return_date_change instead." },
  { kind: 'date', name: 'annualReturnDateEffectiveFrom', nullable: true, describe: "Creation only: the date the initial ARD takes effect from." },
  { kind: 'enum', values: ENUMS.AnnualReturnDateSource, name: 'annualReturnDateSource', nullable: true, describe: "Creation only: where the initial ARD comes from (CroRecord, BroughtForward, ExtendedB73, CourtOrder or ManualOverride)." },
  { kind: 'string', max: 2000, name: 'annualReturnDateEvidenceReference', nullable: true, describe: "Creation only: reference to the evidence for the initial ARD." },
  { kind: 'sha256', name: 'annualReturnDateEvidenceSha256', nullable: true, describe: "Creation only: SHA-256 of that evidence." },
  { kind: 'string', max: 2000, name: 'annualReturnDateChangeReason', nullable: true, describe: "Creation only: why the initial ARD differs from the default." },
  { kind: 'string', max: 200, name: 'registeredOfficeAddress1', nullable: true, personal: true, describe: "Registered office, first line. Personal data: often a director's home." },
  { kind: 'string', max: 200, name: 'registeredOfficeAddress2', nullable: true, personal: true, describe: "Registered office, second line." },
  { kind: 'string', max: 100, name: 'registeredOfficeCity', nullable: true, personal: true, describe: "Registered office town or city." },
  { kind: 'string', max: 100, name: 'registeredOfficeCounty', nullable: true, personal: true, describe: "Registered office county, without \"Co.\"." },
  { kind: 'string', max: 10, name: 'registeredOfficeEircode', nullable: true, personal: true, describe: "Registered office Eircode." },
  { kind: 'boolean', name: 'isGroupMember', describe: "The company is a member of a group." },
  { kind: 'boolean', name: 'isHolding', describe: "Holding company (excluded from the micro regime)." },
  { kind: 'boolean', name: 'isInvestment', describe: "Investment undertaking (excluded from the micro regime)." },
  { kind: 'boolean', name: 'isSubsidiary', describe: "Subsidiary undertaking." },
  { kind: 'boolean', name: 'isDormant', describe: "Dormant: no significant accounting transactions in the period." },
  { kind: 'boolean', name: 'isTrading', describe: "Carries on a trade (affects corporation tax at 12.5%)." },
  { kind: 'boolean', name: 'isVatRegistered', describe: "Registered for VAT." },
  { kind: 'boolean', name: 'isEmployer', describe: "Registered as an employer for PAYE." },
  { kind: 'boolean', name: 'hasStock', describe: "Holds stock or inventory at year end." },
  { kind: 'boolean', name: 'ownsAssets', describe: "Owns fixed assets." },
  { kind: 'boolean', name: 'hasBorrowings', describe: "Has loans or other borrowings." },
  { kind: 'boolean', name: 'hasDirectorLoans', describe: "Has loans or similar arrangements with directors (sections 236 to 245)." },
  { kind: 'boolean', name: 'isListedSecurities', describe: "Has securities admitted to trading on a regulated market (Fifth Schedule ineligible entity)." },
  { kind: 'boolean', name: 'isCreditInstitution', describe: "Credit institution (ineligible entity)." },
  { kind: 'boolean', name: 'isInsuranceUndertaking', describe: "Insurance undertaking (ineligible entity)." },
  { kind: 'boolean', name: 'isPensionFund', describe: "Pension fund (ineligible entity)." },
  { kind: 'boolean', name: 'isFifthScheduleEntity', describe: "Another entity listed in Schedule 5 of the Companies Act 2014." },
  { kind: 'boolean', name: 'isOtherIneligibleEntity', describe: "Another ineligible entity (section 280F)." },
  { kind: 'boolean', name: 'isFinancialHoldingUndertaking', describe: "Financial holding undertaking." },
  { kind: 'boolean', name: 'preparesGroupFinancialStatements', describe: "Prepares group financial statements." },
  { kind: 'boolean', name: 'includedInHigherConsolidatedFinancialStatements', describe: "Included in consolidated financial statements of a larger group." },
  { kind: 'boolean', name: 'isCharitableOrganisation', describe: "A charity registered with the Charities Regulator (adds the charity annual return workflow)." },
  { kind: 'boolean', name: 'holdsCharitableTaxExemption', describe: "Holds Revenue charitable tax exemption (CHY). Only for a charitable organisation; suppresses CT1 deadlines while held." },
  { kind: 'string', max: 200, name: 'charitableTaxExemptionReference', nullable: true, describe: "The CHY reference. Required while the exemption is held." },
  { kind: 'date', name: 'charitableTaxExemptionConfirmedDate', nullable: true, describe: "Date Revenue confirmed the exemption. Required while the exemption is held." },
  { kind: 'enum', values: ENUMS.ChartOfAccountsVariant, name: 'chartOfAccountsVariant', nullable: true, describe: "Creation only: GenericTrading, or CharityClg (only for a company limited by guarantee). Omitted means the default for the company type." },
]);

export const COMPANY_OFFICER_INPUT_FIELDS = bodyFields('CompanyOfficerInput', [
  { kind: 'string', max: 200, name: 'name', required: true, describe: "Full name, as on the CRO register. Required, 200 characters or fewer." },
  { kind: 'enum', values: ENUMS.OfficerRole, name: 'role', required: true, describe: "Director, Secretary or CompanySecretary." },
  { kind: 'date', name: 'appointedDate', nullable: true, describe: "Date of appointment." },
  { kind: 'date', name: 'resignedDate', nullable: true, describe: "Date of resignation; not before the appointment date." },
  { kind: 'string', max: 500, name: 'address', nullable: true, personal: true, describe: "Residential or service address. Personal data, withheld unless the session has full data scope." },
]);

export const ACCOUNTING_PERIOD_INPUT_FIELDS = bodyFields('AccountingPeriodInput', [
  { kind: 'date', name: 'periodStart', required: true, describe: "First day of the period." },
  { kind: 'date', name: 'periodEnd', required: true, describe: "Last day of the period; at most 18 months after the start." },
  { kind: 'boolean', name: 'isFirstYear', describe: "The company's first financial year (no comparatives)." },
  { kind: 'boolean', name: 'memberAuditNoticeReceived', describe: "Members holding at least 10% served notice requiring an audit (section 334)." },
  { kind: 'date', name: 'memberAuditNoticeDate', nullable: true, describe: "Date of that notice; required when received." },
  { kind: 'boolean', name: 'goingConcernConfirmed', describe: "The directors confirm the going-concern basis." },
  { kind: 'string', max: 4000, name: 'goingConcernNote', nullable: true, describe: "Supporting note on going concern." },
]);

export const BANK_ACCOUNT_INPUT_FIELDS = bodyFields('BankAccountInput', [
  { kind: 'string', max: 200, name: 'name', required: true, describe: "Account name, as the bank shows it. Required." },
  { kind: 'string', max: 34, name: 'iban', nullable: true, describe: "IBAN. Cannot change once import evidence is retained." },
  { kind: 'string', max: 3, name: 'currency', nullable: true, describe: "ISO currency code; EUR by default. Cannot change once import evidence is retained." },
  { kind: 'money', name: 'openingBalance', describe: "Balance at the opening balance date." },
  { kind: 'date', name: 'openingBalanceDate', nullable: true, describe: "Date of the opening balance." },
]);

export const COMPANY_ONBOARDING_INPUT_FIELDS = bodyFields('CompanyOnboardingInput', [
  { kind: 'object', fields: COMPANY_INPUT_FIELDS, name: 'company', required: true, describe: "The company, with the same fields as company_create." },
  { kind: 'objectList', fields: COMPANY_OFFICER_INPUT_FIELDS, maxItems: 200, name: 'officers', nullable: true, describe: "Directors and secretary, created with the company." },
  { kind: 'object', fields: ACCOUNTING_PERIOD_INPUT_FIELDS, name: 'firstPeriod', required: true, describe: "The first accounting period." },
  { kind: 'object', fields: BANK_ACCOUNT_INPUT_FIELDS, name: 'openingBankAccount', required: true, describe: "The first bank account." },
]);

export const COMPANY_QUARANTINE_REQUEST_FIELDS = bodyFields('CompanyQuarantineRequest', [
  { kind: 'string', max: 200, name: 'confirmation', required: true, describe: "Type the company's legal name exactly, as a deliberate confirmation." },
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why, 20 to 2,000 characters. Kept with the immutable evidence." },
]);

export const ANNUAL_RETURN_DATE_CHANGE_INPUT_FIELDS = bodyFields('AnnualReturnDateChangeInput', [
  { kind: 'date', name: 'annualReturnDate', required: true, describe: "The new Annual Return Date." },
  { kind: 'date', name: 'effectiveFrom', required: true, describe: "From when the new ARD applies." },
  { kind: 'enum', values: ENUMS.AnnualReturnDateSource, name: 'source', required: true, describe: "Legal basis for the change: CroRecord, BroughtForward, ExtendedB73, CourtOrder or ManualOverride." },
  { kind: 'string', max: 2000, name: 'evidenceReference', required: true, describe: "Reference to the evidence (for example the CRO B73 acknowledgement)." },
  { kind: 'sha256', name: 'evidenceSha256', required: true, describe: "SHA-256 of the evidence document." },
  { kind: 'string', max: 2000, name: 'changeReason', required: true, describe: "Why the ARD is changing." },
]);

export const PERIOD_STATUS_UPDATE_FIELDS = bodyFields('PeriodStatusUpdate', [
  { kind: 'enum', values: ENUMS.PeriodStatus, name: 'status', required: true, describe: "Draft, Review, Finalised or Filed. Finalised and Filed lock the period and need final-output readiness; Filed also needs every filing obligation recorded with a reference." },
  { kind: 'string', max: 200, name: 'lockedBy', nullable: true, describe: "Ignored: FilingBridge records the signed-in person." },
  { kind: 'string', max: 1000, name: 'reopenReason', nullable: true, describe: "Required (at least 10 characters) when moving a locked period back to Draft or Review. Only an Owner may reopen." },
  { kind: 'date', name: 'approvalDate', nullable: true, describe: "Board approval date when finalising: on or after the period end and not in the future." },
]);

export const CREATE_USER_INPUT_FIELDS = bodyFields('CreateUserInput', [
  { kind: 'string', max: 320, name: 'email', required: true, personal: true, describe: "The new user's email, unique within this workspace." },
  { kind: 'string', max: 200, name: 'displayName', required: true, personal: true, describe: "The new user's name as shown in FilingBridge." },
  { kind: 'string', max: 50, name: 'role', required: true, describe: "Owner, Accountant, Reviewer or Client." },
  { kind: 'string', max: 256, min: 20, name: 'temporaryPassword', required: true, describe: "A temporary password of at least 20 characters mixing character classes. The user must change it at first sign-in. Hand it to the person immediately; do not store it." },
  { kind: 'intIdList', maxItems: 500, name: 'companyIds', required: true, describe: "Companies the user may access (Client and Accountant users are limited to these)." },
]);

export const INVITE_USER_INPUT_FIELDS = bodyFields('InviteUserInput', [
  { kind: 'string', max: 320, name: 'email', required: true, personal: true, describe: "The invitee's email, unique within this workspace." },
  { kind: 'string', max: 200, name: 'displayName', required: true, personal: true, describe: "The invitee's name as shown in FilingBridge." },
  { kind: 'string', max: 50, name: 'role', required: true, describe: "Owner, Accountant, Reviewer or Client." },
  { kind: 'intIdList', maxItems: 500, name: 'companyIds', required: true, describe: "Companies the user may access." },
]);

export const SET_USER_ACTIVE_INPUT_FIELDS = bodyFields('SetUserActiveInput', [
  { kind: 'boolean', name: 'active', required: true, describe: "true to reactivate, false to deactivate (revokes the user's sessions and connectors)." },
]);

export const SET_USER_ROLE_INPUT_FIELDS = bodyFields('SetUserRoleInput', [
  { kind: 'string', max: 50, name: 'role', required: true, describe: "Owner, Accountant, Reviewer or Client." },
]);

export const SET_USER_COMPANIES_INPUT_FIELDS = bodyFields('SetUserCompaniesInput', [
  { kind: 'intIdList', maxItems: 500, name: 'companyIds', required: true, describe: "The complete list of companies the user may access; replaces the existing assignments." },
]);

export const SIZE_CLASSIFICATION_INPUT_FIELDS = bodyFields('SizeClassificationInput', [
  { kind: 'money', name: 'turnover', required: true, describe: "Turnover for the period, in euro." },
  { kind: 'money', name: 'balanceSheetTotal', required: true, describe: "Balance sheet total (gross assets), in euro." },
  { kind: 'integer', min: 0, max: 1000000, name: 'avgEmployees', required: true, describe: "Average number of employees in the period." },
  { kind: 'enum', values: ENUMS.CompanySizeClass, name: 'priorYearClass', nullable: true, describe: "Refused if supplied: the prior-year class is derived from retained prior-period figures." },
  { kind: 'date', name: 'thresholdElectionEffectiveFrom', nullable: true, describe: "Date of an early election into the current (2024) thresholds, if made." },
]);

export const HISTORIC_ANCHOR_INPUT_FIELDS = bodyFields('HistoricAnchorInput', [
  { kind: 'money', name: 'turnover', required: true, describe: "Prior-year turnover from the retained historic evidence." },
  { kind: 'money', name: 'balanceSheetTotal', required: true, describe: "Prior-year balance sheet total from that evidence." },
  { kind: 'integer', min: 0, max: 1000000, name: 'avgEmployees', required: true, describe: "Prior-year average employees from that evidence." },
  { kind: 'string', max: 2000, name: 'sourceReference', required: true, describe: "Reference to the source (for example the filed prior-year financial statements)." },
  { kind: 'fileBytes', sha256Field: 'evidenceSha256', name: 'evidenceArtifact', required: true, describe: "The evidence document." },
  { kind: 'sha256', name: 'evidenceSha256', describe: "SHA-256 of the evidence document." },
  { kind: 'enum', values: ENUMS.CompanySizeClass, name: 'attestedEffectiveClass', nullable: true, describe: "The size class that applied, as attested by the evidence." },
  { kind: 'date', name: 'thresholdElectionEffectiveFrom', nullable: true, describe: "Date of an early threshold election, if made." },
]);

export const HISTORIC_ANCHOR_RETIREMENT_INPUT_FIELDS = bodyFields('HistoricAnchorRetirementInput', [
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why the historic anchor is being retired." },
]);

export const SIZE_CLASSIFICATION_OVERRIDE_INPUT_FIELDS = bodyFields('SizeClassificationOverrideInput', [
  { kind: 'enum', values: ENUMS.CompanySizeClass, name: 'overrideClass', required: true, describe: "The size class to apply instead of the computed one." },
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why the computed class is being overridden." },
  { kind: 'fileBytes', sha256Field: 'evidenceSha256', name: 'evidenceArtifact', required: true, describe: "Evidence supporting the override." },
  { kind: 'sha256', name: 'evidenceSha256', describe: "SHA-256 of the evidence." },
]);

export const FILING_REGIME_INPUT_FIELDS = bodyFields('FilingRegimeInput', [
  { kind: 'enum', values: ENUMS.ElectedRegime, name: 'electedRegime', required: true, describe: "The regime the directors elect (Micro, Small, SmallAbridged, Medium or Full). Omit to take the regime the size class allows." },
]);

export const MEMBER_AUDIT_NOTICE_INPUT_FIELDS = bodyFields('MemberAuditNoticeInput', [
  { kind: 'boolean', name: 'received', required: true, describe: "Members served notice requiring an audit (section 334)." },
  { kind: 'date', name: 'noticeDate', nullable: true, describe: "Date of the notice; required when received. Leave it out, or pass null, to clear it." },
]);

export const CATEGORISE_INPUT_FIELDS = bodyFields('CategoriseInput', [
  { kind: 'intId', name: 'categoryId', required: true, describe: "The account category to assign, from category_list." },
]);

export const BULK_CATEGORISE_INPUT_FIELDS = bodyFields('BulkCategoriseInput', [
  { kind: 'intIdList', maxItems: 500, name: 'transactionIds', required: true, describe: "The transactions to categorise, from transaction_list." },
  { kind: 'intId', name: 'categoryId', required: true, describe: "The account category to assign to all of them." },
]);

export const DUPLICATE_REVIEW_DECISION_INPUT_FIELDS = bodyFields('DuplicateReviewDecisionInput', [
  { kind: 'enum', values: ENUMS.DuplicateReviewStatus, name: 'decision', required: true, describe: "Retained (keep the row) or Discarded (treat it as a duplicate)." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Why, for the review record." },
  { kind: 'enum', values: ENUMS.DuplicateReviewStatus, name: 'expectedStatus', required: true, describe: "The candidate's current status as read, so a concurrent decision is not overwritten." },
  { kind: 'integer', min: 0, max: 1000000, name: 'expectedDecisionVersion', required: true, describe: "The decision version as read." },
]);

export const DUPLICATE_REVIEW_BATCH_DECISION_INPUT_FIELDS = bodyFields('DuplicateReviewBatchDecisionInput', [
  { kind: 'enum', values: ENUMS.DuplicateReviewStatus, name: 'decision', required: true, describe: "Retained or Discarded, for every candidate in the import batch." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Why, for the review record." },
  { kind: 'enum', values: ENUMS.DuplicateReviewStatus, name: 'expectedStatus', required: true, describe: "The batch's current status as read." },
  { kind: 'integer', min: 0, max: 1000000, name: 'expectedCandidateCount', required: true, describe: "The number of candidates in the batch as read." },
  { kind: 'string', max: 2000, name: 'expectedDecisionToken', nullable: true, describe: "The batch decision token as read." },
]);

export const ACCOUNT_CATEGORY_INPUT_FIELDS = bodyFields('AccountCategoryInput', [
  { kind: 'string', max: 20, name: 'code', required: true, describe: "Nominal code, unique for the company. Required." },
  { kind: 'string', max: 200, name: 'name', required: true, describe: "Category name. Required." },
  { kind: 'enum', values: ENUMS.AccountCategoryType, name: 'type', required: true, describe: "Income, Expense, Asset, Liability or Equity." },
  { kind: 'enum', values: ENUMS.TaxTreatment, name: 'taxTreatment', describe: "Corporation tax treatment: Deductible, NonDeductible, CapitalAllowance, Exempt or Other." },
  { kind: 'boolean', name: 'isNonTradingIncome', describe: "Income taxed as non-trading (25%) rather than trading (12.5%)." },
  { kind: 'intId', name: 'parentId', nullable: true, describe: "Parent category, for a sub-account." },
  { kind: 'enum', values: ENUMS.AccountStatementLine, name: 'statementLine', nullable: true, describe: "Explicit financial-statement line; omitted means derived from type and code." },
  { kind: 'enum', values: ENUMS.AccountCashFlowClass, name: 'cashFlowClass', nullable: true, describe: "Explicit cash-flow classification; omitted means derived." },
]);

export const TRANSACTION_RULE_INPUT_FIELDS = bodyFields('TransactionRuleInput', [
  { kind: 'string', max: 200, name: 'pattern', required: true, describe: "Text matched against the bank description to categorise automatically." },
  { kind: 'intId', name: 'categoryId', required: true, describe: "Category the rule assigns." },
  { kind: 'integer', min: -1000, max: 1000, name: 'priority', required: true, describe: "Higher priority rules are applied first." },
]);

export const CHART_ADOPTION_INPUT_FIELDS = bodyFields('ChartAdoptionInput', [
  { kind: 'enum', values: ENUMS.ChartOfAccountsVariant, name: 'variant', required: true, describe: "The chart of accounts to adopt." },
  { kind: 'sha256', name: 'planSha256', required: true, describe: "The planSha256 returned by category_chart_adoption_plan, binding this adoption to the plan that was reviewed." },
  { kind: 'boolean', name: 'acknowledgeStaleFilingArtifacts', required: true, describe: "Acknowledge that generated filing artifacts become stale and must be regenerated." },
]);

export const DEBTOR_INPUT_FIELDS = bodyFields('DebtorInput', [
  { kind: 'string', max: 200, name: 'name', required: true, describe: "Who owes the money. Required." },
  { kind: 'money', name: 'amount', required: true, describe: "Amount owed at the period end; not negative." },
  { kind: 'enum', values: ENUMS.DebtorType, name: 'type', required: true, describe: "Trade, Other or Prepayment." },
  { kind: 'string', max: 4000, name: 'notes', nullable: true, describe: "Supporting notes." },
]);

export const CREDITOR_INPUT_FIELDS = bodyFields('CreditorInput', [
  { kind: 'string', max: 200, name: 'name', required: true, describe: "Who is owed. Required." },
  { kind: 'money', name: 'amount', required: true, describe: "Amount owed at the period end; not negative." },
  { kind: 'enum', values: ENUMS.CreditorType, name: 'type', required: true, describe: "Trade, Other, Accrual or Tax." },
  { kind: 'boolean', name: 'dueWithinYear', describe: "Due within one year (current) rather than after one year." },
  { kind: 'string', max: 4000, name: 'notes', nullable: true, describe: "Supporting notes." },
]);

export const FIXED_ASSET_INPUT_FIELDS = bodyFields('FixedAssetInput', [
  { kind: 'string', max: 200, name: 'name', required: true, describe: "Asset name. Required." },
  { kind: 'string', max: 2000, name: 'category', required: true, describe: "Asset category, such as Plant and machinery, Motor vehicles, Fixtures and fittings, Computer equipment, Land and buildings. Required." },
  { kind: 'money', name: 'cost', required: true, describe: "Original cost; not negative." },
  { kind: 'money', name: 'residualValue', describe: "Expected residual value; between zero and cost." },
  { kind: 'date', name: 'acquisitionDate', required: true, describe: "Date acquired." },
  { kind: 'date', name: 'disposalDate', nullable: true, describe: "Date disposed of, if it was; not before acquisition. Requires disposal proceeds." },
  { kind: 'money', name: 'disposalProceeds', nullable: true, describe: "Proceeds on disposal." },
  { kind: 'integer', min: 1, max: 100, name: 'usefulLifeYears', required: true, describe: "Useful life in years, at least one." },
  { kind: 'enum', values: ENUMS.DepreciationMethod, name: 'depreciationMethod', describe: "StraightLine or ReducingBalance." },
  { kind: 'enum', values: ENUMS.CapitalAllowanceTreatment, name: 'capitalAllowanceTreatment', describe: "Unreviewed, NonQualifying, PlantAndMachinery12Point5 (wear and tear over 8 years, s.284 TCA 1997) or UnsupportedSpecialScheme." },
  { kind: 'string', max: 2000, name: 'capitalAllowanceEvidence', nullable: true, describe: "Evidence for a reviewed treatment, at least 20 characters." },
]);

export const INVENTORY_INPUT_FIELDS = bodyFields('InventoryInput', [
  { kind: 'string', max: 2000, name: 'description', required: true, describe: "What the stock is. Required." },
  { kind: 'money', name: 'value', required: true, describe: "Value at the period end; not negative." },
  { kind: 'enum', values: ENUMS.ValuationMethod, name: 'valuationMethod', describe: "Cost, NetRealisableValue or LowerOfCostAndNrv." },
]);

export const LOAN_INPUT_FIELDS = bodyFields('LoanInput', [
  { kind: 'string', max: 2000, name: 'lender', required: true, describe: "Who lent the money. Required." },
  { kind: 'money', name: 'originalAmount', required: true, describe: "Amount originally borrowed." },
  { kind: 'money', name: 'balance', required: true, describe: "Outstanding balance at the balance date." },
  { kind: 'date', name: 'drawdownDate', required: true, describe: "Date drawn down. Required." },
  { kind: 'date', name: 'balanceAsOfDate', required: true, describe: "Date of the balance; not before drawdown. Required." },
  { kind: 'decimal', min: 0, name: 'interestRate', describe: "Annual interest rate, in per cent." },
  { kind: 'boolean', name: 'isDirectorLoan', describe: "The lender is a director (a loan to the company)." },
  { kind: 'money', name: 'dueWithinYear', describe: "Part of the balance due within one year." },
  { kind: 'money', name: 'dueAfterYear', describe: "Part of the balance due after one year." },
]);

export const LOAN_BALANCE_SNAPSHOT_INPUT_FIELDS = bodyFields('LoanBalanceSnapshotInput', [
  { kind: 'intId', name: 'loanId', required: true, describe: "The loan, from loan_list." },
  { kind: 'money', name: 'openingBalance', required: true, describe: "Balance at the period start; not negative." },
  { kind: 'money', name: 'drawdowns', describe: "Drawn in the period." },
  { kind: 'money', name: 'repayments', describe: "Repaid in the period." },
  { kind: 'money', name: 'closingBalance', required: true, describe: "Must equal opening plus drawdowns less repayments." },
  { kind: 'money', name: 'dueWithinYear', describe: "Due within one year; with dueAfterYear it must agree to the closing balance." },
  { kind: 'money', name: 'dueAfterYear', describe: "Due after one year." },
  { kind: 'string', max: 4000, name: 'notes', nullable: true, describe: "Supporting notes." },
]);

export const DIRECTOR_LOAN_MOVEMENT_INPUT_FIELDS = bodyFields('DirectorLoanMovementInput', [
  { kind: 'date', name: 'movementDate', required: true, describe: "Date, within the period and not before the arrangement." },
  { kind: 'enum', values: ENUMS.DirectorLoanMovementType, name: 'movementType', required: true, describe: "Advance or Repayment." },
  { kind: 'money', name: 'amount', required: true, describe: "Positive amount." },
  { kind: 'string', max: 2000, name: 'evidenceReference', nullable: true, describe: "Evidence reference, such as a bank statement line." },
]);

export const DIRECTOR_LOAN_INPUT_FIELDS = bodyFields('DirectorLoanInput', [
  { kind: 'intId', name: 'directorId', nullable: true, describe: "The related director (an officer with role Director who served in the period). Required unless the counterparty is a group company." },
  { kind: 'money', name: 'openingBalance', required: true, describe: "Balance owed to the company at the period start; not negative." },
  { kind: 'money', name: 'advances', required: true, describe: "Advanced in the period." },
  { kind: 'money', name: 'repayments', required: true, describe: "Repaid in the period." },
  { kind: 'money', name: 'closingBalance', required: true, describe: "Must equal opening plus advances less repayments." },
  { kind: 'decimal', min: 0, name: 'interestRate', required: true, describe: "Interest rate in per cent." },
  { kind: 'money', name: 'interestCharged', required: true, describe: "Interest charged in the period." },
  { kind: 'boolean', name: 'isDocumented', required: true, describe: "Ignored: compliance follows termsStatus." },
  { kind: 'string', max: 4000, name: 'loanTerms', nullable: true, personal: true, describe: "Main conditions of the arrangement (section 307 disclosure). Personal data." },
  { kind: 'money', name: 'maxBalanceDuringYear', required: true, describe: "Highest balance in the period; not below the opening or closing balance." },
  { kind: 'enum', values: ENUMS.DirectorLoanCounterpartyType, name: 'counterpartyType', describe: "Director, ConnectedPerson or GroupCompany." },
  { kind: 'string', max: 200, name: 'counterpartyName', nullable: true, personal: true, describe: "Name of the connected person or group company. Personal data." },
  { kind: 'enum', values: ENUMS.DirectorLoanArrangementType, name: 'arrangementType', describe: "Loan, QuasiLoan, CreditTransaction or GuaranteeOrSecurity." },
  { kind: 'date', name: 'arrangementDate', nullable: true, describe: "Date the arrangement was made; not after the period end." },
  { kind: 'enum', values: ENUMS.DirectorLoanTermsStatus, name: 'termsStatus', describe: "Whether the terms are in writing (section 236 presumptions)." },
  { kind: 'money', name: 'allowanceMade', describe: "Allowance made for non-recovery." },
  { kind: 'string', max: 2000, name: 'section236PresumptionEvidenceReference', nullable: true, describe: "Evidence reference for the section 236 position." },
  { kind: 'enum', values: ENUMS.DirectorLoanComplianceBasis, name: 'complianceBasis', describe: "Legal basis relied on under sections 239 to 245." },
  { kind: 'enum', values: ENUMS.DirectorLoanRelevantAssetsBasis, name: 'relevantAssetsBasis', describe: "How relevant assets (section 238(2)) were measured." },
  { kind: 'money', name: 'relevantAssetsAmount', nullable: true, describe: "Relevant assets amount." },
  { kind: 'date', name: 'relevantAssetsAsOfDate', nullable: true, describe: "Date relevant assets were measured at." },
  { kind: 'string', max: 2000, name: 'relevantAssetsReference', nullable: true, describe: "Evidence reference for relevant assets." },
  { kind: 'boolean', name: 'noPriorFinancialStatementsConfirmed', describe: "Confirms no financial statements have yet been laid (called-up share capital basis)." },
  { kind: 'enum', values: ENUMS.DirectorLoanRelevantAssetsFallReview, name: 'relevantAssetsFallReview', describe: "Section 241 review of a later fall in relevant assets." },
  { kind: 'date', name: 'relevantAssetsReductionAwarenessDate', nullable: true, describe: "Date the directors became aware of the fall." },
  { kind: 'date', name: 'termsAmendedDate', nullable: true, describe: "Date the terms were amended within two months." },
  { kind: 'string', max: 2000, name: 'termsAmendmentEvidenceReference', nullable: true, describe: "Evidence of the amendment." },
  { kind: 'string', max: 2000, name: 'exceptionEvidenceReference', nullable: true, describe: "Evidence for the exception relied on." },
  { kind: 'date', name: 'sapDeclarationDate', nullable: true, describe: "Summary Approval Procedure: date of the directors' declaration (within 30 days before the resolution)." },
  { kind: 'date', name: 'sapResolutionDate', nullable: true, describe: "SAP: date of the special resolution." },
  { kind: 'date', name: 'sapActivityStartDate', nullable: true, describe: "SAP: date the restricted activity began (within 12 months of the resolution)." },
  { kind: 'date', name: 'sapCroFilingDate', nullable: true, describe: "SAP: date the declaration was filed with the CRO (within 21 days of the activity starting)." },
  { kind: 'string', max: 2000, name: 'sapDeclarationReference', nullable: true, describe: "SAP declaration reference." },
  { kind: 'string', max: 2000, name: 'sapResolutionReference', nullable: true, describe: "SAP resolution reference." },
  { kind: 'string', max: 2000, name: 'sapCroFilingReference', nullable: true, describe: "SAP CRO filing reference." },
  { kind: 'boolean', name: 'sapDeclarationCoversSection203Matters', describe: "The declaration covers every section 203 matter." },
  { kind: 'date', name: 'expenseIncurredDate', nullable: true, describe: "Section 244: date the vouched expense was incurred." },
  { kind: 'date', name: 'expenseDischargedDate', nullable: true, describe: "Section 244: date it was discharged." },
  { kind: 'boolean', name: 'ordinaryCourseConfirmed', describe: "Section 245: the transaction is in the ordinary course of business." },
  { kind: 'boolean', name: 'noMoreFavourableTermsConfirmed', describe: "Section 245: on terms no more favourable than for an unconnected person." },
  { kind: 'enum', values: ENUMS.DirectorLoanReviewDecision, name: 'reviewDecision', describe: "Unreviewed, Accepted or RemediationRequired. A decision needs a review note of at least 20 characters." },
  { kind: 'string', max: 4000, name: 'reviewNote', nullable: true, personal: true, describe: "The reviewer's note. Personal data." },
  { kind: 'objectList', fields: DIRECTOR_LOAN_MOVEMENT_INPUT_FIELDS, maxItems: 200, name: 'balanceMovements', nullable: true, describe: "Dated advances and repayments; when given they must reconcile to the totals, the closing and the maximum balance." },
]);

export const PAYROLL_SUMMARY_INPUT_FIELDS = bodyFields('PayrollSummaryInput', [
  { kind: 'money', name: 'grossWages', describe: "Gross wages and salaries for the period." },
  { kind: 'money', name: 'directorsFees', describe: "Directors' fees and remuneration." },
  { kind: 'money', name: 'employerPrsi', describe: "Employer PRSI." },
  { kind: 'money', name: 'pensionContributions', describe: "Employer pension contributions." },
  { kind: 'integer', min: 0, max: 1000000, name: 'staffCount', describe: "Average number of staff." },
]);

export const TAX_BALANCE_INPUT_FIELDS = bodyFields('TaxBalanceInput', [
  { kind: 'money', name: 'liability', required: true, describe: "Liability for the period." },
  { kind: 'money', name: 'paid', required: true, describe: "Paid in the period." },
  { kind: 'money', name: 'balance', required: true, describe: "Balance at the period end." },
]);

export const CORPORATION_TAX_DUE_CONFIRMATION_INPUT_FIELDS = bodyFields('CorporationTaxDueConfirmationInput', [
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why corporation tax is confirmed as due for a charitably exempt company." },
  { kind: 'money', name: 'taxableProfitOutsideExemption', nullable: true, describe: "Trading profit outside the exemption." },
  { kind: 'money', name: 'nonTradingProfitOutsideExemption', nullable: true, describe: "Non-trading profit outside the exemption." },
]);

export const DIVIDEND_INPUT_FIELDS = bodyFields('DividendInput', [
  { kind: 'money', name: 'amount', required: true, describe: "Dividend amount; not negative." },
  { kind: 'date', name: 'dateDeclared', nullable: true, describe: "Date declared." },
  { kind: 'date', name: 'datePaid', nullable: true, describe: "Date paid; not before the declaration." },
]);

export const YEAR_END_REVIEW_INPUT_FIELDS = bodyFields('YearEndReviewInput', [
  { kind: 'boolean', name: 'confirmed', required: true, describe: "The section has been reviewed and is complete." },
  { kind: 'string', max: 2000, name: 'confirmedBy', nullable: true, describe: "Ignored where FilingBridge records the signed-in reviewer." },
  { kind: 'string', max: 4000, name: 'note', nullable: true, describe: "Review note. Directors' report sections need one." },
]);

export const NOTES_DISCLOSURE_INPUT_FIELDS = bodyFields('NotesDisclosureInput', [
  { kind: 'string', max: 200, name: 'title', required: true, describe: "Note title. Required." },
  { kind: 'string', max: 20000, name: 'content', required: true, describe: "Note text, up to 20,000 characters. Required." },
  { kind: 'boolean', name: 'isIncluded', describe: "Whether the note appears in the financial statements." },
]);

export const SHARE_CAPITAL_INPUT_FIELDS = bodyFields('ShareCapitalInput', [
  { kind: 'string', max: 2000, name: 'shareClass', required: true, describe: "Share class, such as Ordinary. Required." },
  { kind: 'decimal', min: 0, name: 'nominalValue', required: true, describe: "Nominal value per share." },
  { kind: 'integer', min: 0, max: 9007199254740991, name: 'numberIssued', required: true, describe: "Number of shares issued." },
  { kind: 'boolean', name: 'isFullyPaid', describe: "The shares are fully paid." },
  { kind: 'date', name: 'issueDate', required: true, describe: "Date issued. Required." },
  { kind: 'date', name: 'cancelledDate', nullable: true, describe: "Date cancelled, if they were; not before issue." },
]);

export const OPENING_BALANCE_INPUT_FIELDS = bodyFields('OpeningBalanceInput', [
  { kind: 'money', name: 'debit', required: true, describe: "Opening debit for the category." },
  { kind: 'money', name: 'credit', required: true, describe: "Opening credit for the category." },
  { kind: 'string', max: 2000, name: 'sourceNote', nullable: true, describe: "Where the figure comes from (for example the prior-year signed accounts)." },
  { kind: 'string', max: 2000, name: 'enteredBy', nullable: true, describe: "Ignored: FilingBridge records the signed-in person." },
  { kind: 'boolean', name: 'reviewed', required: true, describe: "The opening balance has been reviewed." },
]);

export const POST_BALANCE_SHEET_EVENT_INPUT_FIELDS = bodyFields('PostBalanceSheetEventInput', [
  { kind: 'string', max: 2000, name: 'description', required: true, describe: "What happened. Required." },
  { kind: 'date', name: 'eventDate', required: true, describe: "When it happened, after the period end." },
  { kind: 'boolean', name: 'isAdjusting', describe: "An adjusting event (conditions existing at the period end) rather than a non-adjusting one." },
  { kind: 'money', name: 'financialImpact', nullable: true, describe: "Estimated financial effect." },
  { kind: 'string', max: 2000, name: 'actionRequired', nullable: true, describe: "Action required in the financial statements." },
]);

export const RELATED_PARTY_TRANSACTION_INPUT_FIELDS = bodyFields('RelatedPartyTransactionInput', [
  { kind: 'string', max: 200, name: 'partyName', required: true, personal: true, describe: "The related party. Personal data." },
  { kind: 'string', max: 2000, name: 'relationship', required: true, describe: "Relationship, such as Director, Connected Person or Group Company." },
  { kind: 'string', max: 2000, name: 'transactionType', required: true, describe: "Type, such as Sale, Purchase, Loan or Management Fee." },
  { kind: 'money', name: 'amount', required: true, describe: "Value of the transactions in the period." },
  { kind: 'money', name: 'balanceOwed', nullable: true, describe: "Balance outstanding at the period end." },
  { kind: 'string', max: 2000, name: 'terms', nullable: true, personal: true, describe: "Terms of the transactions. Personal data." },
]);

export const CONTINGENT_LIABILITY_INPUT_FIELDS = bodyFields('ContingentLiabilityInput', [
  { kind: 'string', max: 2000, name: 'description', required: true, describe: "What the contingency is. Required." },
  { kind: 'string', max: 2000, name: 'nature', required: true, describe: "Nature, such as Guarantee, Litigation, Warranty or Grant." },
  { kind: 'money', name: 'estimatedAmount', nullable: true, describe: "Estimated amount." },
  { kind: 'string', max: 2000, name: 'likelihood', describe: "Probable, Possible or Remote." },
]);

export const GOING_CONCERN_INPUT_FIELDS = bodyFields('GoingConcernInput', [
  { kind: 'boolean', name: 'confirmed', required: true, describe: "The directors confirm the going-concern basis is appropriate." },
  { kind: 'string', max: 4000, name: 'note', nullable: true, describe: "Supporting note; needed when there is material uncertainty." },
]);

export const ADJUSTMENT_INPUT_FIELDS = bodyFields('AdjustmentInput', [
  { kind: 'string', max: 2000, name: 'description', required: true, describe: "What the journal does." },
  { kind: 'intId', name: 'debitCategoryId', nullable: true, describe: "Category debited." },
  { kind: 'intId', name: 'creditCategoryId', nullable: true, describe: "Category credited." },
  { kind: 'money', name: 'amount', required: true, describe: "Journal amount." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Why the adjustment is needed." },
  { kind: 'string', max: 2000, name: 'legalBasis', nullable: true, describe: "Accounting or legal basis (for example FRS 102 section 13)." },
  { kind: 'money', name: 'impactOnProfit', required: true, describe: "Effect on profit (negative reduces it)." },
  { kind: 'money', name: 'impactOnAssets', required: true, describe: "Effect on net assets." },
]);

export const CORPORATION_TAX_SCOPE_REVIEW_INPUT_FIELDS = bodyFields('CorporationTaxScopeReviewInput', [
  { kind: 'boolean', name: 'isCloseCompany', required: true, describe: "A close company (surcharge on undistributed income may apply). null when not yet determined." },
  { kind: 'boolean', name: 'isServiceCompany', required: true, describe: "A close service company. null when not yet determined." },
  { kind: 'boolean', name: 'hasGroupOrConsortiumRelief', required: true, describe: "Group or consortium relief is claimed or surrendered." },
  { kind: 'boolean', name: 'hasChargeableGains', required: true, describe: "There are chargeable gains." },
  { kind: 'boolean', name: 'hasForeignIncomeOrTaxCredits', required: true, describe: "There is foreign income or foreign tax credit." },
  { kind: 'boolean', name: 'hasExceptedTrade', required: true, describe: "There is an excepted trade (25%)." },
  { kind: 'boolean', name: 'hasOtherReliefsOrSpecialRegimes', required: true, describe: "Other reliefs or special regimes apply." },
  { kind: 'boolean', name: 'declaredPassiveIncomePresent', required: true, describe: "Passive (non-trading) income is present." },
  { kind: 'boolean', name: 'passiveIncomeClassificationReviewed', required: true, describe: "Passive income classification has been reviewed." },
  { kind: 'enum', values: ENUMS.CorporationTaxLossTreatment, name: 'lossTreatment', required: true, describe: "How trading losses are treated." },
  { kind: 'money', name: 'broughtForwardTradingLoss', required: true, describe: "Trading loss brought forward." },
  { kind: 'string', max: 2000, name: 'broughtForwardLossEvidence', nullable: true, describe: "Evidence for the loss brought forward." },
  { kind: 'string', max: 4000, name: 'evidenceNote', required: true, describe: "The reviewer's note on scope." },
]);

export const REVIEW_INPUT_FIELDS = bodyFields('ReviewInput', [
  { kind: 'date', name: 'priorPeriodStart', nullable: true, describe: "Start of the prior accounting period (preliminary tax)." },
  { kind: 'date', name: 'priorPeriodEnd', nullable: true, describe: "End of the prior accounting period." },
  { kind: 'money', name: 'priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239', nullable: true, describe: "Prior-period corporation tax liability excluding surcharge and section 239 tax." },
  { kind: 'money', name: 'priorPeriodSection239IncomeTax', nullable: true, describe: "Prior-period section 239 income tax." },
  { kind: 'money', name: 'currentPeriodSection239IncomeTax', required: true, describe: "Current-period section 239 income tax." },
  { kind: 'string', max: 2000, name: 'priorLiabilityEvidenceReference', nullable: true, describe: "Evidence for the prior liability." },
  { kind: 'boolean', name: 'hasInterestLimitationRule', required: true, describe: "The interest limitation rule applies." },
  { kind: 'boolean', name: 'usesNotionalGroupPaymentAllocation', required: true, describe: "Group payment allocation is used." },
  { kind: 'boolean', name: 'hasDirtOrOtherWithholdingCredits', required: true, describe: "DIRT or other withholding credits are claimed." },
  { kind: 'boolean', name: 'hasOtherPreliminaryTaxAdjustments', required: true, describe: "Other preliminary-tax adjustments apply." },
  { kind: 'boolean', name: 'hasMandatoryElectronicFilingExemption', required: true, describe: "Exempt from mandatory electronic filing." },
  { kind: 'string', max: 4000, name: 'evidenceNote', required: true, describe: "The reviewer's note." },
]);

export const PAYMENT_INPUT_FIELDS = bodyFields('PaymentInput', [
  { kind: 'date', name: 'paymentDate', required: true, describe: "Date paid to Revenue." },
  { kind: 'money', name: 'amount', required: true, describe: "Amount paid." },
  { kind: 'enum', values: ENUMS.CorporationTaxPaymentKind, name: 'kind', required: true, describe: "PreliminaryFirst, PreliminarySecondOrSingle, Balance, InterestOrSurcharge or Other." },
  { kind: 'string', max: 2000, name: 'evidenceReference', required: true, describe: "Evidence of payment (for example the ROS payment receipt reference)." },
  { kind: 'string', max: 2000, name: 'externalPaymentReference', nullable: true, describe: "Revenue's payment reference." },
]);

export const DEADLINE_OVERRIDE_INPUT_FIELDS = bodyFields('DeadlineOverrideInput', [
  { kind: 'enum', values: ENUMS.DeadlineType, name: 'deadlineType', required: true, describe: "CRO, Charity or Revenue." },
  { kind: 'date', name: 'overrideDueDate', required: true, describe: "The due date to use instead of the computed one." },
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why the computed deadline is wrong." },
  { kind: 'string', max: 2000, name: 'evidenceReference', nullable: true, describe: "Evidence for the override." },
  { kind: 'sha256', name: 'evidenceSha256', nullable: true, describe: "SHA-256 of that evidence." },
]);

export const MARK_FILED_INPUT_FIELDS = bodyFields('MarkFiledInput', [
  { kind: 'enum', values: ENUMS.DeadlineType, name: 'deadlineType', required: true, describe: "CRO, Charity or Revenue." },
  { kind: 'date', name: 'filedDate', required: true, describe: "Date the return was filed externally." },
  { kind: 'string', max: 2000, name: 'filingReference', nullable: true, describe: "The external filing reference (needed before the period can be marked Filed)." },
]);

export const CHARITY_INFO_INPUT_FIELDS = bodyFields('CharityInfoInput', [
  { kind: 'string', max: 2000, name: 'charityNumber', nullable: true, describe: "Registered charity number (RCN)." },
  { kind: 'string', max: 2000, name: 'charityType', nullable: true, describe: "Type of charity." },
  { kind: 'money', name: 'grossIncome', describe: "Gross income for the period." },
  { kind: 'string', max: 2000, name: 'charitableObjectives', nullable: true, describe: "Charitable objectives." },
  { kind: 'string', max: 2000, name: 'principalActivities', nullable: true, describe: "Principal activities." },
  { kind: 'boolean', name: 'governanceCodeCompliant', nullable: true, describe: "Compliant with the Charities Governance Code." },
  { kind: 'string', max: 2000, name: 'governanceCodeNote', nullable: true, describe: "Explanation of governance code compliance." },
  { kind: 'string', max: 2000, name: 'governanceEvidenceReference', nullable: true, describe: "Evidence reference for governance compliance." },
  { kind: 'fileBytes', name: 'governanceEvidenceArtifact', nullable: true, describe: "Governance evidence document." },
  { kind: 'boolean', name: 'hasInternationalTransfers', describe: "Funds transferred outside Ireland." },
  { kind: 'string', max: 2000, name: 'internationalTransferDetails', nullable: true, describe: "Details of international transfers." },
  { kind: 'boolean', name: 'trusteeRemunerationPaid', describe: "Trustees were remunerated." },
  { kind: 'money', name: 'trusteeRemunerationAmount', describe: "Total trustee remuneration." },
  { kind: 'string', max: 2000, name: 'trusteeExpensesDetails', nullable: true, describe: "Trustee expenses details." },
]);

export const CHARITY_TRUSTEE_REVIEW_INPUT_FIELDS = bodyFields('CharityTrusteeReviewInput', [
  { kind: 'boolean', name: 'accepted', required: true, describe: "The trustee population for the period is accepted as complete." },
  { kind: 'string', max: 2000, name: 'evidenceReference', nullable: true, describe: "Evidence reference for the review." },
  { kind: 'fileBytes', name: 'evidenceArtifact', nullable: true, describe: "Evidence document for the review." },
]);

export const FUND_BALANCE_INPUT_FIELDS = bodyFields('FundBalanceInput', [
  { kind: 'string', max: 2000, name: 'fundName', required: true, describe: "Fund name. Required." },
  { kind: 'string', max: 2000, name: 'fundType', required: true, describe: "Unrestricted, Restricted or Endowment." },
  { kind: 'money', name: 'openingBalance', describe: "Balance at the period start." },
  { kind: 'money', name: 'incomingResources', describe: "Income in the period." },
  { kind: 'money', name: 'resourcesExpended', describe: "Expenditure in the period." },
  { kind: 'money', name: 'transfers', describe: "Transfers between funds (positive in, negative out)." },
  { kind: 'money', name: 'gainsLosses', describe: "Gains and losses on investments." },
  { kind: 'string', max: 4000, name: 'notes', nullable: true, describe: "Supporting notes." },
]);

export const FILING_STATUS_INPUT_FIELDS = bodyFields('FilingStatusInput', [
  { kind: 'enum', values: ENUMS.FilingStatus, name: 'status', required: true, describe: "The CRO workflow status to record. Submitted means the person filed it externally in CORE; Accepted needs confirmed CORE payment; Rejected and CorrectionRequired are only valid after Submitted." },
  { kind: 'string', max: 2000, name: 'by', nullable: true, describe: "Ignored: FilingBridge records the signed-in person." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Reason, for rejections and send-backs." },
  { kind: 'string', max: 2000, name: 'submissionReference', nullable: true, describe: "The CORE reference of the externally filed return, recorded as evidence." },
]);

export const CRO_PAYMENT_INPUT_FIELDS = bodyFields('CroPaymentInput', [
  { kind: 'string', max: 2000, name: 'by', nullable: true, describe: "Ignored: FilingBridge records the signed-in person." },
]);

export const CRO_NOT_DELIVERED_INPUT_FIELDS = bodyFields('CroNotDeliveredInput', [
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why the sent-back or rejected CRO filing is treated as not delivered (5 to 1,000 characters). Owner or Reviewer only." },
]);

export const CHARITY_REPORT_GENERATED_INPUT_FIELDS = bodyFields('CharityReportGeneratedInput', [
  { kind: 'string', max: 2000, name: 'reportType', required: true, describe: "sofa, or trustees-report." },
]);

export const CHARITY_FILING_STATUS_INPUT_FIELDS = bodyFields('CharityFilingStatusInput', [
  { kind: 'enum', values: ENUMS.FilingStatus, name: 'status', required: true, describe: "The charity annual return status to record." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Reason, for rejections and send-backs." },
  { kind: 'string', max: 2000, name: 'annualReturnReference', nullable: true, describe: "The Charities Regulator annual return reference." },
]);

export const REVENUE_EXTERNAL_VALIDATION_INPUT_FIELDS = bodyFields('RevenueExternalValidationInput', [
  { kind: 'sha256', name: 'artifactSha256', required: true, describe: "SHA-256 of the final iXBRL file that was validated externally." },
  { kind: 'string', max: 2000, name: 'externalReference', required: true, describe: "Reference of the external validation (for example the ROS test validation)." },
]);

export const REVENUE_FILING_STATUS_INPUT_FIELDS = bodyFields('RevenueFilingStatusInput', [
  { kind: 'enum', values: ENUMS.FilingStatus, name: 'status', required: true, describe: "The Revenue CT1 workflow status to record." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Reason, for rejections and send-backs." },
  { kind: 'string', max: 2000, name: 'filingReference', nullable: true, describe: "The ROS notice number of the externally filed return." },
]);

export const MARK_GENERATED_INPUT_FIELDS = bodyFields('MarkGeneratedInput', [
  { kind: 'string', max: 2000, name: 'documentType', required: true, describe: "Retired: FilingBridge now records CRO document generation itself and refuses this." },
]);

export const DIRECTORS_APPROVAL_RECORD_INPUT_FIELDS = bodyFields('DirectorsApprovalRecordInput', [
  { kind: 'intId', name: 'approvalPageId', required: true, describe: "The approval page, from filing_directors_approval_page_create." },
  { kind: 'fileBytes', sha256Field: 'signedPageSha256', name: 'signedPageArtifact', required: true, describe: "The signed approval page (s.324)." },
  { kind: 'sha256', name: 'signedPageSha256', describe: "SHA-256 of the signed page." },
  { kind: 'fileBytes', sha256Field: 'resolutionSha256', name: 'resolutionArtifact', required: true, describe: "The board resolution approving the financial statements." },
  { kind: 'sha256', name: 'resolutionSha256', describe: "SHA-256 of the resolution." },
]);

export const DIRECTORS_APPROVAL_PAGE_INPUT_FIELDS = bodyFields('DirectorsApprovalPageInput', [
  { kind: 'date', name: 'approvalDate', required: true, describe: "Board approval date: on or after the period end, not in the future." },
  { kind: 'intId', name: 'firstDirectorOfficerId', required: true, describe: "The first signing director, from officer_list." },
  { kind: 'intId', name: 'secondDirectorOfficerId', nullable: true, describe: "A second signing director, where two sign." },
  { kind: 'string', max: 2000, name: 'resolutionReference', required: true, describe: "Reference of the board resolution." },
]);

export const DIRECTORS_APPROVAL_WITHDRAWAL_INPUT_FIELDS = bodyFields('DirectorsApprovalWithdrawalInput', [
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why the recorded approval is withdrawn." },
  { kind: 'string', max: 4000, name: 'note', required: true, describe: "Supporting note." },
]);

export const CRO_SIGNING_PACK_INPUT_FIELDS = bodyFields('CroSigningPackInput', [
  { kind: 'intId', name: 'directorOfficerId', required: true, describe: "The director who signed the B1, from officer_list." },
  { kind: 'intId', name: 'secretaryOfficerId', required: true, describe: "The company secretary who signed." },
  { kind: 'dateTime', name: 'signedAtUtc', required: true, describe: "When it was signed." },
  { kind: 'fileBytes', sha256Field: 'signedArtifactSha256', name: 'signedArtifact', required: true, describe: "The signed CRO signature pack." },
  { kind: 'sha256', name: 'signedArtifactSha256', describe: "SHA-256 of the signed pack." },
]);

export const AUDITOR_REPORT_INPUT_FIELDS = bodyFields('AuditorReportInput', [
  { kind: 'string', max: 2000, name: 'firmName', required: true, describe: "Audit firm." },
  { kind: 'string', max: 2000, name: 'signerName', required: true, describe: "Statutory auditor who signed." },
  { kind: 'string', max: 2000, name: 'professionalBody', required: true, describe: "Recognised accountancy body." },
  { kind: 'string', max: 2000, name: 'membershipNumber', required: true, describe: "Membership or registration number." },
  { kind: 'string', max: 2000, name: 'reportReference', required: true, describe: "Reference of the audit report." },
  { kind: 'dateTime', name: 'signedAtUtc', required: true, describe: "When the report was signed." },
  { kind: 'boolean', name: 'acceptedByDirectors', required: true, describe: "The directors accept the report." },
  { kind: 'fileBytes', sha256Field: 'reportArtifactSha256', name: 'reportArtifact', required: true, describe: "The signed audit report." },
  { kind: 'sha256', name: 'reportArtifactSha256', describe: "SHA-256 of the report." },
]);

export const ACCOUNTANT_REVIEW_ATTESTATION_INPUT_FIELDS = bodyFields('AccountantReviewAttestationInput', [
  { kind: 'string', max: 2000, name: 'reviewerName', required: true, describe: "Qualified accountant who reviewed the accounts." },
  { kind: 'string', max: 2000, name: 'professionalBody', required: true, describe: "Accountancy body." },
  { kind: 'string', max: 2000, name: 'membershipNumber', required: true, describe: "Membership number." },
  { kind: 'date', name: 'reviewDate', required: true, describe: "Date of the review." },
  { kind: 'fileBytes', sha256Field: 'reviewLetterSha256', name: 'reviewLetter', nullable: true, describe: "The review letter." },
  { kind: 'sha256', name: 'reviewLetterSha256', nullable: true, describe: "SHA-256 of the letter." },
]);

export const AUTHORITY_REQUEST_FIELDS = bodyFields('AuthorityRequest', [
  { kind: 'enum', values: ENUMS.ExternalFilingWorkflow, name: 'workflow', required: true, describe: "CroB1 or RevenueCt1Support." },
  { kind: 'enum', values: ENUMS.ExternalFilingAuthorityKind, name: 'kind', required: true, describe: "CroPresenter, CroElectronicFilingAgent or RevenueRosAgent." },
  { kind: 'string', max: 200, name: 'legalName', required: true, describe: "Legal name of the presenter or agent." },
  { kind: 'string', max: 2000, name: 'practiceName', nullable: true, describe: "Practice name, if any." },
  { kind: 'string', max: 2000, name: 'maskedPresenterOrTain', nullable: true, describe: "Masked presenter number or TAIN (never the full identifier)." },
  { kind: 'string', max: 2000, name: 'authorityScope', required: true, describe: "What the authority covers." },
  { kind: 'string', max: 2000, name: 'engagementReference', required: true, describe: "Engagement letter reference." },
  { kind: 'string', max: 2000, name: 'externalAuthorityReference', required: true, describe: "The authority reference at CRO or Revenue." },
  { kind: 'dateTime', name: 'effectiveFromUtc', required: true, describe: "When the authority starts." },
  { kind: 'dateTime', name: 'effectiveUntilUtc', nullable: true, describe: "When it ends, if it does." },
  { kind: 'fileBytes', sha256Field: 'evidenceSha256', name: 'evidenceArtifact', required: true, describe: "Evidence of the authority." },
  { kind: 'sha256', name: 'evidenceSha256', describe: "SHA-256 of the evidence." },
  { kind: 'string', max: 2000, name: 'evidenceMediaType', required: true, describe: "Media type of the evidence, such as application/pdf." },
  { kind: 'string', max: 2000, name: 'evidenceFileName', required: true, describe: "File name of the evidence." },
]);

export const AUTHORITY_REVOCATION_REQUEST_FIELDS = bodyFields('AuthorityRevocationRequest', [
  { kind: 'string', max: 2000, name: 'reason', required: true, describe: "Why the authority is revoked." },
]);

export const EXTERNAL_HANDOFF_ADDRESS_FIELDS = bodyFields('ExternalHandoffAddress', [
  { kind: 'string', max: 200, name: 'line1', required: true },
  { kind: 'string', max: 200, name: 'line2', nullable: true },
  { kind: 'string', max: 200, name: 'line3', nullable: true },
  { kind: 'string', max: 200, name: 'line4', nullable: true },
  { kind: 'string', max: 200, name: 'line5', nullable: true },
  { kind: 'string', max: 200, name: 'line6', nullable: true },
  { kind: 'string', max: 200, name: 'line7', nullable: true },
]);

export const CRO_OFFICER_INPUT_FIELDS = bodyFields('CroOfficerInput', [
  { kind: 'intId', name: 'officerId', required: true, describe: "The officer, from officer_list." },
  { kind: 'string', max: 100, name: 'firstName', required: true },
  { kind: 'string', max: 100, name: 'lastName', required: true },
  { kind: 'object', fields: EXTERNAL_HANDOFF_ADDRESS_FIELDS, name: 'address', required: true, personal: true, describe: "Officer address as it will be entered in CORE. Personal data." },
  { kind: 'string', max: 2000, name: 'identityType', required: true, personal: true, describe: "Type of identity evidence held (never the identifier itself)." },
  { kind: 'string', max: 2000, name: 'identityEvidenceReference', required: true, personal: true, describe: "Opaque reference to the protected identity record; never a PPSN, date of birth or email." },
  { kind: 'sha256', name: 'identityEvidenceSha256', required: true, personal: true, describe: "SHA-256 of the protected identity record." },
  { kind: 'string', max: 2000, name: 'presenterNotificationEmail', nullable: true, personal: true, describe: "Email for CRO notifications to this officer. Personal data." },
  { kind: 'string', max: 2000, name: 'otherDirectorshipsEvidenceReference', required: true, describe: "Evidence reference for other directorships." },
  { kind: 'boolean', name: 'protectedIdentifierEntryConfirmed', required: true, describe: "Confirms the protected identifier will be entered manually in CORE." },
]);

export const B1_SHAREHOLDER_HANDOFF_FIELDS = bodyFields('B1ShareholderHandoff', [
  { kind: 'string', max: 2000, name: 'memberReference', required: true, describe: "Member register reference." },
  { kind: 'string', max: 200, name: 'name', required: true, personal: true, describe: "Shareholder name. Personal data." },
  { kind: 'object', fields: EXTERNAL_HANDOFF_ADDRESS_FIELDS, name: 'address', required: true, personal: true, describe: "Shareholder address. Personal data." },
  { kind: 'string', max: 2000, name: 'shareClass', required: true },
  { kind: 'string', max: 3, name: 'currency', required: true },
  { kind: 'decimal', min: 0, name: 'openingHolding', required: true },
  { kind: 'decimal', min: 0, name: 'closingHolding', required: true },
  { kind: 'string', max: 2000, name: 'holdingDisplay', required: true },
  { kind: 'string', max: 2000, name: 'evidenceReference', required: true },
]);

export const B1_ALLOTMENT_HANDOFF_FIELDS = bodyFields('B1AllotmentHandoff', [
  { kind: 'string', max: 2000, name: 'allotmentReference', required: true },
  { kind: 'date', name: 'allotmentDate', required: true },
  { kind: 'string', max: 2000, name: 'shareClass', required: true },
  { kind: 'string', max: 3, name: 'currency', required: true },
  { kind: 'integer', min: 0, max: 9007199254740991, name: 'numberAllotted', required: true },
  { kind: 'decimal', min: 0, name: 'nominalValuePerShare', required: true },
  { kind: 'money', name: 'consideration', required: true },
  { kind: 'string', max: 2000, name: 'allotteeMemberReference', required: true, describe: "Member reference of the allottee." },
  { kind: 'string', max: 2000, name: 'evidenceReference', required: true },
]);

export const CRO_SNAPSHOT_INPUT_FIELDS = bodyFields('CroSnapshotInput', [
  { kind: 'date', name: 'madeUpToDate', required: true, describe: "The B1 made-up-to date." },
  { kind: 'string', max: 2000, name: 'annualReturnDateElection', required: true, describe: "The ARD election recorded on the B1." },
  { kind: 'boolean', name: 'financialStatementsAnnexed', required: true, describe: "Financial statements are annexed." },
  { kind: 'boolean', name: 'auditExemptionClaimed', required: true, describe: "Audit exemption is claimed (s.360)." },
  { kind: 'string', max: 2000, name: 'auditorReference', nullable: true, describe: "Auditor reference when audited." },
  { kind: 'string', max: 2000, name: 'reportingCurrency', required: true, describe: "Reporting currency, such as EUR." },
  { kind: 'boolean', name: 'politicalDonationsOverThreshold', required: true, describe: "Political donations above the disclosure threshold were made." },
  { kind: 'money', name: 'politicalDonationsAmount', required: true, describe: "Amount of political donations." },
  { kind: 'string', max: 2000, name: 'politicalDonationsEvidenceReference', required: true, describe: "Evidence reference for political donations." },
  { kind: 'objectList', fields: CRO_OFFICER_INPUT_FIELDS, maxItems: 200, name: 'officers', required: true, describe: "Officer particulars for the B1. Contain personal data." },
  { kind: 'objectList', fields: B1_SHAREHOLDER_HANDOFF_FIELDS, maxItems: 200, name: 'shareholders', required: true, describe: "Shareholder particulars. Contain personal data." },
  { kind: 'objectList', fields: B1_ALLOTMENT_HANDOFF_FIELDS, maxItems: 200, name: 'allotments', required: true, describe: "Share allotments in the return period." },
  { kind: 'boolean', name: 'noAllotmentsInReturnPeriodConfirmed', required: true, describe: "Confirms there were no allotments." },
  { kind: 'sha256', name: 'shareholdersListPdfSha256', nullable: true, describe: "SHA-256 of the shareholders list PDF, if annexed." },
  { kind: 'uuid', name: 'supersedesSnapshotId', nullable: true, describe: "The snapshot this amends." },
  { kind: 'string', max: 2000, name: 'amendmentReason', nullable: true, describe: "Why it is amended." },
]);

export const REVENUE_SNAPSHOT_INPUT_FIELDS = bodyFields('RevenueSnapshotInput', [
  { kind: 'date', name: 'asOfDate', nullable: true, describe: "Date the CT1 support snapshot is taken as of." },
  { kind: 'boolean', name: 'unsupportedSectionsReviewed', required: true, describe: "Confirms the CT1 sections FilingBridge does not prepare were reviewed." },
  { kind: 'stringList', maxItems: 50, maxLength: 500, name: 'manualCt1CompletionItems', required: true, describe: "Items the person will complete manually in ROS." },
  { kind: 'uuid', name: 'supersedesSnapshotId', nullable: true, describe: "The snapshot this amends." },
  { kind: 'string', max: 2000, name: 'amendmentReason', nullable: true, describe: "Why it is amended." },
]);

export const OUTCOME_COMMAND_FIELDS = bodyFields('OutcomeCommand', [
  { kind: 'enum', values: ENUMS.ExternalFilingOutcomeKind, name: 'outcome', required: true, describe: "What happened externally: ReadyForManualHandoff, ExternallySubmittedRecorded, CorrectionRequired, ExternallyRejected, ExternallyAcceptedRecorded or SupersededByAmendment. Recorded only; FilingBridge never files." },
  { kind: 'string', max: 2000, name: 'externalReference', nullable: true, describe: "The CRO or ROS reference of the external action." },
  { kind: 'dateTime', name: 'externalOccurredAtUtc', nullable: true, describe: "When the external action happened." },
  { kind: 'string', max: 2000, name: 'reason', nullable: true, describe: "Reason, for rejections and corrections." },
  { kind: 'dateTime', name: 'correctionDeadlineUtc', nullable: true, describe: "Deadline for a correction." },
  { kind: 'string', max: 2000, name: 'evidenceReference', nullable: true, describe: "Evidence reference (for example the CORE acknowledgement)." },
  { kind: 'fileBytes', sha256Field: 'evidenceSha256', name: 'evidenceArtifact', nullable: true, describe: "Evidence document." },
  { kind: 'sha256', name: 'evidenceSha256', nullable: true, describe: "SHA-256 of the evidence." },
  { kind: 'uuid', name: 'supersedingSnapshotId', nullable: true, describe: "The snapshot that supersedes this one." },
]);

export const ERASURE_REQUEST_FIELDS = bodyFields('ErasureRequest', [
  { kind: 'string', max: 2000, name: 'decisionReason', required: true, describe: "The recorded legal basis and reason for the approved erasure." },
]);

export const INCIDENT_EXERCISE_INPUT_FIELDS = bodyFields('IncidentExerciseInput', [
  { kind: 'string', max: 2000, name: 'exerciseKind', required: true, describe: "Kind of incident exercise." },
  { kind: 'string', max: 2000, name: 'releaseCandidate', required: true, describe: "Release candidate exercised." },
  { kind: 'string', max: 2000, name: 'environmentName', required: true, describe: "Environment exercised." },
  { kind: 'sha256', name: 'scenarioSha256', required: true },
  { kind: 'dateTime', name: 'detectedAtUtc', required: true },
  { kind: 'dateTime', name: 'notificationRoutedAtUtc', required: true },
  { kind: 'dateTime', name: 'containedAtUtc', required: true },
  { kind: 'dateTime', name: 'evidencePreservedAtUtc', required: true },
  { kind: 'dateTime', name: 'recoveryVerifiedAtUtc', required: true },
  { kind: 'dateTime', name: 'reviewedAtUtc', required: true },
  { kind: 'string', max: 2000, name: 'notificationDecision', required: true, describe: "Whether notification (for example to the Data Protection Commission) was required, and why." },
  { kind: 'sha256', name: 'evidenceManifestSha256', required: true },
  { kind: 'string', max: 2000, name: 'reviewDecision', required: true, describe: "Outcome of the post-exercise review." },
  { kind: 'boolean', name: 'usedSyntheticDataOnly', required: true, describe: "Only synthetic data was used." },
  { kind: 'boolean', name: 'tenantIsolationVerified', required: true },
  { kind: 'boolean', name: 'auditIntegrityVerified', required: true },
  { kind: 'boolean', name: 'financialIntegrityVerified', required: true },
]);

/** The schema name behind each field list, for the contract tests. */
export const BODY_SCHEMAS: Record<string, readonly FieldSpec[]> = {
  CompanyInput: COMPANY_INPUT_FIELDS,
  CompanyOfficerInput: COMPANY_OFFICER_INPUT_FIELDS,
  AccountingPeriodInput: ACCOUNTING_PERIOD_INPUT_FIELDS,
  BankAccountInput: BANK_ACCOUNT_INPUT_FIELDS,
  CompanyOnboardingInput: COMPANY_ONBOARDING_INPUT_FIELDS,
  CompanyQuarantineRequest: COMPANY_QUARANTINE_REQUEST_FIELDS,
  AnnualReturnDateChangeInput: ANNUAL_RETURN_DATE_CHANGE_INPUT_FIELDS,
  PeriodStatusUpdate: PERIOD_STATUS_UPDATE_FIELDS,
  CreateUserInput: CREATE_USER_INPUT_FIELDS,
  InviteUserInput: INVITE_USER_INPUT_FIELDS,
  SetUserActiveInput: SET_USER_ACTIVE_INPUT_FIELDS,
  SetUserRoleInput: SET_USER_ROLE_INPUT_FIELDS,
  SetUserCompaniesInput: SET_USER_COMPANIES_INPUT_FIELDS,
  SizeClassificationInput: SIZE_CLASSIFICATION_INPUT_FIELDS,
  HistoricAnchorInput: HISTORIC_ANCHOR_INPUT_FIELDS,
  HistoricAnchorRetirementInput: HISTORIC_ANCHOR_RETIREMENT_INPUT_FIELDS,
  SizeClassificationOverrideInput: SIZE_CLASSIFICATION_OVERRIDE_INPUT_FIELDS,
  FilingRegimeInput: FILING_REGIME_INPUT_FIELDS,
  MemberAuditNoticeInput: MEMBER_AUDIT_NOTICE_INPUT_FIELDS,
  CategoriseInput: CATEGORISE_INPUT_FIELDS,
  BulkCategoriseInput: BULK_CATEGORISE_INPUT_FIELDS,
  DuplicateReviewDecisionInput: DUPLICATE_REVIEW_DECISION_INPUT_FIELDS,
  DuplicateReviewBatchDecisionInput: DUPLICATE_REVIEW_BATCH_DECISION_INPUT_FIELDS,
  AccountCategoryInput: ACCOUNT_CATEGORY_INPUT_FIELDS,
  TransactionRuleInput: TRANSACTION_RULE_INPUT_FIELDS,
  ChartAdoptionInput: CHART_ADOPTION_INPUT_FIELDS,
  DebtorInput: DEBTOR_INPUT_FIELDS,
  CreditorInput: CREDITOR_INPUT_FIELDS,
  FixedAssetInput: FIXED_ASSET_INPUT_FIELDS,
  InventoryInput: INVENTORY_INPUT_FIELDS,
  LoanInput: LOAN_INPUT_FIELDS,
  LoanBalanceSnapshotInput: LOAN_BALANCE_SNAPSHOT_INPUT_FIELDS,
  DirectorLoanMovementInput: DIRECTOR_LOAN_MOVEMENT_INPUT_FIELDS,
  DirectorLoanInput: DIRECTOR_LOAN_INPUT_FIELDS,
  PayrollSummaryInput: PAYROLL_SUMMARY_INPUT_FIELDS,
  TaxBalanceInput: TAX_BALANCE_INPUT_FIELDS,
  CorporationTaxDueConfirmationInput: CORPORATION_TAX_DUE_CONFIRMATION_INPUT_FIELDS,
  DividendInput: DIVIDEND_INPUT_FIELDS,
  YearEndReviewInput: YEAR_END_REVIEW_INPUT_FIELDS,
  NotesDisclosureInput: NOTES_DISCLOSURE_INPUT_FIELDS,
  ShareCapitalInput: SHARE_CAPITAL_INPUT_FIELDS,
  OpeningBalanceInput: OPENING_BALANCE_INPUT_FIELDS,
  PostBalanceSheetEventInput: POST_BALANCE_SHEET_EVENT_INPUT_FIELDS,
  RelatedPartyTransactionInput: RELATED_PARTY_TRANSACTION_INPUT_FIELDS,
  ContingentLiabilityInput: CONTINGENT_LIABILITY_INPUT_FIELDS,
  GoingConcernInput: GOING_CONCERN_INPUT_FIELDS,
  AdjustmentInput: ADJUSTMENT_INPUT_FIELDS,
  CorporationTaxScopeReviewInput: CORPORATION_TAX_SCOPE_REVIEW_INPUT_FIELDS,
  ReviewInput: REVIEW_INPUT_FIELDS,
  PaymentInput: PAYMENT_INPUT_FIELDS,
  DeadlineOverrideInput: DEADLINE_OVERRIDE_INPUT_FIELDS,
  MarkFiledInput: MARK_FILED_INPUT_FIELDS,
  CharityInfoInput: CHARITY_INFO_INPUT_FIELDS,
  CharityTrusteeReviewInput: CHARITY_TRUSTEE_REVIEW_INPUT_FIELDS,
  FundBalanceInput: FUND_BALANCE_INPUT_FIELDS,
  FilingStatusInput: FILING_STATUS_INPUT_FIELDS,
  CroPaymentInput: CRO_PAYMENT_INPUT_FIELDS,
  CroNotDeliveredInput: CRO_NOT_DELIVERED_INPUT_FIELDS,
  CharityReportGeneratedInput: CHARITY_REPORT_GENERATED_INPUT_FIELDS,
  CharityFilingStatusInput: CHARITY_FILING_STATUS_INPUT_FIELDS,
  RevenueExternalValidationInput: REVENUE_EXTERNAL_VALIDATION_INPUT_FIELDS,
  RevenueFilingStatusInput: REVENUE_FILING_STATUS_INPUT_FIELDS,
  MarkGeneratedInput: MARK_GENERATED_INPUT_FIELDS,
  DirectorsApprovalRecordInput: DIRECTORS_APPROVAL_RECORD_INPUT_FIELDS,
  DirectorsApprovalPageInput: DIRECTORS_APPROVAL_PAGE_INPUT_FIELDS,
  DirectorsApprovalWithdrawalInput: DIRECTORS_APPROVAL_WITHDRAWAL_INPUT_FIELDS,
  CroSigningPackInput: CRO_SIGNING_PACK_INPUT_FIELDS,
  AuditorReportInput: AUDITOR_REPORT_INPUT_FIELDS,
  AccountantReviewAttestationInput: ACCOUNTANT_REVIEW_ATTESTATION_INPUT_FIELDS,
  AuthorityRequest: AUTHORITY_REQUEST_FIELDS,
  AuthorityRevocationRequest: AUTHORITY_REVOCATION_REQUEST_FIELDS,
  ExternalHandoffAddress: EXTERNAL_HANDOFF_ADDRESS_FIELDS,
  CroOfficerInput: CRO_OFFICER_INPUT_FIELDS,
  B1ShareholderHandoff: B1_SHAREHOLDER_HANDOFF_FIELDS,
  B1AllotmentHandoff: B1_ALLOTMENT_HANDOFF_FIELDS,
  CroSnapshotInput: CRO_SNAPSHOT_INPUT_FIELDS,
  RevenueSnapshotInput: REVENUE_SNAPSHOT_INPUT_FIELDS,
  OutcomeCommand: OUTCOME_COMMAND_FIELDS,
  ErasureRequest: ERASURE_REQUEST_FIELDS,
  IncidentExerciseInput: INCIDENT_EXERCISE_INPUT_FIELDS,
};
