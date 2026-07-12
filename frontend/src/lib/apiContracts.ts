import { z } from "zod";

const MAX_ACCOUNTING_ABS_VALUE = 1_000_000_000_000_000;
const MONEY_TOLERANCE = 0.011;

function isRealIsoDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year
    && date.getUTCMonth() === month - 1
    && date.getUTCDate() === day;
}

function near(left: number, right: number): boolean {
  return Math.abs(left - right) <= MONEY_TOLERANCE;
}

function addIssue(
  context: z.RefinementCtx,
  path: PropertyKey[],
  message: string,
) {
  context.addIssue({ code: "custom", path, message });
}

export const positiveIdSchema = z.number().int().positive();
export const nonNegativeIntegerSchema = z.number().int().nonnegative();
export const moneySchema = z.number().finite().min(-MAX_ACCOUNTING_ABS_VALUE).max(MAX_ACCOUNTING_ABS_VALUE);
export const nonNegativeMoneySchema = moneySchema.refine((value) => value >= 0, "Expected a non-negative monetary amount");
export const isoDateSchema = z.string().refine(isRealIsoDate, "Expected a valid ISO date (YYYY-MM-DD)");
export const isoDateTimeSchema = z.string().refine(
  (value) => /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value)
    && !Number.isNaN(Date.parse(value)),
  "Expected an ISO timestamp with a UTC or numeric offset",
);

const optionalText = z.string().nullable().optional().transform((value) => value ?? undefined);
const optionalDate = isoDateSchema.nullable().optional().transform((value) => value ?? undefined);
const optionalDateTime = isoDateTimeSchema.nullable().optional().transform((value) => value ?? undefined);

export const companyTypeSchema = z.enum([
  "Private",
  "PrivateUnlimited",
  "DesignatedActivityCompany",
  "CompanyLimitedByGuarantee",
  "PublicLimitedCompany",
]);
export const periodStatusSchema = z.enum(["Draft", "Review", "Finalised", "Filed"]);
export const companySizeSchema = z.enum(["Micro", "Small", "Medium", "Large"]);
export const electedRegimeSchema = z.enum(["Micro", "Small", "SmallAbridged", "Medium", "Full"]);
export const officerRoleSchema = z.enum(["Director", "Secretary", "CompanySecretary"]);
export const accountCategoryTypeSchema = z.enum(["Income", "Expense", "Asset", "Liability", "Equity"]);
export const filingStatusSchema = z.enum([
  "NotStarted",
  "InProgress",
  "ReadyForReview",
  "Approved",
  "PackageGenerated",
  "Submitted",
  "Accepted",
  "Rejected",
  "CorrectionRequired",
]);
export const deadlineTypeSchema = z.enum(["CRO", "Charity", "Revenue"]);

export const officerSchema = z.object({
  id: positiveIdSchema.optional(),
  companyId: positiveIdSchema.optional(),
  name: z.string().trim().min(1),
  role: officerRoleSchema,
  appointedDate: optionalDate,
  resignedDate: optionalDate,
  address: optionalText,
}).superRefine((officer, context) => {
  if (officer.appointedDate && officer.resignedDate && officer.resignedDate < officer.appointedDate) {
    addIssue(context, ["resignedDate"], "Resignation date cannot precede appointment date");
  }
});

export const sizeClassificationSchema = z.object({
  id: positiveIdSchema,
  turnover: nonNegativeMoneySchema,
  balanceSheetTotal: nonNegativeMoneySchema,
  avgEmployees: z.number().int().nonnegative(),
  priorYearClass: companySizeSchema.nullable().optional().transform((value) => value ?? undefined),
  rawCurrentClass: companySizeSchema.nullable().optional().transform((value) => value ?? undefined),
  rawPriorClass: companySizeSchema.nullable().optional().transform((value) => value ?? undefined),
  rawCurrentMicroQualified: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  rawCurrentSmallQualified: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  rawCurrentMediumQualified: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  rawPriorMicroQualified: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  rawPriorSmallQualified: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  rawPriorMediumQualified: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  annualisedTurnover: nonNegativeMoneySchema.nullable().optional().transform((value) => value ?? undefined),
  periodLengthInYears: z.number().finite().positive().max(2).nullable().optional().transform((value) => value ?? undefined),
  thresholdElectionEffectiveFrom: optionalDate,
  thresholdScheduleEffectiveFrom: optionalDate,
  thresholdScheduleCode: optionalText,
  decisionInputFingerprintSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  calculatedClass: companySizeSchema,
  overrideRequiresRereview: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  qualificationNotes: optionalText,
});

export const filingRegimeSchema = z.object({
  id: positiveIdSchema,
  canUseMicro: z.boolean(),
  canFileAbridged: z.boolean(),
  auditExempt: z.boolean(),
  electedRegime: electedRegimeSchema,
}).superRefine((regime, context) => {
  if (regime.electedRegime === "Micro" && !regime.canUseMicro) {
    addIssue(context, ["electedRegime"], "Micro cannot be elected when the company is ineligible");
  }
  if (regime.electedRegime === "SmallAbridged" && !regime.canFileAbridged) {
    addIssue(context, ["electedRegime"], "SmallAbridged cannot be elected when abridgement is unavailable");
  }
});

export const accountingPeriodSchema = z.object({
  id: positiveIdSchema,
  companyId: positiveIdSchema,
  periodStart: isoDateSchema,
  periodEnd: isoDateSchema,
  status: periodStatusSchema,
  lockedAt: z.string().datetime({ offset: true }).nullable().optional().transform((value) => value ?? undefined),
  isFirstYear: z.boolean(),
  memberAuditNoticeReceived: z.boolean(),
  memberAuditNoticeDate: optionalDate,
  goingConcernConfirmed: z.boolean(),
  goingConcernNote: optionalText,
  sizeClassification: sizeClassificationSchema.nullable().optional().transform((value) => value ?? undefined),
  filingRegime: filingRegimeSchema.nullable().optional().transform((value) => value ?? undefined),
}).superRefine((period, context) => {
  if (period.periodEnd < period.periodStart) {
    addIssue(context, ["periodEnd"], "Period end cannot precede period start");
  }
  if (!period.memberAuditNoticeReceived && period.memberAuditNoticeDate) {
    addIssue(context, ["memberAuditNoticeDate"], "A notice date requires memberAuditNoticeReceived=true");
  }
});

export const companySchema = z.object({
  id: positiveIdSchema,
  legalName: z.string().trim().min(1),
  tradingName: optionalText,
  croNumber: optionalText,
  taxReference: optionalText,
  companyType: companyTypeSchema,
  incorporationDate: isoDateSchema,
  financialYearStartMonth: z.number().int().min(1).max(12),
  annualReturnDate: optionalDate,
  registeredOfficeAddress1: optionalText,
  registeredOfficeAddress2: optionalText,
  registeredOfficeCity: optionalText,
  registeredOfficeCounty: optionalText,
  registeredOfficeEircode: optionalText,
  isGroupMember: z.boolean(),
  isHolding: z.boolean(),
  isInvestment: z.boolean(),
  isSubsidiary: z.boolean(),
  isDormant: z.boolean(),
  isTrading: z.boolean(),
  isVatRegistered: z.boolean(),
  isEmployer: z.boolean(),
  hasStock: z.boolean(),
  ownsAssets: z.boolean(),
  hasBorrowings: z.boolean(),
  hasDirectorLoans: z.boolean(),
  isListedSecurities: z.boolean(),
  isCreditInstitution: z.boolean(),
  isInsuranceUndertaking: z.boolean(),
  isPensionFund: z.boolean(),
  isCharitableOrganisation: z.boolean(),
  assignedReviewerName: optionalText,
  assignedReviewerEmail: z.string().email().nullable().optional().transform((value) => value ?? undefined),
  latestPeriod: accountingPeriodSchema.nullable().optional().transform((value) => value ?? undefined),
  officers: z.array(officerSchema).optional(),
  periods: z.array(accountingPeriodSchema).optional(),
  periodCount: nonNegativeIntegerSchema.optional(),
}).superRefine((company, context) => {
  if (company.periodCount != null && company.periods && company.periodCount !== company.periods.length) {
    addIssue(context, ["periodCount"], "periodCount must equal periods.length when both are returned");
  }
  if (company.latestPeriod && company.latestPeriod.companyId !== company.id) {
    addIssue(context, ["latestPeriod", "companyId"], "Latest period belongs to another company");
  }
  company.periods?.forEach((period, index) => {
    if (period.companyId !== company.id) {
      addIssue(context, ["periods", index, "companyId"], "Period belongs to another company");
    }
  });
});

export const accountCategorySchema = z.object({
  id: positiveIdSchema,
  code: z.string().trim().min(1),
  name: z.string().trim().min(1),
  type: accountCategoryTypeSchema,
});

export const importedTransactionSchema = z.object({
  id: positiveIdSchema,
  date: isoDateSchema,
  description: z.string().trim().min(1),
  amount: moneySchema,
  balance: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  categoryId: positiveIdSchema.nullable().optional().transform((value) => value ?? undefined),
  confidenceScore: z.number().finite().min(0).max(1).nullable().optional().transform((value) => value ?? undefined),
  manualOverride: z.boolean(),
  category: accountCategorySchema.nullable().optional().transform((value) => value ?? undefined),
}).superRefine((transaction, context) => {
  if ((transaction.categoryId == null) !== (transaction.category == null)) {
    addIssue(context, ["category"], "category and categoryId must either both be present or both be absent");
  }
  if (transaction.category && transaction.category.id !== transaction.categoryId) {
    addIssue(context, ["category", "id"], "category.id must equal categoryId");
  }
});

export const transactionPageSchema = z.object({
  total: nonNegativeIntegerSchema,
  items: z.array(importedTransactionSchema),
  page: z.number().int().positive(),
  pageSize: z.number().int().positive().max(200),
  totalPages: z.number().int().positive(),
  hasPreviousPage: z.boolean(),
  hasNextPage: z.boolean(),
  sortBy: z.enum(["date", "description", "amount", "confidence"]),
  sortDirection: z.enum(["asc", "desc"]),
  aggregates: z.object({
    total: nonNegativeIntegerSchema,
    categorised: nonNegativeIntegerSchema,
    uncategorised: nonNegativeIntegerSchema,
  }),
}).superRefine((page, context) => {
  const expectedPages = Math.max(1, Math.ceil(page.total / page.pageSize));
  if (page.totalPages !== expectedPages) addIssue(context, ["totalPages"], `Expected ${expectedPages}`);
  if (page.page > page.totalPages) addIssue(context, ["page"], "page cannot exceed totalPages");
  if (page.hasPreviousPage !== (page.page > 1)) addIssue(context, ["hasPreviousPage"], "Inconsistent with page");
  if (page.hasNextPage !== (page.page < page.totalPages)) addIssue(context, ["hasNextPage"], "Inconsistent with page");
  if (page.items.length > page.pageSize || page.items.length > page.total) {
    addIssue(context, ["items"], "Page item count exceeds its envelope");
  }
  if (new Set(page.items.map((item) => item.id)).size !== page.items.length) {
    addIssue(context, ["items"], "Duplicate transaction IDs are not allowed");
  }
  if (page.aggregates.total !== page.aggregates.categorised + page.aggregates.uncategorised) {
    addIssue(context, ["aggregates"], "categorised + uncategorised must equal aggregate total");
  }
  if (page.total > page.aggregates.total) {
    addIssue(context, ["total"], "Filtered total cannot exceed the period aggregate total");
  }
});

export const importResultSchema = z.object({
  totalRows: nonNegativeIntegerSchema,
  importedRows: nonNegativeIntegerSchema,
  duplicateCandidates: nonNegativeIntegerSchema,
  autoCategorised: nonNegativeIntegerSchema,
  warnings: z.array(z.string()),
  importBatchId: positiveIdSchema.nullable().transform((value) => value ?? undefined),
  sourceFilename: z.string().trim().min(1).max(500),
  sourceFileSha256: z.string().regex(/^[a-f0-9]{64}$/i),
  sourceFileBytes: nonNegativeIntegerSchema,
}).superRefine((result, context) => {
  if (result.importedRows > result.totalRows) {
    addIssue(context, ["totalRows"], "Imported rows cannot exceed total rows");
  }
  if (result.duplicateCandidates > result.importedRows) addIssue(context, ["duplicateCandidates"], "Candidate rows must be retained imports");
  if (result.autoCategorised > result.importedRows) {
    addIssue(context, ["autoCategorised"], "Auto-categorised rows cannot exceed imported rows");
  }
  if (result.importedRows > 0 && !result.importBatchId) {
    addIssue(context, ["importBatchId"], "A retained import requires its batch identity");
  }
});

export const yearEndReviewConfirmationSchema = z.object({
  id: positiveIdSchema,
  periodId: positiveIdSchema,
  sectionKey: z.string().trim().min(1),
  confirmed: z.boolean(),
  confirmedBy: optionalText,
  confirmedAt: isoDateTimeSchema,
  note: optionalText,
}).superRefine((confirmation, context) => {
  if (confirmation.confirmed && !confirmation.confirmedBy) {
    addIssue(context, ["confirmedBy"], "A confirmed review requires a named reviewer");
  }
});

