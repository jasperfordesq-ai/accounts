import {
  AlertTriangle,
  CheckCircle2,
  ExternalLink,
  FileCheck2,
  ShieldCheck,
} from "lucide-react";
import type { ReactNode } from "react";
import type { ProductionReadinessReport } from "@/lib/api";
import {
  DataTable,
  MetricStrip,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
  WorkbenchHeader,
  WorkbenchShell,
} from "@/components/workbench";

export function ProductionReadinessWorkbench({ report }: { report: ProductionReadinessReport }) {
  const hardenedAreas = report.areas.filter((area) => area.status === "hardened").length;
  const coveredScenarios = report.goldenFilingCorpus.filter((scenario) => scenario.coverageStatus === "covered").length;
  const enforcedGates = report.operationalGates.filter((gate) => gate.status === "enforced").length;
  const statusTone = report.overallStatus === "ready" ? "good" : "warn";

  return (
    <WorkbenchShell>
      <WorkbenchHeader
        title="Production Readiness Checklist"
        subtitle="Accountant-facing evidence for statutory rules, golden filing coverage, unsupported paths, operational controls, and professional review gates."
        meta={
          <>
            <StatusBadge tone={statusTone}>{formatStatus(report.overallStatus)}</StatusBadge>
            <span className="text-xs text-[var(--muted-foreground)]">Generated {formatDateTime(report.generatedAt)}</span>
          </>
        }
      />

      <MetricStrip
        metrics={[
          { label: "Companies in database", value: `${report.companiesInDatabase} companies`, tone: "default" },
          { label: "Periods in database", value: `${report.periodsInDatabase} periods`, tone: "default" },
          { label: "Hardened areas", value: `${hardenedAreas}/${report.areas.length}`, tone: hardenedAreas === report.areas.length ? "good" : "warn" },
          { label: "Enforced gates", value: `${enforcedGates}/${report.operationalGates.length}`, tone: enforcedGates === report.operationalGates.length ? "good" : "warn" },
        ]}
      />

      <section className="space-y-4">
        <SectionHeader
          eyebrow="Evidence"
          title="Golden filing corpus"
          description="Seed scenarios that prove the end-to-end accounting path, generated outputs, legal gates, PDF text, and iXBRL XML parsing."
          actions={<StatusBadge tone={coveredScenarios === report.goldenFilingCorpus.length ? "good" : "warn"}>{coveredScenarios}/{report.goldenFilingCorpus.length} covered</StatusBadge>}
        />
        <DataTable
          columns={["Scenario", "Company scope", "Expected outcome", "Evidence tests", "Assertions", "Status"]}
          rows={report.goldenFilingCorpus.map((scenario) => [
            <span key="label" className="font-medium">{scenario.label}</span>,
            <span key="scope" className="whitespace-normal text-[var(--muted-foreground)]">{scenario.companyScope}</span>,
            <span key="outcome" className="text-[var(--muted-foreground)]">{formatStatus(scenario.expectedOutcome)}</span>,
            <CodeStack key="tests" items={scenario.evidenceTestNames} />,
            <AssertionList key="assertions" items={scenario.assertions} />,
            <StatusBadge key="status" tone={scenario.coverageStatus === "covered" ? "good" : "warn"}>
              {formatStatus(scenario.coverageStatus)}
            </StatusBadge>,
          ])}
        />
      </section>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(360px,0.9fr)]">
        <ReviewPanel
          title="Backend and statutory coverage"
          description="Implementation areas that must remain source-backed, tested, and fail-closed."
          actions={<StatusBadge tone={hardenedAreas === report.areas.length ? "good" : "warn"}>{hardenedAreas}/{report.areas.length} hardened</StatusBadge>}
        >
          <div className="divide-y divide-[var(--border)]">
            {report.areas.map((area) => (
              <EvidenceRow
                key={area.code}
                icon={area.status === "hardened" ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
                title={area.label}
                detail={area.detail}
                status={formatStatus(area.status)}
                tone={area.status === "hardened" ? "good" : "warn"}
              />
            ))}
          </div>
        </ReviewPanel>

        <ReviewPanel
          title="Unsupported/manual handoff"
          description="Paths that should stop before final filing unless a professional manually takes ownership."
          actions={<StatusBadge tone={report.manualHandoffPaths.length > 0 ? "warn" : "good"}>{report.manualHandoffPaths.length} paths</StatusBadge>}
        >
          {report.manualHandoffPaths.length === 0 ? (
            <EmptyLine label="No manual handoff paths reported." />
          ) : (
            <ul className="divide-y divide-[var(--border)]">
              {report.manualHandoffPaths.map((path) => (
                <li key={path} className="flex items-start gap-3 py-3 first:pt-0 last:pb-0">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-300" />
                  <span className="text-sm leading-6 text-[var(--foreground)]">{path}</span>
                </li>
              ))}
            </ul>
          )}
        </ReviewPanel>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,0.9fr)_minmax(420px,1.1fr)]">
        <ReviewPanel
          title="Operations and security"
          description="Controls that must stay enforced before any real customer filing pack is trusted."
          actions={<StatusBadge tone={enforcedGates === report.operationalGates.length ? "good" : "warn"}>{enforcedGates}/{report.operationalGates.length} enforced</StatusBadge>}
        >
          <div className="divide-y divide-[var(--border)]">
            {report.operationalGates.map((gate) => (
              <EvidenceRow
                key={gate.code}
                icon={gate.status === "enforced" ? <ShieldCheck className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
                title={gate.label}
                detail={gate.detail}
                status={formatStatus(gate.status)}
                tone={gate.status === "enforced" ? "good" : gate.required ? "bad" : "warn"}
              />
            ))}
          </div>
        </ReviewPanel>

        <ReviewPanel
          title="Source-backed statutory rules"
          description={`Snapshot ${report.sourceLawSnapshot.snapshotVersion} from ${formatDate(report.sourceLawSnapshot.snapshotDate)}.`}
          actions={<StatusBadge tone="info">{report.sourceLawSnapshot.sources.length} sources</StatusBadge>}
        >
          <DataTable
            columns={["Source", "Effective date", "Reference"]}
            rows={report.sourceLawSnapshot.sources.map((source) => [
              <span key="title" className="font-medium">{source.title}</span>,
              <span key="effective" className="text-[var(--muted-foreground)]">{formatDate(source.effectiveDate)}</span>,
              <a
                key="link"
                href={source.url}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 text-sm font-medium text-emerald-700 hover:text-emerald-800 dark:text-emerald-300 dark:hover:text-emerald-200"
              >
                {source.title}
                <ExternalLink className="h-3.5 w-3.5" />
              </a>,
            ])}
          />
        </ReviewPanel>
      </div>
    </WorkbenchShell>
  );
}

