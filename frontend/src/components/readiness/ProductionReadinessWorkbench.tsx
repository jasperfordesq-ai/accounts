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
  const assuranceActions = report.assuranceActions ?? [];
  const statutoryRuleMatrix = report.statutoryRuleMatrix ?? [];
  const auditabilityControls = report.auditabilityControls ?? [];
  const visualQaCoverage = report.visualQaCoverage;
  const hardenedAreas = report.areas.filter((area) => area.status === "hardened").length;
  const coveredScenarios = report.goldenFilingCorpus.filter((scenario) => scenario.coverageStatus === "covered").length;
  const enforcedGates = report.operationalGates.filter((gate) => gate.status === "enforced").length;
  const completedAssuranceActions = assuranceActions.filter((action) => action.status === "complete").length;
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
          { label: "Assurance actions", value: `${completedAssuranceActions}/${assuranceActions.length}`, tone: completedAssuranceActions === assuranceActions.length ? "good" : "warn" },
        ]}
      />

      <ReviewPanel
        title="Next assurance actions"
        description="Priority-ranked work that must be evidenced before the platform can be treated as production-ready for real statutory accounts."
        actions={<StatusBadge tone="warn">{assuranceActions.length - completedAssuranceActions} open</StatusBadge>}
      >
        <DataTable
          columns={["Action", "Owner", "Priority", "Evidence required", "Status"]}
          rows={assuranceActions.map((action) => [
            <div key="action" className="min-w-48 whitespace-normal">
              <p className="font-medium">{action.label}</p>
              <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{action.detail}</p>
            </div>,
            <span key="owner" className="text-[var(--muted-foreground)]">{action.owner}</span>,
            <StatusBadge key="priority" tone={priorityTone(action.priority)}>{formatStatus(action.priority)}</StatusBadge>,
            <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{action.evidenceRequired}</span>,
            <StatusBadge key="status" tone={action.status === "complete" ? "good" : action.status === "in-progress" ? "info" : "warn"}>
              {formatStatus(action.status)}
            </StatusBadge>,
          ])}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Production auditability"
        description="Controls proving who changed data, who approved outputs, what evidence was present, what was generated, and how the audit chain is checked."
        actions={<StatusBadge tone={auditabilityControls.every((control) => control.required) ? "good" : "warn"}>{auditabilityControls.length} controls</StatusBadge>}
      >
        <DataTable
          columns={["Control", "Enforcement", "Evidence captured", "Verification", "Audit events"]}
          rows={auditabilityControls.map((control) => [
            <div key="control" className="min-w-48 whitespace-normal">
              <p className="font-medium">{control.label}</p>
              <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{control.required ? "Required production control" : "Advisory control"}</p>
            </div>,
            <span key="enforcement" className="whitespace-normal text-[var(--muted-foreground)]">{control.enforcement}</span>,
            <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{control.evidenceCaptured}</span>,
            <span key="verification" className="whitespace-normal text-[var(--muted-foreground)]">{control.verification}</span>,
            <CodeStack key="events" items={control.auditEventCodes} />,
          ])}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Statutory rules matrix"
        description="Accountant-readable filing paths, required evidence, outputs, fail-closed gates, and source references for the supported and unsupported Irish company workflows."
        actions={<StatusBadge tone="info">{statutoryRuleMatrix.length} paths</StatusBadge>}
      >
        <DataTable
          columns={["Company path", "Regime", "Evidence", "Outputs", "Gates", "Sources"]}
          rows={statutoryRuleMatrix.map((row) => [
            <div key="path" className="min-w-44 whitespace-normal">
              <p className="font-medium">{row.companyScope}</p>
              <StatusBadge tone={supportTone(row.supportLevel)}>{formatStatus(row.supportLevel)}</StatusBadge>
            </div>,
            <span key="regime" className="whitespace-normal text-[var(--muted-foreground)]">{row.sizeOrRegime}</span>,
            <CompactList key="evidence" items={row.requiredEvidence} />,
            <CompactList key="outputs" items={row.requiredOutputs} />,
            <CompactList key="gates" items={row.manualHandoffGates} />,
            <SourceLinkList key="sources" sources={row.sources} />,
          ])}
        />
      </ReviewPanel>

      {visualQaCoverage && (
        <ReviewPanel
          title="Visual QA coverage"
          description="CI screenshot evidence for the accountant workbench in light and dark mode across desktop and mobile viewports."
          actions={<StatusBadge tone="info">{visualQaCoverage.expectedScreenshotCount} screenshots</StatusBadge>}
        >
          <div className="grid gap-4 xl:grid-cols-[minmax(0,0.7fr)_minmax(0,1.3fr)]">
            <div className="space-y-3">
              <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Artifact</p>
                <p className="mt-1 break-all text-sm font-medium text-[var(--foreground)]">{visualQaCoverage.artifactName}</p>
                <p className="mt-2 text-xs leading-5 text-[var(--muted-foreground)]">
                  Enforced by {formatStatus(visualQaCoverage.enforcement)}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                {visualQaCoverage.themes.flatMap((theme) =>
                  visualQaCoverage.viewports.map((viewport) => (
                    <StatusBadge key={`${theme}-${viewport.name}`} tone="default">
                      {formatStatus(theme)} {viewport.name}
                    </StatusBadge>
                  )),
                )}
              </div>
            </div>

            <DataTable
              columns={["Route", "Required text", "Viewport evidence", "Tab action"]}
              rows={visualQaCoverage.routes.map((route) => [
                <div key="route" className="min-w-44 whitespace-normal">
                  <p className="font-medium">{route.label}</p>
                  <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{route.description}</p>
                </div>,
                <span key="required-text" className="whitespace-normal text-[var(--muted-foreground)]">{route.requiredText}</span>,
                <span key="viewport-evidence" className="text-[var(--muted-foreground)]">
                  {visualQaCoverage.themes.length * visualQaCoverage.viewports.length} screenshots
                </span>,
                <StatusBadge key="tab-action" tone={route.openFilingTab ? "info" : "default"}>
                  {route.openFilingTab ? "Open filing tab" : "Initial view"}
                </StatusBadge>,
              ])}
            />
          </div>
        </ReviewPanel>
      )}

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
                className="inline-flex max-w-sm items-start gap-1.5 whitespace-normal break-words text-sm font-medium text-emerald-700 hover:text-emerald-800 dark:text-emerald-300 dark:hover:text-emerald-200"
              >
                {source.title}
                <ExternalLink className="mt-0.5 h-3.5 w-3.5 shrink-0" />
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