export const payrollSummarySchema = z.object({
  id: positiveIdSchema.optional(),
  periodId: positiveIdSchema.optional(),
  grossWages: nonNegativeMoneySchema,
  directorsFees: nonNegativeMoneySchema,
  employerPrsi: nonNegativeMoneySchema,
  pensionContributions: nonNegativeMoneySchema,
  staffCount: nonNegativeIntegerSchema,
});

export const yearEndSummarySchema = z.object({
  debtors: z.object({ count: nonNegativeIntegerSchema, total: moneySchema }),
  creditors: z.object({ count: nonNegativeIntegerSchema, total: moneySchema }),
  fixedAssets: z.object({ count: nonNegativeIntegerSchema, totalCost: nonNegativeMoneySchema }),
  inventory: z.object({ count: nonNegativeIntegerSchema, totalValue: nonNegativeMoneySchema }),
  loans: z.object({ count: nonNegativeIntegerSchema, totalBalance: moneySchema }),
  directorLoans: z.object({ count: nonNegativeIntegerSchema }),
  payroll: z.object({ grossWages: nonNegativeMoneySchema, staffCount: nonNegativeIntegerSchema }).nullable(),
  taxes: z.object({ count: nonNegativeIntegerSchema, totalLiability: nonNegativeMoneySchema, totalBalance: moneySchema }),
  dividends: z.object({ count: nonNegativeIntegerSchema, total: nonNegativeMoneySchema }),
  reviewConfirmations: z.array(yearEndReviewConfirmationSchema),
  completeness: z.object({
    score: z.number().int().min(0).max(100),
    completed: nonNegativeIntegerSchema,
    total: z.number().int().positive(),
    incomplete: z.array(z.string().trim().min(1)),
  }),
}).superRefine((summary, context) => {
  if (summary.completeness.completed > summary.completeness.total) {
    addIssue(context, ["completeness", "completed"], "completed cannot exceed total");
  }
  const expectedScore = Math.round(summary.completeness.completed / summary.completeness.total * 100);
  if (summary.completeness.score !== expectedScore) {
    addIssue(context, ["completeness", "score"], `Expected ${expectedScore}`);
  }
  if (summary.completeness.incomplete.length !== summary.completeness.total - summary.completeness.completed) {
    addIssue(context, ["completeness", "incomplete"], "Incomplete section count does not reconcile");
  }
  const reviewKeys = summary.reviewConfirmations.map((item) => item.sectionKey);
  if (new Set(reviewKeys).size !== reviewKeys.length) {
    addIssue(context, ["reviewConfirmations"], "Duplicate review section keys are not allowed");
  }
});

export const openingBalanceSchema = z.object({
  id: positiveIdSchema,
  periodId: positiveIdSchema,
  accountCategoryId: positiveIdSchema,
  debit: nonNegativeMoneySchema,
  credit: nonNegativeMoneySchema,
  sourceNote: optionalText,
  enteredBy: optionalText,
  enteredAt: isoDateTimeSchema,
  reviewed: z.boolean(),
  reviewedBy: optionalText,
  reviewedAt: optionalDateTime,
  accountCategory: accountCategorySchema,
}).superRefine((balance, context) => {
  if (balance.debit > 0 && balance.credit > 0) {
    addIssue(context, ["credit"], "An opening balance cannot be both debit and credit");
  }
  if (balance.accountCategory.id !== balance.accountCategoryId) {
    addIssue(context, ["accountCategory", "id"], "accountCategory.id must equal accountCategoryId");
  }
  if (balance.reviewed !== Boolean(balance.reviewedBy && balance.reviewedAt)) {
    addIssue(context, ["reviewed"], "Reviewed state requires reviewer and timestamp evidence");
  }
});

export const directorLoanCounterpartyTypeSchema = z.enum(["Director", "ConnectedPerson", "GroupCompany"]);
export const directorLoanArrangementTypeSchema = z.enum(["Loan", "QuasiLoan", "CreditTransaction", "GuaranteeOrSecurity"]);
export const directorLoanTermsStatusSchema = z.enum([
  "Unassessed",
  "NotWritten",
  "WrittenComplete",
  "WrittenAmbiguousRepayment",
  "WrittenAmbiguousInterest",
  "WrittenAmbiguousRepaymentAndInterest",
]);
export const directorLoanComplianceBasisSchema = z.enum([
  "Unassessed",
  "Section240BelowTenPercent",
  "Section242SummaryApprovalProcedure",
  "Section243IntraGroup",
  "Section244VouchedExpense",
  "Section245OrdinaryBusiness",
]);
export const directorLoanRelevantAssetsBasisSchema = z.enum([
  "Unassessed",
  "LastLaidEntityFinancialStatements",
  "CalledUpShareCapitalNoPriorStatements",
]);
export const directorLoanRelevantAssetsFallReviewSchema = z.enum([
  "Unassessed",
  "NoRelevantFall",
  "FallRemainedBelowLimit",
  "TermsAmendedWithinTwoMonths",
  "SapArrangementNotCounted",
]);
export const directorLoanReviewDecisionSchema = z.enum(["Unreviewed", "Accepted", "RemediationRequired"]);
export const directorLoanMovementTypeSchema = z.enum(["Advance", "Repayment"]);

export const directorLoanMovementSchema = z.object({
  id: positiveIdSchema.optional(),
  directorLoanId: positiveIdSchema.optional(),
  movementDate: isoDateSchema,
  movementType: directorLoanMovementTypeSchema,
  amount: nonNegativeMoneySchema.refine((amount) => amount > 0, "Movement amount must be positive"),
  evidenceReference: optionalText,
});

export const directorLoanRowSchema = z.object({
  id: positiveIdSchema.optional(),
  periodId: positiveIdSchema.optional(),
  directorId: positiveIdSchema.nullable().optional().transform((value) => value ?? undefined),
  counterpartyType: directorLoanCounterpartyTypeSchema,
  counterpartyName: optionalText,
  arrangementType: directorLoanArrangementTypeSchema,
  arrangementDate: optionalDate,
  openingBalance: nonNegativeMoneySchema,
  advances: nonNegativeMoneySchema,
  repayments: nonNegativeMoneySchema,
  closingBalance: nonNegativeMoneySchema,
  termsStatus: directorLoanTermsStatusSchema,
  interestRate: nonNegativeMoneySchema,
  interestCharged: nonNegativeMoneySchema,
  allowanceMade: nonNegativeMoneySchema,
  section236PresumptionEvidenceReference: optionalText,
  isDocumented: z.boolean(),
  loanTerms: optionalText,
  maxBalanceDuringYear: nonNegativeMoneySchema,
  complianceBasis: directorLoanComplianceBasisSchema,
  relevantAssetsBasis: directorLoanRelevantAssetsBasisSchema,
  relevantAssetsAmount: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  relevantAssetsAsOfDate: optionalDate,
  relevantAssetsReference: optionalText,
  noPriorFinancialStatementsConfirmed: z.boolean(),
  relevantAssetsFallReview: directorLoanRelevantAssetsFallReviewSchema,
  relevantAssetsReductionAwarenessDate: optionalDate,
  termsAmendedDate: optionalDate,
  termsAmendmentEvidenceReference: optionalText,
  exceptionEvidenceReference: optionalText,
  sapDeclarationDate: optionalDate,
  sapResolutionDate: optionalDate,
  sapActivityStartDate: optionalDate,
  sapCroFilingDate: optionalDate,
  sapDeclarationReference: optionalText,
  sapResolutionReference: optionalText,
  sapCroFilingReference: optionalText,
  sapDeclarationCoversSection203Matters: z.boolean(),
  expenseIncurredDate: optionalDate,
  expenseDischargedDate: optionalDate,
  ordinaryCourseConfirmed: z.boolean(),
  noMoreFavourableTermsConfirmed: z.boolean(),
  reviewDecision: directorLoanReviewDecisionSchema,
  reviewNote: optionalText,
  reviewedBy: optionalText,
  reviewerRole: optionalText,
  reviewedAtUtc: optionalDateTime,
  balanceMovements: z.array(directorLoanMovementSchema),
}).superRefine((loan, context) => {
  if (!near(loan.closingBalance, loan.openingBalance + loan.advances - loan.repayments)) {
    addIssue(context, ["closingBalance"], "Closing balance must reconcile to opening plus advances less repayments");
  }
  if (loan.maxBalanceDuringYear + MONEY_TOLERANCE < Math.max(loan.openingBalance, loan.closingBalance)) {
    addIssue(context, ["maxBalanceDuringYear"], "Maximum balance cannot be below opening or closing balance");
  }
  if (loan.counterpartyType === "GroupCompany") {
    if (loan.directorId !== undefined) addIssue(context, ["directorId"], "Group-company arrangements cannot identify an individual director");
    if (!loan.counterpartyName) addIssue(context, ["counterpartyName"], "Group-company counterparty name is required");
  } else if (loan.directorId === undefined) {
    addIssue(context, ["directorId"], "Director or connected-person arrangements require the related director");
  }
  if (loan.counterpartyType === "ConnectedPerson" && !loan.counterpartyName) {
    addIssue(context, ["counterpartyName"], "Connected-person name is required");
  }
  if (loan.balanceMovements.length > 0) {
    const advances = loan.balanceMovements
      .filter((movement) => movement.movementType === "Advance")
      .reduce((total, movement) => total + movement.amount, 0);
    const repayments = loan.balanceMovements
      .filter((movement) => movement.movementType === "Repayment")
      .reduce((total, movement) => total + movement.amount, 0);
    if (!near(advances, loan.advances)) addIssue(context, ["balanceMovements"], "Movement advances do not reconcile");
    if (!near(repayments, loan.repayments)) addIssue(context, ["balanceMovements"], "Movement repayments do not reconcile");
  }
  const acceptedReviewFields = Boolean(loan.reviewedBy) && Boolean(loan.reviewerRole) && Boolean(loan.reviewedAtUtc);
  if (loan.reviewDecision === "Accepted" && !acceptedReviewFields) {
    addIssue(context, ["reviewDecision"], "Accepted review requires retained reviewer identity and timestamp");
  }
  if (loan.reviewDecision === "Unreviewed" && acceptedReviewFields) {
    addIssue(context, ["reviewDecision"], "Unreviewed arrangements cannot retain accepted-review identity");
  }
});

export const directorLoanLegalSourceSchema = z.object({
  code: z.string().trim().min(1),
  title: z.string().trim().min(1),
  url: z.string().url(),
});

export const directorLoanDetailSchema = z.object({
  id: positiveIdSchema,
  counterpartyName: z.string().trim().min(1),
  relatedDirectorName: optionalText,
  counterpartyType: directorLoanCounterpartyTypeSchema,
  arrangementType: directorLoanArrangementTypeSchema,
  arrangementDate: optionalDate,
  openingBalance: nonNegativeMoneySchema,
  advances: nonNegativeMoneySchema,
  repayments: nonNegativeMoneySchema,
  allowanceMade: nonNegativeMoneySchema,
  maxDuringYear: nonNegativeMoneySchema,
  closingBalance: nonNegativeMoneySchema,
  interestRate: nonNegativeMoneySchema,
  interestCharged: nonNegativeMoneySchema,
  section236PresumedInterest: nonNegativeMoneySchema,
  termsStatus: directorLoanTermsStatusSchema,
  mainConditions: optionalText,
  complianceBasis: directorLoanComplianceBasisSchema,
  relevantAssets: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  section240Threshold: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  section240StrictlyBelowThreshold: z.boolean(),
  section307DisclosureRequired: z.boolean(),
  reviewDecision: directorLoanReviewDecisionSchema,
  reviewedBy: optionalText,
  reviewerRole: optionalText,
  reviewedAtUtc: optionalDateTime,
  readyForFinalOutput: z.boolean(),
  blockingIssues: z.array(z.string().trim().min(1)),
  warnings: z.array(z.string().trim().min(1)),
  balanceMovements: z.array(directorLoanMovementSchema),
}).superRefine((loan, context) => {
  if (loan.readyForFinalOutput !== (loan.blockingIssues.length === 0)) {
    addIssue(context, ["readyForFinalOutput"], "Arrangement readiness must exactly reflect blocking issues");
  }
});

export const directorLoanSignOffPacketSchema = z.object({
  state: z.enum(["accepted", "review-required", "evidence-incomplete"]),
  readyForArrangementReview: z.boolean(),
  readyForFinalOutput: z.boolean(),
  openBlockers: z.array(z.string().trim().min(1)),
  openWarnings: z.array(z.string().trim().min(1)),
  legalSources: z.array(directorLoanLegalSourceSchema).min(10),
}).superRefine((packet, context) => {
  if (packet.readyForFinalOutput !== (packet.openBlockers.length === 0)) {
    addIssue(context, ["readyForFinalOutput"], "Sign-off readiness must exactly reflect blockers");
  }
  if ((packet.state === "accepted") !== packet.readyForFinalOutput) {
    addIssue(context, ["state"], "Only a blocker-free sign-off packet can be accepted");
  }
});

