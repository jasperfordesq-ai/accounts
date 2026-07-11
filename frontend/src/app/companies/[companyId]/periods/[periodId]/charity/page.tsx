"use client";

import { use, useState, useEffect, useCallback, useMemo, useRef, type KeyboardEvent } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import {
  Button,
  Card,
  Chip,
  Spinner,
} from "@heroui/react";
import {
  ArrowLeft,
  Heart,
  Plus,
  Trash2,
  RefreshCw,
  FileText,
  Users,
  DollarSign,
  Globe,
  Shield,
  Download,
  AlertTriangle,
  CheckCircle2,
} from "lucide-react";
import { toast as sonnerToast } from "sonner";
import {
  getCompany,
  getPeriod,
  getSofa,
  getTrusteesReport,
  getFundBalances,
  getFilingWorkflowStatus,
  createFundBalance,
  deleteFundBalance,
  recordCharityReportGenerated,
  updateCharityFilingStatus,
  getCharityArtifactStatus,
  recordCharityTrusteeReview,
  getCharitySofaReviewUrl,
  getCharityTrusteesReportReviewUrl,
  getCharitySofaFinalUrl,
  getCharityTrusteesReportFinalUrl,
  fetchDocumentBlob,
  type Company,
  type AccountingPeriod,
  type SofaData,
  type TrusteesReportData,
  type FundBalance,
  type CharityFilingStatus,
  type CharityArtifactStatus,
} from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import { useAuth } from "@/components/AuthProvider";
import { HorizontalScrollRegion, ReadOnlyNotice } from "@/components/workbench";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  completeResourceLoad,
  failResourceLoad,
  loadResourceGroup,
  type ResourceState,
} from "@/lib/resourceState";
import { InteractionAnnouncement } from "@/components/InteractionAnnouncement";
import {
  captureInteractionFocus,
  patchSearchHref,
  useInteractionAnnouncements,
  useLatestRequestSequence,
} from "@/lib/interactionState";

