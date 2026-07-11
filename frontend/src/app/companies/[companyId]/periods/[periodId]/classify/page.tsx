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
import { useAuth } from "@/components/AuthProvider";
import { ReadOnlyNotice } from "@/components/workbench";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import {
  getCompany,
  getPeriod,
  saveSizeClassification,
  runClassification,
  setFilingRegime,
  saveMemberAuditNotice,
  type Company,
  type AccountingPeriod,
  type ClassificationResult,
} from "@/lib/api";

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}

type ThresholdElectionDate = "2023-01-01" | "2024-01-01";

type RegimeOption = {
  label: string;
  value: string;
};

function toRegimeOption(regime: string): RegimeOption {
  const normalised = regime
    .replace(/[–—]/g, "-")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();

  if (normalised.includes("micro")) return { label: "Micro (FRS 105)", value: "Micro" };
  if (normalised.includes("small") && normalised.includes("abridged")) {
    return { label: "Small - Abridged", value: "SmallAbridged" };
  }
  if (normalised.includes("small")) return { label: "Small - Full", value: "Small" };
  if (normalised.includes("medium")) return { label: "Medium", value: "Medium" };
  return { label: "Full", value: "Full" };
}

function regimeLabel(value: string, availableRegimes: string[] = []): string {
  return availableRegimes
    .map(toRegimeOption)
    .find((option) => option.value === value)?.label ?? toRegimeOption(value).label;
}