export const directorLoanComplianceSchema = z.object({
  totalDirectorLoans: nonNegativeMoneySchema,
  aggregateMaximumExposure: nonNegativeMoneySchema,
  disclosureAggregateMaximumExposure: nonNegativeMoneySchema,
  disclosureOpeningNetAssets: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  disclosureClosingNetAssets: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  section236PresumedInterest: nonNegativeMoneySchema,
  hasUnresolvedComplianceBlockers: z.boolean(),
  requiresAlternativeLegalBasis: z.boolean(),
  loans: z.array(directorLoanDetailSchema),
  blockingIssues: z.array(z.string().trim().min(1)),
  warnings: z.array(z.string().trim().min(1)),
  signOffPacket: directorLoanSignOffPacketSchema,
  legalSources: z.array(directorLoanLegalSourceSchema).min(10),
  warning: optionalText,
}).superRefine((compliance, context) => {
  if (compliance.hasUnresolvedComplianceBlockers !== (compliance.blockingIssues.length > 0)) {
    addIssue(context, ["hasUnresolvedComplianceBlockers"], "Compliance-blocker flag must exactly reflect blocking issues");
  }
  if (compliance.signOffPacket.readyForFinalOutput !== !compliance.hasUnresolvedComplianceBlockers) {
    addIssue(context, ["signOffPacket", "readyForFinalOutput"], "Sign-off packet and compliance readiness disagree");
  }
});

export const adjustmentSchema = z.object({
  id: positiveIdSchema,
  description: z.string().trim().min(1),
  amount: nonNegativeMoneySchema,
  source: z.enum(["Auto", "Manual"]),
  reason: optionalText,
  legalBasis: optionalText,
  impactOnProfit: moneySchema,
  impactOnAssets: moneySchema,
  isAuto: z.boolean(),
  approvedBy: optionalText,
  approvedAt: optionalDateTime,
}).superRefine((adjustment, context) => {
  if (adjustment.isAuto !== (adjustment.source === "Auto")) {
    addIssue(context, ["isAuto"], "isAuto must agree with source");
  }
  if (Boolean(adjustment.approvedBy) !== Boolean(adjustment.approvedAt)) {
    addIssue(context, ["approvedAt"], "Approval name and timestamp must be paired");
  }
});

export const adjustmentSummarySchema = z.object({
  autoGenerated: nonNegativeIntegerSchema,
  manual: nonNegativeIntegerSchema,
  pendingApproval: nonNegativeIntegerSchema,
  approved: nonNegativeIntegerSchema,
  totalImpactOnProfit: moneySchema,
  totalImpactOnAssets: moneySchema,
}).superRefine((summary, context) => {
  if (summary.autoGenerated + summary.manual !== summary.pendingApproval + summary.approved) {
    addIssue(context, [], "Adjustment count aggregates do not reconcile");
  }
});

export const notesDisclosureSchema = z.object({
  id: positiveIdSchema.optional(),
  periodId: positiveIdSchema.optional(),
  noteNumber: z.number().int().positive(),
  code: z.string().trim().min(1).max(80).nullable().optional().transform((value) => value ?? undefined),
  title: z.string().trim().min(1),
  content: optionalText,
  isRequired: z.boolean(),
  isIncluded: z.boolean(),
  checklistState: z.enum(["Required", "NotApplicable", "ExplicitReview"]).optional(),
  reviewEvidence: optionalText,
  reviewedBy: optionalText,
  reviewedAt: optionalDateTime,
}).superRefine((note, context) => {
  if (note.isRequired && (!note.code || !note.checklistState)) {
    addIssue(context, ["code"], "Generated checklist notes require a stable code and state");
  }
  if (note.checklistState === "NotApplicable" && note.isIncluded) {
    addIssue(context, ["isIncluded"], "Not-applicable checklist notes cannot be rendered");
  }
  if (note.checklistState === "ExplicitReview" && note.isIncluded
      && (!note.reviewEvidence || !note.reviewedBy || !note.reviewedAt)) {
    addIssue(context, ["reviewEvidence"], "Included explicit-review notes require retained review evidence");
  }
});

export const readinessScoreSchema = z.object({
  completenessPercent: z.number().int().min(0).max(100),
  filingReadinessPercent: z.number().int().min(0).max(100),
  balanceSheetBalances: z.boolean(),
  missingItems: z.array(z.string().trim().min(1)),
  warnings: z.array(z.string().trim().min(1)),
});

export const statementSourceSummarySchema = z.object({
  code: z.string().trim().min(1),
  name: z.string().trim().min(1),
  type: accountCategoryTypeSchema,
  openingDebit: nonNegativeMoneySchema,
  openingCredit: nonNegativeMoneySchema,
  transactionDebit: nonNegativeMoneySchema,
  transactionCredit: nonNegativeMoneySchema,
  transactionCount: nonNegativeIntegerSchema,
  adjustmentDebit: nonNegativeMoneySchema,
  adjustmentCredit: nonNegativeMoneySchema,
  adjustmentCount: nonNegativeIntegerSchema,
  closingDebit: nonNegativeMoneySchema,
  closingCredit: nonNegativeMoneySchema,
  sourceNotes: z.array(z.string()),
}).superRefine((line, context) => {
  const expectedNet = line.openingDebit - line.openingCredit
    + line.transactionDebit - line.transactionCredit
    + line.adjustmentDebit - line.adjustmentCredit;
  if (!near(line.closingDebit - line.closingCredit, expectedNet)) {
    addIssue(context, ["closingDebit"], "Closing source balance does not reconcile to its movements");
  }
});

export const trialBalanceLineSchema = z.object({
  code: z.string().trim().min(1),
  name: z.string().trim().min(1),
  type: accountCategoryTypeSchema,
  debit: nonNegativeMoneySchema,
  credit: nonNegativeMoneySchema,
}).superRefine((line, context) => {
  if (line.debit > 0 && line.credit > 0) {
    addIssue(context, ["credit"], "A trial-balance line cannot be both debit and credit");
  }
});

const expenseLineSchema = z.object({ code: z.string().min(1), name: z.string().min(1), amount: moneySchema });
const adjustmentLineSchema = z.object({ description: z.string().min(1), amount: moneySchema, approved: z.boolean() });

export const profitAndLossSchema = z.object({
  turnover: moneySchema,
  costOfSales: moneySchema,
  grossProfit: moneySchema,
  otherIncome: moneySchema,
  overheads: z.array(expenseLineSchema),
  totalOverheads: moneySchema,
  operatingProfit: moneySchema,
  interestPayable: moneySchema,
  profitBeforeTax: moneySchema,
  taxCharge: moneySchema,
  profitAfterTax: moneySchema,
  yearEndAdjustments: z.array(adjustmentLineSchema),
  totalYearEndAdjustments: moneySchema,
}).superRefine((statement, context) => {
  const checks: Array<[PropertyKey[], number, number]> = [
    [["grossProfit"], statement.grossProfit, statement.turnover - statement.costOfSales],
    [["totalOverheads"], statement.totalOverheads, statement.overheads.reduce((sum, line) => sum + line.amount, 0)],
    [["operatingProfit"], statement.operatingProfit, statement.grossProfit + statement.otherIncome - statement.totalOverheads],
    [["profitBeforeTax"], statement.profitBeforeTax, statement.operatingProfit - statement.interestPayable],
    [["profitAfterTax"], statement.profitAfterTax, statement.profitBeforeTax - statement.taxCharge],
    [["totalYearEndAdjustments"], statement.totalYearEndAdjustments, statement.yearEndAdjustments.reduce((sum, line) => sum + line.amount, 0)],
  ];
  checks.forEach(([path, actual, expected]) => {
    if (!near(actual, expected)) addIssue(context, path, `Expected reconciled value ${expected}`);
  });
});

const balanceSheetSchemaBase = z.object({
  fixedAssets: z.object({
    categories: z.array(z.object({
      category: z.string().trim().min(1),
      cost: moneySchema,
      depreciation: moneySchema,
      nbv: moneySchema,
    })),
    total: moneySchema,
  }),
  currentAssets: z.object({
    stock: moneySchema,
    debtors: moneySchema,
    prepayments: moneySchema,
    cash: moneySchema,
    total: moneySchema,
  }),
  creditorsWithinYear: z.object({
    tradeCreditors: moneySchema,
    accruals: moneySchema,
    taxCreditors: moneySchema,
    otherCreditors: moneySchema,
    total: moneySchema,
  }),
  netCurrentAssets: moneySchema,
  totalAssetsLessCurrentLiabilities: moneySchema,
  creditorsAfterYear: z.object({ loans: moneySchema, other: moneySchema, total: moneySchema }),
  netAssets: moneySchema,
  capitalAndReserves: z.object({
    shareCapital: moneySchema,
    openingRetainedEarnings: moneySchema,
    profitForYear: moneySchema,
    dividendsPaid: moneySchema,
    otherReserveMovements: moneySchema,
    retainedEarnings: moneySchema,
    total: moneySchema,
    unexplainedDifference: moneySchema,
  }),
  balances: z.boolean(),
});

export const balanceSheetSchema = balanceSheetSchemaBase.superRefine((statement, context) => {
  const fixedAssetTotal = statement.fixedAssets.categories.reduce((sum, line) => sum + line.nbv, 0);
  const currentAssetTotal = statement.currentAssets.stock + statement.currentAssets.debtors
    + statement.currentAssets.prepayments + statement.currentAssets.cash;
  const currentCreditorTotal = statement.creditorsWithinYear.tradeCreditors + statement.creditorsWithinYear.accruals
    + statement.creditorsWithinYear.taxCreditors + statement.creditorsWithinYear.otherCreditors;
  const afterYearTotal = statement.creditorsAfterYear.loans + statement.creditorsAfterYear.other;
  const expectedRetained = statement.capitalAndReserves.openingRetainedEarnings
    + statement.capitalAndReserves.profitForYear
    - statement.capitalAndReserves.dividendsPaid
    + statement.capitalAndReserves.otherReserveMovements;
  const checks: Array<[PropertyKey[], number, number]> = [
    [["fixedAssets", "total"], statement.fixedAssets.total, fixedAssetTotal],
    [["currentAssets", "total"], statement.currentAssets.total, currentAssetTotal],
    [["creditorsWithinYear", "total"], statement.creditorsWithinYear.total, currentCreditorTotal],
    [["netCurrentAssets"], statement.netCurrentAssets, currentAssetTotal - currentCreditorTotal],
    [["totalAssetsLessCurrentLiabilities"], statement.totalAssetsLessCurrentLiabilities, fixedAssetTotal + statement.netCurrentAssets],
    [["creditorsAfterYear", "total"], statement.creditorsAfterYear.total, afterYearTotal],
    [["netAssets"], statement.netAssets, statement.totalAssetsLessCurrentLiabilities - afterYearTotal],
    [["capitalAndReserves", "retainedEarnings"], statement.capitalAndReserves.retainedEarnings, expectedRetained],
    [["capitalAndReserves", "total"], statement.capitalAndReserves.total, statement.capitalAndReserves.shareCapital + statement.capitalAndReserves.retainedEarnings],
    [["capitalAndReserves", "unexplainedDifference"], statement.capitalAndReserves.unexplainedDifference, statement.netAssets - statement.capitalAndReserves.total],
  ];
  checks.forEach(([path, actual, expected]) => {
    if (!near(actual, expected)) addIssue(context, path, `Expected reconciled value ${expected}`);
  });
  const expectedBalances = Math.abs(statement.capitalAndReserves.unexplainedDifference) < 0.01;
  if (statement.balances !== expectedBalances) addIssue(context, ["balances"], "balances flag contradicts unexplainedDifference");
});