function EvidenceRow({
  icon,
  title,
  detail,
  status,
  tone,
}: {
  icon: ReactNode;
  title: string;
  detail: string;
  status: string;
  tone: "good" | "warn" | "bad" | "info" | "default";
}) {
  return (
    <div className="grid gap-3 py-3 first:pt-0 last:pb-0 md:grid-cols-[minmax(0,1fr)_auto] md:items-start">
      <div className="flex min-w-0 items-start gap-3">
        <span className={toneIconClass(tone)}>{icon}</span>
        <div className="min-w-0">
          <p className="text-sm font-medium text-[var(--foreground)]">{title}</p>
          <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{detail}</p>
        </div>
      </div>
      <StatusBadge tone={tone}>{status}</StatusBadge>
    </div>
  );
}

function CodeStack({ items }: { items: string[] }) {
  return (
    <div className="max-w-lg space-y-1 whitespace-normal">
      {items.map((item) => (
        <code
          key={item}
          className="block break-all rounded border border-[var(--border)] bg-[var(--surface-subtle)] px-2 py-1 text-[11px] text-[var(--muted-foreground)]"
        >
          {item}
        </code>
      ))}
    </div>
  );
}

function AssertionList({ items }: { items: string[] }) {
  return (
    <ul className="space-y-1 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
      {items.map((item) => (
        <li key={item} className="flex items-center gap-1.5">
          <FileCheck2 className="h-3.5 w-3.5 shrink-0 text-emerald-600 dark:text-emerald-300" />
          {item}
        </li>
      ))}
    </ul>
  );
}

function EmptyLine({ label }: { label: string }) {
  return <p className="text-sm text-[var(--muted-foreground)]">{label}</p>;
}

function toneIconClass(tone: "good" | "warn" | "bad" | "info" | "default") {
  if (tone === "good") return "mt-0.5 shrink-0 text-emerald-600 dark:text-emerald-300";
  if (tone === "bad") return "mt-0.5 shrink-0 text-red-600 dark:text-red-300";
  if (tone === "warn") return "mt-0.5 shrink-0 text-amber-600 dark:text-amber-300";
  if (tone === "info") return "mt-0.5 shrink-0 text-sky-600 dark:text-sky-300";
  return "mt-0.5 shrink-0 text-[var(--muted-foreground)]";
}

function formatStatus(value: string) {
  const words = value
    .split("-")
    .filter(Boolean)
    .join(" ");

  return words.charAt(0).toUpperCase() + words.slice(1);
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("en-IE", { day: "2-digit", month: "short", year: "numeric" }).format(date);
}

function formatDateTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}
