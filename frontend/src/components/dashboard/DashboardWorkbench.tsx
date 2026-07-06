"use client";

import Link from "next/link";
import { Plus, TriangleAlert } from "lucide-react";
import type { Company, FilingDeadline, ProductionReadinessReport } from "@/lib/api";
import { ProductionReadinessPanel } from "@/components/ProductionReadinessPanel";
import { AccountantDashboardQueue } from "@/components/dashboard/AccountantDashboardQueue";
import { DashboardCompanyDirectory } from "@/components/dashboard/DashboardCompanyDirectory";
import { DashboardPracticeSummary } from "@/components/dashboard/DashboardPracticeSummary";
import { PageShell, StatusBadge } from "@/components/workbench";

interface DashboardWorkbenchProps {
  companies: Company[];
  deadlines: Record<number, FilingDeadline | null>;
  isOwner: boolean;
  readinessReport: ProductionReadinessReport | null;
  readinessError: string | null;
  error: string | null;
}

export function DashboardWorkbench({
  companies,
  deadlines,
  isOwner,
  readinessReport,
  readinessError,
  error,
}: DashboardWorkbenchProps) {
  const blockerCount = readinessReport?.releaseBlockerRegister.filter((blocker) => blocker.blocksRelease).length ?? 0;

  return (
    <PageShell
      title="Firm command centre"
      subtitle="Irish statutory accounts workload, filing pressure and production release evidence."
      meta={
        <>
          <StatusBadge tone="info">{formatCompanyCount(companies.length)}</StatusBadge>
          <StatusBadge tone={blockerCount > 0 ? "warn" : "good"}>
            {blockerCount > 0 ? "Production gated" : "Production clear"}
          </StatusBadge>
        </>
      }
      actions={isOwner ? <AddCompanyLink /> : undefined}
    >
      <div className="space-y-6">
        {error && (
          <div className="flex items-start gap-3 rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/40 dark:text-red-100">
            <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <ProductionReadinessPanel report={readinessReport} error={readinessError} />

        <AccountantDashboardQueue
          companies={companies}
          deadlines={deadlines}
          productionReleaseBlockers={readinessReport?.releaseBlockerRegister ?? []}
        />

        <DashboardPracticeSummary companies={companies} deadlines={deadlines} />

        <DashboardCompanyDirectory companies={companies} deadlines={deadlines} isOwner={isOwner} />
      </div>
    </PageShell>
  );
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
