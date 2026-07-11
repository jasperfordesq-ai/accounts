"use client";

import { use, useState, useEffect, useCallback, useMemo } from "react";
import {
  Users,
  Building2,
  Package,
  Landmark,
  Receipt,
  Banknote,
  PiggyBank,
  CreditCard,
  UserCheck,
  CalendarCheck,
  ShieldAlert,
  HeartPulse,
  FileCheck2,
  ShieldCheck,
} from "lucide-react";
import { toast } from "sonner";
import { LoansManager } from "@/components/LoansManager";
import { DirectorLoansManager, type DirectorOption } from "@/components/DirectorLoansManager";
import { useAuth } from "@/components/AuthProvider";
import { YearEndContingentLiabilitiesSection } from "@/components/period/YearEndContingentLiabilitiesSection";
import { YearEndDirectorLoanComplianceSummary } from "@/components/period/YearEndDirectorLoanComplianceSummary";
import { YearEndDividendsSection } from "@/components/period/YearEndDividendsSection";
import { YearEndQuestionnaireHeader } from "@/components/period/YearEndQuestionnaireHeader";
import { YearEndFixedAssetsSection } from "@/components/period/YearEndFixedAssetsSection";
import { YearEndGoingConcernSection } from "@/components/period/YearEndGoingConcernSection";
import { YearEndInventorySection } from "@/components/period/YearEndInventorySection";
import { YearEndMoneyListSection } from "@/components/period/YearEndMoneyListSection";
import { YearEndPayrollSection } from "@/components/period/YearEndPayrollSection";
import { YearEndPostBalanceSheetEventsSection } from "@/components/period/YearEndPostBalanceSheetEventsSection";
import { YearEndQuestionnaireSection as Section } from "@/components/period/YearEndQuestionnaireSection";
import { YearEndRelatedPartyTransactionsSection } from "@/components/period/YearEndRelatedPartyTransactionsSection";
import { YearEndTaxBalancesSection } from "@/components/period/YearEndTaxBalancesSection";
import { CorporationTaxScopeReviewSection } from "@/components/period/CorporationTaxScopeReviewSection";
import { CorporationTaxFilingSupportPanel } from "@/components/period/CorporationTaxFilingSupportPanel";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import { ReadOnlyNotice } from "@/components/workbench";
import {
  getCompany,
  getPeriod,
  getDebtors,
  createDebtor,
  deleteDebtor,
  getCreditors,
  createCreditor,
  deleteCreditor,
  getFixedAssets,
  createFixedAsset,
  deleteFixedAsset,
  getInventory,
  createInventory,
  deleteInventory,
  getPayroll,
  savePayroll,
  getTaxBalances,
  saveTaxBalance,
  getDividends,
  createDividend,
  deleteDividend,
  type Company,
  type AccountingPeriod,
  type Debtor,
  type Creditor,
  type FixedAsset,
  type InventoryItem,
  type PayrollSummary,
  type TaxBalance,
  type Dividend,
  getPostBalanceSheetEvents,
  createPostBalanceSheetEvent,
  deletePostBalanceSheetEvent,
  getRelatedPartyTransactions,
  createRelatedPartyTransaction,
  deleteRelatedPartyTransaction,
  getContingentLiabilities,
  createContingentLiability,
  deleteContingentLiability,
  getGoingConcern,
  saveGoingConcern,
  getDirectorLoanCompliance,
  type PostBalanceSheetEvent,
  type RelatedPartyTransaction,
  type ContingentLiability,
  type DirectorLoanCompliance,
  getYearEndReviewConfirmations,
  saveYearEndReviewConfirmation,
  type YearEndReviewConfirmation,
  getCorporationTaxScopeReview,
  saveCorporationTaxScopeReview,
  type CorporationTaxScopeReviewInput,
  type CorporationTaxScopeReviewResponse,
  getCorporationTaxFilingSupport,
  saveCorporationTaxFilingSupportReview,
  recordCorporationTaxPayment,
  deleteCorporationTaxPayment,
  type CorporationTaxFilingSupportResponse,
  type CorporationTaxFilingSupportReviewInput,
  type CorporationTaxPaymentInput,
} from "@/lib/api";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  canUseResourceAsEvidence,
  completeResourceLoad,
  failResourceLoad,
  loadResourceGroup,
  type ResourceState,
} from "@/lib/resourceState";
import {
  createCreditorDraft,
  createDebtorDraft,
  createDividendDraft,
  createEmptyTaxScopeReview,
  createFixedAssetDraft,
  createInitialTaxForms,
  createInventoryDraft,
  createPayrollDraft,
  taxScopeInputFromReview,
} from "@/lib/yearEndQuestionnaireForms";

const PRINCIPAL_ACTIVITIES_REVIEW_KEY = "directors-report-principal-activities";
const AUDIT_INFORMATION_REVIEW_KEY = "directors-report-audit-information";
const evidenceTextareaClass =
  "mt-1 min-h-28 w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

