"use client";

import Link from "next/link";
import { Plus } from "lucide-react";
import type { Company, DashboardDeadlineState, FilingDeadline, ProductionReadinessReport, QuarantinedCompanySummary } from "@/lib/api";
import { AccountantDashboardQueue } from "@/components/dashboard/AccountantDashboardQueue";
import { DashboardCompanyDirectory } from "@/components/dashboard/DashboardCompanyDirectory";
import { DashboardPracticeSummary } from "@/components/dashboard/DashboardPracticeSummary";
import { DashboardProductionSummary } from "@/components/dashboard/DashboardProductionSummary";
import { QuarantinedCompanyRecoveryPanel } from "@/components/dashboard/QuarantinedCompanyRecoveryPanel";
import { PageShell, StatusBadge } from "@/components/workbench";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import type { ResourceState } from "@/lib/resourceState";
import { INITIAL_RESOURCE_STATE } from "@/lib/resourceState";
import { DeadlineRiskQueue } from "@/components/dashboard/DeadlineRiskQueue";
import type { DeadlineRiskQueueItem } from "@/lib/operations";

interface DashboardWorkbenchProps {
  companies: Company[];
  deadlines: Record<number, FilingDeadline | null>;
  canCreateCompany: boolean;
  canRecoverCompany: boolean;
  canReviewReleaseEvidence: boolean;
  readinessReport: ProductionReadinessReport | null;
  companyState?: ResourceState;
  readinessState?: ResourceState;
  deadlineState?: ResourceState;
  deadlineUnavailableCompanyIds?: number[];
  deadlineStates?: Record<number, DashboardDeadlineState>;
  quarantinedCompanies?: QuarantinedCompanySummary[];
  onCompanyRecovered?: () => void | Promise<void>;
  onRetryCompanies?: () => void | Promise<void>;
  onRetryReadiness?: () => void | Promise<void>;
  onRetryDeadlines?: () => void | Promise<void>;
  canManageDeadlineRisk?: boolean;
  canRunDeadlineDelivery?: boolean;
  deadlineRiskItems?: DeadlineRiskQueueItem[];
  deadlineRiskState?: ResourceState;
  deadlineRiskBusyKey?: string | null;
  deadlineRiskActionMessage?: string | null;
  onReloadDeadlineRisk?: () => void | Promise<void>;
  onRetryDeadlineReminder?: (outboxId: string) => void | Promise<void>;
  onRunDeadlineDelivery?: () => void | Promise<void>;
  /** @deprecated Compatibility for retained render fixtures. */
  readinessError?: string | null;
  /** @deprecated Compatibility for retained render fixtures. */
  error?: string | null;
}

