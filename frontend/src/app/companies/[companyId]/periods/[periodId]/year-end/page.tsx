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
    true, // director loans placeholder
  ];
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
                color={completedCount >= 7 ? "success" : completedCount >= 4 ? "warning" : "default"}
                variant="soft"
                size="sm"
              >
                {completedCount} of 9 sections completed
              </Chip>
            </div>
            <div className="w-full bg-gray-200 dark:bg-neutral-700 rounded-full h-2.5">
              <div
                className="bg-emerald-500 h-2.5 rounded-full transition-all"
                style={{ width: `${Math.round((completedCount / 9) * 100)}%` }}
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
          completed={true}
        >
          <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Director loan balances are managed in Company Setup. Any existing director loans will be reflected in the year-end summary automatically.
            </p>
          </div>
        </Section>
      </div>
    </div>
  );
}
