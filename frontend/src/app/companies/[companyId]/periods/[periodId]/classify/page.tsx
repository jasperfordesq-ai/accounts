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
import { HistoricSizeFiguresCard } from "@/components/period/HistoricSizeFiguresCard";
import { RetireHistoricFiguresPanel } from "@/components/period/RetireHistoricFiguresPanel";
import { ReadOnlyNotice } from "@/components/workbench";
import {
  HISTORIC_FIGURES_BADGE,
  HISTORIC_FIGURES_PROVENANCE,
  isHistoricFigures,
  getArchivedHistoricFigures,
  isRetiredHistoricFigures,
  type ArchivedHistoricFigures,
} from "@/lib/historicSizeFigures";
import { ArchivedHistoricFiguresCard } from "@/components/period/ArchivedHistoricFiguresCard";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import {
  getClassificationPrerequisites,
  getCompany,
  getPeriod,
  runClassification,
  setFilingRegime,
  saveMemberAuditNotice,
  type Company,
  type AccountingPeriod,
  type ClassificationPrerequisites,
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

// Inputs hold exactly what was typed. A zero is a real answer (a charity with no staff), so it
// must display as "0", and an empty box must not be sent as a silent zero.
function parseNonNegative(value: string, integer = false): number | undefined {
  if (value.trim() === "") return undefined;
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0 || (integer && !Number.isInteger(parsed))) return undefined;
  return parsed;
}