export const taxComputationSchema = z.object({
  accountingProfit: moneySchema,
  adjustments: z.array(z.object({ description: z.string().min(1), amount: moneySchema, basis: z.string().min(1) })),
  taxableProfit: moneySchema,
  tradingLossAvailable: nonNegativeMoneySchema,
  corporationTaxAt125: nonNegativeMoneySchema,
  corporationTaxAt25: nonNegativeMoneySchema,
  totalCorporationTax: nonNegativeMoneySchema,
  preliminaryTaxPaid: nonNegativeMoneySchema,
  balanceDue: moneySchema,
  notes: z.string(),
  tradingProfitBeforeLossRelief: moneySchema,
  tradingProfitAfterLossRelief: nonNegativeMoneySchema,
  passiveNonTradingIncome: moneySchema,
  broughtForwardTradingLoss: nonNegativeMoneySchema,
  tradingLossUsed: nonNegativeMoneySchema,
  tradingLossCarriedForward: nonNegativeMoneySchema,
  capitalAllowances: nonNegativeMoneySchema,
  balancingAllowances: nonNegativeMoneySchema,
  balancingCharges: nonNegativeMoneySchema,
  supportStatus: z.enum(["machine-supported-simple-scope", "manual-review-required"]),
  finalTaxChargeSupported: z.boolean(),
  manualReviewRequired: z.literal(true),
  outputKind: z.literal("corporation-tax-support-data-not-ct1-return"),
  isCompleteCt1Return: z.literal(false),
  blockingReasons: z.array(z.string().min(1)),
  sources: z.array(z.object({
    code: z.string().min(1),
    title: z.string().min(1),
    url: z.string().url(),
  })).min(1),
  calculationSha256: z.string().regex(/^[0-9a-f]{64}$/),
}).superRefine((tax, context) => {
  if (!near(tax.totalCorporationTax, tax.corporationTaxAt125 + tax.corporationTaxAt25)) {
    addIssue(context, ["totalCorporationTax"], "Tax-rate components do not reconcile");
  }
  if (!near(tax.balanceDue, tax.totalCorporationTax - tax.preliminaryTaxPaid)) {
    addIssue(context, ["balanceDue"], "Balance due does not reconcile");
  }
  if (!near(tax.taxableProfit, tax.tradingProfitAfterLossRelief + Math.max(0, tax.passiveNonTradingIncome))) {
    addIssue(context, ["taxableProfit"], "Taxable profit does not reconcile to trading and passive streams");
  }
  if (!near(
    tax.tradingLossCarriedForward,
    tax.broughtForwardTradingLoss - tax.tradingLossUsed + tax.tradingLossAvailable,
  )) {
    addIssue(context, ["tradingLossCarriedForward"], "Trading-loss movement does not reconcile");
  }
  if (tax.tradingLossUsed > tax.broughtForwardTradingLoss) {
    addIssue(context, ["tradingLossUsed"], "Loss used cannot exceed the brought-forward loss");
  }
  const expectedSupported = tax.blockingReasons.length === 0;
  if (tax.finalTaxChargeSupported !== expectedSupported) {
    addIssue(context, ["finalTaxChargeSupported"], "Final-charge support flag contradicts blocking reasons");
  }
  if (tax.supportStatus !== (expectedSupported ? "machine-supported-simple-scope" : "manual-review-required")) {
    addIssue(context, ["supportStatus"], "Support status contradicts blocking reasons");
  }
});

export const corporationTaxPaymentKindSchema = z.enum([
  "PreliminaryFirst",
  "PreliminarySecondOrSingle",
  "Balance",
  "InterestOrSurcharge",
  "Other",
]);

export const corporationTaxPaymentClassSchema = z.enum([
  "Unresolved",
  "StartUpExempt",
  "Small",
  "Large",
]);

export const corporationTaxPaymentRecordSchema = z.object({
  id: positiveIdSchema,
  periodId: positiveIdSchema,
  paymentDate: isoDateSchema,
  amount: nonNegativeMoneySchema.refine((amount) => amount > 0, "Payment amount must be positive"),
  kind: corporationTaxPaymentKindSchema,
  evidenceReference: z.string().trim().min(20),
  externalPaymentReference: optionalText,
  recordedBy: z.string().trim().min(1),
  recordedAtUtc: isoDateTimeSchema,
});

export const corporationTaxFilingSupportReviewSchema = z.object({
  id: positiveIdSchema,
  periodId: positiveIdSchema,
  priorPeriodStart: optionalDate,
  priorPeriodEnd: optionalDate,
  priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239: nonNegativeMoneySchema.nullable().optional().transform((value) => value ?? undefined),
  priorPeriodSection239IncomeTax: nonNegativeMoneySchema.nullable().optional().transform((value) => value ?? undefined),
  currentPeriodSection239IncomeTax: nonNegativeMoneySchema,
  priorLiabilityEvidenceReference: optionalText,
  hasInterestLimitationRule: z.boolean(),
  usesNotionalGroupPaymentAllocation: z.boolean(),
  hasDirtOrOtherWithholdingCredits: z.boolean(),
  hasOtherPreliminaryTaxAdjustments: z.boolean(),
  hasMandatoryElectronicFilingExemption: z.boolean(),
  evidenceNote: z.string().trim().min(20),
  preparedBy: z.string().trim().min(1),
  preparedAtUtc: isoDateTimeSchema,
}).superRefine((review, context) => {
  if (Boolean(review.priorPeriodStart) !== Boolean(review.priorPeriodEnd)) {
    addIssue(context, ["priorPeriodEnd"], "Preceding-period start and end dates must be supplied together");
  }
  if (review.priorPeriodStart && review.priorPeriodEnd && review.priorPeriodEnd < review.priorPeriodStart) {
    addIssue(context, ["priorPeriodEnd"], "Preceding-period end cannot precede its start");
  }
  const hasPriorCt = review.priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 !== undefined;
  const hasPriorSection239 = review.priorPeriodSection239IncomeTax !== undefined;
  if (hasPriorCt !== hasPriorSection239) {
    addIssue(context, ["priorPeriodSection239IncomeTax"], "Preceding-period CT and section 239 amounts must be supplied together (enter zero where applicable)");
  }
  if ((hasPriorCt || hasPriorSection239) && (!review.priorPeriodStart || !review.priorPeriodEnd)) {
    addIssue(context, ["priorPeriodStart"], "Preceding-period liability amounts require the exact accounting-period dates");
  }
  if ((hasPriorCt || hasPriorSection239) && (review.priorLiabilityEvidenceReference?.trim().length ?? 0) < 20) {
    addIssue(context, ["priorLiabilityEvidenceReference"], "Preceding-period liability amounts require retained evidence");
  }
});

const corporationTaxSafeHarbourBasisSchema = z.object({
  code: z.string().trim().min(1),
  label: z.string().trim().min(1),
  amount: nonNegativeMoneySchema,
  available: z.boolean(),
});

const corporationTaxDueItemSchema = z.object({
  code: z.string().trim().min(1),
  label: z.string().trim().min(1),
  dueDate: isoDateSchema,
  cumulativeTaxRequired: nonNegativeMoneySchema,
  paidByDueDate: nonNegativeMoneySchema,
  shortfallAtDueDate: nonNegativeMoneySchema,
  basis: z.string().trim().min(1),
}).superRefine((item, context) => {
  const expected = Math.max(0, item.cumulativeTaxRequired - item.paidByDueDate);
  if (!near(item.shortfallAtDueDate, expected)) {
    addIssue(context, ["shortfallAtDueDate"], "Due-item shortfall does not reconcile");
  }
});

const corporationTaxInterestSegmentSchema = z.object({
  dueDate: isoDateSchema,
  throughDate: isoDateSchema,
  principal: nonNegativeMoneySchema,
  inclusiveDays: z.number().int().positive(),
  interest: nonNegativeMoneySchema,
  basis: z.string().trim().min(1),
}).superRefine((segment, context) => {
  if (segment.throughDate < segment.dueDate) {
    addIssue(context, ["throughDate"], "Interest period cannot end before its due date");
  }
});

const corporationTaxLateFilingExposureSchema = z.object({
  isLate: z.boolean(),
  returnDueDate: isoDateSchema,
  exposureDate: isoDateSchema,
  daysLate: z.number().int().nonnegative(),
  rate: nonNegativeMoneySchema,
  cap: nonNegativeMoneySchema,
  estimatedSurcharge: nonNegativeMoneySchema,
  reliefRestrictionExposure: z.boolean(),
  detail: z.string().trim().min(1),
}).superRefine((exposure, context) => {
  if (!exposure.isLate && (exposure.daysLate !== 0 || exposure.rate !== 0 || exposure.estimatedSurcharge !== 0)) {
    addIssue(context, ["isLate"], "A non-late return cannot carry surcharge exposure");
  }
  if (exposure.isLate && exposure.exposureDate <= exposure.returnDueDate) {
    addIssue(context, ["exposureDate"], "Late-return exposure date must be after the return due date");
  }
});

export const corporationTaxFilingSupportSchema = z.object({
  outputKind: z.literal("corporation-tax-filing-support-not-ct1-return"),
  isCompleteCt1Return: z.literal(false),
  directRosSubmissionSupported: z.literal(false),
  companyClass: corporationTaxPaymentClassSchema,
  companyClassLabel: z.string().trim().min(1),
  isShortAccountingPeriod: z.boolean(),
  currentTotalTaxForPaymentSupport: nonNegativeMoneySchema,
  annualisedPriorCorporationTax: nonNegativeMoneySchema.nullable().optional().transform((value) => value ?? undefined),
  preliminaryFirstDueDate: isoDateSchema,
  preliminarySecondOrSingleDueDate: isoDateSchema,
  returnAndBalanceDueDate: isoDateSchema,
  safeHarbourBases: z.array(corporationTaxSafeHarbourBasisSchema).min(1),
  preliminaryTaxSafeHarbourAmount: nonNegativeMoneySchema,
  safeHarbourMet: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  dueItems: z.array(corporationTaxDueItemSchema),
  preliminaryTaxPaymentsRecorded: nonNegativeMoneySchema,
  taxPaymentsRecorded: nonNegativeMoneySchema,
  estimatedLatePaymentInterest: nonNegativeMoneySchema,
  interestSegments: z.array(corporationTaxInterestSegmentSchema),
  lateFiling: corporationTaxLateFilingExposureSchema,
  manualReviewRequired: z.literal(true),
  filingSupportReady: z.boolean(),
  blockingReasons: z.array(z.string().trim().min(1)),
  warnings: z.array(z.string().trim().min(1)).min(1),
  calculationSha256: z.string().regex(/^[0-9a-f]{64}$/),
}).superRefine((support, context) => {
  if (support.preliminaryTaxPaymentsRecorded > support.taxPaymentsRecorded + MONEY_TOLERANCE) {
    addIssue(context, ["preliminaryTaxPaymentsRecorded"], "Preliminary payments cannot exceed all recorded tax payments");
  }
  const segmentInterest = support.interestSegments.reduce((total, segment) => total + segment.interest, 0);
  if (!near(segmentInterest, support.estimatedLatePaymentInterest)) {
    addIssue(context, ["estimatedLatePaymentInterest"], "Interest total does not reconcile to the retained segments");
  }
  if (support.filingSupportReady !== (support.blockingReasons.length === 0)) {
    addIssue(context, ["filingSupportReady"], "Filing-support readiness contradicts blocking reasons");
  }
  if (support.lateFiling.returnDueDate !== support.returnAndBalanceDueDate) {
    addIssue(context, ["lateFiling", "returnDueDate"], "Late-filing due date differs from the return-and-balance due date");
  }
});

const corporationTaxWorksheetFieldSchema = z.object({
  code: z.string().trim().min(1),
  publishedPanelNumber: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  panelTitle: z.string().trim().min(1),
  publishedFieldLabel: z.string().trim().min(1),
  mappingStatus: z.enum([
    "published-panel",
    "published-exact-field-label",
    "published-panel-only",
    "internal-support-only",
    "latest-published-panel-orientation",
    "latest-published-field-orientation",
  ]),
  valueType: z.enum(["money", "text"]),
  numericValue: moneySchema.nullable().optional().transform((value) => value ?? undefined),
  textValue: z.string().nullable().optional().transform((value) => value ?? undefined),
  source: z.string().trim().min(1),
  machineSupported: z.boolean(),
  note: z.string().trim().min(1),
}).superRefine((field, context) => {
  if (field.valueType === "money" && field.numericValue === undefined) {
    addIssue(context, ["numericValue"], "Money worksheet fields require a numeric value");
  }
  if (field.valueType === "text" && field.textValue === undefined) {
    addIssue(context, ["textValue"], "Text worksheet fields require a text value");
  }
});

const corporationTaxWorksheetReconciliationSchema = z.object({
  code: z.string().trim().min(1),
  left: moneySchema,
  right: moneySchema,
  difference: moneySchema,
  reconciles: z.boolean(),
  detail: z.string().trim().min(1),
}).superRefine((reconciliation, context) => {
  if (!near(reconciliation.difference, reconciliation.left - reconciliation.right)) {
    addIssue(context, ["difference"], "Worksheet reconciliation difference is inconsistent");
  }
  if (reconciliation.reconciles !== (Math.abs(reconciliation.difference) < 0.01)) {
    addIssue(context, ["reconciles"], "Worksheet reconciliation status contradicts its difference");
  }
});

const corporationTaxSourceReferenceSchema = z.object({
  code: z.string().trim().min(1),
  title: z.string().trim().min(1),
  url: z.string().url(),
});

export const corporationTaxSupportWorksheetSchema = z.object({
  outputKind: z.literal("scoped-ct1-support-worksheet-not-submittable-return"),
  isCompleteCt1Return: z.literal(false),
  directRosSubmissionSupported: z.literal(false),
  warning: z.string().includes("NOT A CT1 RETURN"),
  companyName: z.string().trim().min(1),
  taxReference: z.string(),
  periodStart: isoDateSchema,
  periodEnd: isoDateSchema,
  mappingVersion: z.string().trim().min(1),
  yearSpecificMappingAvailable: z.boolean(),
  generatedAsOf: isoDateSchema,
  fields: z.array(corporationTaxWorksheetFieldSchema).min(1),
  reconciliations: z.array(corporationTaxWorksheetReconciliationSchema).min(1),
  preliminaryTax: corporationTaxFilingSupportSchema,
  supportWorksheetReady: z.boolean(),
  qualifiedAccountantReviewRequired: z.literal(true),
  blockingReasons: z.array(z.string().trim().min(1)),
  manualCompletionItems: z.array(z.string().trim().min(1)).min(1),
  sources: z.array(corporationTaxSourceReferenceSchema).min(1),
  worksheetSha256: z.string().regex(/^[0-9a-f]{64}$/),
}).superRefine((worksheet, context) => {
  if (worksheet.periodEnd < worksheet.periodStart) {
    addIssue(context, ["periodEnd"], "Worksheet period end cannot precede its start");
  }
  if (worksheet.supportWorksheetReady !== (worksheet.blockingReasons.length === 0)) {
    addIssue(context, ["supportWorksheetReady"], "Worksheet readiness contradicts blocking reasons");
  }
});

