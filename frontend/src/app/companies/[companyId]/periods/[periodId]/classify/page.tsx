"use client";

import { use, useState, useEffect, useCallback } from "react";
import Link from "next/link";
import {
  Button,
  Card,
  Chip,
  Spinner,
} from "@heroui/react";
import { ArrowLeft, CheckCircle2, AlertTriangle, Scale } from "lucide-react";
import { toast } from "sonner";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import {
  getCompany,
  getPeriod,
  saveSizeClassification,
  runClassification,
  setFilingRegime,
  type Company,
  type AccountingPeriod,
} from "@/lib/api";

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}

interface ClassificationResult {
  calculatedClass: string;
  qualificationNotes: string;
  canUseMicro: boolean;
  canFileAbridged: boolean;
  auditExempt: boolean;
  availableRegimes: string[];
}

export default function ClassifyPage({
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

  // Form state
  const [turnover, setTurnover] = useState<number>(0);
  const [balanceSheetTotal, setBalanceSheetTotal] = useState<number>(0);
  const [avgEmployees, setAvgEmployees] = useState<number>(0);
  const [priorYearClass, setPriorYearClass] = useState<string>("");

  // Result state
  const [result, setResult] = useState<ClassificationResult | null>(null);
  const [classifying, setClassifying] = useState(false);
  const [classifyError, setClassifyError] = useState<string | null>(null);

  // Filing regime
  const [selectedRegime, setSelectedRegime] = useState<string>("");
  const [confirmingRegime, setConfirmingRegime] = useState(false);
  const [regimeConfirmed, setRegimeConfirmed] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [companyData, periodData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);

      // Pre-fill from existing classification if available
      if (periodData.sizeClassification) {
        const sc = periodData.sizeClassification;
        setTurnover(sc.turnover);
        setBalanceSheetTotal(sc.balanceSheetTotal);
        setAvgEmployees(sc.avgEmployees);
      }

      if (periodData.filingRegime) {
        setSelectedRegime(periodData.filingRegime.electedRegime);
        setRegimeConfirmed(true);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load classification data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleClassify() {
    setClassifying(true);
    setClassifyError(null);
    setResult(null);
    setRegimeConfirmed(false);
    setSelectedRegime("");
    try {
      await saveSizeClassification(cId, pId, {
        turnover,
        balanceSheetTotal,
        avgEmployees,
        priorYearClass: priorYearClass || undefined,
      });
      const classResult = await runClassification(cId, pId);
      setResult(classResult);
      if (classResult.availableRegimes.length > 0) {
        setSelectedRegime(classResult.availableRegimes[0]);
      }
      toast.success(`Classified as ${classResult.calculatedClass} company`);
    } catch (err) {
      setClassifyError(err instanceof Error ? err.message : "Classification failed");
      toast.error(err instanceof Error ? err.message : "Classification failed");
    } finally {
      setClassifying(false);
    }
  }

  async function handleConfirmRegime() {
    if (!selectedRegime) return;
    setConfirmingRegime(true);
    try {
      await setFilingRegime(cId, pId, selectedRegime);
      setRegimeConfirmed(true);
      toast.success(`Filing regime set to ${selectedRegime}`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to confirm regime");
    } finally {
      setConfirmingRegime(false);
    }
  }

  const classColorMap: Record<string, "success" | "warning" | "danger" | "default"> = {
    Micro: "success",
    Small: "success",
    Medium: "warning",
    Large: "danger",
  };

  if (loading) {
    return <PeriodWorkspaceSkeleton />;
  }

  return (
    <div className="max-w-2xl mx-auto animate-fade-in">
      {/* Breadcrumbs */}
      <Breadcrumbs
        items={[
          { label: "Company", href: `/companies/${companyId}` },
          { label: "Period", href: `/companies/${companyId}/periods/${periodId}` },
          { label: "Classification" },
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
          Company Size Classification
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
          {company?.legalName ?? "Company"} &mdash;{" "}
          {period
            ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}`
            : ""}
        </p>
      </div>

      {/* Input Form */}
      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 mb-6">
        <Card.Header>
          <Card.Title className="flex items-center gap-2">
            <Scale className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
            Classification Data
          </Card.Title>
          <Card.Description>
            Enter the company&apos;s financial thresholds to determine its size classification under Irish company law.
          </Card.Description>
        </Card.Header>
        <Card.Content>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Turnover
              </label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={turnover || ""}
                onChange={(e) => setTurnover(Number(e.target.value))}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Balance Sheet Total
              </label>
              <input
                type="number"
                className={inputClass}
                placeholder="0.00"
                value={balanceSheetTotal || ""}
                onChange={(e) => setBalanceSheetTotal(Number(e.target.value))}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Average Employees
              </label>
              <input
                type="number"
                className={inputClass}
                placeholder="0"
                value={avgEmployees || ""}
                onChange={(e) => setAvgEmployees(Number(e.target.value))}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Prior Year Classification
              </label>
              <select
                className={inputClass}
                value={priorYearClass}
                onChange={(e) => setPriorYearClass(e.target.value)}
                title="Prior Year Classification"
                aria-label="Prior Year Classification"
              >
                <option value="">None / First Year</option>
                <option value="Micro">Micro</option>
                <option value="Small">Small</option>
                <option value="Medium">Medium</option>
                <option value="Large">Large</option>
              </select>
            </div>
          </div>

          <div className="mt-6">
            <Button
              variant="primary"
              onPress={handleClassify}
              isDisabled={classifying}
            >
              {classifying ? (
                <>
                  <Spinner size="sm" className="mr-2" />
                  Classifying...
                </>
              ) : (
                <>
                  <Scale className="w-4 h-4 mr-1.5" />
                  Classify Company
                </>
              )}
            </Button>
          </div>

          {classifyError && (
            <div className="mt-4 rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400">
              {classifyError}
            </div>
          )}
        </Card.Content>
      </Card>

      {/* Results */}
      {result && (
        <>
          <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 mb-6">
            <Card.Header>
              <Card.Title>Classification Result</Card.Title>
            </Card.Header>
            <Card.Content>
              <div className="space-y-6">
                {/* Big classification chip */}
                <div className="flex items-center justify-center">
                  <Chip
                    color={classColorMap[result.calculatedClass] ?? "default"}
                    variant="primary"
                    size="lg"
                    className="text-lg px-6 py-3 font-bold"
                  >
                    {result.calculatedClass} Company
                  </Chip>
                </div>

                {/* Qualification notes */}
                {result.qualificationNotes && (
                  <div className="rounded-lg bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 px-4 py-3">
                    <h4 className="text-sm font-medium text-blue-800 dark:text-blue-300 mb-1">Qualification Notes</h4>
                    <p className="text-sm text-blue-700 dark:text-blue-400 whitespace-pre-line">
                      {result.qualificationNotes}
                    </p>
                  </div>
                )}

                {/* Status badges */}
                <div className="grid grid-cols-3 gap-4">
                  <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                    <div className="flex items-center justify-center mb-2">
                      {result.canUseMicro ? (
                        <CheckCircle2 className="w-6 h-6 text-emerald-500" />
                      ) : (
                        <AlertTriangle className="w-6 h-6 text-gray-400 dark:text-gray-500" />
                      )}
                    </div>
                    <p className="text-xs font-medium text-gray-600 dark:text-gray-400">Micro Regime</p>
                    <p className="text-sm font-semibold text-gray-900 dark:text-gray-100 mt-0.5">
                      {result.canUseMicro ? "Available" : "Not available"}
                    </p>
                  </div>
                  <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                    <div className="flex items-center justify-center mb-2">
                      {result.canFileAbridged ? (
                        <CheckCircle2 className="w-6 h-6 text-emerald-500" />
                      ) : (
                        <AlertTriangle className="w-6 h-6 text-gray-400 dark:text-gray-500" />
                      )}
                    </div>
                    <p className="text-xs font-medium text-gray-600 dark:text-gray-400">Abridged Filing</p>
                    <p className="text-sm font-semibold text-gray-900 dark:text-gray-100 mt-0.5">
                      {result.canFileAbridged ? "Available" : "Not available"}
                    </p>
                  </div>
                  <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                    <div className="flex items-center justify-center mb-2">
                      {result.auditExempt ? (
                        <CheckCircle2 className="w-6 h-6 text-emerald-500" />
                      ) : (
                        <AlertTriangle className="w-6 h-6 text-gray-400 dark:text-gray-500" />
                      )}
                    </div>
                    <p className="text-xs font-medium text-gray-600 dark:text-gray-400">Audit Exemption</p>
                    <p className="text-sm font-semibold text-gray-900 dark:text-gray-100 mt-0.5">
                      {result.auditExempt ? "Exempt" : "Audit required"}
                    </p>
                  </div>
                </div>
              </div>
            </Card.Content>
          </Card>

          {/* Filing Regime Selection */}
          {result.availableRegimes.length > 0 && (
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 mb-6">
              <Card.Header>
                <Card.Title>Select Filing Regime</Card.Title>
                <Card.Description>
                  Choose the filing regime for this accounting period.
                </Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="space-y-4">
                  <div className="flex flex-wrap gap-3">
                    {result.availableRegimes.map((regime) => (
                      <Button
                        key={regime}
                        variant={selectedRegime === regime ? "primary" : "outline"}
                        onPress={() => {
                          setSelectedRegime(regime);
                          setRegimeConfirmed(false);
                        }}
                      >
                        {regime}
                      </Button>
                    ))}
                  </div>

                  {selectedRegime && (
                    <div className="flex items-center gap-4 pt-2 border-t border-gray-100 dark:border-neutral-700">
                      <Button
                        variant="primary"
                        onPress={handleConfirmRegime}
                        isDisabled={confirmingRegime || regimeConfirmed}
                      >
                        {confirmingRegime ? (
                          <>
                            <Spinner size="sm" className="mr-2" />
                            Confirming...
                          </>
                        ) : regimeConfirmed ? (
                          <>
                            <CheckCircle2 className="w-4 h-4 mr-1.5" />
                            Regime Confirmed
                          </>
                        ) : (
                          "Confirm Regime"
                        )}
                      </Button>
                      {regimeConfirmed && (
                        <span className="text-sm text-emerald-600 dark:text-emerald-400 font-medium">
                          {selectedRegime} regime has been set for this period.
                        </span>
                      )}
                    </div>
                  )}
                </div>
              </Card.Content>
            </Card>
          )}
        </>
      )}

      {/* Pre-existing classification display (when loaded from period data) */}
      {!result && period?.sizeClassification && (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 mb-6">
          <Card.Header>
            <Card.Title>Current Classification</Card.Title>
          </Card.Header>
          <Card.Content>
            <div className="space-y-4">
              <div className="flex items-center justify-center">
                <Chip
                  color={classColorMap[period.sizeClassification.calculatedClass] ?? "default"}
                  variant="primary"
                  size="lg"
                  className="text-lg px-6 py-3 font-bold"
                >
                  {period.sizeClassification.calculatedClass} Company
                </Chip>
              </div>
              {period.sizeClassification.qualificationNotes && (
                <div className="rounded-lg bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 px-4 py-3">
                  <p className="text-sm text-blue-700 dark:text-blue-400">
                    {period.sizeClassification.qualificationNotes}
                  </p>
                </div>
              )}
              <div className="grid grid-cols-3 gap-4 text-center">
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-gray-500 dark:text-gray-400">Turnover</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatCurrency(period.sizeClassification.turnover)}
                  </p>
                </div>
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-gray-500 dark:text-gray-400">Balance Sheet</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatCurrency(period.sizeClassification.balanceSheetTotal)}
                  </p>
                </div>
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-gray-500 dark:text-gray-400">Avg Employees</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {period.sizeClassification.avgEmployees}
                  </p>
                </div>
              </div>
              <p className="text-xs text-gray-400 dark:text-gray-500 text-center">
                Re-classify above to update.
              </p>
            </div>
          </Card.Content>
        </Card>
      )}
    </div>
  );
}
