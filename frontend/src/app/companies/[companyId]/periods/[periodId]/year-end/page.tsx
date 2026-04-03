"use client";

import { use, useState, useEffect, useCallback } from "react";
import Link from "next/link";
import {
  Button,
  Card,
  Chip,
  Spinner,
} from "@heroui/react";
import {
  ChevronDown,
  ChevronRight,
  Plus,
  Trash2,
  ArrowLeft,
  CheckCircle2,
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
} from "lucide-react";
import { toast } from "sonner";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
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
} from "@/lib/api";

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}

/* ------------------------------------------------------------------ */
/*  Section wrapper -- collapsible card                                */
/* ------------------------------------------------------------------ */
function Section({
  title,
  subtitle,
  icon: Icon,
  completed,
  children,
  defaultOpen = false,
}: {
  title: string;
  subtitle: string;
  icon: React.ComponentType<{ className?: string }>;
  completed: boolean;
  children: React.ReactNode;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 dark:bg-neutral-900">
      <button
        type="button"
        className="w-full text-left px-6 py-4 flex items-center gap-4 hover:bg-gray-50/50 dark:hover:bg-neutral-800/50 transition-colors"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-label={`${open ? "Collapse" : "Expand"} ${title} section`}
      >
        <div className="w-10 h-10 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 flex items-center justify-center shrink-0">
          <Icon className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">{title}</h3>
            {completed && (
              <CheckCircle2 className="w-4 h-4 text-emerald-500" />
            )}
          </div>
          <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">{subtitle}</p>
        </div>
        {open ? (
          <ChevronDown className="w-5 h-5 text-gray-400 dark:text-gray-500 shrink-0" />
        ) : (
          <ChevronRight className="w-5 h-5 text-gray-400 dark:text-gray-500 shrink-0" />
        )}
      </button>
      {open && (
        <div className="animate-slide-down px-6 pb-6 border-t border-gray-100 dark:border-neutral-700 pt-4">
          {children}
        </div>
      )}
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Main page component                                                */
/* ------------------------------------------------------------------ */
export default function YearEndQuestionnairePage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [loading, setLoading] = useState(true);

  // Section data
  const [debtors, setDebtors] = useState<Debtor[]>([]);
  const [creditors, setCreditors] = useState<Creditor[]>([]);
  const [fixedAssets, setFixedAssets] = useState<FixedAsset[]>([]);
  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [payroll, setPayroll] = useState<PayrollSummary | null>(null);
  const [taxBalances, setTaxBalances] = useState<TaxBalance[]>([]);
  const [dividends, setDividends] = useState<Dividend[]>([]);

  // Form state for adding items
  const [newDebtor, setNewDebtor] = useState<Debtor>({ name: "", amount: 0, type: "Trade" });
  const [newCreditor, setNewCreditor] = useState<Creditor>({ name: "", amount: 0, type: "Trade", dueWithinYear: true });
  const [newAsset, setNewAsset] = useState<FixedAsset>({ name: "", category: "Equipment", cost: 0, acquisitionDate: "", usefulLifeYears: 5, depreciationMethod: "StraightLine" });
  const [newInventoryItem, setNewInventoryItem] = useState<InventoryItem>({ description: "", value: 0, valuationMethod: "FIFO" });
  const [payrollForm, setPayrollForm] = useState<PayrollSummary>({ grossWages: 0, employerPrsi: 0, pensionContributions: 0, staffCount: 0 });
  const [newDividend, setNewDividend] = useState<Dividend>({ amount: 0, dateDeclared: "", datePaid: "" });

  // Tax form state for 3 tax types
  const [taxForms, setTaxForms] = useState<Record<string, TaxBalance>>({
    "CorporationTax": { taxType: "CorporationTax", liability: 0, paid: 0, balance: 0 },
    "VAT": { taxType: "VAT", liability: 0, paid: 0, balance: 0 },
    "PAYE_PRSI": { taxType: "PAYE_PRSI", liability: 0, paid: 0, balance: 0 },
  });

  // Phase 2: Interrogation data
  const [postBsEvents, setPostBsEvents] = useState<PostBalanceSheetEvent[]>([]);
  const [relatedParties, setRelatedParties] = useState<RelatedPartyTransaction[]>([]);
  const [contingencies, setContingencies] = useState<ContingentLiability[]>([]);
  const [goingConcernConfirmed, setGoingConcernConfirmed] = useState(true);
  const [goingConcernNote, setGoingConcernNote] = useState("");
  const [directorLoanCompliance, setDirectorLoanCompliance] = useState<DirectorLoanCompliance | null>(null);

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

  // Saving indicators
  const [savingSection, setSavingSection] = useState<string | null>(null);

  const loadAllData = useCallback(async () => {
    setLoading(true);
    try {
      const [companyData, periodData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);

      // Load all section data in parallel
      const [
        debtorsData,
        creditorsData,
        assetsData,
        inventoryData,
        payrollData,
        taxData,
        dividendsData,
      ] = await Promise.all([
        getDebtors(cId, pId).catch(() => []),
        getCreditors(cId, pId).catch(() => []),
        getFixedAssets(cId).catch(() => []),
        getInventory(cId, pId).catch(() => []),
        getPayroll(cId, pId),
        getTaxBalances(cId, pId).catch(() => []),
        getDividends(cId, pId).catch(() => []),
      ]);

      setDebtors(debtorsData);
      setCreditors(creditorsData);
      setFixedAssets(assetsData);
      setInventory(inventoryData);
      setPayroll(payrollData);
      setDividends(dividendsData);

      // Phase 2: Interrogation data
      try { const pbse = await getPostBalanceSheetEvents(cId, pId); setPostBsEvents(pbse); } catch {}
      try { const rpt = await getRelatedPartyTransactions(cId, pId); setRelatedParties(rpt); } catch {}
      try { const cl = await getContingentLiabilities(cId, pId); setContingencies(cl); } catch {}
      try { const gc = await getGoingConcern(cId, pId); setGoingConcernConfirmed(gc.goingConcernConfirmed); setGoingConcernNote(gc.goingConcernNote ?? ""); } catch {}
      try { const dlc = await getDirectorLoanCompliance(cId, pId); setDirectorLoanCompliance(dlc); } catch {}

      if (payrollData) {
        setPayrollForm(payrollData);
      }

      // Populate tax forms from loaded data
      if (taxData.length > 0) {
        const newTaxForms = { ...taxForms };
        for (const tb of taxData) {
          newTaxForms[tb.taxType] = tb;
        }
        setTaxForms(newTaxForms);
      }
      setTaxBalances(taxData);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load year-end data");
    } finally {
      setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cId, pId]);

  useEffect(() => {
    loadAllData();
  }, [loadAllData]);

  /* ---- Debtor handlers ---- */
  async function handleAddDebtor() {
    if (!newDebtor.name || newDebtor.amount <= 0) return;
    setSavingSection("debtors");
    try {
      const created = await createDebtor(cId, pId, newDebtor);
      setDebtors((prev) => [...prev, created]);
      setNewDebtor({ name: "", amount: 0, type: "Trade" });
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
      setNewCreditor({ name: "", amount: 0, type: "Trade", dueWithinYear: true });
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
      setNewAsset({ name: "", category: "Equipment", cost: 0, acquisitionDate: "", usefulLifeYears: 5, depreciationMethod: "StraightLine" });
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
      setNewInventoryItem({ description: "", value: 0, valuationMethod: "FIFO" });
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

  /* ---- Dividend handlers ---- */
  async function handleAddDividend() {
    if (newDividend.amount <= 0) return;
    setSavingSection("dividends");
    try {
      const created = await createDividend(cId, pId, newDividend);
      setDividends((prev) => [...prev, created]);
      setNewDividend({ amount: 0, dateDeclared: "", datePaid: "" });
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
      toast.success("Going concern status saved");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save going concern");
    }
    setSavingSection(null);
  }

  /* ---- Completeness tracking ---- */
  const sectionCompleteness = [
    debtors.length > 0,
    creditors.length > 0,
    fixedAssets.length > 0,
    inventory.length > 0,
    true, // loans placeholder
    payroll !== null,
    taxBalances.length > 0,
    dividends.length > 0,
    directorLoanCompliance !== null, // director loans
    postBsEvents.length > 0 || true, // post-BS events (optional — always counts)
    relatedParties.length > 0 || true, // related parties (optional — always counts)
    contingencies.length > 0 || true, // contingencies (optional — always counts)
    true, // going concern (always answered)
  ];
  const totalSections = sectionCompleteness.length;
  const completedCount = sectionCompleteness.filter(Boolean).length;

  const periodLabel = period
    ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}`
    : "";

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto">
        <PeriodWorkspaceSkeleton />
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto animate-fade-in">
      {/* Breadcrumbs */}
      <Breadcrumbs
        items={[
          { label: company?.legalName ?? "Company", href: `/companies/${companyId}` },
          { label: periodLabel, href: `/companies/${companyId}/periods/${periodId}` },
          { label: "Year-End" },
        ]}
      />

      {/* Header */}
      <div className="mb-6">
        <Link
          href={`/companies/${companyId}/periods/${periodId}`}
          className="inline-flex items-center gap-1.5 text-sm text-emerald-700 hover:text-emerald-800 dark:text-emerald-400 dark:hover:text-emerald-300 mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Year-End Questionnaire
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
          {company?.legalName ?? "Company"} &mdash;{" "}
          {periodLabel}
        </p>
      </div>

      {/* Progress */}
      <div className="mb-6">
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-300">
                Progress
              </span>
              <Chip
                color={completedCount >= totalSections - 2 ? "success" : completedCount >= Math.floor(totalSections / 2) ? "warning" : "default"}
                variant="soft"
                size="sm"
              >
                {completedCount} of {totalSections} sections completed
              </Chip>
            </div>
            <div className="w-full bg-gray-200 dark:bg-neutral-700 rounded-full h-2.5">
              <div
                className="bg-emerald-500 h-2.5 rounded-full transition-all"
                style={{ width: `${Math.round((completedCount / totalSections) * 100)}%` }}
              />
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* 9 Sections */}
      <div className="space-y-4">
        {/* 1. Debtors & Prepayments */}
        <Section
          title="Debtors & Prepayments"
          subtitle="Does anyone owe the company money at year-end?"
          icon={Users}
          completed={debtors.length > 0}
        >
          {/* Existing items */}
          {debtors.length > 0 && (
            <div className="space-y-2 mb-4">
              {debtors.map((d) => (
                <div
                  key={d.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{d.name}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <Chip variant="soft" size="sm" color="default">{d.type}</Chip>
                      {d.notes && <span className="text-xs text-gray-400 dark:text-gray-500">{d.notes}</span>}
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatCurrency(d.amount)}
                    </span>
                    <button
                      type="button"
                      onClick={() => d.id && handleDeleteDebtor(d.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete debtor ${d.name}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* Add form */}
          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-4">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Name</label>
              <input
                type="text"
                className={inputClass}
                placeholder="Who owes you?"
                value={newDebtor.name}
                onChange={(e) => setNewDebtor({ ...newDebtor, name: e.target.value })}
                aria-label="Debtor name"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newDebtor.amount || ""}
                onChange={(e) => setNewDebtor({ ...newDebtor, amount: Number(e.target.value) })}
                aria-label="Debtor amount"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Type</label>
              <select
                className={selectClass}
                value={newDebtor.type}
                onChange={(e) => setNewDebtor({ ...newDebtor, type: e.target.value })}
                title="Debtor type"
                aria-label="Debtor type"
              >
                <option value="Trade">Trade</option>
                <option value="Other">Other</option>
                <option value="Prepayment">Prepayment</option>
              </select>
            </div>
            <div className="col-span-2">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddDebtor}
                isDisabled={savingSection === "debtors"}
                className="w-full"
              >
                {savingSection === "debtors" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 2. Creditors & Accruals */}
        <Section
          title="Creditors & Accruals"
          subtitle="Does the company owe anyone money at year-end?"
          icon={CreditCard}
          completed={creditors.length > 0}
        >
          {creditors.length > 0 && (
            <div className="space-y-2 mb-4">
              {creditors.map((c) => (
                <div
                  key={c.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{c.name}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <Chip variant="soft" size="sm" color="default">{c.type}</Chip>
                      <Chip variant="soft" size="sm" color={c.dueWithinYear ? "warning" : "default"}>
                        {c.dueWithinYear ? "Due < 1 year" : "Due > 1 year"}
                      </Chip>
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatCurrency(c.amount)}
                    </span>
                    <button
                      type="button"
                      onClick={() => c.id && handleDeleteCreditor(c.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete creditor ${c.name}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Name</label>
              <input
                type="text"
                className={inputClass}
                placeholder="Who do you owe?"
                value={newCreditor.name}
                onChange={(e) => setNewCreditor({ ...newCreditor, name: e.target.value })}
                aria-label="Creditor name"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newCreditor.amount || ""}
                onChange={(e) => setNewCreditor({ ...newCreditor, amount: Number(e.target.value) })}
                aria-label="Creditor amount"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Type</label>
              <select
                className={selectClass}
                value={newCreditor.type}
                onChange={(e) => setNewCreditor({ ...newCreditor, type: e.target.value })}
                title="Creditor type"
                aria-label="Creditor type"
              >
                <option value="Trade">Trade</option>
                <option value="Other">Other</option>
                <option value="Accrual">Accrual</option>
                <option value="Tax">Tax</option>
              </select>
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Due within year?</label>
              <select
                className={selectClass}
                value={newCreditor.dueWithinYear ? "yes" : "no"}
                onChange={(e) => setNewCreditor({ ...newCreditor, dueWithinYear: e.target.value === "yes" })}
                title="Due within one year"
                aria-label="Due within one year"
              >
                <option value="yes">Yes</option>
                <option value="no">No</option>
              </select>
            </div>
            <div className="col-span-2">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddCreditor}
                isDisabled={savingSection === "creditors"}
                className="w-full"
              >
                {savingSection === "creditors" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 3. Fixed Assets */}
        <Section
          title="Fixed Assets"
          subtitle="Did you buy or sell equipment, vehicles, or property during the year?"
          icon={Building2}
          completed={fixedAssets.length > 0}
        >
          {fixedAssets.length > 0 && (
            <div className="space-y-2 mb-4">
              {fixedAssets.map((a) => (
                <div
                  key={a.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{a.name}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <Chip variant="soft" size="sm" color="default">{a.category}</Chip>
                      <span className="text-xs text-gray-400 dark:text-gray-500">
                        {a.usefulLifeYears}yr {a.depreciationMethod}
                      </span>
                      <span className="text-xs text-gray-400 dark:text-gray-500">
                        Acquired {new Date(a.acquisitionDate).toLocaleDateString("en-IE")}
                      </span>
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatCurrency(a.cost)}
                    </span>
                    <button
                      type="button"
                      onClick={() => a.id && handleDeleteAsset(a.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete asset ${a.name}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="space-y-3">
            <div className="grid grid-cols-12 gap-3 items-end">
              <div className="col-span-4">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Asset Name</label>
                <input
                  type="text"
                  className={inputClass}
                  placeholder="e.g. Company Van"
                  value={newAsset.name}
                  onChange={(e) => setNewAsset({ ...newAsset, name: e.target.value })}
                  aria-label="Asset name"
                />
              </div>
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Category</label>
                <select
                  className={selectClass}
                  value={newAsset.category}
                  onChange={(e) => setNewAsset({ ...newAsset, category: e.target.value })}
                  title="Asset category"
                  aria-label="Asset category"
                >
                  <option value="Equipment">Equipment</option>
                  <option value="Vehicles">Vehicles</option>
                  <option value="Property">Property</option>
                  <option value="Furniture">Furniture &amp; Fixtures</option>
                  <option value="IT">IT Equipment</option>
                  <option value="Other">Other</option>
                </select>
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Cost</label>
                <input
                  type="number"
                  className={inputClass}
                  placeholder="0.00"
                  value={newAsset.cost || ""}
                  onChange={(e) => setNewAsset({ ...newAsset, cost: Number(e.target.value) })}
                  aria-label="Asset cost"
                />
              </div>
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Acquisition Date</label>
                <input
                  type="date"
                  className={inputClass}
                  value={newAsset.acquisitionDate}
                  onChange={(e) => setNewAsset({ ...newAsset, acquisitionDate: e.target.value })}
                  aria-label="Asset acquisition date"
                />
              </div>
            </div>
            <div className="grid grid-cols-12 gap-3 items-end">
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Useful Life (years)</label>
                <input
                  type="number"
                  className={inputClass}
                  value={newAsset.usefulLifeYears}
                  onChange={(e) => setNewAsset({ ...newAsset, usefulLifeYears: Number(e.target.value) })}
                  aria-label="Useful life in years"
                />
              </div>
              <div className="col-span-4">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Depreciation Method</label>
                <select
                  className={selectClass}
                  value={newAsset.depreciationMethod}
                  onChange={(e) => setNewAsset({ ...newAsset, depreciationMethod: e.target.value })}
                  title="Depreciation method"
                  aria-label="Depreciation method"
                >
                  <option value="StraightLine">Straight Line</option>
                  <option value="ReducingBalance">Reducing Balance</option>
                </select>
              </div>
              <div className="col-span-5 flex justify-end">
                <Button
                  variant="primary"
                  size="sm"
                  onPress={handleAddAsset}
                  isDisabled={savingSection === "assets"}
                >
                  {savingSection === "assets" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add Asset</>}
                </Button>
              </div>
            </div>
          </div>
        </Section>

        {/* 4. Stock & Inventory */}
        <Section
          title="Stock & Inventory"
          subtitle="Does the company hold stock or work in progress at year-end?"
          icon={Package}
          completed={inventory.length > 0}
        >
          {inventory.length > 0 && (
            <div className="space-y-2 mb-4">
              {inventory.map((item) => (
                <div
                  key={item.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{item.description}</p>
                    <Chip variant="soft" size="sm" color="default">{item.valuationMethod}</Chip>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatCurrency(item.value)}
                    </span>
                    <button
                      type="button"
                      onClick={() => item.id && handleDeleteInventory(item.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete inventory item ${item.description}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-4">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</label>
              <input
                type="text"
                className={inputClass}
                placeholder="e.g. Finished goods"
                value={newInventoryItem.description}
                onChange={(e) => setNewInventoryItem({ ...newInventoryItem, description: e.target.value })}
                aria-label="Inventory item description"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Value</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newInventoryItem.value || ""}
                onChange={(e) => setNewInventoryItem({ ...newInventoryItem, value: Number(e.target.value) })}
                aria-label="Inventory item value"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Valuation Method</label>
              <select
                className={selectClass}
                value={newInventoryItem.valuationMethod}
                onChange={(e) => setNewInventoryItem({ ...newInventoryItem, valuationMethod: e.target.value })}
                title="Valuation method"
                aria-label="Valuation method"
              >
                <option value="FIFO">FIFO</option>
                <option value="WeightedAverage">Weighted Average</option>
                <option value="LIFO">LIFO</option>
              </select>
            </div>
            <div className="col-span-2">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddInventory}
                isDisabled={savingSection === "inventory"}
                className="w-full"
              >
                {savingSection === "inventory" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 5. Loans */}
        <Section
          title="Loans & Borrowings"
          subtitle="Does the company have any loans or borrowings outstanding?"
          icon={Landmark}
          completed={true}
        >
          <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Loan data is managed in the Company Setup section. Any existing loans linked to this company will appear in the year-end summary automatically.
            </p>
          </div>
        </Section>

        {/* 6. Payroll */}
        <Section
          title="Payroll"
          subtitle="How many staff does the company employ? What are the total wages?"
          icon={Receipt}
          completed={payroll !== null}
        >
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Number of Staff</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0"
                value={payrollForm.staffCount || ""}
                onChange={(e) => setPayrollForm({ ...payrollForm, staffCount: Number(e.target.value) })}
                aria-label="Number of staff"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Gross Wages</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={payrollForm.grossWages || ""}
                onChange={(e) => setPayrollForm({ ...payrollForm, grossWages: Number(e.target.value) })}
                aria-label="Gross wages"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Employer PRSI</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={payrollForm.employerPrsi || ""}
                onChange={(e) => setPayrollForm({ ...payrollForm, employerPrsi: Number(e.target.value) })}
                aria-label="Employer PRSI"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Pension Contributions</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={payrollForm.pensionContributions || ""}
                onChange={(e) => setPayrollForm({ ...payrollForm, pensionContributions: Number(e.target.value) })}
                aria-label="Pension contributions"
              />
            </div>
          </div>
          <div className="mt-4 flex justify-end">
            <Button
              variant="primary"
              size="sm"
              onPress={handleSavePayroll}
              isDisabled={savingSection === "payroll"}
            >
              {savingSection === "payroll" ? <Spinner size="sm" /> : "Save Payroll"}
            </Button>
          </div>
        </Section>

        {/* 7. Tax */}
        <Section
          title="Tax Balances"
          subtitle="Corporation Tax, VAT, and PAYE/PRSI balances at year-end"
          icon={Banknote}
          completed={taxBalances.length > 0}
        >
          <div className="space-y-6">
            {[
              { key: "CorporationTax", label: "Corporation Tax" },
              { key: "VAT", label: "VAT" },
              { key: "PAYE_PRSI", label: "PAYE / PRSI" },
            ].map(({ key, label }) => (
              <div key={key}>
                <h4 className="text-sm font-medium text-gray-800 dark:text-gray-200 mb-3">{label}</h4>
                <div className="grid grid-cols-12 gap-3 items-end">
                  <div className="col-span-3">
                    <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Liability</label>
                    <input
                      type="number"
                      className={inputClass}
                      placeholder="0.00"
                      value={taxForms[key]?.liability || ""}
                      onChange={(e) =>
                        setTaxForms((prev) => ({
                          ...prev,
                          [key]: { ...prev[key], liability: Number(e.target.value) },
                        }))
                      }
                      aria-label={`${label} liability`}
                    />
                  </div>
                  <div className="col-span-3">
                    <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Paid</label>
                    <input
                      type="number"
                      className={inputClass}
                      placeholder="0.00"
                      value={taxForms[key]?.paid || ""}
                      onChange={(e) =>
                        setTaxForms((prev) => ({
                          ...prev,
                          [key]: { ...prev[key], paid: Number(e.target.value) },
                        }))
                      }
                      aria-label={`${label} paid`}
                    />
                  </div>
                  <div className="col-span-3">
                    <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Balance</label>
                    <input
                      type="number"
                      className={inputClass}
                      placeholder="0.00"
                      value={taxForms[key]?.balance || ""}
                      onChange={(e) =>
                        setTaxForms((prev) => ({
                          ...prev,
                          [key]: { ...prev[key], balance: Number(e.target.value) },
                        }))
                      }
                      aria-label={`${label} balance`}
                    />
                  </div>
                  <div className="col-span-3">
                    <Button
                      variant="outline"
                      size="sm"
                      onPress={() => handleSaveTax(key)}
                      isDisabled={savingSection === "tax-" + key}
                      className="w-full"
                    >
                      {savingSection === "tax-" + key ? <Spinner size="sm" /> : "Save"}
                    </Button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </Section>

        {/* 8. Dividends */}
        <Section
          title="Dividends"
          subtitle="Were any dividends declared or paid during the year?"
          icon={PiggyBank}
          completed={dividends.length > 0}
        >
          {dividends.length > 0 && (
            <div className="space-y-2 mb-4">
              {dividends.map((d) => (
                <div
                  key={d.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
                      {formatCurrency(d.amount)}
                    </p>
                    <div className="flex items-center gap-2 mt-0.5">
                      {d.dateDeclared && (
                        <span className="text-xs text-gray-400 dark:text-gray-500">
                          Declared: {new Date(d.dateDeclared).toLocaleDateString("en-IE")}
                        </span>
                      )}
                      {d.datePaid && (
                        <span className="text-xs text-gray-400 dark:text-gray-500">
                          Paid: {new Date(d.datePaid).toLocaleDateString("en-IE")}
                        </span>
                      )}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => d.id && handleDeleteDividend(d.id)}
                    className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                    aria-label={`Delete dividend of ${formatCurrency(d.amount)}`}
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newDividend.amount || ""}
                onChange={(e) => setNewDividend({ ...newDividend, amount: Number(e.target.value) })}
                aria-label="Dividend amount"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Date Declared</label>
              <input
                type="date"
                className={inputClass}
                value={newDividend.dateDeclared}
                onChange={(e) => setNewDividend({ ...newDividend, dateDeclared: e.target.value })}
                aria-label="Date dividend declared"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Date Paid</label>
              <input
                type="date"
                className={inputClass}
                value={newDividend.datePaid}
                onChange={(e) => setNewDividend({ ...newDividend, datePaid: e.target.value })}
                aria-label="Date dividend paid"
              />
            </div>
            <div className="col-span-3">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddDividend}
                isDisabled={savingSection === "dividends"}
                className="w-full"
              >
                {savingSection === "dividends" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 9. Director Loans */}
        <Section
          title="Director Loans"
          subtitle="Are there any loans between directors and the company?"
          icon={UserCheck}
          completed={directorLoanCompliance !== null}
        >
          {directorLoanCompliance ? (
            <div className="space-y-4">
              {/* Warning banner */}
              {directorLoanCompliance.warning && (
                <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-700 p-3">
                  <p className="text-sm font-medium text-amber-800 dark:text-amber-300">
                    {directorLoanCompliance.warning}
                  </p>
                </div>
              )}

              {/* Summary cards */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                <div className="rounded-lg border border-gray-200 dark:border-neutral-700 p-3 dark:bg-neutral-800/50">
                  <p className="text-xs text-gray-500 dark:text-gray-400">Total Loans</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(directorLoanCompliance.totalDirectorLoans)}</p>
                </div>
                <div className="rounded-lg border border-gray-200 dark:border-neutral-700 p-3 dark:bg-neutral-800/50">
                  <p className="text-xs text-gray-500 dark:text-gray-400">Net Assets</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(directorLoanCompliance.netAssets)}</p>
                </div>
                <div className="rounded-lg border border-gray-200 dark:border-neutral-700 p-3 dark:bg-neutral-800/50">
                  <p className="text-xs text-gray-500 dark:text-gray-400">10% Threshold</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(directorLoanCompliance.thresholdAmount)}</p>
                </div>
                <div className="rounded-lg border border-gray-200 dark:border-neutral-700 p-3 dark:bg-neutral-800/50">
                  <p className="text-xs text-gray-500 dark:text-gray-400">Status</p>
                  <Chip variant="soft" size="sm" color={directorLoanCompliance.exceedsThreshold ? "danger" : "success"}>
                    {directorLoanCompliance.exceedsThreshold ? "Exceeds Threshold" : "Within Limits"}
                  </Chip>
                </div>
              </div>

              {directorLoanCompliance.sapRequired && (
                <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700 p-3">
                  <p className="text-sm font-medium text-red-800 dark:text-red-300">
                    Shareholder Approval Process (SAP) required under s.239 Companies Act 2014
                  </p>
                </div>
              )}

              {/* Loans table */}
              {directorLoanCompliance.loans.length > 0 && (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-gray-200 dark:border-neutral-700">
                        <th className="text-left py-2 px-2 text-xs font-medium text-gray-500 dark:text-gray-400">Director</th>
                        <th className="text-right py-2 px-2 text-xs font-medium text-gray-500 dark:text-gray-400">Opening</th>
                        <th className="text-right py-2 px-2 text-xs font-medium text-gray-500 dark:text-gray-400">Max During Year</th>
                        <th className="text-right py-2 px-2 text-xs font-medium text-gray-500 dark:text-gray-400">Closing</th>
                        <th className="text-right py-2 px-2 text-xs font-medium text-gray-500 dark:text-gray-400">Interest</th>
                        <th className="text-center py-2 px-2 text-xs font-medium text-gray-500 dark:text-gray-400">Documented</th>
                      </tr>
                    </thead>
                    <tbody>
                      {directorLoanCompliance.loans.map((loan) => (
                        <tr key={loan.id} className="border-b border-gray-100 dark:border-neutral-800">
                          <td className="py-2 px-2 text-gray-900 dark:text-gray-100">{loan.directorName}</td>
                          <td className="py-2 px-2 text-right text-gray-900 dark:text-gray-100">{formatCurrency(loan.openingBalance)}</td>
                          <td className="py-2 px-2 text-right text-gray-900 dark:text-gray-100">{formatCurrency(loan.maxDuringYear)}</td>
                          <td className="py-2 px-2 text-right text-gray-900 dark:text-gray-100">{formatCurrency(loan.closingBalance)}</td>
                          <td className="py-2 px-2 text-right text-gray-900 dark:text-gray-100">{formatCurrency(loan.interestCharged)}</td>
                          <td className="py-2 px-2 text-center">
                            <Chip variant="soft" size="sm" color={loan.isDocumented ? "success" : "warning"}>
                              {loan.isDocumented ? "Yes" : "No"}
                            </Chip>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          ) : (
            <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
              <p className="text-sm text-gray-500 dark:text-gray-400">
                No director loan compliance data available. Add director loans in Company Setup first.
              </p>
            </div>
          )}
        </Section>

        {/* 10. Post-Balance Sheet Events */}
        <Section
          title="Post-Balance Sheet Events"
          subtitle="Has anything significant happened between year-end and today?"
          icon={CalendarCheck}
          completed={postBsEvents.length > 0}
        >
          {postBsEvents.length > 0 && (
            <div className="space-y-2 mb-4">
              {postBsEvents.map((evt) => (
                <div
                  key={evt.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{evt.description}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <span className="text-xs text-gray-400 dark:text-gray-500">
                        {new Date(evt.eventDate).toLocaleDateString("en-IE")}
                      </span>
                      <Chip variant="soft" size="sm" color={evt.isAdjusting ? "warning" : "default"}>
                        {evt.isAdjusting ? "Adjusting" : "Non-adjusting"}
                      </Chip>
                      {evt.financialImpact != null && evt.financialImpact !== 0 && (
                        <span className="text-xs text-gray-500 dark:text-gray-400">
                          Impact: {formatCurrency(evt.financialImpact)}
                        </span>
                      )}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => evt.id && handleDeletePbse(evt.id)}
                    className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                    aria-label={`Delete event ${evt.description}`}
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-4">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</label>
              <input
                type="text"
                className={inputClass}
                placeholder="e.g. Major contract signed"
                value={newPbseDesc}
                onChange={(e) => setNewPbseDesc(e.target.value)}
                aria-label="Event description"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Date</label>
              <input
                type="date"
                className={inputClass}
                value={newPbseDate}
                onChange={(e) => setNewPbseDate(e.target.value)}
                aria-label="Event date"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Financial Impact</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newPbseImpact || ""}
                onChange={(e) => setNewPbseImpact(Number(e.target.value))}
                aria-label="Financial impact"
              />
            </div>
            <div className="col-span-2 flex items-center gap-2 pb-2">
              <input
                type="checkbox"
                id="pbse-adjusting"
                checked={newPbseAdjusting}
                onChange={(e) => setNewPbseAdjusting(e.target.checked)}
                className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
              />
              <label htmlFor="pbse-adjusting" className="text-xs font-medium text-gray-600 dark:text-gray-400">Adjusting</label>
            </div>
            <div className="col-span-2">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddPbse}
                isDisabled={savingSection === "pbse"}
                className="w-full"
              >
                {savingSection === "pbse" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 11. Related Party Transactions */}
        <Section
          title="Related Party Transactions"
          subtitle="Were there any transactions with directors, connected persons, or group companies?"
          icon={Users}
          completed={relatedParties.length > 0}
        >
          {relatedParties.length > 0 && (
            <div className="space-y-2 mb-4">
              {relatedParties.map((rpt) => (
                <div
                  key={rpt.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{rpt.partyName}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <Chip variant="soft" size="sm" color="default">{rpt.relationship}</Chip>
                      <Chip variant="soft" size="sm" color="default">{rpt.transactionType}</Chip>
                      {rpt.balanceOwed != null && rpt.balanceOwed !== 0 && (
                        <span className="text-xs text-gray-500 dark:text-gray-400">
                          Balance owed: {formatCurrency(rpt.balanceOwed)}
                        </span>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatCurrency(rpt.amount)}
                    </span>
                    <button
                      type="button"
                      onClick={() => rpt.id && handleDeleteRpt(rpt.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete transaction with ${rpt.partyName}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Party Name</label>
              <input
                type="text"
                className={inputClass}
                placeholder="e.g. John Smith"
                value={newRptName}
                onChange={(e) => setNewRptName(e.target.value)}
                aria-label="Party name"
              />
            </div>
            <div className="col-span-3">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Relationship</label>
              <select
                className={selectClass}
                value={newRptRelationship}
                onChange={(e) => setNewRptRelationship(e.target.value)}
                title="Relationship"
                aria-label="Relationship"
              >
                <option value="Director">Director</option>
                <option value="Connected Person">Connected Person</option>
                <option value="Group Company">Group Company</option>
                <option value="Key Management">Key Management</option>
              </select>
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Type</label>
              <select
                className={selectClass}
                value={newRptType}
                onChange={(e) => setNewRptType(e.target.value)}
                title="Transaction type"
                aria-label="Transaction type"
              >
                <option value="Sale">Sale</option>
                <option value="Purchase">Purchase</option>
                <option value="Loan">Loan</option>
                <option value="Management Fee">Management Fee</option>
                <option value="Other">Other</option>
              </select>
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newRptAmount || ""}
                onChange={(e) => setNewRptAmount(Number(e.target.value))}
                aria-label="Transaction amount"
              />
            </div>
            <div className="col-span-2">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddRpt}
                isDisabled={savingSection === "rpt"}
                className="w-full"
              >
                {savingSection === "rpt" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 12. Contingent Liabilities */}
        <Section
          title="Contingent Liabilities"
          subtitle="Are there any potential liabilities that depend on the outcome of uncertain future events?"
          icon={ShieldAlert}
          completed={contingencies.length > 0}
        >
          {contingencies.length > 0 && (
            <div className="space-y-2 mb-4">
              {contingencies.map((cl) => (
                <div
                  key={cl.id}
                  className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{cl.description}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <Chip variant="soft" size="sm" color="default">{cl.nature}</Chip>
                      <Chip
                        variant="soft"
                        size="sm"
                        color={cl.likelihood === "Probable" ? "danger" : cl.likelihood === "Possible" ? "warning" : "success"}
                      >
                        {cl.likelihood}
                      </Chip>
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    {cl.estimatedAmount != null && cl.estimatedAmount !== 0 && (
                      <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                        {formatCurrency(cl.estimatedAmount)}
                      </span>
                    )}
                    <button
                      type="button"
                      onClick={() => cl.id && handleDeleteContingency(cl.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete contingency ${cl.description}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-12 gap-3 items-end">
            <div className="col-span-4">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</label>
              <input
                type="text"
                className={inputClass}
                placeholder="e.g. Pending legal claim"
                value={newClDesc}
                onChange={(e) => setNewClDesc(e.target.value)}
                aria-label="Contingency description"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Nature</label>
              <select
                className={selectClass}
                value={newClNature}
                onChange={(e) => setNewClNature(e.target.value)}
                title="Nature"
                aria-label="Contingency nature"
              >
                <option value="Guarantee">Guarantee</option>
                <option value="Legal Claim">Legal Claim</option>
                <option value="Warranty">Warranty</option>
                <option value="Environmental">Environmental</option>
                <option value="Other">Other</option>
              </select>
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Est. Amount</label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={newClAmount || ""}
                onChange={(e) => setNewClAmount(Number(e.target.value))}
                aria-label="Estimated amount"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Likelihood</label>
              <select
                className={selectClass}
                value={newClLikelihood}
                onChange={(e) => setNewClLikelihood(e.target.value)}
                title="Likelihood"
                aria-label="Contingency likelihood"
              >
                <option value="Probable">Probable</option>
                <option value="Possible">Possible</option>
                <option value="Remote">Remote</option>
              </select>
            </div>
            <div className="col-span-2">
              <Button
                variant="primary"
                size="sm"
                onPress={handleAddContingency}
                isDisabled={savingSection === "contingency"}
                className="w-full"
              >
                {savingSection === "contingency" ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
              </Button>
            </div>
          </div>
        </Section>

        {/* 13. Going Concern */}
        <Section
          title="Going Concern"
          subtitle="Do the directors confirm the company will continue in business for at least 12 months?"
          icon={HeartPulse}
          completed={true}
        >
          <div className="space-y-4">
            {!goingConcernConfirmed && (
              <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700 p-3">
                <p className="text-sm font-medium text-red-800 dark:text-red-300">
                  Warning: Going concern is not confirmed. Material uncertainty disclosures will be required in the financial statements.
                </p>
              </div>
            )}

            <div className="flex items-center gap-3">
              <input
                type="checkbox"
                id="going-concern-confirmed"
                checked={goingConcernConfirmed}
                onChange={(e) => setGoingConcernConfirmed(e.target.checked)}
                className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500 w-5 h-5"
              />
              <label htmlFor="going-concern-confirmed" className="text-sm font-medium text-gray-900 dark:text-gray-100">
                The directors confirm the company is a going concern
              </label>
            </div>

            {!goingConcernConfirmed && (
              <div>
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
                  Material uncertainty / going concern note
                </label>
                <textarea
                  className={inputClass + " min-h-[100px]"}
                  placeholder="Describe the material uncertainties that cast significant doubt on the company's ability to continue as a going concern..."
                  value={goingConcernNote}
                  onChange={(e) => setGoingConcernNote(e.target.value)}
                  aria-label="Going concern note"
                />
              </div>
            )}

            <div className="flex justify-end">
              <Button
                variant="primary"
                size="sm"
                onPress={handleSaveGoingConcern}
                isDisabled={savingSection === "goingConcern"}
              >
                {savingSection === "goingConcern" ? <Spinner size="sm" /> : "Save Going Concern"}
              </Button>
            </div>
          </div>
        </Section>
      </div>
    </div>
  );
}