export const corporationTaxFilingSupportResponseSchema = z.object({
  review: corporationTaxFilingSupportReviewSchema.nullable().optional().transform((value) => value ?? undefined),
  payments: z.array(corporationTaxPaymentRecordSchema),
  filingSupport: corporationTaxFilingSupportSchema,
  worksheet: corporationTaxSupportWorksheetSchema,
}).superRefine((response, context) => {
  if (response.worksheet.preliminaryTax.calculationSha256 !== response.filingSupport.calculationSha256) {
    addIssue(context, ["worksheet", "preliminaryTax", "calculationSha256"], "Worksheet and filing-support calculations differ");
  }
  if (response.review && response.payments.some((payment) => payment.periodId !== response.review?.periodId)) {
    addIssue(context, ["payments"], "Payment records do not belong to the filing-support review period");
  }
});

export const cashFlowStatementSchema = z.object({
  operatingProfit: moneySchema,
  operatingAdjustments: z.array(z.object({ description: z.string().min(1), amount: moneySchema })),
  cashFromOperations: moneySchema,
  taxPaid: moneySchema,
  netCashFromOperating: moneySchema,
  capitalExpenditurePurchases: moneySchema,
  capitalExpenditureDisposals: moneySchema,
  netCashFromInvesting: moneySchema,
  loanRepayments: moneySchema,
  loanDrawdowns: moneySchema,
  dividendsPaid: moneySchema,
  shareIssues: moneySchema,
  otherFinancing: moneySchema,
  netCashFromFinancing: moneySchema,
  netIncreaseInCash: moneySchema,
  openingCash: moneySchema,
  closingCash: moneySchema,
}).superRefine((cash, context) => {
  if (!near(cash.closingCash, cash.openingCash + cash.netIncreaseInCash)) {
    addIssue(context, ["closingCash"], "Closing cash does not reconcile to opening cash and movement");
  }
  if (!near(cash.netIncreaseInCash, cash.netCashFromOperating + cash.netCashFromInvesting + cash.netCashFromFinancing)) {
    addIssue(context, ["netIncreaseInCash"], "Cash-flow sections do not reconcile");
  }
});

export const equityChangesSchema = z.object({
  openingShareCapital: moneySchema,
  openingRetainedEarnings: moneySchema,
  openingTotal: moneySchema,
  profitForYear: moneySchema,
  dividendsPaid: moneySchema,
  otherReserveMovements: moneySchema,
  sharesIssued: moneySchema,
  closingShareCapital: moneySchema,
  closingRetainedEarnings: moneySchema,
  closingTotal: moneySchema,
}).superRefine((equity, context) => {
  if (!near(equity.openingTotal, equity.openingShareCapital + equity.openingRetainedEarnings)) {
    addIssue(context, ["openingTotal"], "Opening equity does not reconcile");
  }
  if (!near(equity.closingShareCapital, equity.openingShareCapital + equity.sharesIssued)) {
    addIssue(context, ["closingShareCapital"], "Closing share capital does not reconcile");
  }
  const expectedClosingRetained = equity.openingRetainedEarnings + equity.profitForYear
    - equity.dividendsPaid + equity.otherReserveMovements;
  if (!near(equity.closingRetainedEarnings, expectedClosingRetained)) {
    addIssue(context, ["closingRetainedEarnings"], "Closing retained earnings do not reconcile");
  }
  if (!near(equity.closingTotal, equity.closingShareCapital + equity.closingRetainedEarnings)) {
    addIssue(context, ["closingTotal"], "Closing equity does not reconcile");
  }
});

const officerServicePeriodSchema = z.object({
  name: z.string().trim().min(1),
  role: officerRoleSchema,
  appointedDate: isoDateSchema,
  resignedDate: optionalDate,
}).superRefine((officer, context) => {
  if (officer.resignedDate && officer.resignedDate < officer.appointedDate) {
    addIssue(context, ["resignedDate"], "Resignation date cannot precede appointment date");
  }
});

export const directorsReportSchema = z.object({
  companyName: z.string().trim().min(1),
  periodStart: isoDateSchema,
  periodEnd: isoDateSchema,
  directorNames: z.array(z.string().trim().min(1)),
  directorServicePeriods: z.array(officerServicePeriodSchema),
  secretaryName: optionalText,
  secretaryServicePeriod: officerServicePeriodSchema.nullable().optional().transform((value) => value ?? undefined),
  principalActivities: z.string().trim().min(1),
  principalActivitiesReviewed: z.boolean(),
  principalActivitiesReviewedBy: optionalText,
  principalActivitiesReviewedAt: optionalDateTime,
  resultsAndDividends: z.string().trim().min(1),
  profitOrLossAfterTax: moneySchema,
  dividendsPaid: nonNegativeMoneySchema,
  dividendsDeclaredNotPaid: nonNegativeMoneySchema,
  accountingRecordsStatement: z.string().trim().min(1),
  postBalanceSheetEvents: optionalText,
  postBalanceSheetEventsReviewed: z.boolean(),
  goingConcernStatement: optionalText,
  auditInformationStatement: optionalText,
  auditInformationEvidenceRequired: z.boolean(),
  auditInformationEvidenceRecorded: z.boolean(),
  auditInformationConfirmedBy: optionalText,
  auditInformationConfirmedAt: optionalDateTime,
  officerTimelineComplete: z.boolean(),
  isMicroExempt: z.boolean(),
  isSmallExemptFromBusinessReview: z.boolean(),
  electedRegime: electedRegimeSchema,
}).superRefine((report, context) => {
  if (report.periodEnd < report.periodStart) {
    addIssue(context, ["periodEnd"], "Period end cannot precede period start");
  }
  const serviceNames = report.directorServicePeriods.map((officer) => officer.name);
  if (report.directorNames.length !== serviceNames.length
      || report.directorNames.some((name, index) => name !== serviceNames[index])) {
    addIssue(context, ["directorNames"], "Director names do not match the dated service-period evidence");
  }
  report.directorServicePeriods.forEach((officer, index) => {
    if (officer.role !== "Director") addIssue(context, ["directorServicePeriods", index, "role"], "Expected Director");
    if (officer.appointedDate > report.periodEnd
        || (officer.resignedDate && officer.resignedDate < report.periodStart)) {
      addIssue(context, ["directorServicePeriods", index], "Director did not serve during the reporting period");
    }
  });
  if (Boolean(report.secretaryName) !== Boolean(report.secretaryServicePeriod)
      || (report.secretaryServicePeriod && report.secretaryServicePeriod.name !== report.secretaryName)) {
    addIssue(context, ["secretaryServicePeriod"], "Secretary identity and service period must be paired");
  }
  if (report.secretaryServicePeriod) {
    if (report.secretaryServicePeriod.role === "Director") {
      addIssue(context, ["secretaryServicePeriod", "role"], "Expected Secretary or CompanySecretary");
    }
    if (report.secretaryServicePeriod.appointedDate > report.periodEnd
        || (report.secretaryServicePeriod.resignedDate
          && report.secretaryServicePeriod.resignedDate < report.periodStart)) {
      addIssue(context, ["secretaryServicePeriod"], "Secretary did not serve during the reporting period");
    }
  }
  if (report.officerTimelineComplete && report.directorServicePeriods.length === 0) {
    addIssue(context, ["officerTimelineComplete"], "A complete officer timeline requires a reporting-period director");
  }
  if (report.principalActivitiesReviewed !== Boolean(
      report.principalActivitiesReviewedBy && report.principalActivitiesReviewedAt)) {
    addIssue(context, ["principalActivitiesReviewed"], "Principal-activities review evidence is inconsistent");
  }
  if (report.principalActivitiesReviewed && report.principalActivities.startsWith("UNREVIEWED -")) {
    addIssue(context, ["principalActivities"], "Reviewed principal activities cannot carry the unreviewed marker");
  }
  if (!report.principalActivitiesReviewed && !report.principalActivities.startsWith("UNREVIEWED -")) {
    addIssue(context, ["principalActivities"], "Unreviewed principal activities must be visibly marked");
  }
  if (!report.postBalanceSheetEventsReviewed && report.postBalanceSheetEvents) {
    addIssue(context, ["postBalanceSheetEvents"], "An unreviewed no-event assertion cannot be emitted");
  }
  if (report.auditInformationEvidenceRequired) {
    if (Boolean(report.auditInformationConfirmedBy) !== Boolean(report.auditInformationConfirmedAt)) {
      addIssue(context, ["auditInformationConfirmedBy"], "Audit-information reviewer identity and timestamp must be paired");
    }
    if (report.auditInformationStatement && !report.auditInformationEvidenceRecorded) {
      addIssue(context, ["auditInformationStatement"], "Audit-information statement lacks complete director evidence");
    }
    if (report.auditInformationEvidenceRecorded !== Boolean(
        report.auditInformationConfirmedBy
        && report.auditInformationConfirmedAt
        && report.auditInformationStatement)) {
      addIssue(context, ["auditInformationEvidenceRecorded"], "Audit-information statement lacks complete director evidence");
    }
  } else if (report.auditInformationStatement) {
    addIssue(context, ["auditInformationStatement"], "Audit-exempt reports must not emit the statutory audit-information statement");
  }
  if (report.isMicroExempt !== (report.electedRegime === "Micro")) {
    addIssue(context, ["isMicroExempt"], "Micro exemption flag contradicts electedRegime");
  }
});

export const filingDeadlineSchema = z.object({
  id: positiveIdSchema,
  companyId: positiveIdSchema,
  periodId: positiveIdSchema,
  deadlineType: deadlineTypeSchema,
  calculatedDueDate: isoDateSchema,
  dueDate: isoDateSchema,
  annualReturnDate: optionalDate,
  annualReturnDateRecordId: positiveIdSchema.nullable().optional().transform((value) => value ?? undefined),
  returnMadeUpToDate: optionalDate,
  financialStatementsLatestMadeUpToDate: optionalDate,
  deliveryDueDate: optionalDate,
  madeUpToDateBroughtForwardForAccountsAge: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  calculationRuleVersion: optionalText,
  calculationSourceUrl: z.string().url().nullable().optional().transform((value) => value ?? undefined),
  calculationFingerprintSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  manualOverrideStatus: z.enum(["Active", "NeedsReview"]).nullable().optional().transform((value) => value ?? undefined),
  manualOverrideDueDate: optionalDate,
  manualOverrideReason: optionalText,
  manualOverrideEvidenceReference: optionalText,
  manualOverrideEvidenceSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  manualOverrideByUserId: optionalText,
  manualOverrideByDisplayName: optionalText,
  manualOverrideAtUtc: optionalDateTime,
  manualOverrideCalculationFingerprintSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  filedDate: optionalDate,
  filingReference: optionalText,
  isLate: z.boolean(),
  penaltyAmount: nonNegativeMoneySchema,
  notes: optionalText,
}).superRefine((deadline, context) => {
  const effectiveDueDate = deadline.manualOverrideStatus === "Active"
    ? deadline.manualOverrideDueDate
    : deadline.calculatedDueDate;
  if (deadline.dueDate !== effectiveDueDate) {
    addIssue(context, ["dueDate"], "dueDate does not match the statutory calculation or active override");
  }
  if (deadline.deadlineType === "CRO" && deadline.calculationRuleVersion) {
    if (!deadline.annualReturnDate) addIssue(context, ["annualReturnDate"], "CRO rule evidence requires an exact ARD");
    if (!deadline.returnMadeUpToDate) addIssue(context, ["returnMadeUpToDate"], "CRO rule evidence requires a B1 made-up-to date");
    if (!deadline.financialStatementsLatestMadeUpToDate) addIssue(context, ["financialStatementsLatestMadeUpToDate"], "CRO rule evidence requires the accounts-age limit date");
    if (!deadline.deliveryDueDate) addIssue(context, ["deliveryDueDate"], "CRO rule evidence requires a delivery due date");
  }
  if (deadline.manualOverrideStatus && (!deadline.manualOverrideEvidenceReference || !deadline.manualOverrideEvidenceSha256 || !deadline.manualOverrideReason)) {
    addIssue(context, ["manualOverrideStatus"], "Manual override status requires retained evidence and a reason");
  }
  if (deadline.filingReference && !deadline.filedDate) {
    addIssue(context, ["filingReference"], "A filing reference requires a filed date");
  }
  if (deadline.filedDate && deadline.isLate !== (deadline.filedDate > deadline.dueDate)) {
    addIssue(context, ["isLate"], "isLate contradicts filedDate and dueDate");
  }
});

