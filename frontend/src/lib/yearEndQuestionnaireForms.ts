import type {
  CorporationTaxScopeReviewInput,
  CorporationTaxScopeReviewResponse,
  Creditor,
  Debtor,
  Dividend,
  FixedAsset,
  InventoryItem,
  PayrollSummary,
  TaxBalance,
} from "@/lib/api";

export function createEmptyTaxScopeReview(): CorporationTaxScopeReviewInput {
  return {
    isCloseCompany: null,
    isServiceCompany: null,
    hasGroupOrConsortiumRelief: false,
    hasChargeableGains: false,
    hasForeignIncomeOrTaxCredits: false,
    hasExceptedTrade: false,
    hasOtherReliefsOrSpecialRegimes: false,
    declaredPassiveIncomePresent: false,
    passiveIncomeClassificationReviewed: false,
    lossTreatment: "Unreviewed",
    broughtForwardTradingLoss: 0,
    broughtForwardLossEvidence: "",
    evidenceNote: "",
  };
}

export function createDebtorDraft(): Debtor {
  return { name: "", amount: 0, type: "Trade" };
}

export function createCreditorDraft(): Creditor {
  return { name: "", amount: 0, type: "Trade", dueWithinYear: true };
}

export function createFixedAssetDraft(): FixedAsset {
  return {
    name: "",
    category: "Equipment",
    cost: 0,
    residualValue: 0,
    acquisitionDate: "",
    usefulLifeYears: 5,
    depreciationMethod: "StraightLine",
    capitalAllowanceTreatment: "Unreviewed",
  };
}

export function createInventoryDraft(): InventoryItem {
  return { description: "", value: 0, valuationMethod: "FIFO" };
}

export function createPayrollDraft(): PayrollSummary {
  return { grossWages: 0, directorsFees: 0, employerPrsi: 0, pensionContributions: 0, staffCount: 0 };
}

export function createDividendDraft(): Dividend {
  return { amount: 0, dateDeclared: "", datePaid: "" };
}

export function createInitialTaxForms(): Record<string, TaxBalance> {
  return {
    CorporationTax: { taxType: "CorporationTax", liability: 0, paid: 0, balance: 0 },
    VAT: { taxType: "VAT", liability: 0, paid: 0, balance: 0 },
    PAYE_PRSI: { taxType: "PAYE_PRSI", liability: 0, paid: 0, balance: 0 },
  };
}

export function taxScopeInputFromReview(
  review: NonNullable<CorporationTaxScopeReviewResponse["review"]>,
): CorporationTaxScopeReviewInput {
  return {
    isCloseCompany: review.isCloseCompany,
    isServiceCompany: review.isServiceCompany,
    hasGroupOrConsortiumRelief: review.hasGroupOrConsortiumRelief,
    hasChargeableGains: review.hasChargeableGains,
    hasForeignIncomeOrTaxCredits: review.hasForeignIncomeOrTaxCredits,
    hasExceptedTrade: review.hasExceptedTrade,
    hasOtherReliefsOrSpecialRegimes: review.hasOtherReliefsOrSpecialRegimes,
    declaredPassiveIncomePresent: review.declaredPassiveIncomePresent,
    passiveIncomeClassificationReviewed: review.passiveIncomeClassificationReviewed,
    lossTreatment: review.lossTreatment,
    broughtForwardTradingLoss: review.broughtForwardTradingLoss,
    broughtForwardLossEvidence: review.broughtForwardLossEvidence ?? "",
    evidenceNote: review.evidenceNote,
  };
}
