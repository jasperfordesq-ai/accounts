import Link from "next/link";
import { ArrowRight, ShieldCheck } from "lucide-react";

import type { ProductionReadinessReport } from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

export function DashboardProductionSummary({
  report,
}: {
  report: ProductionReadinessReport;
}) {
  const releaseBlockers = report.releaseBlockerRegister.filter((blocker) => blocker.blocksRelease);
  const humanGates = (report.humanReleaseEvidence ?? []).filter((evidence) => evidence.blocksRelease);
  const ready = report.overallStatus === "ready" && releaseBlockers.length === 0;

  return (
    <ReviewPanel
      title="Platform release status"
      description="Compact engineering assurance summary; full evidence lives in the production-readiness workspace."
      actions={(
        <Link
          href="/production-readiness"
          className="inline-flex min-h-9 items-center gap-1.5 rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 text-sm font-semibold text-[var(--foreground)] hover:border-[var(--ring)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--ring)]"
        >
          Open release evidence
          <ArrowRight className="h-4 w-4" aria-hidden="true" />
        </Link>
      )}
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          <span className="rounded-md bg-[var(--surface-subtle)] p-2 text-[var(--muted-foreground)]">
            <ShieldCheck className="h-5 w-5" aria-hidden="true" />
          </span>
          <div className="min-w-0">
            <p className="text-sm font-semibold text-[var(--foreground)]">
              {ready ? "Engineering release controls are clear" : `${releaseBlockers.length} release gate${releaseBlockers.length === 1 ? "" : "s"} remain`}
            </p>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
              Daily accounts work stays available, but real filing use remains subject to the dedicated release gates and named professional review.
            </p>
          </div>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2">
          <StatusBadge tone={ready ? "good" : "warn"}>{formatStatus(report.overallStatus)}</StatusBadge>
          {humanGates.length > 0 && <StatusBadge tone="bad">{humanGates.length} human gates</StatusBadge>}
        </div>
      </div>
    </ReviewPanel>
  );
}

function formatStatus(value: string) {
  return value
    .split("-")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}