export const charityInfoSchema = z.object({
  id: positiveIdSchema.optional(),
  companyId: positiveIdSchema.optional(),
  charityNumber: optionalText,
  charityType: optionalText,
  grossIncome: nonNegativeMoneySchema,
  sorpTier: z.number().int().min(1).max(3),
  charitableObjectives: optionalText,
  principalActivities: optionalText,
  governanceCodeCompliant: z.boolean().nullable(),
  governanceCodeNote: optionalText,
  governanceEvidenceReference: optionalText,
  governanceReviewedBy: optionalText,
  governanceReviewedAtUtc: optionalDateTime,
  governanceEvidenceArtifactSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  hasInternationalTransfers: z.boolean(),
  internationalTransferDetails: optionalText,
  trusteeRemunerationPaid: z.boolean(),
  trusteeRemunerationAmount: nonNegativeMoneySchema,
  trusteeExpensesDetails: optionalText,
}).superRefine((info, context) => {
  const governanceEvidenceComplete = Boolean(
    info.governanceEvidenceReference
    && info.governanceReviewedBy
    && info.governanceReviewedAtUtc
    && info.governanceEvidenceArtifactSha256,
  );
  if (info.governanceCodeCompliant !== null && !governanceEvidenceComplete) {
    addIssue(context, ["governanceEvidenceReference"], "An explicit governance answer requires retained reviewer and artifact evidence");
  }
  if (info.governanceCodeCompliant === null && governanceEvidenceComplete) {
    addIssue(context, ["governanceCodeCompliant"], "Governance evidence cannot be treated as accepted without an explicit answer");
  }
  if (info.governanceCodeCompliant === false && !info.governanceCodeNote) {
    addIssue(context, ["governanceCodeNote"], "A No governance answer requires an explanatory note");
  }
  if (!info.trusteeRemunerationPaid && info.trusteeRemunerationAmount !== 0) {
    addIssue(context, ["trusteeRemunerationAmount"], "No remuneration can be reported when trusteeRemunerationPaid=false");
  }
  if (info.hasInternationalTransfers && !info.internationalTransferDetails) {
    addIssue(context, ["internationalTransferDetails"], "International transfers require details");
  }
});

export const absentCharityInfoSchema = z.object({ message: z.literal("No charity info configured") });

const fundTypeSchema = z.enum(["Unrestricted", "Designated", "Restricted", "Endowment"]);
export const fundLineSchema = z.object({
  fundName: z.string().trim().min(1),
  fundType: fundTypeSchema,
  openingBalance: moneySchema,
  incomingResources: moneySchema,
  resourcesExpended: moneySchema,
  transfers: moneySchema,
  gainsLosses: moneySchema,
  closingBalance: moneySchema,
}).superRefine((fund, context) => {
  const expected = fund.openingBalance + fund.incomingResources - fund.resourcesExpended + fund.transfers + fund.gainsLosses;
  if (!near(fund.closingBalance, expected)) addIssue(context, ["closingBalance"], "Fund closing balance does not reconcile");
});

export const fundBalanceSchema = fundLineSchema.and(z.object({
  id: positiveIdSchema.optional(),
  periodId: positiveIdSchema.optional(),
  notes: optionalText,
}));

export const sofaSchema = z.object({
  unrestrictedFunds: z.array(fundLineSchema),
  restrictedFunds: z.array(fundLineSchema),
  endowmentFunds: z.array(fundLineSchema),
  totalIncoming: moneySchema,
  totalExpended: moneySchema,
  totalTransfers: moneySchema,
  totalGainsLosses: moneySchema,
  netMovement: moneySchema,
  totalOpeningFunds: moneySchema,
  totalClosingFunds: moneySchema,
}).superRefine((sofa, context) => {
  const funds = [...sofa.unrestrictedFunds, ...sofa.restrictedFunds, ...sofa.endowmentFunds];
  const sums = {
    incoming: funds.reduce((sum, fund) => sum + fund.incomingResources, 0),
    expended: funds.reduce((sum, fund) => sum + fund.resourcesExpended, 0),
    transfers: funds.reduce((sum, fund) => sum + fund.transfers, 0),
    gains: funds.reduce((sum, fund) => sum + fund.gainsLosses, 0),
    opening: funds.reduce((sum, fund) => sum + fund.openingBalance, 0),
    closing: funds.reduce((sum, fund) => sum + fund.closingBalance, 0),
  };
  const checks: Array<[PropertyKey[], number, number]> = [
    [["totalIncoming"], sofa.totalIncoming, sums.incoming],
    [["totalExpended"], sofa.totalExpended, sums.expended],
    [["totalTransfers"], sofa.totalTransfers, sums.transfers],
    [["totalGainsLosses"], sofa.totalGainsLosses, sums.gains],
    [["netMovement"], sofa.netMovement, sums.incoming - sums.expended + sums.transfers + sums.gains],
    [["totalOpeningFunds"], sofa.totalOpeningFunds, sums.opening],
    [["totalClosingFunds"], sofa.totalClosingFunds, sums.closing],
  ];
  checks.forEach(([path, actual, expected]) => {
    if (!near(actual, expected)) addIssue(context, path, `Expected reconciled value ${expected}`);
  });
  if (!near(sofa.totalClosingFunds, sofa.totalOpeningFunds + sofa.netMovement)) {
    addIssue(context, ["totalClosingFunds"], "Closing funds do not reconcile to opening funds and net movement");
  }
});

const reportDateSchema = z.string().refine(
  (value) => /^(?:0?[1-9]|[12]\d|3[01]) (?:January|February|March|April|May|June|July|August|September|October|November|December) \d{4}$/.test(value),
  "Expected a report date such as 31 December 2026",
);

export const trusteesReportSchema = z.object({
  charityName: z.string().trim().min(1),
  charityNumber: z.string(),
  croNumber: z.string(),
  periodStart: reportDateSchema,
  periodEnd: reportDateSchema,
  trustees: z.array(z.object({
    officerId: positiveIdSchema,
    name: z.string().trim().min(1),
    appointedDate: isoDateSchema,
    resignedDate: optionalDate,
  })).min(1),
  charitableObjectives: z.string().trim().min(1),
  principalActivities: z.string().trim().min(1),
  totalIncome: moneySchema,
  totalExpenditure: moneySchema,
  netMovement: moneySchema,
  closingFunds: moneySchema,
  governanceCodeCompliant: z.boolean(),
  governanceCodeNote: optionalText,
  governanceEvidenceReference: z.string().trim().min(1),
  governanceReviewedBy: z.string().trim().min(1),
  governanceReviewedAtUtc: isoDateTimeSchema,
  trusteeRemunerationPaid: z.boolean(),
  trusteeRemunerationAmount: nonNegativeMoneySchema,
  trusteeExpensesDetails: optionalText,
  hasInternationalTransfers: z.boolean(),
  internationalTransferDetails: optionalText,
  filingDeadline: reportDateSchema,
}).superRefine((report, context) => {
  if (!near(report.netMovement, report.totalIncome - report.totalExpenditure)) {
    addIssue(context, ["netMovement"], "Net movement does not reconcile to income and expenditure");
  }
});

const sha256Schema = z.string().regex(/^[a-f0-9]{64}$/i);

export const charitySorpSourceSchema = z.object({
  sourceId: z.string().trim().min(1),
  title: z.string().trim().min(1),
  url: z.string().url(),
  documentSha256: sha256Schema.nullable(),
  basis: z.string().trim().min(1),
});

export const charitySorpDecisionSchema = z.object({
  frameworkCode: z.enum(["SORP-2019-FRS102", "SORP-2026-FRS102"]),
  frameworkTitle: z.string().trim().min(1),
  effectiveFrom: isoDateSchema,
  tier: z.number().int().min(1).max(3).nullable(),
  sofaBasis: z.enum(["natural-or-activity", "activity", "undetermined"]),
  automatedArtifactsSupported: z.boolean(),
  manualProfessionalHandoffRequired: z.boolean(),
  decisionReason: z.string().trim().min(1),
  sources: z.array(charitySorpSourceSchema).min(1),
  decisionSha256: sha256Schema,
}).superRefine((decision, context) => {
  if (decision.automatedArtifactsSupported === decision.manualProfessionalHandoffRequired) {
    addIssue(context, ["automatedArtifactsSupported"], "Automated support and manual handoff must be exact opposites");
  }
  if (decision.automatedArtifactsSupported
    && (decision.frameworkCode !== "SORP-2026-FRS102" || decision.tier !== 1)) {
    addIssue(context, ["tier"], "This release supports automated artifacts only for SORP 2026 Tier 1");
  }
});

const charityArtifactPackageSchema = z.object({
  filingStatus: filingStatusSchema,
  sofaGenerated: z.boolean(),
  trusteesReportGenerated: z.boolean(),
  sofaSha256: sha256Schema.nullable(),
  trusteesReportSha256: sha256Schema.nullable(),
  artifactReleaseCandidate: z.string().trim().min(1).nullable(),
  artifactSourceFingerprintSha256: sha256Schema.nullable(),
  sorpFrameworkCode: z.string().trim().min(1).nullable(),
  sorpTier: z.number().int().min(1).max(3).nullable(),
  sofaBasis: z.string().trim().min(1).nullable(),
  charityNumberSnapshot: z.string().trim().min(1).nullable(),
  sofaClosingFunds: moneySchema.nullable(),
  balanceSheetNetAssets: moneySchema.nullable(),
  reconciliationDifference: moneySchema.nullable(),
  reconciledAtUtc: isoDateTimeSchema.nullable(),
  trusteeReviewAccepted: z.boolean(),
  trusteeReviewReference: z.string().trim().min(1).nullable(),
  trusteeReviewedBy: z.string().trim().min(1).nullable(),
  trusteeReviewedAtUtc: isoDateTimeSchema.nullable(),
  trusteeReviewArtifactSha256: sha256Schema.nullable(),
  trusteePopulationSha256: sha256Schema.nullable(),
  manualProfessionalHandoffReason: z.string().trim().min(1).nullable(),
  approvedBy: z.string().trim().min(1).nullable(),
  approvedAt: isoDateTimeSchema.nullable(),
  approvedArtifactManifestSha256: sha256Schema.nullable(),
  approvedReleaseCandidate: z.string().trim().min(1).nullable(),
}).superRefine((artifact, context) => {
  if (artifact.sofaGenerated || artifact.trusteesReportGenerated) {
    if (!artifact.artifactSourceFingerprintSha256 || !artifact.artifactReleaseCandidate) {
      addIssue(context, ["artifactSourceFingerprintSha256"], "Generated charity artifacts require candidate-bound source evidence");
    }
    if (artifact.reconciliationDifference === null || !near(artifact.reconciliationDifference, 0)) {
      addIssue(context, ["reconciliationDifference"], "Generated charity artifacts require a reconciled zero difference");
    }
  }
  if (artifact.trusteeReviewAccepted && (!artifact.trusteeReviewReference
    || !artifact.trusteeReviewedBy
    || !artifact.trusteeReviewedAtUtc
    || !artifact.trusteeReviewArtifactSha256
    || !artifact.trusteePopulationSha256)) {
    addIssue(context, ["trusteeReviewAccepted"], "Accepted trustee review lacks retained evidence");
  }
});

export const charityArtifactStatusSchema = z.object({
  decision: charitySorpDecisionSchema,
  package: charityArtifactPackageSchema.nullable(),
});

export const legalSourceSchema = z.object({
  sourceId: z.string().trim().min(1),
  title: z.string().trim().min(1),
  effectiveDate: isoDateSchema,
  url: z.string().url().refine((url) => url.startsWith("https://"), "Legal source URL must use HTTPS"),
});

const revenueTaxonomySchema = z.object({
  taxonomyKey: z.string().trim().min(1),
  taxonomyDate: isoDateSchema,
  label: z.string().trim().min(1),
  schemaRef: z.string().url().refine((url) => url.startsWith("https://"), "Taxonomy schemaRef must use HTTPS"),
  acceptedByRevenue: z.boolean(),
  effectiveForPeriodsStartingOnOrAfter: isoDateSchema,
  sources: z.array(legalSourceSchema).min(1),
});