function eur(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

function WorkflowEvidenceRow({ label, done }: { label: string; done: boolean }) {
  return (
    <div className="flex items-center justify-between rounded-lg border border-gray-200 px-3 py-2 text-sm dark:border-neutral-700">
      <span className="font-medium text-gray-900 dark:text-gray-100">{label}</span>
      <Chip size="sm" variant="soft" color={done ? "success" : "warning"}>{done ? "Done" : "Open"}</Chip>
    </div>
  );
}

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";
const selectClass = inputClass;

const charityTabs = [
  { id: "sofa", label: "Statement of Financial Activities", icon: DollarSign },
  { id: "funds", label: "Fund Balances", icon: FileText },
  { id: "tar", label: "Trustees' Annual Report", icon: Users },
] as const;

type CharityTabId = (typeof charityTabs)[number]["id"];
const CHARITY_TAB_IDS = new Set<CharityTabId>(charityTabs.map((tab) => tab.id));

function normaliseCharityTab(value: string | null): CharityTabId {
  return value && CHARITY_TAB_IDS.has(value as CharityTabId) ? value as CharityTabId : "sofa";
}

export default function CharityReportingPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const { canApprove, canReview, canWriteWorkingPapers } = useAuth();
  const { announcement, announce } = useInteractionAnnouncements();
  const toast = useMemo(() => ({
    success(message: string) {
      sonnerToast.success(message);
      announce("success", message);
    },
    error(message: string) {
      sonnerToast.error(message);
      announce("error", message);
    },
    warning(message: string) {
      sonnerToast.warning(message);
      announce("warning", message);
    },
  }), [announce]);
  const shellRequestSequence = useLatestRequestSequence();
  const evidenceRequestSequence = useLatestRequestSequence();

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [sofa, setSofa] = useState<SofaData | null>(null);
  const [tar, setTar] = useState<TrusteesReportData | null>(null);
  const [funds, setFunds] = useState<FundBalance[]>([]);
  const [filingStatus, setFilingStatus] = useState<CharityFilingStatus | null>(null);
  const [artifactStatus, setArtifactStatus] = useState<CharityArtifactStatus | null>(null);
  const [shellState, setShellState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [evidenceState, setEvidenceState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [updatingWorkflow, setUpdatingWorkflow] = useState<string | null>(null);
  const [annualReturnReference, setAnnualReturnReference] = useState("");
  const [trusteeReviewReference, setTrusteeReviewReference] = useState("");
  const [trusteeReviewArtifact, setTrusteeReviewArtifact] = useState("");
  const [trusteeReviewAccepted, setTrusteeReviewAccepted] = useState(false);
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  // Add fund form
  const [showAddFund, setShowAddFund] = useState(false);
  const [newFundName, setNewFundName] = useState("");
  const [newFundType, setNewFundType] = useState("Unrestricted");
  const [newOpening, setNewOpening] = useState(0);
  const [newIncoming, setNewIncoming] = useState(0);
  const [newExpended, setNewExpended] = useState(0);
  const [newTransfers, setNewTransfers] = useState(0);
  const [newGains, setNewGains] = useState(0);
  const [addingFund, setAddingFund] = useState(false);

  // Active tab
  const [activeTab, setActiveTab] = useState<CharityTabId>(() => normaliseCharityTab(searchParams.get("tab")));
  const charityTabRefs = useRef<Partial<Record<CharityTabId, HTMLButtonElement | null>>>({});

  useEffect(() => {
    setActiveTab(normaliseCharityTab(searchParams.get("tab")));
  }, [searchParams]);

  const selectCharityTab = useCallback((tab: CharityTabId) => {
    setActiveTab(tab);
    const currentSearch = typeof window === "undefined" ? searchParams.toString() : window.location.search;
    router.push(patchSearchHref(pathname, currentSearch, { tab: tab === "sofa" ? null : tab }), { scroll: false });
  }, [pathname, router, searchParams]);

  const handleCharityTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, currentIndex: number) => {
    let nextIndex: number | null = null;
    if (event.key === "ArrowRight") nextIndex = (currentIndex + 1) % charityTabs.length;
    if (event.key === "ArrowLeft") nextIndex = (currentIndex - 1 + charityTabs.length) % charityTabs.length;
    if (event.key === "Home") nextIndex = 0;
    if (event.key === "End") nextIndex = charityTabs.length - 1;
    if (nextIndex === null) return;

    event.preventDefault();
    const nextTabId = charityTabs[nextIndex].id;
    selectCharityTab(nextTabId);
    charityTabRefs.current[nextTabId]?.focus();
  };

  const hasCharityDraft = useMemo(() => {
    const fundDraft = showAddFund && (
      newFundName !== ""
      || newFundType !== "Unrestricted"
      || newOpening !== 0
      || newIncoming !== 0
      || newExpended !== 0
      || newTransfers !== 0
      || newGains !== 0
    );
    const filingReferenceDraft = annualReturnReference !== (filingStatus?.annualReturnReference ?? "");
    const trusteeReviewDraft = trusteeReviewArtifact !== ""
      || trusteeReviewReference !== (artifactStatus?.package?.trusteeReviewReference ?? "")
      || trusteeReviewAccepted !== (artifactStatus?.package?.trusteeReviewAccepted ?? false);
    return fundDraft || filingReferenceDraft || trusteeReviewDraft;
  }, [
    annualReturnReference, artifactStatus, filingStatus, newExpended, newFundName, newFundType,
    newGains, newIncoming, newOpening, newTransfers, showAddFund, trusteeReviewAccepted,
    trusteeReviewArtifact, trusteeReviewReference,
  ]);
  useUnsavedChanges(hasCharityDraft);

  useEffect(() => () => {
    shellRequestSequence.invalidate();
    evidenceRequestSequence.invalidate();
  }, [evidenceRequestSequence, shellRequestSequence]);

  const loadShell = useCallback(async (onlyKeys?: string[]) => {
    const request = shellRequestSequence.begin();
    const loaders = {
      company: () => getCompany(cId),
      period: () => getPeriod(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setShellState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (!request.isLatest()) return;
    if (result.values.company) setCompany(result.values.company);
    if (result.values.period) setPeriod(result.values.period);
    if (result.failedResourceKeys.length === 0) setShellState(completeResourceLoad(false));
    else setShellState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId, shellRequestSequence]);

  const loadEvidence = useCallback(async (onlyKeys?: string[]) => {
    const request = evidenceRequestSequence.begin();
    const loaders = {
      sofa: () => getSofa(cId, pId),
      "trustees-report": () => getTrusteesReport(cId, pId),
      funds: () => getFundBalances(cId, pId),
      "filing-status": () => getFilingWorkflowStatus(cId, pId),
      "artifact-status": () => getCharityArtifactStatus(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setEvidenceState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (!request.isLatest()) return;
    if (result.values.sofa !== undefined) setSofa(result.values.sofa);
    if (result.values["trustees-report"] !== undefined) setTar(result.values["trustees-report"]);
    if (result.values.funds !== undefined) setFunds(result.values.funds);
    if (result.values["filing-status"] !== undefined) {
      const charityStatus = result.values["filing-status"].charity;
      setFilingStatus(charityStatus);
      setAnnualReturnReference(charityStatus.annualReturnReference ?? "");
    }
    if (result.values["artifact-status"] !== undefined) {
      const nextArtifactStatus = result.values["artifact-status"];
      setArtifactStatus(nextArtifactStatus);
      setTrusteeReviewReference(nextArtifactStatus.package?.trusteeReviewReference ?? "");
      setTrusteeReviewAccepted(nextArtifactStatus.package?.trusteeReviewAccepted ?? false);
    }
    if (result.failedResourceKeys.length === 0) setEvidenceState(completeResourceLoad(false));
    else setEvidenceState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, evidenceRequestSequence, pId]);

  const loadData = useCallback(async () => {
    await Promise.all([loadShell(), loadEvidence()]);
  }, [loadEvidence, loadShell]);

  useEffect(() => { loadData(); }, [loadData]);

  async function handleAddFund() {
    if (!newFundName.trim()) { toast.error("Fund name is required"); return; }
    const focus = captureInteractionFocus("charity-add-fund");
    setAddingFund(true);
    try {
      await createFundBalance(cId, pId, {
        fundName: newFundName.trim(),
        fundType: newFundType,
        openingBalance: newOpening,
        incomingResources: newIncoming,
        resourcesExpended: newExpended,
        transfers: newTransfers,
        gainsLosses: newGains,
        closingBalance: 0, // Computed by backend
      });
      toast.success("Fund added");
      setNewFundName(""); setNewOpening(0); setNewIncoming(0); setNewExpended(0); setNewTransfers(0); setNewGains(0);
      setShowAddFund(false);
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add fund");
    } finally {
      setAddingFund(false);
      focus.restore();
    }
  }

  async function handleDeleteFund(id: number) {
    try {
      await deleteFundBalance(cId, pId, id);
      toast.success("Fund removed");
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete fund");
      throw err;
    }
  }

  async function updateWorkflow(action: string, run: () => Promise<unknown>, success: string) {
    if (evidenceState.status === "loading"
      || evidenceState.status === "stale/retrying"
      || evidenceState.failedResourceKeys.length > 0
      || filingStatus == null) {
      toast.error("Required charity evidence is unavailable. Retry it before updating the annual return workflow.");
      return;
    }
    const focus = captureInteractionFocus("charity-annual-return-workflow");
    setUpdatingWorkflow(action);
    try {
      await run();
      toast.success(success);
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to update annual return workflow");
    } finally {
      setUpdatingWorkflow(null);
      focus.restore();
    }
  }

  if (shellState.status === "loading" && !shellState.hasRetainedData) return <PeriodWorkspaceSkeleton />;

  if (!company || !period) {
    return (
      <div className="mx-auto max-w-3xl py-8">
        <ResourceStateNotice state={shellState} label="charity company and period context" onRetry={() => loadShell(shellState.failedResourceKeys)} />
      </div>
    );
  }

  async function retainTrusteeReview() {
    if (!trusteeReviewAccepted) {
      toast.error("Explicitly accept the trustee population before retaining the review");
      return;
    }
    if (!trusteeReviewReference.trim() || !trusteeReviewArtifact) {
      toast.error("Trustee review reference and evidence artifact are required");
      return;
    }
    const focus = captureInteractionFocus();
    setUpdatingWorkflow("trustee-review");
    try {
      await recordCharityTrusteeReview(cId, pId, {
        accepted: true,
        evidenceReference: trusteeReviewReference.trim(),
        evidenceArtifact: trusteeReviewArtifact,
      });
      toast.success("Trustee population review retained; prior charity artifacts and approval were revoked");
      setTrusteeReviewArtifact("");
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to retain trustee review");
    } finally {
      setUpdatingWorkflow(null);
      focus.restore();
    }
  }

  async function downloadPdf(url: string, filename: string) {
    const focus = captureInteractionFocus();
    try {
      const blob = await fetchDocumentBlob(url);
      const objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = objectUrl;
      anchor.download = filename;
      anchor.click();
      URL.revokeObjectURL(objectUrl);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "PDF download failed");
    } finally {
      focus.restore();
    }
  }

  const workflowEvidenceUnavailable = evidenceState.status === "loading"
    || evidenceState.status === "stale/retrying"
    || evidenceState.failedResourceKeys.length > 0
    || filingStatus == null;
  const fundEvidenceUnavailable = evidenceState.status === "loading"
    || evidenceState.status === "stale/retrying"
    || evidenceState.failedResourceKeys.includes("funds");

  return (
    <div className="animate-fade-in">
      <InteractionAnnouncement announcement={announcement} />
      <Breadcrumbs
        items={[
          { label: company?.legalName ?? "Company", href: `/companies/${companyId}` },
          { label: "Period", href: `/companies/${companyId}/periods/${periodId}` },
          { label: "Charity Reporting" },
        ]}
      />

      <div className="mb-4 space-y-3">
        <ResourceStateNotice state={shellState} label="charity company and period context" onRetry={() => loadShell(shellState.failedResourceKeys)} />
        <ResourceStateNotice
          state={evidenceState}
          label="charity reporting evidence"
          onRetry={() => loadEvidence(evidenceState.failedResourceKeys)}
        />
      </div>

      {!canWriteWorkingPapers && (
        <ReadOnlyNotice
          subject="charity reporting"
          detail={canReview
            ? "You can inspect retained charity evidence and advance the reviewed filing workflow; editing fund evidence requires Owner or Accountant access."
            : undefined}
        />
      )}

      <div className="mb-6">
        <Link
          href={`/companies/${companyId}/periods/${periodId}`}
          className="inline-flex items-center gap-1.5 text-sm text-emerald-700 hover:text-emerald-800 dark:text-emerald-400 dark:hover:text-emerald-300 mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100 flex items-center gap-2">
          <Heart className="w-7 h-7 text-emerald-600 dark:text-emerald-400" />
          Charity Reporting
        </h1>
        <p className="text-sm text-[var(--muted-foreground)] mt-1">
          {company?.legalName} &mdash; period-specific charity reporting decision &mdash;{" "}
          {period ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}` : ""}
        </p>
      </div>

      {artifactStatus && (
        <div className={`mb-6 rounded-lg border p-4 ${artifactStatus.decision.manualProfessionalHandoffRequired
          ? "border-amber-300 bg-amber-50 text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100"
          : "border-emerald-300 bg-emerald-50 text-emerald-950 dark:border-emerald-800 dark:bg-emerald-950/30 dark:text-emerald-100"}`}>
          <div className="flex items-start gap-3">
            {artifactStatus.decision.manualProfessionalHandoffRequired
              ? <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
              : <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0" />}
            <div>
              <p className="font-semibold">
                {artifactStatus.decision.frameworkCode}
                {artifactStatus.decision.tier ? ` - Tier ${artifactStatus.decision.tier}` : " - tier requires professional determination"}
              </p>
              <p className="mt-1 text-sm">{artifactStatus.decision.decisionReason}</p>
              <p className="mt-2 text-xs">
                Decision evidence {artifactStatus.decision.decisionSha256.slice(0, 16)}...; final PDF export remains blocked until exact-artifact qualified-accountant approval.
              </p>
            </div>
          </div>
        </div>
      )}

      {company?.isCharitableOrganisation && (
        <Card id="charity-annual-return-workflow" tabIndex={-1} className="mb-6 border border-gray-200 bg-white shadow-sm outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 dark:border-neutral-700 dark:bg-neutral-900">
          <Card.Header>
            <div className="flex flex-wrap items-center justify-between gap-3 w-full">
              <div>
                <Card.Title className="text-gray-900 dark:text-gray-100">Annual Return Workflow</Card.Title>
                <Card.Description>Retained PDF preparation and external workflow record - no direct Regulator submission</Card.Description>
              </div>
              <Chip size="sm" variant="soft" color={filingStatus?.status === "Accepted" ? "success" : filingStatus?.status === "Submitted" ? "accent" : filingStatus?.status === "Approved" ? "warning" : "default"}>
                {evidenceState.failedResourceKeys.includes("filing-status")
                  ? "Unavailable"
                  : filingStatus?.status === "Submitted"
                    ? "External submission recorded"
                    : filingStatus?.status ?? "Loading"}
              </Chip>
            </div>
          </Card.Header>
          <Card.Content className="space-y-5">
            <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800/60">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">Trustee population review</p>
                  <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                    Includes directors appointed by period end who had not resigned before period start. Missing appointment dates block review.
                  </p>
                </div>
                <Chip size="sm" variant="soft" color={artifactStatus?.package?.trusteeReviewAccepted ? "success" : "warning"}>
                  {artifactStatus?.package?.trusteeReviewAccepted ? "Retained" : "Required"}
                </Chip>
              </div>
              {canWriteWorkingPapers && (
                <div className="mt-3 grid gap-3 lg:grid-cols-[1fr_1fr_auto] lg:items-end">
                  <label className="text-xs font-medium text-gray-600 dark:text-gray-400">
                    Review evidence reference
                    <input
                      className={`${inputClass} mt-1`}
                      value={trusteeReviewReference}
                      onChange={(event) => setTrusteeReviewReference(event.target.value)}
                      placeholder="Board minute / working-paper reference"
                    />
                  </label>
                  <label className="text-xs font-medium text-gray-600 dark:text-gray-400">
                    Review evidence artifact
                    <input
                      type="file"
                      className={`${inputClass} mt-1`}
                      onChange={(event) => {
                        const file = event.target.files?.[0];
                        if (!file) { setTrusteeReviewArtifact(""); return; }
                        void file.arrayBuffer().then((buffer) => {
                          const bytes = new Uint8Array(buffer);
                          let binary = "";
                          for (const byte of bytes) binary += String.fromCharCode(byte);
                          setTrusteeReviewArtifact(btoa(binary));
                        });
                      }}
                    />
                  </label>
                  <div className="space-y-2">
                    <label className="flex items-center gap-2 text-xs font-medium text-gray-700 dark:text-gray-300">
                      <input
                        type="checkbox"
                        checked={trusteeReviewAccepted}
                        onChange={(event) => setTrusteeReviewAccepted(event.target.checked)}
                      />
                      Population accepted
                    </label>
                    <Button
                      size="sm"
                      variant="outline"
                      isDisabled={updatingWorkflow !== null || !trusteeReviewArtifact}
                      onPress={retainTrusteeReview}
                    >
                      Retain review
                    </Button>
                  </div>
                </div>
              )}
            </div>
            <div className="grid gap-4 lg:grid-cols-[1fr_auto] lg:items-end">
              <div className="grid gap-3 sm:grid-cols-2">
                {canWriteWorkingPapers ? <button
                  type="button"
                  onClick={() => updateWorkflow("sofa", () => recordCharityReportGenerated(cId, pId, "sofa"), "Retained SoFA PDF generated")}
                  disabled={workflowEvidenceUnavailable || filingStatus?.sofaGenerated || updatingWorkflow !== null || !artifactStatus?.package?.trusteeReviewAccepted || artifactStatus?.decision.manualProfessionalHandoffRequired}
                  className="flex items-center justify-between rounded-lg border border-[var(--control-border)] px-3 py-2 text-left text-sm transition hover:bg-gray-50 disabled:cursor-not-allowed disabled:bg-[var(--muted-surface)] dark:hover:bg-neutral-800"
                >
                  <span className="font-medium text-gray-900 dark:text-gray-100">Generate retained SoFA PDF</span>
                  <Chip size="sm" variant="soft" color={filingStatus?.sofaGenerated ? "success" : "warning"}>{filingStatus?.sofaGenerated ? "Done" : "Open"}</Chip>
                </button> : <WorkflowEvidenceRow label="SoFA generated" done={Boolean(filingStatus?.sofaGenerated)} />}
                {canWriteWorkingPapers ? <button
                  type="button"
                  onClick={() => updateWorkflow("tar", () => recordCharityReportGenerated(cId, pId, "trustees-report"), "Retained Trustees' Annual Report PDF generated")}
                  disabled={workflowEvidenceUnavailable || filingStatus?.trusteesReportGenerated || updatingWorkflow !== null || !artifactStatus?.package?.trusteeReviewAccepted || artifactStatus?.decision.manualProfessionalHandoffRequired}
                  className="flex items-center justify-between rounded-lg border border-[var(--control-border)] px-3 py-2 text-left text-sm transition hover:bg-gray-50 disabled:cursor-not-allowed disabled:bg-[var(--muted-surface)] dark:hover:bg-neutral-800"
                >
                  <span className="font-medium text-gray-900 dark:text-gray-100">Generate retained Trustees&apos; Annual Report PDF</span>
                  <Chip size="sm" variant="soft" color={filingStatus?.trusteesReportGenerated ? "success" : "warning"}>{filingStatus?.trusteesReportGenerated ? "Done" : "Open"}</Chip>
                </button> : <WorkflowEvidenceRow label="Trustees' report generated" done={Boolean(filingStatus?.trusteesReportGenerated)} />}
                {canReview && <div className="sm:col-span-2">
                  <label htmlFor="charity-annual-return-reference" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Annual return reference</label>
                  <input
                    id="charity-annual-return-reference"
                    className={inputClass}
                    value={annualReturnReference}
                    onChange={(event) => setAnnualReturnReference(event.target.value)}
                    placeholder="CRA annual return reference"
                    aria-label="Charities Regulator annual return reference"
                    disabled={workflowEvidenceUnavailable}
                  />
                </div>}
              </div>
              <div className="flex flex-wrap gap-2">
                {canApprove && filingStatus?.status !== "Approved" && filingStatus?.status !== "Submitted" && filingStatus?.status !== "Accepted" && (
                  <Chip size="sm" variant="soft" color="warning">Qualified-accountant evidence approval required</Chip>
                )}
                {canReview && filingStatus?.status === "Approved" ? (
                  <Button
                    size="sm"
                    variant="primary"
                    aria-label="Record external submission with Charities Regulator"
                    isDisabled={workflowEvidenceUnavailable || updatingWorkflow !== null}
                    onPress={() => {
                      const reference = annualReturnReference.trim();
                      if (!reference) { toast.error("Annual return reference is required"); return; }
                      updateWorkflow("submit", () => updateCharityFilingStatus(cId, pId, { status: "Submitted", annualReturnReference: reference }), "External Charities Regulator submission recorded");
                    }}
                  >
                    {updatingWorkflow === "submit" ? <Spinner size="sm" /> : "Record external submission"}
                  </Button>
                ) : canReview && filingStatus?.status === "Submitted" ? (
                  <Button
                    size="sm"
                    variant="outline"
                    aria-label="Record regulator acceptance — Charities Regulator"
                    isDisabled={workflowEvidenceUnavailable || updatingWorkflow !== null}
                    onPress={() => updateWorkflow("accept", () => updateCharityFilingStatus(cId, pId, { status: "Accepted" }), "External Charities Regulator acceptance recorded")}
                  >
                    {updatingWorkflow === "accept" ? <Spinner size="sm" /> : "Record regulator acceptance"}
                  </Button>
                ) : filingStatus?.status === "Accepted" ? (
                  <Chip size="sm" variant="soft" color="success">Accepted</Chip>
                ) : null}
              </div>
            </div>
            <div className="flex flex-wrap gap-2 border-t border-gray-200 pt-4 dark:border-neutral-700">
              <Button size="sm" variant="outline" onPress={() => downloadPdf(getCharitySofaReviewUrl(cId, pId), `REVIEW_charity_sofa_${pId}.pdf`)}>
                <Download className="h-4 w-4" /> SoFA review PDF
              </Button>
              <Button size="sm" variant="outline" onPress={() => downloadPdf(getCharityTrusteesReportReviewUrl(cId, pId), `REVIEW_trustees_report_${pId}.pdf`)}>
                <Download className="h-4 w-4" /> TAR review PDF
              </Button>
              <Button size="sm" variant="primary" isDisabled={filingStatus?.status !== "Approved" && filingStatus?.status !== "Submitted" && filingStatus?.status !== "Accepted"} onPress={() => downloadPdf(getCharitySofaFinalUrl(cId, pId), `charity_sofa_${pId}.pdf`)}>
                <Download className="h-4 w-4" /> Final approved SoFA
              </Button>
              <Button size="sm" variant="primary" isDisabled={filingStatus?.status !== "Approved" && filingStatus?.status !== "Submitted" && filingStatus?.status !== "Accepted"} onPress={() => downloadPdf(getCharityTrusteesReportFinalUrl(cId, pId), `trustees_annual_report_${pId}.pdf`)}>
                <Download className="h-4 w-4" /> Final approved TAR
              </Button>
            </div>
          </Card.Content>
        </Card>
      )}

      {/* Tab Navigation */}
      <div className="mb-6 space-y-2 no-print">
        <p
          id="charity-reporting-tab-help"
          role="note"
          aria-label="Charity reporting tab navigation instructions"
          className="rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-xs font-medium text-sky-900 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-100"
        >
          Swipe to reveal more charity tabs. Use Left and Right Arrow keys, Home, or End to move between tabs.
        </p>
        <div
          role="tablist"
          aria-label="Charity reporting tabs"
          aria-describedby="charity-reporting-tab-help"
          data-overflow-tablist="true"
          className="flex max-w-full gap-1 overflow-x-auto overscroll-x-contain whitespace-nowrap border-b border-gray-200 dark:border-neutral-700"
        >
        {charityTabs.map((tab, index) => (
          <button
            key={tab.id}
            ref={(element) => {
              charityTabRefs.current[tab.id] = element;
            }}
            id={`charity-tab-${tab.id}`}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.id}
            aria-controls={`charity-panel-${tab.id}`}
            tabIndex={activeTab === tab.id ? 0 : -1}
            onClick={() => selectCharityTab(tab.id)}
            onKeyDown={(event) => handleCharityTabKeyDown(event, index)}
            className={`shrink-0 px-4 py-2.5 text-sm font-medium border-b-2 cursor-pointer outline-none transition-colors focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 ${
              activeTab === tab.id
                ? "border-emerald-600 text-emerald-700 dark:text-emerald-400"
                : "border-transparent text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-200"
            }`}
          >
            <tab.icon className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            {tab.label}
          </button>
        ))}
        </div>
      </div>

      {/* SoFA Tab */}
      {activeTab === "sofa" && (
        <div id="charity-panel-sofa" role="tabpanel" aria-labelledby="charity-tab-sofa" tabIndex={0} className="space-y-6 outline-none focus-visible:ring-2 focus-visible:ring-emerald-500">
          {sofa ? (
            <>
              {/* Income & Expenditure Summary */}
              <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-4">
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Total Income</p>
                    <p className="text-xl font-bold text-emerald-700 dark:text-emerald-400 mt-1">{eur(sofa.totalIncoming)}</p>
                  </Card.Content>
                </Card>
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Total Expenditure</p>
                    <p className="text-xl font-bold text-red-700 dark:text-red-400 mt-1">{eur(sofa.totalExpended)}</p>
                  </Card.Content>
                </Card>
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Net Movement</p>
                    <p className={`text-xl font-bold mt-1 ${sofa.netMovement >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                      {eur(sofa.netMovement)}
                    </p>
                  </Card.Content>
                </Card>
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Total Funds</p>
                    <p className="text-xl font-bold text-gray-900 dark:text-gray-100 mt-1">{eur(sofa.totalClosingFunds)}</p>
                  </Card.Content>
                </Card>
              </div>

              {/* Fund Columns */}
              {[
                { title: "Unrestricted Funds", funds: sofa.unrestrictedFunds },
                { title: "Restricted Funds", funds: sofa.restrictedFunds },
                { title: "Endowment Funds", funds: sofa.endowmentFunds },
              ].map(({ title, funds: fundList }) => (
                fundList.length > 0 && (
                  <Card key={title} className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                    <Card.Header>
                      <Card.Title className="text-gray-900 dark:text-gray-100">{title}</Card.Title>
                    </Card.Header>
                    <Card.Content>
                      <HorizontalScrollRegion label={`${title} table`}>
                        <table className="min-w-[58rem] w-full text-sm">
                          <thead>
                            <tr className="bg-gray-50 dark:bg-neutral-800 text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wide">
                              <th className="text-left px-3 py-2">Fund</th>
                              <th className="text-right px-3 py-2">Opening</th>
                              <th className="text-right px-3 py-2">Incoming</th>
                              <th className="text-right px-3 py-2">Expended</th>
                              <th className="text-right px-3 py-2">Transfers</th>
                              <th className="text-right px-3 py-2">Gains/Losses</th>
                              <th className="text-right px-3 py-2">Closing</th>
                            </tr>
                          </thead>
                          <tbody>
                            {fundList.map((f, idx) => (
                              <tr key={idx} className={`border-t border-gray-100 dark:border-neutral-700 ${idx % 2 === 1 ? "bg-gray-50/50 dark:bg-neutral-800/25" : ""}`}>
                                <td className="px-3 py-2 text-gray-900 dark:text-gray-100 font-medium">{f.fundName}</td>
                                <td className="px-3 py-2 text-right font-mono text-gray-700 dark:text-gray-300">{eur(f.openingBalance)}</td>
                                <td className="px-3 py-2 text-right font-mono text-emerald-700 dark:text-emerald-400">{eur(f.incomingResources)}</td>
                                <td className="px-3 py-2 text-right font-mono text-red-700 dark:text-red-400">{eur(f.resourcesExpended)}</td>
                                <td className="px-3 py-2 text-right font-mono text-gray-700 dark:text-gray-300">{eur(f.transfers)}</td>
                                <td className="px-3 py-2 text-right font-mono text-gray-700 dark:text-gray-300">{eur(f.gainsLosses)}</td>
                                <td className="px-3 py-2 text-right font-mono font-bold text-gray-900 dark:text-gray-100">{eur(f.closingBalance)}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </HorizontalScrollRegion>
                    </Card.Content>
                  </Card>
                )
              ))}

              {sofa.unrestrictedFunds.length === 0 && sofa.restrictedFunds.length === 0 && sofa.endowmentFunds.length === 0 && (
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="text-center py-12">
                    <DollarSign className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
                    <p className="text-sm text-[var(--muted-foreground)]">No fund balances entered yet.</p>
                    <p className="text-xs text-[var(--muted-foreground)] mt-1">Go to the Fund Balances tab to add funds.</p>
                  </Card.Content>
                </Card>
              )}
            </>
          ) : (
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Content className="text-center py-12">
                <DollarSign className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
                <p className="text-sm text-[var(--muted-foreground)]">
                  {evidenceState.failedResourceKeys.includes("sofa")
                    ? "Statement of Financial Activities evidence failed to load."
                    : "Statement of Financial Activities not available."}
                </p>
                <p className="text-xs text-[var(--muted-foreground)] mt-1">
                  {evidenceState.failedResourceKeys.includes("sofa")
                    ? "Retry the failed SoFA resource before relying on this view."
                    : "Add fund balances first, then the SoFA will be generated automatically."}
                </p>
              </Card.Content>
            </Card>
          )}
        </div>
      )}

      {/* Fund Balances Tab */}
      {activeTab === "funds" && (
        <div id="charity-panel-funds" role="tabpanel" aria-labelledby="charity-tab-funds" tabIndex={0} className="space-y-6 outline-none focus-visible:ring-2 focus-visible:ring-emerald-500">
          <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
            <Card.Header>
              <div className="flex items-center justify-between w-full">
                <div>
                  <Card.Title className="text-gray-900 dark:text-gray-100">Fund Balances</Card.Title>
                  <Card.Description>{funds.length} fund{funds.length !== 1 ? "s" : ""} configured</Card.Description>
                </div>
                <div className="flex items-center gap-2">
                  {canWriteWorkingPapers && (
                    <Button id="charity-add-fund" variant="primary" size="sm" onPress={() => setShowAddFund(true)} isDisabled={fundEvidenceUnavailable}>
                      <Plus className="w-4 h-4 mr-1" /> Add Fund
                    </Button>
                  )}
                  <Button variant="ghost" size="sm" isIconOnly aria-label="Refresh charity fund balances" onPress={loadData}>
                    <RefreshCw className="w-4 h-4" />
                  </Button>
                </div>
              </div>
            </Card.Header>
            <Card.Content>
              {canWriteWorkingPapers && showAddFund && (
                <div className="mb-6 p-4 rounded-lg border border-emerald-200 dark:border-emerald-800 bg-emerald-50/30 dark:bg-emerald-900/10 animate-slide-down">
                  <h4 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-3">Add New Fund</h4>
                  <div className="grid gap-3 sm:grid-cols-2 md:grid-cols-3">
                    <div>
                      <label htmlFor="charity-fund-name" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Fund Name *</label>
                      <input id="charity-fund-name" className={inputClass} value={newFundName} onChange={(e) => setNewFundName(e.target.value)} placeholder="e.g. General Fund" />
                    </div>
                    <div>
                      <label htmlFor="charity-fund-type" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Fund Type</label>
                      <select id="charity-fund-type" className={selectClass} value={newFundType} onChange={(e) => setNewFundType(e.target.value)} title="Fund type" aria-label="Fund type">
                        <option value="Unrestricted">Unrestricted</option>
                        <option value="Designated">Designated</option>
                        <option value="Restricted">Restricted</option>
                        <option value="Endowment">Endowment</option>
                      </select>
                    </div>
                    <div>
                      <label htmlFor="charity-fund-opening-balance" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Opening Balance</label>
                      <input id="charity-fund-opening-balance" type="number" className={inputClass} value={newOpening || ""} onChange={(e) => setNewOpening(Number(e.target.value))} placeholder="0.00" />
                    </div>
                    <div>
                      <label htmlFor="charity-fund-incoming-resources" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Incoming Resources</label>
                      <input id="charity-fund-incoming-resources" type="number" className={inputClass} value={newIncoming || ""} onChange={(e) => setNewIncoming(Number(e.target.value))} placeholder="0.00" />
                    </div>
                    <div>
                      <label htmlFor="charity-fund-resources-expended" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Resources Expended</label>
                      <input id="charity-fund-resources-expended" type="number" className={inputClass} value={newExpended || ""} onChange={(e) => setNewExpended(Number(e.target.value))} placeholder="0.00" />
                    </div>
                    <div>
                      <label htmlFor="charity-fund-transfers" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Transfers</label>
                      <input id="charity-fund-transfers" type="number" className={inputClass} value={newTransfers || ""} onChange={(e) => setNewTransfers(Number(e.target.value))} placeholder="0.00" />
                    </div>
                  </div>
                  <div className="flex items-center gap-3 mt-4">
                    <Button variant="primary" size="sm" aria-label="Create Fund — charity" onPress={handleAddFund} isDisabled={addingFund || !newFundName.trim()}>
                      {addingFund ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Create Fund</>}
                    </Button>
                    <Button variant="ghost" size="sm" onPress={() => setShowAddFund(false)}>Cancel</Button>
                  </div>
                </div>
              )}

              {funds.length === 0 ? (
                <div className="text-center py-8">
                  <FileText className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
                  <p className="text-sm text-[var(--muted-foreground)]">
                    {evidenceState.failedResourceKeys.includes("funds") ? "Fund balance evidence failed to load." : "No fund balances recorded."}
                  </p>
                  <p className="text-xs text-[var(--muted-foreground)] mt-1">
                    {evidenceState.failedResourceKeys.includes("funds")
                      ? "Retry the failed funds resource; this is not evidence that no funds exist."
                      : "Add unrestricted, restricted, and endowment funds to generate the SoFA."}
                  </p>
                </div>
              ) : (
                <div className="space-y-3">
                  {funds.map((fund) => (
                    <div
                      key={fund.id}
                      className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 hover:bg-gray-50 dark:hover:bg-neutral-800/50 transition-colors"
                    >
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{fund.fundName}</p>
                          <Chip size="sm" variant="soft" color={
                            fund.fundType === "Restricted" ? "accent" :
                            fund.fundType === "Endowment" ? "warning" :
                            fund.fundType === "Designated" ? "default" : "success"
                          }>
                            {fund.fundType}
                          </Chip>
                        </div>
                        <div className="flex items-center gap-4 text-xs text-[var(--muted-foreground)]">
                          <span>Opening: {eur(fund.openingBalance)}</span>
                          <span className="text-[var(--emerald-700)]">In: {eur(fund.incomingResources)}</span>
                          <span className="text-red-600 dark:text-red-400">Out: {eur(fund.resourcesExpended)}</span>
                          <span className="font-medium text-gray-900 dark:text-gray-100">Closing: {eur(fund.closingBalance)}</span>
                        </div>
                      </div>
                      {canWriteWorkingPapers && (
                        <Button
                          variant="ghost"
                          size="sm"
                          isIconOnly
                          onPress={() => requestDestructiveAction({
                            recordLabel: `charity fund ${fund.fundName}`,
                            consequence: `This permanently removes the ${fund.fundType} fund and its ${eur(fund.closingBalance)} closing balance from the retained SoFA working papers. The removal cannot be undone.`,
                            onConfirm: () => handleDeleteFund(fund.id!),
                            successAnnouncement: `Charity fund ${fund.fundName} was removed.`,
                          })}
                          aria-label={`Delete fund ${fund.fundName}`}
                        >
                          <Trash2 className="w-4 h-4 text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300" />
                        </Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </Card.Content>
          </Card>
        </div>
      )}

      {/* Trustees' Annual Report Tab */}
      {activeTab === "tar" && (
        <div id="charity-panel-tar" role="tabpanel" aria-labelledby="charity-tab-tar" tabIndex={0} className="space-y-6 outline-none focus-visible:ring-2 focus-visible:ring-emerald-500">
          {tar ? (
            <>
              {/* Header Info */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Content className="p-5">
                  <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-4">
                    <div>
                      <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Charity Number</p>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mt-1">{tar.charityNumber || "—"}</p>
                    </div>
                    <div>
                      <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">SORP decision</p>
                      <Chip size="sm" variant="soft" color={artifactStatus?.decision.manualProfessionalHandoffRequired ? "warning" : "success"} className="mt-1">
                        {artifactStatus?.decision.frameworkCode ?? "Unavailable"}{artifactStatus?.decision.tier ? ` / Tier ${artifactStatus.decision.tier}` : ""}
                      </Chip>
                    </div>
                    <div>
                      <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Filing Deadline</p>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mt-1">{tar.filingDeadline}</p>
                    </div>
                    <div>
                      <p className="text-xs text-[var(--muted-foreground)] uppercase tracking-wide">Trustees</p>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mt-1">{tar.trustees.length}</p>
                    </div>
                  </div>
                </Card.Content>
              </Card>

              {/* Trustees */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Trustees</Card.Title></Card.Header>
                <Card.Content>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {tar.trustees.map((trustee) => (
                      <div key={trustee.officerId} className="rounded-lg border border-gray-200 p-3 text-sm dark:border-neutral-700">
                        <p className="font-medium text-gray-900 dark:text-gray-100"><Users className="mr-1 inline h-3.5 w-3.5" />{trustee.name}</p>
                        <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                          Appointed {new Date(`${trustee.appointedDate}T00:00:00`).toLocaleDateString("en-IE")}
                          {trustee.resignedDate ? `; resigned ${new Date(`${trustee.resignedDate}T00:00:00`).toLocaleDateString("en-IE")}` : ""}
                        </p>
                      </div>
                    ))}
                  </div>
                </Card.Content>
              </Card>

              {/* Objectives & Activities */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Objectives &amp; Activities</Card.Title></Card.Header>
                <Card.Content className="space-y-3">
                  <div>
                    <p className="text-xs font-medium text-[var(--muted-foreground)] uppercase tracking-wide mb-1">Charitable Objectives</p>
                    <p className="text-sm text-gray-900 dark:text-gray-100">{tar.charitableObjectives}</p>
                  </div>
                  <div>
                    <p className="text-xs font-medium text-[var(--muted-foreground)] uppercase tracking-wide mb-1">Principal Activities</p>
                    <p className="text-sm text-gray-900 dark:text-gray-100">{tar.principalActivities}</p>
                  </div>
                </Card.Content>
              </Card>

              {/* Financial Review */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Financial Review</Card.Title></Card.Header>
                <Card.Content>
                  <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-4">
                    <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 p-4 text-center">
                      <p className="text-xs text-[var(--muted-foreground)]">Total Income</p>
                      <p className="text-lg font-bold text-emerald-700 dark:text-emerald-400 mt-1">{eur(tar.totalIncome)}</p>
                    </div>
                    <div className="rounded-lg bg-red-50 dark:bg-red-900/20 p-4 text-center">
                      <p className="text-xs text-[var(--muted-foreground)]">Total Expenditure</p>
                      <p className="text-lg font-bold text-red-700 dark:text-red-400 mt-1">{eur(tar.totalExpenditure)}</p>
                    </div>
                    <div className="rounded-lg bg-blue-50 dark:bg-blue-900/20 p-4 text-center">
                      <p className="text-xs text-[var(--muted-foreground)]">Net Movement</p>
                      <p className={`text-lg font-bold mt-1 ${tar.netMovement >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>{eur(tar.netMovement)}</p>
                    </div>
                    <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                      <p className="text-xs text-[var(--muted-foreground)]">Closing Funds</p>
                      <p className="text-lg font-bold text-gray-900 dark:text-gray-100 mt-1">{eur(tar.closingFunds)}</p>
                    </div>
                  </div>
                </Card.Content>
              </Card>

              {/* Governance & Compliance */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Governance &amp; Compliance</Card.Title></Card.Header>
                <Card.Content className="space-y-4">
                  <div className="flex items-center gap-3">
                    <Shield className={`w-5 h-5 ${tar.governanceCodeCompliant ? "text-emerald-500" : "text-red-500"}`} />
                    <span className="text-sm text-gray-900 dark:text-gray-100">
                      Charities Governance Code: {tar.governanceCodeCompliant ? "Compliant" : "Non-compliant"}
                    </span>
                  </div>
                  {tar.governanceCodeNote && (
                    <p className="text-sm text-[var(--muted-foreground)] pl-8">{tar.governanceCodeNote}</p>
                  )}
                  <p className="text-sm text-[var(--muted-foreground)] pl-8">
                    Evidence {tar.governanceEvidenceReference}; reviewed by {tar.governanceReviewedBy} at {new Date(tar.governanceReviewedAtUtc).toLocaleString("en-IE")}.
                  </p>

                  <div className="flex items-center gap-3">
                    <DollarSign className={`w-5 h-5 ${tar.trusteeRemunerationPaid ? "text-amber-500" : "text-emerald-500"}`} />
                    <span className="text-sm text-gray-900 dark:text-gray-100">
                      Trustee Remuneration: {tar.trusteeRemunerationPaid ? eur(tar.trusteeRemunerationAmount) : "None paid"}
                    </span>
                  </div>
                  {tar.trusteeExpensesDetails && (
                    <p className="text-sm text-[var(--muted-foreground)] pl-8">{tar.trusteeExpensesDetails}</p>
                  )}

                  <div className="flex items-center gap-3">
                    <Globe className={`w-5 h-5 ${tar.hasInternationalTransfers ? "text-amber-500" : "text-[var(--muted-foreground)]"}`} />
                    <span className="text-sm text-gray-900 dark:text-gray-100">
                      International Transfers: {tar.hasInternationalTransfers ? "Yes" : "None"}
                    </span>
                  </div>
                  {tar.internationalTransferDetails && (
                    <p className="text-sm text-[var(--muted-foreground)] pl-8">{tar.internationalTransferDetails}</p>
                  )}
                </Card.Content>
              </Card>
            </>
          ) : (
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Content className="text-center py-12">
                <Users className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
                <p className="text-sm text-[var(--muted-foreground)]">
                  {evidenceState.failedResourceKeys.includes("trustees-report")
                    ? "Trustees' Annual Report evidence failed to load."
                    : "Trustees' Annual Report not available."}
                </p>
                <p className="text-xs text-[var(--muted-foreground)] mt-1">
                  {evidenceState.failedResourceKeys.includes("trustees-report")
                    ? "Retry the failed trustees' report resource before relying on this view."
                    : "Configure charity info on the company page and add fund balances first."}
                </p>
              </Card.Content>
            </Card>
          )}
        </div>
      )}
      {destructiveActionConfirmation}
    </div>
  );
}