export function DashboardWorkbench({
  companies,
  deadlines,
  canCreateCompany,
  canRecoverCompany,
  canReviewReleaseEvidence,
  readinessReport,
  companyState,
  readinessState,
  deadlineState,
  deadlineUnavailableCompanyIds,
  deadlineStates = {},
  quarantinedCompanies = [],
  onCompanyRecovered,
  onRetryCompanies,
  onRetryReadiness,
  onRetryDeadlines,
  canManageDeadlineRisk = false,
  canRunDeadlineDelivery = false,
  deadlineRiskItems = [],
  deadlineRiskState = INITIAL_RESOURCE_STATE,
  deadlineRiskBusyKey = null,
  deadlineRiskActionMessage = null,
  onReloadDeadlineRisk = () => undefined,
  onRetryDeadlineReminder = () => undefined,
  onRunDeadlineDelivery = () => undefined,
  readinessError = null,
  error = null,
}: DashboardWorkbenchProps) {
  const resolvedCompanyState = companyState ?? legacyResourceState(error, companies.length > 0, companies.length === 0);
  const resolvedReadinessState = readinessState ?? legacyResourceState(readinessError, readinessReport != null, false);
  const resolvedDeadlineState = deadlineState ?? legacyResourceState(null, Object.keys(deadlines).length > 0, Object.keys(deadlines).length === 0);
  const unavailableDeadlineIds = deadlineUnavailableCompanyIds ?? [];
  const blockerCount = readinessReport?.releaseBlockerRegister.filter((blocker) => blocker.blocksRelease).length ?? 0;

  return (
    <PageShell
      title="Firm command centre"
      subtitle="Deadlines, reviewer ownership and the next action across the practice."
      meta={
        <>
          <StatusBadge tone="info">{formatCompanyCount(companies.length)}</StatusBadge>
          {canReviewReleaseEvidence && (
            <StatusBadge tone={blockerCount > 0 ? "warn" : "good"}>
              {blockerCount > 0 ? "Production gated" : "Production clear"}
            </StatusBadge>
          )}
        </>
      }
      actions={canCreateCompany ? <AddCompanyLink /> : undefined}
    >
      <div className="space-y-6">
        <ResourceStateNotice state={resolvedCompanyState} label="company directory" onRetry={onRetryCompanies} />

        {(resolvedCompanyState.status === "loaded" || resolvedCompanyState.status === "empty" || resolvedCompanyState.hasRetainedData) && (
          <>
            {companies.length > 0 && (
              <ResourceStateNotice state={resolvedDeadlineState} label="filing deadlines" onRetry={onRetryDeadlines} />
            )}
            {canManageDeadlineRisk && (
              <DeadlineRiskQueue
                items={deadlineRiskItems}
                state={deadlineRiskState}
                canRunDelivery={canRunDeadlineDelivery}
                busyKey={deadlineRiskBusyKey}
                actionMessage={deadlineRiskActionMessage}
                onReload={onReloadDeadlineRisk}
                onRetry={onRetryDeadlineReminder}
                onRunDelivery={onRunDeadlineDelivery}
              />
            )}
            <AccountantDashboardQueue
              companies={companies}
              deadlines={deadlines}
              deadlineUnavailableCompanyIds={unavailableDeadlineIds}
              deadlineStates={deadlineStates}
              productionReleaseBlockers={readinessReport?.releaseBlockerRegister ?? []}
            />

            <DashboardPracticeSummary
              companies={companies}
              deadlines={deadlines}
              deadlineUnavailableCompanyIds={unavailableDeadlineIds}
              deadlineStates={deadlineStates}
            />

            <DashboardCompanyDirectory
              companies={companies}
              deadlines={deadlines}
              deadlineUnavailableCompanyIds={unavailableDeadlineIds}
              deadlineStates={deadlineStates}
              canCreateCompany={canCreateCompany}
            />

            {canRecoverCompany && (
              <QuarantinedCompanyRecoveryPanel
                companies={quarantinedCompanies}
                onRecovered={onCompanyRecovered ?? (() => undefined)}
              />
            )}
          </>
        )}

        {canReviewReleaseEvidence && (
          <>
            <ResourceStateNotice state={resolvedReadinessState} label="production readiness evidence" onRetry={onRetryReadiness} />
            {readinessReport && <DashboardProductionSummary report={readinessReport} />}
          </>
        )}
      </div>
    </PageShell>
  );
}

function legacyResourceState(message: string | null, hasRetainedData: boolean, isEmpty: boolean): ResourceState {
  if (message) {
    return {
      status: hasRetainedData ? "partial-error" : "error",
      error: message,
      failedResourceKeys: ["legacy-resource"],
      hasRetainedData,
    };
  }
  return {
    status: isEmpty ? "empty" : "loaded",
    error: null,
    failedResourceKeys: [],
    hasRetainedData: !isEmpty,
  };
}

function AddCompanyLink() {
  return (
    <Link
      href="/companies/new"
      className="inline-flex min-h-9 items-center gap-2 rounded-md border border-emerald-700 bg-emerald-700 px-3 text-sm font-semibold text-white shadow-sm transition hover:bg-emerald-800 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
    >
      <Plus className="h-4 w-4" />
      Add Company
    </Link>
  );
}

function formatCompanyCount(count: number) {
  return `${count} compan${count === 1 ? "y" : "ies"}`;
}