const filingEvidenceSchema = z.object({
  code: z.string().trim().min(1),
  label: z.string().trim().min(1),
  required: z.boolean(),
  satisfied: z.boolean(),
  detail: optionalText,
  sources: z.array(legalSourceSchema),
});
const filingIssueSchema = z.object({
  code: z.string().trim().min(1),
  severity: z.enum(["blocking", "warning"]),
  message: z.string().trim().min(1),
  sources: z.array(legalSourceSchema),
});
const signOffStepSchema = z.object({
  code: z.string().trim().min(1),
  label: z.string().trim().min(1),
  state: z.enum(["complete", "blocked", "warning", "pending"]),
  detail: z.string().trim().min(1),
  sources: z.array(legalSourceSchema),
});
const signOffPacketSchema = z.object({
  state: z.enum([
    "manual-handoff",
    "ready-for-external-filing",
    "approved-external-evidence-open",
    "ready-for-accountant-review",
    "blocked",
  ]),
  stateLabel: z.string().trim().min(1),
  readyForAccountantApproval: z.boolean(),
  readyForExternalFiling: z.boolean(),
  approvedBy: optionalText,
  approvedAt: optionalDateTime,
  steps: z.array(signOffStepSchema).min(1),
  openBlockers: z.array(z.string().trim().min(1)),
  openWarnings: z.array(z.string().trim().min(1)),
  allowedNextActions: z.array(z.string().trim().min(1)),
}).superRefine((packet, context) => {
  if (Boolean(packet.approvedBy) !== Boolean(packet.approvedAt)) {
    addIssue(context, ["approvedAt"], "Approval name and timestamp must be paired");
  }
  if (packet.readyForExternalFiling && packet.openBlockers.length > 0) {
    addIssue(context, ["readyForExternalFiling"], "External filing cannot be ready while blockers remain");
  }
});

export const filingReadinessProfileSchema = z.object({
  companyId: positiveIdSchema,
  periodId: positiveIdSchema,
  companyType: companyTypeSchema,
  sizeClass: companySizeSchema.nullable().optional().transform((value) => value ?? undefined),
  electedRegime: electedRegimeSchema.nullable().optional().transform((value) => value ?? undefined),
  auditExempt: z.boolean().nullable().optional().transform((value) => value ?? undefined),
  supportedPath: z.boolean(),
  manualProfessionalReviewRequired: z.boolean(),
  accountantReviewRequired: z.boolean(),
  accountantReviewState: z.string().trim().min(1),
  directCroSubmissionSupported: z.literal(false),
  directRosSubmissionSupported: z.literal(false),
  revenueIxbrlGenerationSupported: z.boolean(),
  revenueManualHandoffRequired: z.boolean(),
  revenueGenerationSupportReason: z.string().trim().min(1),
  revenueTaxonomy: revenueTaxonomySchema,
  signOffPacket: signOffPacketSchema,
  requiredEvidence: z.array(filingEvidenceSchema),
  blockingIssues: z.array(filingIssueSchema),
  warningIssues: z.array(filingIssueSchema),
  sourceReferences: z.array(legalSourceSchema).min(1),
  allowedNextActions: z.array(z.string().trim().min(1)),
}).superRefine((profile, context) => {
  if (!profile.accountantReviewRequired) {
    addIssue(context, ["accountantReviewRequired"], "Qualified-accountant review must remain mandatory");
  }
  if (profile.revenueIxbrlGenerationSupported === profile.revenueManualHandoffRequired) {
    addIssue(context, ["revenueManualHandoffRequired"], "Revenue generation support and manual handoff flags are inconsistent");
  }
  if (!profile.supportedPath && !profile.manualProfessionalReviewRequired) {
    addIssue(context, ["manualProfessionalReviewRequired"], "Unsupported paths require professional handoff");
  }
  if (profile.blockingIssues.some((issue) => issue.severity !== "blocking")) {
    addIssue(context, ["blockingIssues"], "Blocking issue collection contains a non-blocking severity");
  }
  if (profile.warningIssues.some((issue) => issue.severity !== "warning")) {
    addIssue(context, ["warningIssues"], "Warning issue collection contains a non-warning severity");
  }
  const requiredCodes = profile.requiredEvidence.map((item) => item.code);
  if (new Set(requiredCodes).size !== requiredCodes.length) {
    addIssue(context, ["requiredEvidence"], "Duplicate evidence codes are not allowed");
  }
  const expectedBlockers = [...new Set(profile.blockingIssues.map((issue) => issue.message))];
  const expectedWarnings = [...new Set(profile.warningIssues.map((issue) => issue.message))];
  if (profile.signOffPacket.openBlockers.length !== expectedBlockers.length
      || expectedBlockers.some((message) => !profile.signOffPacket.openBlockers.includes(message))) {
    addIssue(context, ["signOffPacket", "openBlockers"], "Open blockers do not reconcile to blocking issues");
  }
  if (profile.signOffPacket.openWarnings.length !== expectedWarnings.length
      || expectedWarnings.some((message) => !profile.signOffPacket.openWarnings.includes(message))) {
    addIssue(context, ["signOffPacket", "openWarnings"], "Open warnings do not reconcile to warning issues");
  }
  if (profile.signOffPacket.allowedNextActions.length !== profile.allowedNextActions.length
      || profile.allowedNextActions.some((action, index) => action !== profile.signOffPacket.allowedNextActions[index])) {
    addIssue(context, ["allowedNextActions"], "Top-level actions do not match the sign-off packet");
  }
});

const croFilingStatusSchema = z.object({
  status: filingStatusSchema,
  accountsPdfReady: z.boolean(),
  signaturePageReady: z.boolean(),
  paymentCompleted: z.boolean(),
  submissionReference: optionalText,
  rejectionReason: optionalText,
  correctionDeadline: optionalDateTime,
});
export const revenueFilingStatusSchema = z.object({
  status: filingStatusSchema,
  ixbrlReady: z.boolean(),
  ixbrlInternalChecksPassed: z.boolean(),
  ixbrlValid: z.boolean(),
  validationErrors: optionalText,
  ct1Reference: optionalText,
  generationSupport: z.enum(["filing-ready", "manual-handoff-only"]),
  manualHandoffRequired: z.boolean(),
  reviewPrototypeChecksPassed: z.boolean(),
}).superRefine((status, context) => {
  const manualOnly = status.generationSupport === "manual-handoff-only";
  if (status.manualHandoffRequired !== manualOnly) {
    addIssue(context, ["manualHandoffRequired"], "Manual handoff flag contradicts generationSupport");
  }
  if (manualOnly && (status.ixbrlReady || status.ixbrlInternalChecksPassed || status.ixbrlValid)) {
    addIssue(context, ["generationSupport"], "Manual-handoff-only Revenue output cannot claim filing readiness or validation");
  }
});
export const charityFilingStatusSchema = z.object({
  status: filingStatusSchema,
  sofaGenerated: z.boolean(),
  trusteesReportGenerated: z.boolean(),
  annualReturnReference: optionalText,
  rejectionReason: optionalText,
  correctionDeadline: optionalDateTime,
  submittedBy: optionalText,
  submittedAt: optionalDateTime,
  acceptedBy: optionalText,
  acceptedAt: optionalDateTime,
}).superRefine((status, context) => {
  if (Boolean(status.submittedBy) !== Boolean(status.submittedAt)) {
    addIssue(context, ["submittedAt"], "Submission name and timestamp must be paired");
  }
  if (Boolean(status.acceptedBy) !== Boolean(status.acceptedAt)) {
    addIssue(context, ["acceptedAt"], "Acceptance name and timestamp must be paired");
  }
});

export const filingWorkflowStatusSchema = z.object({
  cro: croFilingStatusSchema,
  revenue: revenueFilingStatusSchema,
  charity: charityFilingStatusSchema,
  blockingIssues: z.array(z.string().trim().min(1)),
  warningIssues: z.array(z.string().trim().min(1)),
  readyToFile: z.boolean(),
}).superRefine((status, context) => {
  if (status.readyToFile !== (status.blockingIssues.length === 0)) {
    addIssue(context, ["readyToFile"], "readyToFile must exactly reflect the absence of blocking issues");
  }
});

export const authUserSchema = z.object({
  userId: positiveIdSchema,
  tenantId: positiveIdSchema,
  tenantName: z.string().trim().min(1),
  tenantSlug: z.string().trim().min(3).max(120).regex(/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])$/),
  email: z.string().email(),
  displayName: z.string().trim().min(1),
  role: z.enum(["Owner", "Accountant", "Reviewer", "Client"]),
  allowedCompanyIds: z.array(positiveIdSchema),
  mustChangePassword: z.boolean(),
  mfaVerified: z.boolean().default(false),
  mfaMethod: z.enum(["totp", "recovery"]).nullable().default(null),
}).superRefine((user, context) => {
  if (new Set(user.allowedCompanyIds).size !== user.allowedCompanyIds.length) {
    addIssue(context, ["allowedCompanyIds"], "Duplicate company access IDs are not allowed");
  }
});

export const mfaChallengeSchema = z.object({
  challengeToken: z.string().min(32),
  requiresEnrollment: z.boolean(),
  expiresAtUtc: isoDateTimeSchema,
  enrollmentSecret: z.string().regex(/^[A-Z2-7]{16,128}$/).nullable(),
  otpAuthUri: z.string().startsWith("otpauth://totp/").nullable(),
}).strict().superRefine((challenge, context) => {
  if (challenge.requiresEnrollment !== Boolean(challenge.enrollmentSecret && challenge.otpAuthUri)) {
    addIssue(context, ["requiresEnrollment"], "Enrollment challenges must carry both enrollment fields and login challenges must carry neither");
  }
});

export const mfaCompletionSchema = z.object({
  user: authUserSchema,
  recoveryCodes: z.array(z.string().regex(/^[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}$/)).max(10),
}).strict();

export const userAdministrationSummarySchema = z.object({
  userId: positiveIdSchema,
  email: z.string().email(),
  displayName: z.string().trim().min(2).max(200),
  role: z.enum(["Owner", "Accountant", "Reviewer", "Client"]),
  isActive: z.boolean(),
  mustChangePassword: z.boolean(),
  isLocked: z.boolean(),
  lockedUntilUtc: isoDateTimeSchema.nullable(),
  mfaEnabled: z.boolean(),
  companyIds: z.array(positiveIdSchema),
  inviteAcceptedAtUtc: isoDateTimeSchema.nullable(),
  deactivatedAtUtc: isoDateTimeSchema.nullable(),
  offboardedAtUtc: isoDateTimeSchema.nullable(),
  sessionVersion: z.number().int().positive(),
}).strict().superRefine((user, context) => {
  if (new Set(user.companyIds).size !== user.companyIds.length) {
    addIssue(context, ["companyIds"], "Duplicate company assignment IDs are not allowed");
  }
  if (user.offboardedAtUtc && user.isActive) {
    addIssue(context, ["isActive"], "An offboarded account cannot be active");
  }
});

export const userProvisioningResultSchema = z.object({
  user: userAdministrationSummarySchema,
  actionToken: z.string().min(32),
  expiresAtUtc: isoDateTimeSchema,
}).strict();

const workingPaperSha256Schema = z.string().regex(/^[0-9a-f]{64}$/);

export const workingPaperSourceReferenceSchema = z.object({
  sourceType: z.string().trim().min(1),
  entityId: positiveIdSchema,
  periodId: positiveIdSchema,
  label: z.string().trim().min(1),
  amount: moneySchema.nullable(),
  evidenceReference: z.string().nullable(),
  reviewedBy: z.string().nullable(),
  reviewedAtUtc: isoDateTimeSchema.nullable(),
  drillDownRoute: z.string().startsWith("/companies/"),
}).strict();

const workingPaperReconciliationSchema = z.object({
  code: z.string().trim().min(1),
  description: z.string().trim().min(1),
  left: moneySchema,
  right: moneySchema,
  difference: moneySchema,
  reconciles: z.boolean(),
}).strict().superRefine((item, context) => {
  const expectedDifference = item.left - item.right;
  if (!near(item.difference, expectedDifference)) {
    addIssue(context, ["difference"], "Reconciliation difference does not equal left less right");
  }
  if (item.reconciles !== near(expectedDifference, 0)) {
    addIssue(context, ["reconciles"], "Reconciliation status does not match the difference");
  }
});

const workingPaperIdentitySchema = z.object({
  schemaVersion: z.literal("accounts-working-papers-v1"),
  artifactVersion: positiveIdSchema,
  tenantId: positiveIdSchema,
  companyId: positiveIdSchema,
  companyName: z.string().trim().min(1),
  periodId: positiveIdSchema,
  periodStart: isoDateSchema,
  periodEnd: isoDateSchema,
  generatedByUserId: z.string().trim().min(1),
  generatedByDisplayName: z.string().trim().min(1),
  generatedByRole: z.enum(["Owner", "Accountant", "Reviewer"]),
  generatedAtUtc: isoDateTimeSchema,
  releaseCandidate: z.string().trim().min(1).max(200),
  sourceDataSha256: workingPaperSha256Schema,
  artifactSha256: workingPaperSha256Schema,
}).strict().superRefine((identity, context) => {
  if (identity.periodEnd < identity.periodStart) {
    addIssue(context, ["periodEnd"], "Working-paper period end cannot precede its start");
  }
});