export default function ClassifyPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);
  const { canWriteWorkingPapers } = useAuth();

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [loading, setLoading] = useState(true);

  // Form state
  const [turnover, setTurnover] = useState<number>(0);
  const [balanceSheetTotal, setBalanceSheetTotal] = useState<number>(0);
  const [avgEmployees, setAvgEmployees] = useState<number>(0);
  const [thresholdElectionEffectiveFrom, setThresholdElectionEffectiveFrom] = useState<ThresholdElectionDate>("2024-01-01");

  // Unsaved-changes guard: the size figures are only persisted when "Run classification" saves them
  // (shared guard across notes/year-end/classify/charity).
  const [dirty, setDirty] = useState(false);

  // Result state
  const [result, setResult] = useState<ClassificationResult | null>(null);
  const [classifying, setClassifying] = useState(false);
  const [classifyError, setClassifyError] = useState<string | null>(null);

  // Member audit notice (s.334)
  const [memberAuditNotice, setMemberAuditNotice] = useState(false);

  // Filing regime
  const [selectedRegime, setSelectedRegime] = useState<string>("");
  const [confirmingRegime, setConfirmingRegime] = useState(false);
  const [regimeConfirmed, setRegimeConfirmed] = useState(false);
  useUnsavedChanges(dirty || (selectedRegime !== "" && !regimeConfirmed));

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
        setThresholdElectionEffectiveFrom(
          sc.thresholdElectionEffectiveFrom === "2023-01-01" ? "2023-01-01" : "2024-01-01",
        );
      }

      setMemberAuditNotice(periodData.memberAuditNoticeReceived ?? false);

      if (periodData.filingRegime) {
        setSelectedRegime(periodData.filingRegime.electedRegime);
        setRegimeConfirmed(true);
      }
      setDirty(false);
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
        thresholdElectionEffectiveFrom,
      });
      setDirty(false);
      const classResult = await runClassification(cId, pId);
      setResult(classResult);
      if (classResult.availableRegimes.length > 0) {
        setSelectedRegime(toRegimeOption(classResult.availableRegimes[0]).value);
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
      toast.success(`Filing regime set to ${regimeLabel(selectedRegime, result?.availableRegimes)}`);
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
        <p className="text-sm text-[var(--muted-foreground)] mt-1">
          {company?.legalName ?? "Company"} &mdash;{" "}
          {period
            ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}`
            : ""}
        </p>
      </div>

      {!canWriteWorkingPapers && <ReadOnlyNotice subject="classification and filing-regime decisions" />}

      {/* Input Form */}
      {canWriteWorkingPapers && <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
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
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="classification-turnover" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Turnover
              </label>
              <input
                id="classification-turnover"
                type="number"
                min={0}
                step="0.01"
                className={inputClass}
                placeholder="0.00"
                value={turnover || ""}
                onChange={(e) => { setTurnover(Number(e.target.value)); setDirty(true); }}
              />
            </div>
            <div>
              <label htmlFor="classification-balance-sheet-total" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Balance Sheet Total
              </label>
              <input
                id="classification-balance-sheet-total"
                type="number"
                min={0}
                step="0.01"
                className={inputClass}
                placeholder="0.00"
                value={balanceSheetTotal || ""}
                onChange={(e) => { setBalanceSheetTotal(Number(e.target.value)); setDirty(true); }}
              />
            </div>
            <div>
              <label htmlFor="classification-average-employees" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Average Employees
              </label>
              <input
                id="classification-average-employees"
                type="number"
                min={0}
                step="1"
                className={inputClass}
                placeholder="0"
                value={avgEmployees || ""}
                onChange={(e) => { setAvgEmployees(Number(e.target.value)); setDirty(true); }}
              />
            </div>
            <div>
              <label htmlFor="classification-threshold-election" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                2024 threshold adjustment election
              </label>
              <select
                id="classification-threshold-election"
                className={inputClass}
                value={thresholdElectionEffectiveFrom}
                onChange={(e) => {
                  setThresholdElectionEffectiveFrom(e.target.value as ThresholdElectionDate);
                  setDirty(true);
                }}
                title="2024 threshold adjustment election"
                aria-label="2024 threshold adjustment election"
              >
                <option value="2024-01-01">Apply adjusted thresholds from financial years beginning in 2024</option>
                <option value="2023-01-01">Elect adjusted thresholds from financial years beginning in 2023</option>
              </select>
              <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                Prior-period raw figures and the prior effective class are derived from retained accounting periods; they cannot be supplied here.
              </p>
            </div>
          </div>

          <div className="mt-6">
            <Button
              variant="primary"
              onPress={handleClassify}
              isDisabled={classifying || turnover < 0 || balanceSheetTotal < 0 || avgEmployees < 0}
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
      </Card>}

      {/* Member Audit Notice (s.334) */}
      {canWriteWorkingPapers && <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
        <Card.Header>
          <Card.Title className="text-gray-900 dark:text-gray-100">Member Audit Notice</Card.Title>
          <Card.Description>
            Under s.334 Companies Act 2014, members may serve notice requiring a statutory audit.
          </Card.Description>
        </Card.Header>
        <Card.Content>
          <div className="flex items-center gap-4">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={memberAuditNotice}
                className="workbench-checkbox"
                onChange={async (e) => {
                  setMemberAuditNotice(e.target.checked);
                  try {
                    await saveMemberAuditNotice(cId, pId, { received: e.target.checked });
                    toast.success(e.target.checked ? "Audit notice recorded" : "Audit notice removed");
                  } catch {
                    toast.error("Failed to save audit notice");
                  }
                }}
                aria-label="Member audit notice received"
                title="Member audit notice received"
              />
              <span className="text-sm text-gray-700 dark:text-gray-300">
                A member has served notice requiring a statutory audit
              </span>
            </label>
          </div>
          {memberAuditNotice && (
            <div className="mt-3 rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-4 py-3 text-sm text-amber-700 dark:text-amber-400">
              Audit exemption is overridden. A statutory audit will be required for this period regardless of company size.
            </div>
          )}
        </Card.Content>
      </Card>}

      {/* Results */}
      {result && (
        <>
          <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
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

                {/* Ineligible entity banner */}
                {result.isIneligibleEntity && (
                  <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3">
                    <h4 className="text-sm font-medium text-red-800 dark:text-red-300 mb-1">Ineligible Entity</h4>
                    <p className="text-sm text-red-700 dark:text-red-400">
                      {result.ineligibleReason ?? "This company is an ineligible entity under the Fifth Schedule. Micro, small, and medium exemptions are not available."}
                    </p>
                  </div>
                )}

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
                        <AlertTriangle className="w-6 h-6 text-[var(--muted-foreground)]" />
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
                        <AlertTriangle className="w-6 h-6 text-[var(--muted-foreground)]" />
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
                        <AlertTriangle className="w-6 h-6 text-[var(--muted-foreground)]" />
                      )}
                    </div>
                    <p className="text-xs font-medium text-gray-600 dark:text-gray-400">Audit Exemption</p>
                    <p className="text-sm font-semibold text-gray-900 dark:text-gray-100 mt-0.5">
                      {result.auditExempt ? "Exempt" : "Audit required"}
                    </p>
                  </div>
                </div>

                <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800">
                  <h4 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Threshold decision evidence</h4>
                  <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
                    <DecisionEvidence label="Current raw class" value={result.rawCurrentClass} />
                    <DecisionEvidence label="Prior raw class" value={result.rawPriorClass ?? "First year / no retained prior period"} />
                    <DecisionEvidence label="Annualised turnover" value={formatCurrency(result.annualisedTurnover)} />
                    <DecisionEvidence label="Period length" value={`${result.periodLengthInYears.toFixed(4)} years`} />
                    <DecisionEvidence label="Threshold schedule" value={result.thresholdScheduleCode ?? "Not recorded"} />
                    <DecisionEvidence
                      label="Schedule effective from"
                      value={result.thresholdScheduleEffectiveFrom
                        ? new Date(`${result.thresholdScheduleEffectiveFrom}T00:00:00Z`).toLocaleDateString("en-IE")
                        : "Not recorded"}
                    />
                  </dl>
                </div>
              </div>
            </Card.Content>
          </Card>

          {/* Filing Regime Selection */}
          {canWriteWorkingPapers && result.availableRegimes.length > 0 && (
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
              <Card.Header>
                <Card.Title>Select Filing Regime</Card.Title>
                <Card.Description>
                  Choose the filing regime for this accounting period.
                </Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="space-y-4">
                  <div className="flex flex-wrap gap-3">
                    {result.availableRegimes.map((regime) => {
                      const option = toRegimeOption(regime);
                      return (
                      <Button
                        key={option.value}
                        variant={selectedRegime === option.value ? "primary" : "outline"}
                        onPress={() => {
                          setSelectedRegime(option.value);
                          setRegimeConfirmed(false);
                        }}
                      >
                        {option.label}
                      </Button>
                      );
                    })}
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
                        <span className="text-sm font-medium text-[var(--emerald-700)]">
                          {regimeLabel(selectedRegime, result.availableRegimes)} regime has been set for this period.
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
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
          <Card.Header>
            <Card.Title>
              {period.sizeClassification.decisionInputFingerprintSha256
                && !period.sizeClassification.overrideRequiresRereview
                ? "Current Classification"
                : "Classification Requires Re-run"}
            </Card.Title>
          </Card.Header>
          <Card.Content>
            <div className="space-y-4">
              {(!period.sizeClassification.decisionInputFingerprintSha256
                || period.sizeClassification.overrideRequiresRereview) && (
                <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-300">
                  The retained decision is stale. Re-run classification before selecting a regime or using filing outputs.
                </div>
              )}
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
                  <p className="text-xs text-[var(--muted-foreground)]">Turnover</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatCurrency(period.sizeClassification.turnover)}
                  </p>
                </div>
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-[var(--muted-foreground)]">Balance Sheet</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatCurrency(period.sizeClassification.balanceSheetTotal)}
                  </p>
                </div>
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-[var(--muted-foreground)]">Avg Employees</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {period.sizeClassification.avgEmployees}
                  </p>
                </div>
              </div>
              <p className="text-xs leading-5 text-[var(--muted-foreground)] text-center">
                Threshold schedule {period.sizeClassification.thresholdScheduleCode ?? "not yet recorded"}
                {period.sizeClassification.thresholdScheduleEffectiveFrom
                  ? `, effective ${new Date(`${period.sizeClassification.thresholdScheduleEffectiveFrom}T00:00:00Z`).toLocaleDateString("en-IE")}`
                  : ""}. Prior-period class evidence is derived from retained period inputs.
              </p>
              <p className="text-xs text-[var(--muted-foreground)] text-center">
                Re-classify above to update.
              </p>
            </div>
          </Card.Content>
        </Card>
      )}
    </div>
  );
}

function DecisionEvidence({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-[var(--muted-foreground)]">{label}</dt>
      <dd className="mt-1 font-semibold text-gray-900 dark:text-gray-100">{value}</dd>
    </div>
  );
}