/* ------------------------------------------------------------------ */
/*  Main page component                                                */
/* ------------------------------------------------------------------ */
export function YearEndQuestionnaireRoute({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);
  const { canWriteWorkingPapers, canReview } = useAuth();

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [shellState, setShellState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [evidenceState, setEvidenceState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);

  // Section data
  const [debtors, setDebtors] = useState<Debtor[]>([]);
  const [creditors, setCreditors] = useState<Creditor[]>([]);
  const [fixedAssets, setFixedAssets] = useState<FixedAsset[]>([]);
  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [payroll, setPayroll] = useState<PayrollSummary | null>(null);
  const [taxBalances, setTaxBalances] = useState<TaxBalance[]>([]);
  const [taxScopeResult, setTaxScopeResult] = useState<CorporationTaxScopeReviewResponse | null>(null);
  const [taxFilingSupport, setTaxFilingSupport] = useState<CorporationTaxFilingSupportResponse | null>(null);
  const [taxFilingSupportDirty, setTaxFilingSupportDirty] = useState(false);
  const [deletingTaxPaymentId, setDeletingTaxPaymentId] = useState<number | null>(null);
  const [taxScopeForm, setTaxScopeForm] = useState<CorporationTaxScopeReviewInput>(createEmptyTaxScopeReview);
  const [savedTaxScopeForm, setSavedTaxScopeForm] = useState<CorporationTaxScopeReviewInput>(createEmptyTaxScopeReview);
  const [dividends, setDividends] = useState<Dividend[]>([]);

  // Form state for adding items
  const [newDebtor, setNewDebtor] = useState<Debtor>(createDebtorDraft);
  const [newCreditor, setNewCreditor] = useState<Creditor>(createCreditorDraft);
  const [newAsset, setNewAsset] = useState<FixedAsset>(createFixedAssetDraft);
  const [newInventoryItem, setNewInventoryItem] = useState<InventoryItem>(createInventoryDraft);
  const [payrollForm, setPayrollForm] = useState<PayrollSummary>(createPayrollDraft);
  const [newDividend, setNewDividend] = useState<Dividend>(createDividendDraft);

  // Tax form state for 3 tax types
  const [taxForms, setTaxForms] = useState<Record<string, TaxBalance>>(createInitialTaxForms);

  // Phase 2: Interrogation data
  const [postBsEvents, setPostBsEvents] = useState<PostBalanceSheetEvent[]>([]);
  const [relatedParties, setRelatedParties] = useState<RelatedPartyTransaction[]>([]);
  const [contingencies, setContingencies] = useState<ContingentLiability[]>([]);
  const [goingConcernConfirmed, setGoingConcernConfirmed] = useState(true);
  const [goingConcernNote, setGoingConcernNote] = useState("");
  const [savedGoingConcern, setSavedGoingConcern] = useState({ confirmed: true, note: "" });
  const [directorLoanCompliance, setDirectorLoanCompliance] = useState<DirectorLoanCompliance | null>(null);
  const [loanCount, setLoanCount] = useState(0);
  const [directorLoanCount, setDirectorLoanCount] = useState(0);
  const [reviewConfirmations, setReviewConfirmations] = useState<Record<string, YearEndReviewConfirmation>>({});
  const [principalActivitiesNarrative, setPrincipalActivitiesNarrative] = useState("");
  const [auditInformationEvidence, setAuditInformationEvidence] = useState("");
  const [loanManagerState, setLoanManagerState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [directorLoanManagerState, setDirectorLoanManagerState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);

  const directorOptions: DirectorOption[] = (company?.officers ?? [])
    .filter((o) => o.role === "Director" && typeof o.id === "number")
    .map((o) => ({ id: o.id as number, name: o.name }));

  const payrollDirty = useMemo(() => {
    const saved = payroll ?? { grossWages: 0, directorsFees: 0, employerPrsi: 0, pensionContributions: 0, staffCount: 0 };
    return payrollForm.grossWages !== saved.grossWages
      || payrollForm.directorsFees !== saved.directorsFees
      || payrollForm.employerPrsi !== saved.employerPrsi
      || payrollForm.pensionContributions !== saved.pensionContributions
      || payrollForm.staffCount !== saved.staffCount;
  }, [payroll, payrollForm]);
  const taxScopeDirty = useMemo(
    () => JSON.stringify(taxScopeForm) !== JSON.stringify(savedTaxScopeForm),
    [savedTaxScopeForm, taxScopeForm],
  );
  const directorsReportEvidenceDirty = useMemo(() => {
    const savedPrincipal = reviewConfirmations[PRINCIPAL_ACTIVITIES_REVIEW_KEY]?.note ?? "";
    const savedAudit = reviewConfirmations[AUDIT_INFORMATION_REVIEW_KEY]?.note ?? "";
    return principalActivitiesNarrative !== savedPrincipal
      || auditInformationEvidence !== savedAudit;
  }, [auditInformationEvidence, principalActivitiesNarrative, reviewConfirmations]);

  const [newPbseDesc, setNewPbseDesc] = useState("");
  const [newPbseDate, setNewPbseDate] = useState("");
  const [newPbseAdjusting, setNewPbseAdjusting] = useState(false);
  const [newPbseImpact, setNewPbseImpact] = useState<number>(0);
  const [newRptName, setNewRptName] = useState("");
  const [newRptRelationship, setNewRptRelationship] = useState("Director");
  const [newRptType, setNewRptType] = useState("Sale");
  const [newRptAmount, setNewRptAmount] = useState<number>(0);
  const [newClDesc, setNewClDesc] = useState("");
  const [newClNature, setNewClNature] = useState("Guarantee");
  const [newClAmount, setNewClAmount] = useState<number>(0);
  const [newClLikelihood, setNewClLikelihood] = useState("Possible");

  const yearEndDraftDirty = useMemo(() => {
    const debtorDraft = newDebtor.name !== "" || newDebtor.amount !== 0 || newDebtor.type !== "Trade";
    const creditorDraft = newCreditor.name !== ""
      || newCreditor.amount !== 0
      || newCreditor.type !== "Trade"
      || newCreditor.dueWithinYear !== true;
    const assetDraft = newAsset.name !== ""
      || newAsset.category !== "Equipment"
      || newAsset.cost !== 0
      || newAsset.residualValue !== 0
      || newAsset.acquisitionDate !== ""
      || newAsset.usefulLifeYears !== 5
      || newAsset.depreciationMethod !== "StraightLine"
      || newAsset.capitalAllowanceTreatment !== "Unreviewed"
      || Boolean(newAsset.capitalAllowanceEvidence);
    const inventoryDraft = newInventoryItem.description !== ""
      || newInventoryItem.value !== 0
      || newInventoryItem.valuationMethod !== "FIFO";
    const dividendDraft = newDividend.amount !== 0
      || newDividend.dateDeclared !== ""
      || newDividend.datePaid !== "";
    const taxDraft = Object.entries(taxForms).some(([taxType, form]) => {
      const saved = taxBalances.find((balance) => balance.taxType === taxType)
        ?? { liability: 0, paid: 0, balance: 0 };
      return form.liability !== saved.liability
        || form.paid !== saved.paid
        || form.balance !== saved.balance;
    });
    const goingConcernDirty = goingConcernConfirmed !== savedGoingConcern.confirmed
      || goingConcernNote !== savedGoingConcern.note;
    const postBalanceSheetDraft = newPbseDesc !== ""
      || newPbseDate !== ""
      || newPbseAdjusting
      || newPbseImpact !== 0;
    const relatedPartyDraft = newRptName !== ""
      || newRptRelationship !== "Director"
      || newRptType !== "Sale"
      || newRptAmount !== 0;
    const contingencyDraft = newClDesc !== ""
      || newClNature !== "Guarantee"
      || newClAmount !== 0
      || newClLikelihood !== "Possible";
    return payrollDirty
      || taxScopeDirty
      || directorsReportEvidenceDirty
      || debtorDraft
      || creditorDraft
      || assetDraft
      || inventoryDraft
      || dividendDraft
      || taxDraft
      || taxFilingSupportDirty
      || goingConcernDirty
      || postBalanceSheetDraft
      || relatedPartyDraft
      || contingencyDraft;
  }, [
    directorsReportEvidenceDirty, goingConcernConfirmed, goingConcernNote, newAsset,
    newClAmount, newClDesc, newClLikelihood, newClNature, newCreditor, newDebtor,
    newDividend, newInventoryItem, newPbseAdjusting, newPbseDate, newPbseDesc,
    newPbseImpact, newRptAmount, newRptName, newRptRelationship, newRptType,
    payrollDirty, savedGoingConcern, taxBalances, taxFilingSupportDirty, taxForms, taxScopeDirty,
  ]);
  useUnsavedChanges(yearEndDraftDirty);

  // Saving indicators
  const [savingSection, setSavingSection] = useState<string | null>(null);
  const [savingReviewKey, setSavingReviewKey] = useState<string | null>(null);

  const loadShell = useCallback(async (onlyKeys?: string[]) => {
    const loaders = {
      company: () => getCompany(cId),
      period: () => getPeriod(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setShellState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (result.values.company) setCompany(result.values.company);
    if (result.values.period) setPeriod(result.values.period);
    if (result.failedResourceKeys.length === 0) setShellState(completeResourceLoad(false));
    else setShellState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId]);

  const loadEvidence = useCallback(async (onlyKeys?: string[]) => {
    const loaders = {
      debtors: () => getDebtors(cId, pId),
      creditors: () => getCreditors(cId, pId),
      "fixed-assets": () => getFixedAssets(cId),
      inventory: () => getInventory(cId, pId),
      payroll: () => getPayroll(cId, pId),
      tax: () => getTaxBalances(cId, pId),
      "tax-scope": () => getCorporationTaxScopeReview(cId, pId),
      "tax-filing-support": () => getCorporationTaxFilingSupport(cId, pId),
      dividends: () => getDividends(cId, pId),
      "review-confirmations": () => getYearEndReviewConfirmations(cId, pId),
      "post-balance-sheet-events": () => getPostBalanceSheetEvents(cId, pId),
      "related-parties": () => getRelatedPartyTransactions(cId, pId),
      "contingent-liabilities": () => getContingentLiabilities(cId, pId),
      "going-concern": () => getGoingConcern(cId, pId),
      "director-loan-compliance": () => getDirectorLoanCompliance(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setEvidenceState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);

    if (result.values.debtors !== undefined) setDebtors(result.values.debtors);
    if (result.values.creditors !== undefined) setCreditors(result.values.creditors);
    if (result.values["fixed-assets"] !== undefined) setFixedAssets(result.values["fixed-assets"]);
    if (result.values.inventory !== undefined) setInventory(result.values.inventory);
    if (result.values.payroll !== undefined) {
      setPayroll(result.values.payroll);
      if (result.values.payroll) setPayrollForm(result.values.payroll);
    }
    if (result.values.tax !== undefined) {
      setTaxBalances(result.values.tax);
      setTaxForms((current) => {
        const next = { ...current };
        for (const balance of result.values.tax ?? []) next[balance.taxType] = balance;
        return next;
      });
    }
    if (result.values["tax-scope"] !== undefined) {
      const response = result.values["tax-scope"];
      setTaxScopeResult(response);
      const next = response.review ? taxScopeInputFromReview(response.review) : createEmptyTaxScopeReview();
      setTaxScopeForm(next);
      setSavedTaxScopeForm(next);
    }
    if (result.values["tax-filing-support"] !== undefined) {
      setTaxFilingSupport(result.values["tax-filing-support"]);
      setTaxFilingSupportDirty(false);
    }
    if (result.values.dividends !== undefined) setDividends(result.values.dividends);
    if (result.values["review-confirmations"] !== undefined) {
      const confirmations = Object.fromEntries(
        result.values["review-confirmations"].map((review) => [review.sectionKey, review]),
      );
      setReviewConfirmations(confirmations);
      setPrincipalActivitiesNarrative(confirmations[PRINCIPAL_ACTIVITIES_REVIEW_KEY]?.note ?? "");
      setAuditInformationEvidence(confirmations[AUDIT_INFORMATION_REVIEW_KEY]?.note ?? "");
    }
    if (result.values["post-balance-sheet-events"] !== undefined) setPostBsEvents(result.values["post-balance-sheet-events"]);
    if (result.values["related-parties"] !== undefined) setRelatedParties(result.values["related-parties"]);
    if (result.values["contingent-liabilities"] !== undefined) setContingencies(result.values["contingent-liabilities"]);
    if (result.values["going-concern"] !== undefined) {
      setGoingConcernConfirmed(result.values["going-concern"].goingConcernConfirmed);
      setGoingConcernNote(result.values["going-concern"].goingConcernNote ?? "");
      setSavedGoingConcern({
        confirmed: result.values["going-concern"].goingConcernConfirmed,
        note: result.values["going-concern"].goingConcernNote ?? "",
      });
    }
    if (result.values["director-loan-compliance"] !== undefined) {
      setDirectorLoanCompliance(result.values["director-loan-compliance"]);
    }

    if (result.failedResourceKeys.length === 0) setEvidenceState(completeResourceLoad(false));
    else setEvidenceState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId]);

  const loadAllData = useCallback(async () => {
    await Promise.all([loadShell(), loadEvidence()]);
  }, [loadEvidence, loadShell]);

  const refreshDirectorLoanCompliance = useCallback(async () => {
    await loadEvidence(Array.from(new Set([
      ...evidenceState.failedResourceKeys,
      "director-loan-compliance",
    ])));
  }, [evidenceState.failedResourceKeys, loadEvidence]);

  useEffect(() => {
    loadAllData();
  }, [loadAllData]);

  const sectionEvidenceUnavailable = (sectionKey: string) => {
    if (sectionKey === "loans" && !canUseResourceAsEvidence(loanManagerState)) return true;
    if (sectionKey === "director-loans" && !canUseResourceAsEvidence(directorLoanManagerState)) return true;
    if (evidenceState.status === "loading" || evidenceState.status === "stale/retrying") return true;
    if (evidenceState.failedResourceKeys.includes("review-confirmations")) return true;
    if (evidenceState.failedResourceKeys.includes(sectionKey)) return true;
    if (sectionKey === "tax" && (
      evidenceState.failedResourceKeys.includes("tax-scope")
      || evidenceState.failedResourceKeys.includes("tax-filing-support")
    )) return true;
    return sectionKey === "director-loans"
      && evidenceState.failedResourceKeys.includes("director-loan-compliance");
  };

  const reviewGuard = (sectionKey: string) => ({
    reviewDisabled: sectionEvidenceUnavailable(sectionKey),
    reviewDisabledReason: "Required evidence did not load. Retry the failed resource before recording or refreshing this professional confirmation.",
  });

  async function handleConfirmReview(sectionKey: string, note?: string) {
    if (!canReview) {
      toast.error("Owner or Reviewer access is required to confirm a professional review.");
      return;
    }
    if (sectionEvidenceUnavailable(sectionKey)) {
      toast.error("Required evidence is unavailable. Retry it before confirming this section.");
      return;
    }
    setSavingReviewKey(sectionKey);
    try {
      const updated = await saveYearEndReviewConfirmation(cId, pId, sectionKey, {
        confirmed: true,
        note,
      });
      setReviewConfirmations((current) => ({ ...current, [sectionKey]: updated }));
      toast.success("Section review confirmed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to confirm section review");
    } finally {
      setSavingReviewKey(null);
    }
  }

  /* ---- Debtor handlers ---- */
  async function handleAddDebtor() {
    if (!newDebtor.name || newDebtor.amount <= 0) return;
    setSavingSection("debtors");
    try {
      const created = await createDebtor(cId, pId, newDebtor);
      setDebtors((prev) => [...prev, created]);
      setNewDebtor(createDebtorDraft());
      toast.success("Debtor added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add debtor");
    }
    setSavingSection(null);
  }

  async function handleDeleteDebtor(id: number) {
    setSavingSection("debtors");
    try {
      await deleteDebtor(cId, pId, id);
      setDebtors((prev) => prev.filter((d) => d.id !== id));
      toast.success("Debtor removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete debtor");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Creditor handlers ---- */
  async function handleAddCreditor() {
    if (!newCreditor.name || newCreditor.amount <= 0) return;
    setSavingSection("creditors");
    try {
      const created = await createCreditor(cId, pId, newCreditor);
      setCreditors((prev) => [...prev, created]);
      setNewCreditor(createCreditorDraft());
      toast.success("Creditor added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add creditor");
    }
    setSavingSection(null);
  }

  async function handleDeleteCreditor(id: number) {
    setSavingSection("creditors");
    try {
      await deleteCreditor(cId, pId, id);
      setCreditors((prev) => prev.filter((c) => c.id !== id));
      toast.success("Creditor removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete creditor");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Fixed Asset handlers ---- */
  async function handleAddAsset() {
    if (!newAsset.name || newAsset.cost <= 0 || !newAsset.acquisitionDate) return;
    setSavingSection("assets");
    try {
      const created = await createFixedAsset(cId, newAsset);
      setFixedAssets((prev) => [...prev, created]);
      setNewAsset(createFixedAssetDraft());
      toast.success("Fixed asset added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add fixed asset");
    }
    setSavingSection(null);
  }

  async function handleDeleteAsset(id: number) {
    setSavingSection("assets");
    try {
      await deleteFixedAsset(cId, id);
      setFixedAssets((prev) => prev.filter((a) => a.id !== id));
      toast.success("Fixed asset removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete fixed asset");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Inventory handlers ---- */
  async function handleAddInventory() {
    if (!newInventoryItem.description || newInventoryItem.value <= 0) return;
    setSavingSection("inventory");
    try {
      const created = await createInventory(cId, pId, newInventoryItem);
      setInventory((prev) => [...prev, created]);
      setNewInventoryItem(createInventoryDraft());
      toast.success("Inventory item added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add inventory item");
    }
    setSavingSection(null);
  }

  async function handleDeleteInventory(id: number) {
    setSavingSection("inventory");
    try {
      await deleteInventory(cId, pId, id);
      setInventory((prev) => prev.filter((i) => i.id !== id));
      toast.success("Inventory item removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete inventory item");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Payroll handler ---- */
  async function handleSavePayroll() {
    setSavingSection("payroll");
    try {
      const saved = await savePayroll(cId, pId, payrollForm);
      setPayroll(saved);
      toast.success("Payroll saved");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save payroll");
    }
    setSavingSection(null);
  }

  /* ---- Tax handler ---- */
  async function handleSaveTax(taxType: string) {
    setSavingSection("tax-" + taxType);
    try {
      const saved = await saveTaxBalance(cId, pId, taxType, taxForms[taxType]);
      setTaxBalances((prev) => {
        const idx = prev.findIndex((t) => t.taxType === taxType);
        if (idx >= 0) {
          const copy = [...prev];
          copy[idx] = saved;
          return copy;
        }
        return [...prev, saved];
      });
      toast.success(`${taxType === "PAYE_PRSI" ? "PAYE/PRSI" : taxType === "CorporationTax" ? "Corporation Tax" : taxType} saved`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : `Failed to save ${taxType}`);
    }
    setSavingSection(null);
  }

  async function handleSaveTaxScope() {
    setSavingSection("tax-scope");
    try {
      const saved = await saveCorporationTaxScopeReview(cId, pId, taxScopeForm);
      setTaxScopeResult(saved);
      const next = saved.review ? taxScopeInputFromReview(saved.review) : taxScopeForm;
      setTaxScopeForm(next);
      setSavedTaxScopeForm(next);
      toast.success(saved.computation?.finalTaxChargeSupported
        ? "Tax scope saved; simple machine checks pass"
        : "Tax scope saved; manual review blockers retained");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save corporation-tax scope");
    }
    setSavingSection(null);
  }

  async function handleSaveTaxFilingSupportReview(input: CorporationTaxFilingSupportReviewInput) {
    setSavingSection("tax-filing-support-review");
    try {
      const saved = await saveCorporationTaxFilingSupportReview(cId, pId, input);
      setTaxFilingSupport(saved);
      setTaxFilingSupportDirty(false);
      if (saved.filingSupport.filingSupportReady) {
        toast.success("Preliminary-tax review saved; machine support checks are clear");
      } else {
        toast.warning("Preliminary-tax review saved with professional-review blockers retained");
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save preliminary-tax review");
    } finally {
      setSavingSection(null);
    }
  }

  async function handleRecordTaxPayment(input: CorporationTaxPaymentInput) {
    setSavingSection("tax-filing-support-payment");
    try {
      const saved = await recordCorporationTaxPayment(cId, pId, input);
      setTaxFilingSupport(saved);
      setTaxFilingSupportDirty(false);
      toast.success("Retained Corporation Tax payment evidence recorded");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to record Corporation Tax payment evidence");
    } finally {
      setSavingSection(null);
    }
  }

  async function handleDeleteTaxPayment(paymentId: number) {
    setDeletingTaxPaymentId(paymentId);
    try {
      const saved = await deleteCorporationTaxPayment(cId, pId, paymentId);
      setTaxFilingSupport(saved);
      toast.success("Incorrect payment-evidence row removed from the tracker");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to correct Corporation Tax payment evidence");
    } finally {
      setDeletingTaxPaymentId(null);
    }
  }

  /* ---- Dividend handlers ---- */
  async function handleAddDividend() {
    if (newDividend.amount <= 0) return;
    setSavingSection("dividends");
    try {
      const created = await createDividend(cId, pId, newDividend);
      setDividends((prev) => [...prev, created]);
      setNewDividend(createDividendDraft());
      toast.success("Dividend added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add dividend");
    }
    setSavingSection(null);
  }

  async function handleDeleteDividend(id: number) {
    setSavingSection("dividends");
    try {
      await deleteDividend(cId, pId, id);
      setDividends((prev) => prev.filter((d) => d.id !== id));
      toast.success("Dividend removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete dividend");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Post-Balance Sheet Event handlers ---- */
  async function handleAddPbse() {
    if (!newPbseDesc || !newPbseDate) return;
    setSavingSection("pbse");
    try {
      const created = await createPostBalanceSheetEvent(cId, pId, {
        description: newPbseDesc,
        eventDate: newPbseDate,
        isAdjusting: newPbseAdjusting,
        financialImpact: newPbseImpact || undefined,
      });
      setPostBsEvents((prev) => [...prev, created]);
      setNewPbseDesc("");
      setNewPbseDate("");
      setNewPbseAdjusting(false);
      setNewPbseImpact(0);
      toast.success("Post-balance sheet event added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add event");
    }
    setSavingSection(null);
  }

  async function handleDeletePbse(id: number) {
    setSavingSection("pbse");
    try {
      await deletePostBalanceSheetEvent(cId, pId, id);
      setPostBsEvents((prev) => prev.filter((e) => e.id !== id));
      toast.success("Event removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete event");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Related Party Transaction handlers ---- */
  async function handleAddRpt() {
    if (!newRptName || newRptAmount <= 0) return;
    setSavingSection("rpt");
    try {
      const created = await createRelatedPartyTransaction(cId, pId, {
        partyName: newRptName,
        relationship: newRptRelationship,
        transactionType: newRptType,
        amount: newRptAmount,
      });
      setRelatedParties((prev) => [...prev, created]);
      setNewRptName("");
      setNewRptRelationship("Director");
      setNewRptType("Sale");
      setNewRptAmount(0);
      toast.success("Related party transaction added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add transaction");
    }
    setSavingSection(null);
  }

  async function handleDeleteRpt(id: number) {
    setSavingSection("rpt");
    try {
      await deleteRelatedPartyTransaction(cId, pId, id);
      setRelatedParties((prev) => prev.filter((r) => r.id !== id));
      toast.success("Transaction removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete transaction");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Contingent Liability handlers ---- */
  async function handleAddContingency() {
    if (!newClDesc) return;
    setSavingSection("contingency");
    try {
      const created = await createContingentLiability(cId, pId, {
        description: newClDesc,
        nature: newClNature,
        estimatedAmount: newClAmount || undefined,
        likelihood: newClLikelihood,
      });
      setContingencies((prev) => [...prev, created]);
      setNewClDesc("");
      setNewClNature("Guarantee");
      setNewClAmount(0);
      setNewClLikelihood("Possible");
      toast.success("Contingent liability added");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add contingency");
    }
    setSavingSection(null);
  }

  async function handleDeleteContingency(id: number) {
    setSavingSection("contingency");
    try {
      await deleteContingentLiability(cId, pId, id);
      setContingencies((prev) => prev.filter((c) => c.id !== id));
      toast.success("Contingency removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete contingency");
      setSavingSection(null);
      throw err;
    }
    setSavingSection(null);
  }

  /* ---- Going Concern handler ---- */
  async function handleSaveGoingConcern() {
    setSavingSection("goingConcern");
    try {
      const result = await saveGoingConcern(cId, pId, {
        confirmed: goingConcernConfirmed,
        note: goingConcernNote || undefined,
      });
      setGoingConcernConfirmed(result.goingConcernConfirmed);
      setGoingConcernNote(result.goingConcernNote ?? "");
      setSavedGoingConcern({
        confirmed: result.goingConcernConfirmed,
        note: result.goingConcernNote ?? "",
      });
      toast.success("Going concern status saved");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save going concern");
    }
    setSavingSection(null);
  }

  /* ---- Completeness tracking ---- */
  const isReviewed = (key: string) => reviewConfirmations[key]?.confirmed === true;
  const sectionIsComplete = (key: string, hasEvidence: boolean) => hasEvidence || isReviewed(key);
  const sectionCompleteness = [
    sectionIsComplete("debtors", debtors.length > 0),
    sectionIsComplete("creditors", creditors.length > 0),
    sectionIsComplete("fixed-assets", fixedAssets.length > 0),
    sectionIsComplete("inventory", inventory.length > 0),
    sectionIsComplete("loans", loanCount > 0),
    sectionIsComplete("director-loans", directorLoanCount > 0),
    sectionIsComplete("payroll", payroll !== null),
    sectionIsComplete("tax", taxBalances.length > 0),
    sectionIsComplete("dividends", dividends.length > 0),
    sectionIsComplete("post-balance-sheet-events", postBsEvents.length > 0),
    sectionIsComplete("related-parties", relatedParties.length > 0),
    sectionIsComplete("contingent-liabilities", contingencies.length > 0),
    sectionIsComplete("going-concern", false),
    sectionIsComplete(PRINCIPAL_ACTIVITIES_REVIEW_KEY, false),
    ...(period?.filingRegime?.auditExempt === false
      ? [sectionIsComplete(AUDIT_INFORMATION_REVIEW_KEY, false)]
      : []),
  ];
  const totalSections = sectionCompleteness.length;
  const completedCount = sectionCompleteness.filter(Boolean).length;

  const periodLabel = period
    ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}`
    : "";

  if (shellState.status === "loading" && !shellState.hasRetainedData) {
    return (
      <div className="max-w-4xl mx-auto">
        <PeriodWorkspaceSkeleton />
      </div>
    );
  }

  if (!company || !period) {
    return (
      <div className="mx-auto max-w-4xl py-8">
        <ResourceStateNotice state={shellState} label="year-end company and period context" onRetry={() => loadShell(shellState.failedResourceKeys)} />
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto animate-fade-in">
      <YearEndQuestionnaireHeader
        companyId={companyId}
        periodId={periodId}
        companyName={company?.legalName ?? "Company"}
        periodLabel={periodLabel}
        backHref={`/companies/${companyId}/periods/${periodId}`}
        completedCount={completedCount}
        totalSections={totalSections}
      />

      <div className="mb-4 space-y-3">
        <ResourceStateNotice state={shellState} label="year-end company and period context" onRetry={() => loadShell(shellState.failedResourceKeys)} />
        <ResourceStateNotice
          state={evidenceState}
          label="year-end review evidence"
          onRetry={() => loadEvidence(evidenceState.failedResourceKeys)}
        />
      </div>

      {!canWriteWorkingPapers && (
        <div className="mb-4">
          <ReadOnlyNotice
            subject="year-end accounting inputs"
            detail={canReview
              ? "You can inspect the retained evidence and record professional review confirmations; editing requires Owner or Accountant access."
              : undefined}
          />
        </div>
      )}

      {/* 9 Sections */}
      <div className="space-y-4">
        {/* 1. Debtors & Prepayments */}
        <Section
          {...reviewGuard("debtors")}
          title="Debtors & Prepayments"
          subtitle="Does anyone owe the company money at year-end?"
          icon={Users}
          completed={debtors.length > 0}
          review={reviewConfirmations["debtors"]}
          reviewSaving={savingReviewKey === "debtors"}
          onConfirmReview={canReview ? () => handleConfirmReview("debtors", debtors.length === 0 ? "Confirmed no year-end debtors, prepayments, or other receivables to disclose." : undefined) : undefined}
        >
          <YearEndMoneyListSection
            canWrite={canWriteWorkingPapers}
            mode="debtors"
            items={debtors}
            draft={newDebtor}
            typeOptions={["Trade", "Other", "Prepayment"]}
            namePlaceholder="Who owes you?"
            saving={savingSection === "debtors"}
            onDraftChange={setNewDebtor}
            onAdd={handleAddDebtor}
            onDelete={handleDeleteDebtor}
          />
        </Section>

        {/* 2. Creditors & Accruals */}
        <Section
          {...reviewGuard("creditors")}
          title="Creditors & Accruals"
          subtitle="Does the company owe anyone money at year-end?"
          icon={CreditCard}
          completed={creditors.length > 0}
          review={reviewConfirmations["creditors"]}
          reviewSaving={savingReviewKey === "creditors"}
          onConfirmReview={canReview ? () => handleConfirmReview("creditors", creditors.length === 0 ? "Confirmed no year-end creditors, accruals, or other payables to disclose." : undefined) : undefined}
        >
          <YearEndMoneyListSection
            canWrite={canWriteWorkingPapers}
            mode="creditors"
            items={creditors}
            draft={newCreditor}
            typeOptions={["Trade", "Other", "Accrual", "Tax"]}
            namePlaceholder="Who do you owe?"
            saving={savingSection === "creditors"}
            onDraftChange={setNewCreditor}
            onAdd={handleAddCreditor}
            onDelete={handleDeleteCreditor}
          />
        </Section>

        {/* 3. Fixed Assets */}
        <Section
          {...reviewGuard("fixed-assets")}
          title="Fixed Assets"
          subtitle="Did you buy or sell equipment, vehicles, or property during the year?"
          icon={Building2}
          completed={fixedAssets.length > 0}
          review={reviewConfirmations["fixed-assets"]}
          reviewSaving={savingReviewKey === "fixed-assets"}
          onConfirmReview={canReview ? () => handleConfirmReview("fixed-assets", fixedAssets.length === 0 ? "Confirmed no fixed assets requiring disclosure or depreciation for this period." : undefined) : undefined}
        >
          <YearEndFixedAssetsSection
            canWrite={canWriteWorkingPapers}
            assets={fixedAssets}
            draft={newAsset}
            saving={savingSection === "assets"}
            onDraftChange={setNewAsset}
            onAdd={handleAddAsset}
            onDelete={handleDeleteAsset}
          />
        </Section>

        {/* 4. Stock & Inventory */}
        <Section
          {...reviewGuard("inventory")}
          title="Stock & Inventory"
          subtitle="Does the company hold stock or work in progress at year-end?"
          icon={Package}
          completed={inventory.length > 0}
          review={reviewConfirmations["inventory"]}
          reviewSaving={savingReviewKey === "inventory"}
          onConfirmReview={canReview ? () => handleConfirmReview("inventory", inventory.length === 0 ? "Confirmed no stock or work in progress at year-end." : undefined) : undefined}
        >
          <YearEndInventorySection
            canWrite={canWriteWorkingPapers}
            items={inventory}
            draft={newInventoryItem}
            saving={savingSection === "inventory"}
            onDraftChange={setNewInventoryItem}
            onAdd={handleAddInventory}
            onDelete={handleDeleteInventory}
          />
        </Section>

        {/* 5. Loans */}
        <Section
          {...reviewGuard("loans")}
          title="Loans & Borrowings"
          subtitle="Does the company have any loans or borrowings outstanding?"
          icon={Landmark}
          completed={loanCount > 0}
          review={reviewConfirmations["loans"]}
          reviewSaving={savingReviewKey === "loans"}
          onConfirmReview={canReview ? () => handleConfirmReview("loans", loanCount === 0 ? "Confirmed the company has no loans or borrowings outstanding at year-end." : undefined) : undefined}
        >
          <LoansManager
            companyId={cId}
            periodEnd={period?.periodEnd}
            canWrite={canWriteWorkingPapers}
            onCountChange={setLoanCount}
            onResourceStateChange={setLoanManagerState}
          />
        </Section>

        {/* 6. Payroll */}
        <Section
          {...reviewGuard("payroll")}
          title="Payroll"
          subtitle="How many staff does the company employ? What are the total wages?"
          icon={Receipt}
          completed={payroll !== null}
          review={reviewConfirmations["payroll"]}
          reviewSaving={savingReviewKey === "payroll"}
          onConfirmReview={canReview ? () => handleConfirmReview("payroll", payroll === null ? "Confirmed no payroll or staff costs for this period." : undefined) : undefined}
        >
          <YearEndPayrollSection
            canWrite={canWriteWorkingPapers}
            form={payrollForm}
            saving={savingSection === "payroll"}
            onFormChange={setPayrollForm}
            onSave={handleSavePayroll}
          />
        </Section>

        {/* 7. Tax */}
        <Section
          {...reviewGuard("tax")}
          title="Tax Balances"
          subtitle="Corporation Tax, VAT, and PAYE/PRSI balances at year-end"
          icon={Banknote}
          completed={taxBalances.length > 0}
          review={reviewConfirmations["tax"]}
          reviewSaving={savingReviewKey === "tax"}
          onConfirmReview={canReview ? () => handleConfirmReview("tax", taxBalances.length === 0 ? "Confirmed no tax creditor/debtor balances requiring recognition at year-end." : undefined) : undefined}
        >
          <YearEndTaxBalancesSection
            canWrite={canWriteWorkingPapers}
            forms={taxForms}
            savingKey={savingSection}
            onFormChange={(taxType, balance) =>
              setTaxForms((prev) => ({ ...prev, [taxType]: balance }))
            }
            onSave={handleSaveTax}
          />
          <CorporationTaxScopeReviewSection
            canWrite={canWriteWorkingPapers}
            form={taxScopeForm}
            result={taxScopeResult}
            saving={savingSection === "tax-scope"}
            onFormChange={setTaxScopeForm}
            onSave={handleSaveTaxScope}
          />
          <div className="mt-6 border-t border-gray-200 pt-5 dark:border-neutral-700">
            <CorporationTaxFilingSupportPanel
              companyId={cId}
              periodId={pId}
              response={taxFilingSupport}
              canWrite={canWriteWorkingPapers}
              savingReview={savingSection === "tax-filing-support-review"}
              savingPayment={savingSection === "tax-filing-support-payment"}
              deletingPaymentId={deletingTaxPaymentId}
              onSaveReview={handleSaveTaxFilingSupportReview}
              onRecordPayment={handleRecordTaxPayment}
              onDeletePayment={handleDeleteTaxPayment}
              onDirtyChange={setTaxFilingSupportDirty}
            />
          </div>
        </Section>

        {/* 8. Dividends */}
        <Section
          {...reviewGuard("dividends")}
          title="Dividends"
          subtitle="Were any dividends declared or paid during the year?"
          icon={PiggyBank}
          completed={dividends.length > 0}
          review={reviewConfirmations["dividends"]}
          reviewSaving={savingReviewKey === "dividends"}
          onConfirmReview={canReview ? () => handleConfirmReview("dividends", dividends.length === 0 ? "Confirmed no dividends were declared or paid during the period." : undefined) : undefined}
        >
          <YearEndDividendsSection
            canWrite={canWriteWorkingPapers}
            dividends={dividends}
            draft={newDividend}
            saving={savingSection === "dividends"}
            onDraftChange={setNewDividend}
            onAdd={handleAddDividend}
            onDelete={handleDeleteDividend}
          />
        </Section>

        {/* 9. Director Loans */}
        <Section
          {...reviewGuard("director-loans")}
          title="Director Loans"
          subtitle="Are there any loans between directors and the company?"
          icon={UserCheck}
          completed={directorLoanCount > 0}
          review={reviewConfirmations["director-loans"]}
          reviewSaving={savingReviewKey === "director-loans"}
          onConfirmReview={canReview ? () => handleConfirmReview("director-loans", directorLoanCount === 0 ? "Confirmed there are no loans between the directors and the company in the period." : undefined) : undefined}
        >
          <div className="space-y-4">
            <YearEndDirectorLoanComplianceSummary compliance={directorLoanCompliance} />

            <DirectorLoansManager
              companyId={cId}
              periodId={pId}
              directors={directorOptions}
              canWrite={canWriteWorkingPapers}
              onCountChange={setDirectorLoanCount}
              onSaved={refreshDirectorLoanCompliance}
              onResourceStateChange={setDirectorLoanManagerState}
            />
          </div>
        </Section>

        {/* 10. Post-Balance Sheet Events */}
        <Section
          {...reviewGuard("post-balance-sheet-events")}
          title="Post-Balance Sheet Events"
          subtitle="Has anything significant happened between year-end and today?"
          icon={CalendarCheck}
          completed={postBsEvents.length > 0}
          review={reviewConfirmations["post-balance-sheet-events"]}
          reviewSaving={savingReviewKey === "post-balance-sheet-events"}
          onConfirmReview={canReview ? () => handleConfirmReview("post-balance-sheet-events", postBsEvents.length === 0 ? "Confirmed no adjusting or material non-adjusting post balance sheet events identified." : undefined) : undefined}
        >
          <YearEndPostBalanceSheetEventsSection
            canWrite={canWriteWorkingPapers}
            events={postBsEvents}
            draft={{
              description: newPbseDesc,
              eventDate: newPbseDate,
              isAdjusting: newPbseAdjusting,
              financialImpact: newPbseImpact,
            }}
            saving={savingSection === "pbse"}
            onDraftChange={(draft) => {
              setNewPbseDesc(draft.description);
              setNewPbseDate(draft.eventDate);
              setNewPbseAdjusting(draft.isAdjusting);
              setNewPbseImpact(draft.financialImpact ?? 0);
            }}
            onAdd={handleAddPbse}
            onDelete={handleDeletePbse}
          />
        </Section>

        {/* 11. Related Party Transactions */}
        <Section
          {...reviewGuard("related-parties")}
          title="Related Party Transactions"
          subtitle="Were there any transactions with directors, connected persons, or group companies?"
          icon={Users}
          completed={relatedParties.length > 0}
          review={reviewConfirmations["related-parties"]}
          reviewSaving={savingReviewKey === "related-parties"}
          onConfirmReview={canReview ? () => handleConfirmReview("related-parties", relatedParties.length === 0 ? "Confirmed no related party transactions requiring disclosure were identified." : undefined) : undefined}
        >
          <YearEndRelatedPartyTransactionsSection
            canWrite={canWriteWorkingPapers}
            transactions={relatedParties}
            draft={{
              partyName: newRptName,
              relationship: newRptRelationship,
              transactionType: newRptType,
              amount: newRptAmount,
            }}
            saving={savingSection === "rpt"}
            onDraftChange={(draft) => {
              setNewRptName(draft.partyName);
              setNewRptRelationship(draft.relationship);
              setNewRptType(draft.transactionType);
              setNewRptAmount(draft.amount);
            }}
            onAdd={handleAddRpt}
            onDelete={handleDeleteRpt}
          />
        </Section>

        {/* 12. Contingent Liabilities */}
        <Section
          {...reviewGuard("contingent-liabilities")}
          title="Contingent Liabilities"
          subtitle="Are there any potential liabilities that depend on the outcome of uncertain future events?"
          icon={ShieldAlert}
          completed={contingencies.length > 0}
          review={reviewConfirmations["contingent-liabilities"]}
          reviewSaving={savingReviewKey === "contingent-liabilities"}
          onConfirmReview={canReview ? () => handleConfirmReview("contingent-liabilities", contingencies.length === 0 ? "Confirmed no contingent liabilities requiring disclosure were identified." : undefined) : undefined}
        >
          <YearEndContingentLiabilitiesSection
            canWrite={canWriteWorkingPapers}
            contingencies={contingencies}
            draft={{
              description: newClDesc,
              nature: newClNature,
              estimatedAmount: newClAmount,
              likelihood: newClLikelihood,
            }}
            saving={savingSection === "contingency"}
            onDraftChange={(draft) => {
              setNewClDesc(draft.description);
              setNewClNature(draft.nature);
              setNewClAmount(draft.estimatedAmount ?? 0);
              setNewClLikelihood(draft.likelihood);
            }}
            onAdd={handleAddContingency}
            onDelete={handleDeleteContingency}
          />
        </Section>

        {/* 13. Going Concern */}
        <Section
          {...reviewGuard("going-concern")}
          title="Going Concern"
          subtitle="Do the directors confirm the company will continue in business for at least 12 months?"
          icon={HeartPulse}
          completed={false}
          review={reviewConfirmations["going-concern"]}
          reviewSaving={savingReviewKey === "going-concern"}
          onConfirmReview={canReview ? () => handleConfirmReview("going-concern", goingConcernConfirmed ? "Directors' going concern assessment reviewed." : goingConcernNote || "Going concern uncertainty noted for review.") : undefined}
        >
          <YearEndGoingConcernSection
            canWrite={canWriteWorkingPapers}
            confirmed={goingConcernConfirmed}
            note={goingConcernNote}
            saving={savingSection === "goingConcern"}
            onConfirmedChange={setGoingConcernConfirmed}
            onNoteChange={setGoingConcernNote}
            onSave={handleSaveGoingConcern}
          />
        </Section>

        {/* Directors' report evidence */}
        <Section
          {...reviewGuard(PRINCIPAL_ACTIVITIES_REVIEW_KEY)}
          title="Directors' Report - Principal Activities"
          subtitle="Retain the exact directors-approved narrative; the platform will not infer this representation from a trading flag."
          icon={FileCheck2}
          completed={isReviewed(PRINCIPAL_ACTIVITIES_REVIEW_KEY)}
          review={reviewConfirmations[PRINCIPAL_ACTIVITIES_REVIEW_KEY]}
          reviewSaving={savingReviewKey === PRINCIPAL_ACTIVITIES_REVIEW_KEY}
          onConfirmReview={canReview && principalActivitiesNarrative.trim().length >= 20
            ? () => handleConfirmReview(PRINCIPAL_ACTIVITIES_REVIEW_KEY, principalActivitiesNarrative.trim())
            : undefined}
        >
          <label htmlFor="directors-report-principal-activities" className="block text-sm font-medium text-gray-800 dark:text-gray-200">
            Approved principal-activities narrative
          </label>
          <p id="directors-report-principal-activities-help" className="mt-1 text-xs text-gray-500 dark:text-gray-400">
            Use the wording approved by the directors for this reporting period (minimum 20 characters).
          </p>
          <textarea
            id="directors-report-principal-activities"
            aria-describedby="directors-report-principal-activities-help"
            className={evidenceTextareaClass}
            value={principalActivitiesNarrative}
            onChange={(event) => setPrincipalActivitiesNarrative(event.target.value)}
            rows={4}
            maxLength={4_000}
            readOnly={!canReview}
            required
          />
          {!isReviewed(PRINCIPAL_ACTIVITIES_REVIEW_KEY) && principalActivitiesNarrative.trim().length < 20 && (
            <p role="status" className="mt-2 text-sm text-amber-700 dark:text-amber-300">
              Enter at least 20 characters before recording the professional review.
            </p>
          )}
        </Section>

        {period.filingRegime?.auditExempt === false && (
          <Section
            {...reviewGuard(AUDIT_INFORMATION_REVIEW_KEY)}
            title="Directors' Report - Relevant Audit Information"
            subtitle="Record the enquiries and retained evidence supporting the Companies Act section 330 statement."
            icon={ShieldCheck}
            completed={isReviewed(AUDIT_INFORMATION_REVIEW_KEY)}
            review={reviewConfirmations[AUDIT_INFORMATION_REVIEW_KEY]}
            reviewSaving={savingReviewKey === AUDIT_INFORMATION_REVIEW_KEY}
            onConfirmReview={canReview && auditInformationEvidence.trim().length >= 20
              ? () => handleConfirmReview(AUDIT_INFORMATION_REVIEW_KEY, auditInformationEvidence.trim())
              : undefined}
          >
            <label htmlFor="directors-report-audit-information" className="block text-sm font-medium text-gray-800 dark:text-gray-200">
              Director confirmation and evidence reference
            </label>
            <p id="directors-report-audit-information-help" className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              Identify the director confirmations, enquiries and retained working-paper or board-minute reference (minimum 20 characters).
            </p>
            <textarea
              id="directors-report-audit-information"
              aria-describedby="directors-report-audit-information-help"
              className={evidenceTextareaClass}
              value={auditInformationEvidence}
              onChange={(event) => setAuditInformationEvidence(event.target.value)}
              rows={4}
              maxLength={4_000}
              readOnly={!canReview}
              required
            />
            {!isReviewed(AUDIT_INFORMATION_REVIEW_KEY) && auditInformationEvidence.trim().length < 20 && (
              <p role="status" className="mt-2 text-sm text-amber-700 dark:text-amber-300">
                Record the supporting evidence before the statutory wording can be emitted.
              </p>
            )}
          </Section>
        )}
      </div>
    </div>
  );
}