const leadScheduleRowSchema = z.object({
  code: z.string().trim().min(1),
  name: z.string().trim().min(1),
  accountType: z.enum(["Income", "Expense", "Asset", "Liability", "Equity"]),
  openingDebit: nonNegativeMoneySchema,
  openingCredit: nonNegativeMoneySchema,
  transactionDebit: nonNegativeMoneySchema,
  transactionCredit: nonNegativeMoneySchema,
  journalDebit: nonNegativeMoneySchema,
  journalCredit: nonNegativeMoneySchema,
  closingDebit: nonNegativeMoneySchema,
  closingCredit: nonNegativeMoneySchema,
  sources: z.array(workingPaperSourceReferenceSchema),
}).strict().superRefine((row, context) => {
  const net = row.openingDebit + row.transactionDebit + row.journalDebit
    - row.openingCredit - row.transactionCredit - row.journalCredit;
  if (!near(row.closingDebit, Math.max(0, net)) || !near(row.closingCredit, Math.max(0, -net))) {
    addIssue(context, ["closingDebit"], "Lead-schedule closing balance does not reconcile to its movements");
  }
});

const leadSchedulesSchema = z.object({
  outputKind: z.literal("lead-schedules"),
  rows: z.array(leadScheduleRowSchema),
  reconciliations: z.array(workingPaperReconciliationSchema).min(2),
  artifactSha256: workingPaperSha256Schema,
}).strict();

const categorizedTransactionRowSchema = z.object({
  transactionId: positiveIdSchema,
  date: isoDateSchema,
  description: z.string().trim().min(1),
  amount: moneySchema,
  bankAccountId: positiveIdSchema,
  bankAccountName: z.string().trim().min(1),
  importBatchId: positiveIdSchema.nullable(),
  importFilename: z.string().nullable(),
  sourceFileSha256: workingPaperSha256Schema.nullable(),
  categoryId: positiveIdSchema.nullable(),
  categoryCode: z.string().nullable(),
  categoryName: z.string().nullable(),
  categorizationStatus: z.enum(["categorised", "uncategorised"]),
  confidenceScore: z.number().finite().min(0).max(1).nullable(),
  manualOverride: z.boolean(),
  includedInLedger: z.boolean(),
  duplicateReviewStatus: z.enum(["NotCandidate", "Pending", "LegacyLockedUnverified", "Retained", "Discarded"]),
  duplicateDecisionBy: z.string().nullable(),
  duplicateDecisionAtUtc: isoDateTimeSchema.nullable(),
  sources: z.array(workingPaperSourceReferenceSchema).min(1),
}).strict().superRefine((row, context) => {
  const categorised = row.categoryId !== null;
  if (categorised !== (row.categorizationStatus === "categorised")) {
    addIssue(context, ["categorizationStatus"], "Categorization status does not match category identity");
  }
  if ((row.categoryCode === null) !== (row.categoryId === null)
    || (row.categoryName === null) !== (row.categoryId === null)) {
    addIssue(context, ["categoryCode"], "Category ID, code, and name must be present or absent together");
  }
  if ((row.duplicateDecisionBy === null) !== (row.duplicateDecisionAtUtc === null)) {
    addIssue(context, ["duplicateDecisionAtUtc"], "Duplicate reviewer identity and timestamp must be paired");
  }
});

const categorizedTransactionsSchema = z.object({
  outputKind: z.literal("categorized-transactions"),
  totalCount: nonNegativeIntegerSchema,
  categorizedCount: nonNegativeIntegerSchema,
  uncategorisedCount: nonNegativeIntegerSchema,
  includedNetMovement: moneySchema,
  rows: z.array(categorizedTransactionRowSchema),
  artifactSha256: workingPaperSha256Schema,
}).strict().superRefine((artifact, context) => {
  if (artifact.totalCount !== artifact.rows.length
    || artifact.categorizedCount !== artifact.rows.filter((row) => row.categoryId !== null).length
    || artifact.uncategorisedCount !== artifact.rows.filter((row) => row.categoryId === null).length) {
    addIssue(context, ["totalCount"], "Categorized transaction counts do not match the retained rows");
  }
  const included = artifact.rows.filter((row) => row.includedInLedger).reduce((sum, row) => sum + row.amount, 0);
  if (!near(artifact.includedNetMovement, included)) {
    addIssue(context, ["includedNetMovement"], "Included movement does not equal the retained ledger rows");
  }
});

const reviewExceptionsSchema = z.object({
  outputKind: z.literal("review-exceptions"),
  blockingCount: nonNegativeIntegerSchema,
  warningCount: nonNegativeIntegerSchema,
  items: z.array(z.object({
    code: z.string().trim().min(1),
    severity: z.enum(["blocking", "warning"]),
    message: z.string().trim().min(1),
    resolutionRoute: z.string().startsWith("/companies/"),
    sources: z.array(workingPaperSourceReferenceSchema),
  }).strict()),
  artifactSha256: workingPaperSha256Schema,
}).strict().superRefine((artifact, context) => {
  if (artifact.blockingCount !== artifact.items.filter((item) => item.severity === "blocking").length
    || artifact.warningCount !== artifact.items.filter((item) => item.severity === "warning").length) {
    addIssue(context, ["blockingCount"], "Review-exception counts do not match the retained items");
  }
});

const adjustedTrialBalanceRowSchema = z.object({
  code: z.string().trim().min(1),
  name: z.string().trim().min(1),
  accountType: z.enum(["Income", "Expense", "Asset", "Liability", "Equity"]),
  unadjustedDebit: nonNegativeMoneySchema,
  unadjustedCredit: nonNegativeMoneySchema,
  journalDebit: nonNegativeMoneySchema,
  journalCredit: nonNegativeMoneySchema,
  adjustedDebit: nonNegativeMoneySchema,
  adjustedCredit: nonNegativeMoneySchema,
  sources: z.array(workingPaperSourceReferenceSchema),
}).strict();

const adjustedTrialBalanceSchema = z.object({
  outputKind: z.literal("adjusted-trial-balance"),
  totalUnadjustedDebits: nonNegativeMoneySchema,
  totalUnadjustedCredits: nonNegativeMoneySchema,
  totalJournalDebits: nonNegativeMoneySchema,
  totalJournalCredits: nonNegativeMoneySchema,
  totalAdjustedDebits: nonNegativeMoneySchema,
  totalAdjustedCredits: nonNegativeMoneySchema,
  rows: z.array(adjustedTrialBalanceRowSchema),
  reconciliations: z.array(workingPaperReconciliationSchema).min(4),
  artifactSha256: workingPaperSha256Schema,
}).strict().superRefine((artifact, context) => {
  const totals = {
    totalUnadjustedDebits: artifact.rows.reduce((sum, row) => sum + row.unadjustedDebit, 0),
    totalUnadjustedCredits: artifact.rows.reduce((sum, row) => sum + row.unadjustedCredit, 0),
    totalJournalDebits: artifact.rows.reduce((sum, row) => sum + row.journalDebit, 0),
    totalJournalCredits: artifact.rows.reduce((sum, row) => sum + row.journalCredit, 0),
    totalAdjustedDebits: artifact.rows.reduce((sum, row) => sum + row.adjustedDebit, 0),
    totalAdjustedCredits: artifact.rows.reduce((sum, row) => sum + row.adjustedCredit, 0),
  };
  for (const [key, expected] of Object.entries(totals)) {
    if (!near(artifact[key as keyof typeof totals] as number, expected)) {
      addIssue(context, [key], "Trial-balance total does not match its rows");
    }
  }
});

const corporationTaxBridgeSchema = z.object({
  outputKind: z.literal("corporation-tax-bridge-not-ct1-return"),
  isCompleteCt1Return: z.literal(false),
  directRosSubmissionSupported: z.literal(false),
  qualifiedAccountantReviewRequired: z.literal(true),
  taxCalculationSha256: workingPaperSha256Schema,
  accountingProfitBeforeTax: moneySchema,
  tradingProfitBeforeLossRelief: moneySchema,
  capitalAllowances: nonNegativeMoneySchema,
  tradingLossUsed: nonNegativeMoneySchema,
  tradingProfitAfterLossRelief: moneySchema,
  passiveNonTradingIncome: moneySchema,
  taxableProfit: moneySchema,
  corporationTaxDue: moneySchema,
  preliminaryTaxPaid: moneySchema,
  balanceDue: moneySchema,
  rows: z.array(z.object({
    code: z.string().trim().min(1),
    description: z.string().trim().min(1),
    amount: moneySchema,
    basis: z.string().trim().min(1),
    sources: z.array(workingPaperSourceReferenceSchema),
  }).strict()).min(9),
  reconciliations: z.array(workingPaperReconciliationSchema).min(3),
  blockingReasons: z.array(z.string().trim().min(1)),
  artifactSha256: workingPaperSha256Schema,
}).strict().superRefine((artifact, context) => {
  if (!near(artifact.taxableProfit,
    Math.max(0, artifact.tradingProfitAfterLossRelief) + Math.max(0, artifact.passiveNonTradingIncome))) {
    addIssue(context, ["taxableProfit"], "Taxable profit does not equal the supported tax streams");
  }
  if (!near(artifact.balanceDue, artifact.corporationTaxDue - artifact.preliminaryTaxPaid)) {
    addIssue(context, ["balanceDue"], "Corporation-tax balance does not reconcile to tax less preliminary tax");
  }
});

const workingPaperIndexSchema = z.object({
  outputKind: z.literal("working-paper-index"),
  entries: z.array(z.object({
    code: z.enum(["lead-schedules", "categorized-transactions", "review-exceptions", "adjusted-trial-balance", "corporation-tax-bridge"]),
    title: z.string().trim().min(1),
    endpoint: z.string().startsWith("/api/companies/"),
    itemCount: nonNegativeIntegerSchema,
    status: z.enum(["ready", "review-required", "blocked", "retained"]),
    artifactSha256: workingPaperSha256Schema,
  }).strict()).length(5),
  artifactSha256: workingPaperSha256Schema,
}).strict().superRefine((index, context) => {
  if (new Set(index.entries.map((entry) => entry.code)).size !== 5) {
    addIssue(context, ["entries"], "Working-paper index must contain each output exactly once");
  }
});

export const accountantWorkingPaperPackSchema = z.object({
  outputKind: z.literal("internal-accountant-working-paper-pack"),
  isFilingArtifact: z.literal(false),
  directSubmissionSupported: z.literal(false),
  qualifiedAccountantReviewRequired: z.literal(true),
  warning: z.string().includes("NOT A CRO OR CT1 RETURN"),
  identity: workingPaperIdentitySchema,
  leadSchedules: leadSchedulesSchema,
  categorizedTransactions: categorizedTransactionsSchema,
  reviewExceptions: reviewExceptionsSchema,
  adjustedTrialBalance: adjustedTrialBalanceSchema,
  workingPaperIndex: workingPaperIndexSchema,
  corporationTaxBridge: corporationTaxBridgeSchema,
}).strict().superRefine((pack, context) => {
  const hashes = new Map([
    ["lead-schedules", pack.leadSchedules.artifactSha256],
    ["categorized-transactions", pack.categorizedTransactions.artifactSha256],
    ["review-exceptions", pack.reviewExceptions.artifactSha256],
    ["adjusted-trial-balance", pack.adjustedTrialBalance.artifactSha256],
    ["corporation-tax-bridge", pack.corporationTaxBridge.artifactSha256],
  ]);
  for (const [index, entry] of pack.workingPaperIndex.entries.entries()) {
    if (hashes.get(entry.code) !== entry.artifactSha256) {
      addIssue(context, ["workingPaperIndex", "entries", index, "artifactSha256"], "Index hash does not match the retained output");
    }
  }
  if (pack.corporationTaxBridge.accountingProfitBeforeTax !== pack.corporationTaxBridge.rows[0]?.amount) {
    addIssue(context, ["corporationTaxBridge", "rows", 0], "Tax bridge does not begin with accounting profit");
  }
});

export class ApiContractError extends Error {
  readonly contract: string;
  readonly issuePath: string;

  constructor(contract: string, issuePath: string, detail: string) {
    super(`Invalid ${contract} response contract at ${issuePath}: ${detail}`);
    this.name = "ApiContractError";
    this.contract = contract;
    this.issuePath = issuePath;
  }
}

export function parseApiContract<T>(
  schema: z.ZodType<T>,
  payload: unknown,
  contract: string,
): T {
  const result = schema.safeParse(payload);
  if (!result.success) {
    const issue = result.error.issues[0];
    const path = issue?.path.length ? issue.path.join(".") : "root";
    throw new ApiContractError(contract, path, issue?.message ?? "Invalid payload");
  }
  return result.data;
}

export function parseAuthUser(payload: unknown) {
  return parseApiContract(authUserSchema, payload, "authentication user");
}

export function parseMfaChallenge(payload: unknown) {
  return parseApiContract(mfaChallengeSchema, payload, "MFA challenge");
}

export function parseMfaCompletion(payload: unknown) {
  return parseApiContract(mfaCompletionSchema, payload, "MFA completion");
}

export function parseUserAdministrationList(payload: unknown) {
  return parseApiContract(z.array(userAdministrationSummarySchema), payload, "user administration list");
}

export function parseUserAdministrationSummary(payload: unknown) {
  return parseApiContract(userAdministrationSummarySchema, payload, "user administration summary");
}

export function parseUserProvisioningResult(payload: unknown) {
  return parseApiContract(userProvisioningResultSchema, payload, "user provisioning result");
}
