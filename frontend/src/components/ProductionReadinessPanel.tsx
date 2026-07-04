import {
  ArrowRight,
  CheckCircle2,
  FileCheck2,
  Scale,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react";
import Link from "next/link";
import type { ReactNode } from "react";
import type { ProductionReadinessReport } from "@/lib/api";
import { DataTable, ReviewPanel, StatusBadge } from "@/components/workbench";

export function ProductionReadinessPanel({
  report,
  error,
}: {
  report: ProductionReadinessReport | null;
  error?: string | null;
}) {
  if (error) {
    return (
      <ReviewPanel title="Production Readiness" description="Control-room assurance status">
        <div className="flex items-start gap-3 rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/40 dark:text-red-100">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{error}</span>
        </div>
      </ReviewPanel>
    );
  }

  if (!report) {
    return (
      <ReviewPanel title="Production Readiness" description="Control-room assurance status">
        <div className="grid gap-3 md:grid-cols-4">
          {["Backend", "Corpus", "Sources", "Gates"].map((label) => (
            <div key={label} className="h-20 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)]" />
          ))}
        </div>
      </ReviewPanel>
    );
  }

  const hardenedAreas = report.areas.filter((area) => area.status === "hardened").length;
  const coveredScenarios = report.goldenFilingCorpus.filter((scenario) => scenario.coverageStatus === "covered").length;
  const nextAction = report.assuranceActions?.[0];
  const statusTone = report.overallStatus === "ready" ? "good" : "warn";

  return (
    <ReviewPanel
      title="Production Readiness"
      description="Evidence that the platform can be trusted, and the gates that still require professional control."
      actions={
        <>
          <StatusBadge tone={statusTone}>{formatStatus(report.overallStatus)}</StatusBadge>
          <Link
            href="/production-readiness"
            className="inline-flex min-h-7 items-center gap-1.5 rounded-full border border-[var(--border)] bg-[var(--surface-subtle)] px-2.5 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
          >
            Open checklist
            <ArrowRight className="h-3.5 w-3.5" />
          </Link>
        </>
      }
    >
      <div className="grid gap-3 md:grid-cols-4">
        <ReadinessMetric
          icon={<ShieldCheck className="h-4 w-4" />}
          label="Hardened areas"
          value={`${hardenedAreas}/${report.areas.length}`}
        />
        <ReadinessMetric
          icon={<FileCheck2 className="h-4 w-4" />}
          label="Golden corpus"
          value={`${coveredScenarios}/${report.goldenFilingCorpus.length}`}
        />
        <ReadinessMetric
          icon={<Scale className="h-4 w-4" />}
          label="Source snapshot"
          value={report.sourceLawSnapshot.sourceCount}
        />
        <ReadinessMetric
          icon={<CheckCircle2 className="h-4 w-4" />}
          label="Operational gates"
          value={report.operationalGates.length}
        />
      </div>

      {nextAction && (
        <div className="mt-4 rounded-md border border-amber-200 bg-amber-50 p-3 text-amber-950 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-100">
          <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase">Next assurance action</p>
              <p className="mt-1 text-sm font-semibold">{nextAction.label}</p>
              <p className="mt-1 text-xs leading-5">{nextAction.evidenceRequired}</p>
            </div>
            <div className="flex shrink-0 flex-wrap items-center gap-2">
              <StatusBadge tone={nextAction.priority === "critical" ? "bad" : "warn"}>
                {formatStatus(nextAction.priority)}
              </StatusBadge>
              <StatusBadge tone={nextAction.status === "complete" ? "good" : nextAction.status === "in-progress" ? "info" : "warn"}>
                {formatStatus(nextAction.status)}
              </StatusBadge>
            </div>
          </div>
        </div>
      )}

      <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(320px,0.8fr)]">
        <DataTable
          columns={["Golden scenario", "Scope", "Outcome", "Evidence tests", "Status"]}
          rows={report.goldenFilingCorpus.map((scenario) => [
            <span key="label" className="font-medium">{scenario.label}</span>,
            <span key="scope" className="text-[var(--muted-foreground)]">{scenario.companyScope}</span>,
            <span key="outcome" className="text-[var(--muted-foreground)]">{formatStatus(scenario.expectedOutcome)}</span>,
            <div key="evidence" className="max-w-md space-y-1 whitespace-normal">
              {scenario.evidenceTestNames.map((testName) => (
                <code
                  key={testName}
                  className="block break-all rounded border border-[var(--border)] bg-[var(--surface-subtle)] px-2 py-1 text-[11px] text-[var(--muted-foreground)]"
                >
                  {testName}
                </code>
              ))}
            </div>,
            <StatusBadge key="status" tone={scenario.coverageStatus === "covered" ? "good" : "warn"}>
              {formatStatus(scenario.coverageStatus)}
            </StatusBadge>,
          ])}
        />

        <div className="space-y-3">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Required gates</p>
            <div className="mt-3 space-y-2">
              {report.operationalGates.map((gate) => (
                <div key={gate.code} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-sm font-medium text-[var(--foreground)]">{gate.label}</p>
                    <StatusBadge tone={gate.status === "enforced" ? "good" : "warn"}>{formatStatus(gate.status)}</StatusBadge>
                  </div>
                  <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{gate.detail}</p>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Source snapshot</p>
            <p className="mt-1 text-sm font-medium text-[var(--foreground)]">{report.sourceLawSnapshot.snapshotVersion}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              {report.sourceLawSnapshot.sources.slice(0, 4).map((source) => (
                <a
                  key={source.sourceId}
                  href={source.url}
                  target="_blank"
                  rel="noreferrer"
                  className="rounded-full border border-[var(--border)] bg-[var(--surface-subtle)] px-2.5 py-1 text-xs font-medium text-[var(--foreground)] hover:border-[var(--ring)]"
                >
                  {source.title}
                </a>
              ))}
            </div>
          </div>
        </div>
      </div>
    </ReviewPanel>
  );
}

function ReadinessMetric({
  icon,
  label,
  value,
}: {
  icon: ReactNode;
  label: string;
  value: ReactNode;
}) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <div className="flex items-center gap-2 text-[var(--muted-foreground)]">
        {icon}
        <span className="text-xs font-medium">{label}</span>
      </div>
      <p className="mt-2 text-xl font-semibold text-[var(--foreground)]">{value}</p>
    </div>
  );
}

function formatStatus(value: string) {
  const words = value
    .split("-")
    .filter(Boolean)
    .join(" ");

  return words.charAt(0).toUpperCase() + words.slice(1);
}
