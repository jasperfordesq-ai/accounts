"use client";

import { useCallback, useEffect, useState } from "react";
import {
  getCompanies,
  getProductionReadinessReport,
  getDashboardDeadlines,
  getQuarantinedCompanies,
  type Company,
  type FilingDeadline,
  type DashboardDeadlineState,
  type ProductionReadinessReport,
  type QuarantinedCompanySummary,
  ApiError,
} from "@/lib/api";
import { DashboardSkeleton } from "@/components/Skeleton";
import { useAuth } from "@/components/AuthProvider";
import { DashboardWorkbench } from "@/components/dashboard/DashboardWorkbench";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  completeResourceLoad,
  failResourceLoad,
  type ResourceState,
} from "@/lib/resourceState";
import {
  getDeadlineRiskQueue,
  retryDeadlineReminder,
  runDeadlineReminders,
  type DeadlineRiskQueueItem,
} from "@/lib/operations";

export default function Dashboard() {
  const { canCreateCompany, canDeleteCompany, canReviewReleaseEvidence, canReadInternalWorkingPapers, isOwner } = useAuth();
  const [companies, setCompanies] = useState<Company[]>([]);
  const [deadlines, setDeadlines] = useState<Record<number, FilingDeadline | null>>({});
  const [companyState, setCompanyState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [deadlineState, setDeadlineState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [deadlineUnavailableCompanyIds, setDeadlineUnavailableCompanyIds] = useState<number[]>([]);
  const [deadlineStates, setDeadlineStates] = useState<Record<number, DashboardDeadlineState>>({});
  const [readinessReport, setReadinessReport] = useState<ProductionReadinessReport | null>(null);
  const [readinessState, setReadinessState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [quarantinedCompanies, setQuarantinedCompanies] = useState<QuarantinedCompanySummary[]>([]);
  const [deadlineRiskItems, setDeadlineRiskItems] = useState<DeadlineRiskQueueItem[]>([]);
  const [deadlineRiskState, setDeadlineRiskState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [deadlineRiskBusyKey, setDeadlineRiskBusyKey] = useState<string | null>(null);
  const [deadlineRiskActionMessage, setDeadlineRiskActionMessage] = useState<string | null>(null);

  const loadCompanies = useCallback(async () => {
    setCompanyState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const data = await getCompanies();
      setCompanies(data);
      setCompanyState(completeResourceLoad(data.length === 0));
      return data;
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Failed to load companies";
      setCompanyState((current) => failResourceLoad({
        failedResourceKeys: ["companies"],
        errors: { companies: message },
      }, current.hasRetainedData));
      return null;
    }
  }, []);

  const loadReadiness = useCallback(async () => {
    setReadinessState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const report = await getProductionReadinessReport();
      setReadinessReport(report);
      setReadinessState(completeResourceLoad(false));
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Failed to load production readiness";
      setReadinessState((current) => failResourceLoad({
        failedResourceKeys: ["production-readiness"],
        errors: { "production-readiness": message },
      }, current.hasRetainedData));
    }
  }, []);

  const loadQuarantined = useCallback(async () => {
    if (!canDeleteCompany) {
      setQuarantinedCompanies([]);
      return;
    }
    try {
      setQuarantinedCompanies(await getQuarantinedCompanies());
    } catch {
      setQuarantinedCompanies([]);
    }
  }, [canDeleteCompany]);

  const loadDeadlines = useCallback(async (companyData: Company[]) => {
    setDeadlineState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const batch = await getDashboardDeadlines();
      if (batch.totalCompanies !== companyData.length) {
        throw new Error(`Dashboard deadline scope mismatch: expected ${companyData.length}, received ${batch.totalCompanies}`);
      }
      setDeadlines(Object.fromEntries(batch.items.map((item) => [item.companyId, item.deadline])));
      setDeadlineStates(Object.fromEntries(batch.items.map((item) => [item.companyId, item.state])));
      setDeadlineUnavailableCompanyIds(batch.items
        .filter((item) => item.state === "unavailable")
        .map((item) => item.companyId));
      setDeadlineState(completeResourceLoad(batch.totalCompanies === 0));
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Failed to load dashboard deadlines";
      setDeadlineUnavailableCompanyIds(companyData.map((company) => company.id));
      setDeadlineState((current) => failResourceLoad({
        failedResourceKeys: ["dashboard-deadlines"],
        errors: { "dashboard-deadlines": message },
      }, current.hasRetainedData));
    }
  }, []);

  const loadDeadlineRisk = useCallback(async () => {
    if (!canReadInternalWorkingPapers) {
      setDeadlineRiskItems([]);
      setDeadlineRiskState(INITIAL_RESOURCE_STATE);
      return;
    }
    setDeadlineRiskState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const items = await getDeadlineRiskQueue();
      setDeadlineRiskItems(items);
      setDeadlineRiskState(completeResourceLoad(items.length === 0));
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Failed to load reminder delivery risk";
      setDeadlineRiskState((current) => failResourceLoad({
        failedResourceKeys: ["deadline-reminder-risk"],
        errors: { "deadline-reminder-risk": message },
      }, current.hasRetainedData));
    }
  }, [canReadInternalWorkingPapers]);

  const retryReminder = useCallback(async (outboxId: string) => {
    setDeadlineRiskBusyKey(outboxId);
    setDeadlineRiskActionMessage(null);
    try {
      await retryDeadlineReminder(outboxId);
      setDeadlineRiskActionMessage("The failed delivery was returned to the immediate retry queue. No filing status was changed.");
      await loadDeadlineRisk();
    } catch (error) {
      setDeadlineRiskActionMessage(operationMessage(error));
    } finally {
      setDeadlineRiskBusyKey(null);
    }
  }, [loadDeadlineRisk]);

  const runDelivery = useCallback(async () => {
    setDeadlineRiskBusyKey("run");
    setDeadlineRiskActionMessage(null);
    try {
      const result = await runDeadlineReminders();
      setDeadlineRiskActionMessage(`Delivery cycle examined ${result.examinedCount}, delivered ${result.deliveredCount}, failed ${result.failedCount}, and cancelled ${result.cancelledCount}. Evidence ${result.evidenceSha256.slice(0, 12)}… retained.`);
      await loadDeadlineRisk();
    } catch (error) {
      setDeadlineRiskActionMessage(operationMessage(error));
    } finally {
      setDeadlineRiskBusyKey(null);
    }
  }, [loadDeadlineRisk]);

  useEffect(() => {
    async function loadDailyWork() {
      const data = await loadCompanies();
      if (data) await loadDeadlines(data);
    }

    // The daily queue must not wait for the much larger release-evidence report. Load company and
    // deadline work as one priority chain; release assurance and Owner quarantine data can settle
    // independently without delaying the first actionable row.
    void loadDailyWork();
    if (canReviewReleaseEvidence) void loadReadiness();
    void loadQuarantined();
    if (canReadInternalWorkingPapers) void loadDeadlineRisk();
  }, [canReadInternalWorkingPapers, canReviewReleaseEvidence, loadCompanies, loadDeadlineRisk, loadDeadlines, loadQuarantined, loadReadiness]);

  if (companyState.status === "loading" && !companyState.hasRetainedData) return <DashboardSkeleton />;

  return (
    <DashboardWorkbench
      companies={companies}
      deadlines={deadlines}
      canCreateCompany={canCreateCompany}
      canRecoverCompany={canDeleteCompany}
      canReviewReleaseEvidence={canReviewReleaseEvidence}
      readinessReport={readinessReport}
      companyState={companyState}
      readinessState={readinessState}
      deadlineState={deadlineState}
      deadlineUnavailableCompanyIds={deadlineUnavailableCompanyIds}
      deadlineStates={deadlineStates}
      canManageDeadlineRisk={canReadInternalWorkingPapers}
      canRunDeadlineDelivery={isOwner}
      deadlineRiskItems={deadlineRiskItems}
      deadlineRiskState={deadlineRiskState}
      deadlineRiskBusyKey={deadlineRiskBusyKey}
      deadlineRiskActionMessage={deadlineRiskActionMessage}
      onReloadDeadlineRisk={loadDeadlineRisk}
      onRetryDeadlineReminder={retryReminder}
      onRunDeadlineDelivery={runDelivery}
      quarantinedCompanies={quarantinedCompanies}
      onCompanyRecovered={async () => {
        const data = await loadCompanies();
        await loadQuarantined();
        if (data) await loadDeadlines(data);
      }}
      onRetryCompanies={async () => {
        const data = await loadCompanies();
        if (data) await loadDeadlines(data);
      }}
      onRetryReadiness={loadReadiness}
      onRetryDeadlines={() => loadDeadlines(companies)}
    />
  );
}

function operationMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 428)
    return "Recent password and TOTP verification is required before running this privileged reminder action.";
  if (error instanceof ApiError && error.status === 404)
    return "That delivery is no longer retryable. Refresh the queue to see its current state.";
  return error instanceof Error ? error.message : "The deadline-reminder operation could not be completed.";
}