function CompactList({ items }: { items: string[] }) {
  return (
    <ul className="max-w-md space-y-1 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
      {items.map((item) => (
        <li key={item} className="flex items-start gap-1.5">
          <FileCheck2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-emerald-600 dark:text-emerald-300" />
          <span>{item}</span>
        </li>
      ))}
    </ul>
  );
}

function SourceLinkList({ sources }: { sources: ProductionReadinessReport["sourceLawSnapshot"]["sources"] }) {
  return (
    <div className="flex max-w-xs flex-wrap gap-1.5 whitespace-normal">
      {sources.map((source) => (
        <a
          key={source.sourceId}
          href={source.url}
          target="_blank"
          rel="noreferrer"
          className="inline-flex items-center gap-1 rounded-full border border-[var(--border)] bg-[var(--surface-subtle)] px-2 py-1 text-[11px] font-medium text-[var(--foreground)] hover:border-[var(--ring)]"
        >
          {source.title}
          <ExternalLink className="h-3 w-3 shrink-0" />
        </a>
      ))}
    </div>
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

function priorityTone(priority: string) {
  if (priority === "critical") return "bad";
  if (priority === "high") return "warn";
  return "default";
}

function supportTone(supportLevel: string) {
  if (supportLevel === "supported") return "good";
  if (supportLevel === "supported-with-review") return "info";
  if (supportLevel === "manual-handoff") return "warn";
  if (supportLevel === "unsupported") return "bad";
  return "default";
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