function formatPeriodDate(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString("en-IE", {
    day: "numeric",
    month: "short",
    year: "numeric",
    timeZone: "UTC",
  });
}

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
  const { canWriteWorkingPapers, canApprove } = useAuth();
  // Retiring historic figures is an Owner act.
  const { isOwner, canReadInternalWorkingPapers } = useAuth();
  // Retired or replaced historic evidence (Owner, Reviewer and Accountant).
  const [archivedHistoricFigures, setArchivedHistoricFigures] = useState<ArchivedHistoricFigures[]>([]);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  // s.280A: a later year's size test needs every earlier period classified, in order. Null when
  // unavailable; the server still enforces the requirement on submit.
  const [prerequisites, setPrerequisites] = useState<ClassificationPrerequisites | null>(null);
  const blockedByEarlierPeriods = prerequisites !== null && !prerequisites.ready;
  const [loading, setLoading] = useState(true);
  // Figures keyed in from this period's filed accounts (Owner/Reviewer): the root of the next
  // period's size test, never a classification of this period.
  const holdsHistoricFigures = isHistoricFigures(period?.sizeClassification);
  const canRecordHistoricFigures = canApprove && prerequisites?.canRecordHistoricFigures === true;
  // Figures an Owner retired: kept as evidence only; the period is classified from its own figures.
  const holdsRetiredHistoricFigures = isRetiredHistoricFigures(period?.sizeClassification);
  const [historicMode, setHistoricMode] = useState(false);

  // Form state
  const [turnover, setTurnover] = useState("");
  const [balanceSheetTotal, setBalanceSheetTotal] = useState("");
  const [avgEmployees, setAvgEmployees] = useState("");
  const parsedTurnover = parseNonNegative(turnover);
  const parsedBalanceSheetTotal = parseNonNegative(balanceSheetTotal);
  const parsedAvgEmployees = parseNonNegative(avgEmployees, true);
  const inputsValid = parsedTurnover !== undefined
    && parsedBalanceSheetTotal !== undefined
    && parsedAvgEmployees !== undefined;
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
      const [companyData, periodData, prerequisiteData, archiveData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
        getClassificationPrerequisites(cId, pId).catch(() => null),
        canReadInternalWorkingPapers
          ? getArchivedHistoricFigures(cId, pId).catch(() => [] as ArchivedHistoricFigures[])
          : Promise.resolve([] as ArchivedHistoricFigures[]),
      ]);
      setCompany(companyData);
      setPeriod(periodData);
      setPrerequisites(prerequisiteData);
      setArchivedHistoricFigures(archiveData);

      // Pre-fill from existing classification if available (never from retired historic figures)
      if (periodData.sizeClassification && !isRetiredHistoricFigures(periodData.sizeClassification)) {
        const sc = periodData.sizeClassification;
        setTurnover(String(sc.turnover));
        setBalanceSheetTotal(String(sc.balanceSheetTotal));
        setAvgEmployees(String(sc.avgEmployees));
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
  }, [cId, pId, canReadInternalWorkingPapers]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleClassify() {
    if (parsedTurnover === undefined || parsedBalanceSheetTotal === undefined || parsedAvgEmployees === undefined) {
      setClassifyError("Enter turnover, balance sheet total and average employees (0 is a valid answer).");
      return;
    }
    setClassifying(true);
    setClassifyError(null);
    setResult(null);
    setRegimeConfirmed(false);
    setSelectedRegime("");
    try {
      const classResult = await runClassification(cId, pId, {
        turnover: parsedTurnover,
        balanceSheetTotal: parsedBalanceSheetTotal,
        avgEmployees: parsedAvgEmployees,
        thresholdElectionEffectiveFrom,
      });
      setDirty(false);
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

      {blockedByEarlierPeriods && prerequisites && !holdsHistoricFigures && (
        <div
          role="status"
          aria-labelledby="classification-prerequisites-title"
          className="mb-6 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-300"
        >
          <h2 id="classification-prerequisites-title" className="mb-1 flex items-center gap-2 font-semibold">
            <AlertTriangle className="h-4 w-4" aria-hidden="true" />
            Earlier periods must be classified first
          </h2>
          <p>{prerequisites.message}</p>
          {prerequisites.firstBlockingPeriod && (
            <p className="mt-2">
              Start here:{" "}
              <Link
                href={`/companies/${companyId}/periods/${prerequisites.firstBlockingPeriod.periodId}/classify`}
                className="font-medium underline"
              >
                Classify {formatPeriodDate(prerequisites.firstBlockingPeriod.periodStart)} &ndash;{" "}
                {formatPeriodDate(prerequisites.firstBlockingPeriod.periodEnd)}
              </Link>{" "}
              ({prerequisites.firstBlockingPeriod.reason})
            </p>
          )}
          {prerequisites.historicFiguresPeriod && (
            <p className="mt-2">
              Or:{" "}
              <Link
                href={`/companies/${companyId}/periods/${prerequisites.historicFiguresPeriod.periodId}/classify`}
                className="font-medium underline"
              >
                {`Record historic figures from the filed accounts for ${formatPeriodDate(prerequisites.historicFiguresPeriod.periodStart)} – ${formatPeriodDate(prerequisites.historicFiguresPeriod.periodEnd)}`}
              </Link>{" "}
              (keyed in from accounts filed elsewhere, with the PDF as evidence)
            </p>
          )}
        </div>
      )}

      {!canWriteWorkingPapers && <ReadOnlyNotice subject="classification and filing-regime decisions" />}

      {canRecordHistoricFigures && !historicMode && (
        <div className="mb-6 flex flex-col gap-2 rounded-lg border border-gray-200 bg-white px-4 py-3 text-sm dark:border-neutral-700 dark:bg-neutral-900 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-[var(--muted-foreground)]">
            {holdsHistoricFigures
              ? "Replace these figures with a corrected set from the filed accounts."
              : "No system classification for this period? Key in its figures from the accounts filed for it."}
          </p>
          <Button size="sm" variant="outline" onPress={() => setHistoricMode(true)}>
            {holdsHistoricFigures ? "Replace the historic figures" : "Enter figures from the filed accounts instead"}
          </Button>
        </div>
      )}

      {canRecordHistoricFigures && historicMode && (
        <HistoricSizeFiguresCard
          companyId={cId}
          periodId={pId}
          initialElection={prerequisites?.suggestedThresholdElection === "2023-01-01" ? "2023-01-01" : prerequisites?.suggestedThresholdElection === "2024-01-01" ? "2024-01-01" : null}
          onCancel={() => setHistoricMode(false)}
          onRecorded={async () => {
            setHistoricMode(false);
            await loadData();
          }}
        />
      )}

      {/* Input Form */}
      {canWriteWorkingPapers && !holdsHistoricFigures && <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
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
                value={turnover}
                onChange={(e) => { setTurnover(e.target.value); setDirty(true); }}
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
                value={balanceSheetTotal}
                onChange={(e) => { setBalanceSheetTotal(e.target.value); setDirty(true); }}
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
                value={avgEmployees}
                onChange={(e) => { setAvgEmployees(e.target.value); setDirty(true); }}
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
              isDisabled={classifying || !inputsValid || blockedByEarlierPeriods}
              aria-describedby={blockedByEarlierPeriods ? "classification-prerequisites-hint" : undefined}
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
            {blockedByEarlierPeriods && (
              <p id="classification-prerequisites-hint" className="mt-2 text-xs leading-5 text-[var(--muted-foreground)]">
                Classification is unavailable until the earlier periods have been classified, in order (see the notice above).
              </p>
            )}
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

      {/* Historic figures keyed in from filed accounts: provenance, never a computed class */}
      {isHistoricFigures(period?.sizeClassification) && (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
          <Card.Header>
            <Card.Title className="flex flex-wrap items-center gap-2">
              Preceding-year evidence
              <Chip color="warning" variant="soft" size="sm">{HISTORIC_FIGURES_BADGE}</Chip>
            </Card.Title>
          </Card.Header>
          <Card.Content>
            <div className="space-y-3 text-sm text-gray-700 dark:text-gray-300">
              <p className="font-medium">{HISTORIC_FIGURES_PROVENANCE}</p>
              <p>
                Source: {period.sizeClassification.historicSourceReference ?? "not recorded"}
                {period.sizeClassification.historicAttestedBy
                  ? ` (recorded by ${period.sizeClassification.historicAttestedBy}${period.sizeClassification.historicAttestedRole ? `, ${period.sizeClassification.historicAttestedRole}` : ""})`
                  : ""}
              </p>
              <p>
                {period.sizeClassification.historicAttestedEffectiveClass
                  ? `Size class stated in the filed accounts: ${period.sizeClassification.historicAttestedEffectiveClass}. The following year may rely on it for the consecutive-year rule.`
                  : "No size class is attested: the following year treats this year as not qualified, so both years' figures must meet the size conditions."}
              </p>
              <div className="grid grid-cols-3 gap-4 text-center">
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-[var(--muted-foreground)]">Turnover</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(period.sizeClassification.turnover)}</p>
                </div>
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-[var(--muted-foreground)]">Balance Sheet</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(period.sizeClassification.balanceSheetTotal)}</p>
                </div>
                <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-3">
                  <p className="text-xs text-[var(--muted-foreground)]">Avg Employees</p>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{period.sizeClassification.avgEmployees}</p>
                </div>
              </div>
              <p className="text-xs leading-5 text-[var(--muted-foreground)]">
                These figures are evidence for the following year&apos;s size test only. This period is not classified, and no
                filing regime or filing is produced from them here.
              </p>
              {isOwner && (
                <RetireHistoricFiguresPanel
                  companyId={cId}
                  periodId={pId}
                  onRetired={async () => {
                    setHistoricMode(false);
                    await loadData();
                  }}
                />
              )}
            </div>
          </Card.Content>
        </Card>
      )}

      {/* Historic figures an Owner retired: their evidence is in the archive below */}
      {holdsRetiredHistoricFigures && (
        <p role="status" className="mb-6 rounded-lg border border-gray-200 bg-gray-50 px-4 py-3 text-sm text-gray-700 dark:border-neutral-700 dark:bg-neutral-800 dark:text-gray-300">
          The historic figures recorded for this period were retired. Enter this period&apos;s own figures to classify it.
        </p>
      )}

      {/* Retired or replaced historic figures: kept as evidence, visible after the period is classified again */}
      <ArchivedHistoricFiguresCard companyId={cId} periodId={pId} records={archivedHistoricFigures} />

      {/* Pre-existing classification display (when loaded from period data) */}
      {!result && period?.sizeClassification && !holdsHistoricFigures && !holdsRetiredHistoricFigures && (
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
                  {period.sizeClassification.calculatedClass
                    ? "The retained decision is stale. Re-run classification before selecting a regime or using filing outputs."
                    : "These figures have been saved but not yet classified. Run classification before selecting a regime or using filing outputs."}
                </div>
              )}
              <div className="flex items-center justify-center">
                <Chip
                  color={classColorMap[period.sizeClassification.calculatedClass ?? ""] ?? "default"}
                  variant="primary"
                  size="lg"
                  className="text-lg px-6 py-3 font-bold"
                >
                  {period.sizeClassification.calculatedClass
                    ? `${period.sizeClassification.calculatedClass} Company`
                    : "Not yet classified"}
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
