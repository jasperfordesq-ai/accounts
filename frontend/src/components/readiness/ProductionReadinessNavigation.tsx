"use client";

import { AlertTriangle, ChevronDown, Search } from "lucide-react";
import type { ReactNode } from "react";
import type { ProductionReadinessReport } from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

export const readinessSections = [
  {
    id: "readiness-priority",
    navigationGroup: "Decision",
    label: "Priority release decision",
    description: "Current blockers, release posture, and the next professional gates.",
    keywords: "blockers release decision critical human external accountant sign-off",
  },
  {
    id: "readiness-release-evidence",
    navigationGroup: "Assurance",
    label: "Release evidence",
    description: "Scorecard, human evidence, closeout path, and assurance packet.",
    keywords: "scorecard human release evidence closeout assurance packet",
  },
  {
    id: "readiness-law-taxonomy",
    navigationGroup: "Statutory",
    label: "Source law and taxonomy",
    description: "Source-law maintenance, legal review ledger, and Revenue taxonomy ranges.",
    keywords: "source law maintenance review ledger revenue taxonomy legal cro frc charities regulator",
  },
  {
    id: "readiness-release-controls",
    navigationGroup: "Decision",
    label: "Release controls",
    description: "Assurance actions, blocker register, completion map, checklist, and verifier manifest.",
    keywords: "assurance actions blocker register completion map checklist verification manifest",
  },
  {
    id: "readiness-accountant-review",
    navigationGroup: "Assurance",
    label: "Accountant and visual review",
    description: "Walkthrough, journey acceptance, evidence packs, and visual acceptance.",
    keywords: "accountant workflow walkthrough journey acceptance evidence visual workbench criteria",
  },
  {
    id: "readiness-audit-operations",
    navigationGroup: "Platform",
    label: "Audit and operations",
    description: "Auditability, monitoring, dependencies, deployment safety, and operations evidence.",
    keywords: "audit timeline monitoring dependency deployment safety operations security evidence",
  },
  {
    id: "readiness-statutory-filing",
    navigationGroup: "Statutory",
    label: "Statutory and filing evidence",
    description: "Rules coverage, visual QA, golden corpus, legal snapshots, and evidence ledger.",
    keywords: "statutory rules coverage visual qa golden filing corpus legal basis evidence ledger",
  },
  {
    id: "readiness-coverage-boundaries",
    navigationGroup: "Platform",
    label: "Coverage and boundaries",
    description: "Hardened areas, unsupported handoffs, operational gates, and pinned sources.",
    keywords: "backend coverage unsupported manual handoff operations security source backed traceability",
  },
] as const;

export type ReadinessSectionId = (typeof readinessSections)[number]["id"];
export type ReadinessSection = (typeof readinessSections)[number];

const readinessSectionIds = new Set<ReadinessSectionId>(
  readinessSections.map((section) => section.id),
);

export function sectionIdFromHash(hash: string): ReadinessSectionId | null {
  const candidate = decodeURIComponent(hash.replace(/^#/, "")) as ReadinessSectionId;
  return readinessSectionIds.has(candidate) ? candidate : null;
}

export function ReadinessControlSurface({
  activeSectionId,
  blockers,
  matchingSectionIds,
  pendingHumanEvidenceCount,
  search,
  onSearchChange,
  onSectionAnchor,
}: {
  activeSectionId: ReadinessSectionId | null;
  blockers: ProductionReadinessReport["releaseBlockerRegister"];
  matchingSectionIds: Set<ReadinessSectionId>;
  pendingHumanEvidenceCount: number;
  search: string;
  onSearchChange: (value: string) => void;
  onSectionAnchor: (sectionId: ReadinessSectionId) => void;
}) {
  const navigationGroups = ["Decision", "Assurance", "Statutory", "Platform"] as const;
  const openGateCount = blockers.length + pendingHumanEvidenceCount;
  const releaseIsGated = openGateCount > 0;

  return (
    <div
      className="no-print z-30 grid min-w-0 gap-3 lg:sticky lg:top-3 lg:grid-cols-[minmax(280px,0.8fr)_minmax(0,1.2fr)]"
      data-mobile-priority-surface="true"
      data-mobile-priority-within-viewports="2"
    >
      <section
        aria-label="Persistent production blocker summary"
        className={`rounded-md border p-3 shadow-sm ${releaseIsGated
          ? "border-red-200 bg-red-50 dark:border-red-900/70 dark:bg-red-950/40"
          : "border-emerald-200 bg-emerald-50 dark:border-emerald-900/70 dark:bg-emerald-950/30"}`}
      >
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <p className={`text-sm font-semibold ${releaseIsGated ? "text-red-950 dark:text-red-100" : "text-emerald-950 dark:text-emerald-100"}`}>
              {releaseIsGated ? "Release remains gated" : "Release gates are clear"}
            </p>
            <p className={`mt-1 text-xs leading-5 ${releaseIsGated ? "text-red-800 dark:text-red-200" : "text-emerald-800 dark:text-emerald-200"}`}>
              {releaseIsGated
                ? "Human and external acceptance stays blocking until retained evidence is reviewed and accepted."
                : "No blocking release-register or retained human-evidence gate is currently reported."}
            </p>
          </div>
          <StatusBadge tone={releaseIsGated ? "bad" : "good"}>
            {openGateCount} open gates
          </StatusBadge>
        </div>
        {blockers.length > 0 && (
          <ul className="mt-3 space-y-1.5 text-xs leading-5 text-red-950 dark:text-red-100">
            {blockers.slice(0, 2).map((blocker) => (
              <li key={blocker.code} className="flex items-start gap-2">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{blocker.blockingIssue}</span>
              </li>
            ))}
          </ul>
        )}
        <a
          href="#readiness-release-controls"
          onClick={() => onSectionAnchor("readiness-release-controls")}
          className={`mt-3 inline-flex min-h-8 items-center text-xs font-semibold underline underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 ${releaseIsGated
            ? "text-red-900 decoration-red-400 hover:text-red-700 focus-visible:outline-red-600 dark:text-red-100 dark:hover:text-red-200"
            : "text-emerald-900 decoration-emerald-400 hover:text-emerald-700 focus-visible:outline-emerald-600 dark:text-emerald-100 dark:hover:text-emerald-200"}`}
        >
          Open blocker register and release controls
        </a>
      </section>

      <section
        aria-label="Production readiness navigation and search"
        className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 shadow-sm"
      >
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-[var(--muted-foreground)]" aria-hidden="true" />
          <input
            type="search"
            value={search}
            onChange={(event) => onSearchChange(event.target.value)}
            aria-label="Search readiness sections"
            placeholder="Find monitoring, source law, visual QA…"
            className="min-h-9 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] py-2 pl-9 pr-3 text-sm text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20"
          />
        </div>
        <p className="mt-2 text-xs text-[var(--muted-foreground)]" role="status" aria-live="polite">
          {matchingSectionIds.size} of {readinessSections.length} sections match
        </p>
        <nav aria-label="Production readiness section navigation" className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {navigationGroups.map((group) => (
            <div key={group}>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--muted-foreground)]">{group}</p>
              <ul className="mt-1 space-y-1">
                {readinessSections.filter((section) => section.navigationGroup === group).map((section) => (
                  <li key={section.id} className={matchingSectionIds.has(section.id) ? "" : "opacity-45"}>
                    <a
                      href={`#${section.id}`}
                      onClick={() => onSectionAnchor(section.id)}
                      aria-current={activeSectionId === section.id ? "location" : undefined}
                      className="inline-flex min-h-7 items-center text-xs font-medium text-emerald-800 underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500 dark:text-emerald-300"
                    >
                      {section.label}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>
      </section>
    </div>
  );
}

export function ReadinessDisclosure({
  section,
  open,
  visible,
  searchActive,
  onOpenChange,
  children,
}: {
  section: ReadinessSection;
  open: boolean;
  visible: boolean;
  searchActive: boolean;
  onOpenChange: (open: boolean) => void;
  children: ReactNode;
}) {
  return (
    <section
      id={section.id}
      data-readiness-section={section.id}
      data-supporting-ledger={section.id === "readiness-priority" ? "priority" : "collapsed-initially"}
      className={`scroll-mt-52 ${visible ? "" : "hidden print:block"}`}
    >
      <details
        open={open}
        onToggle={(event) => {
          if (!searchActive) onOpenChange(event.currentTarget.open);
        }}
        className="readiness-disclosure overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] shadow-sm"
      >
        <summary className="flex min-h-16 cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-emerald-500 [&::-webkit-details-marker]:hidden">
          <span className="min-w-0">
            <span className="block text-sm font-semibold text-[var(--foreground)]">{section.label}</span>
            <span className="mt-0.5 block text-xs leading-5 text-[var(--muted-foreground)]">{section.description}</span>
          </span>
          <span className="flex shrink-0 items-center gap-1 text-xs font-medium text-[var(--muted-foreground)]">
            {open ? "Hide" : "Show"}
            <ChevronDown className={`h-4 w-4 transition-transform ${open ? "rotate-180" : ""}`} aria-hidden="true" />
          </span>
        </summary>
        <div className="space-y-6 border-t border-[var(--border)] bg-[var(--background)] p-3 sm:p-4" data-readiness-section-content="true">
          {children}
        </div>
      </details>
    </section>
  );
}

export function PriorityReadinessOverview({
  accountantAcceptanceSummary,
  assurancePacket,
  blockers,
  pendingHumanEvidenceCount,
  releaseReady,
}: {
  accountantAcceptanceSummary: ProductionReadinessReport["accountantAcceptanceSummary"];
  assurancePacket: ProductionReadinessReport["assurancePacket"];
  blockers: ProductionReadinessReport["releaseBlockerRegister"];
  pendingHumanEvidenceCount: number;
  releaseReady: boolean;
}) {
  return (
    <ReviewPanel
      title="Release posture and priority blockers"
      description="The smallest complete release decision: what is blocked, who must act, and which evidence must remain external or professionally accepted."
      actions={
        <StatusBadge tone={releaseReady ? "good" : "bad"}>
          {releaseReady ? "Ready for controlled release" : "Do not use for real filings"}
        </StatusBadge>
      }
    >
      <div className="grid min-w-0 grid-cols-1 overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] sm:grid-cols-2 xl:grid-cols-4 xl:divide-x divide-y sm:[&>*:nth-child(odd)]:border-r xl:[&>*]:border-r-0 xl:divide-y-0 divide-[var(--border)]">
        <DecisionSummaryItem
          label="Critical actions"
          value={`${assurancePacket.openCriticalActions} open`}
          detail={assurancePacket.releaseBlockers[0] ?? "No critical assurance action reported."}
          tone={assurancePacket.openCriticalActions === 0 ? "good" : "bad"}
        />
        <DecisionSummaryItem
          label="Release register"
          value={`${blockers.length} blocking ${blockers.length === 1 ? "item" : "items"}`}
          detail={blockers[0]?.blockingIssue ?? "No release-register blocker reported."}
          tone={blockers.length === 0 ? "good" : "bad"}
        />
        <DecisionSummaryItem
          label="Human / external gates"
          value={`${pendingHumanEvidenceCount} pending`}
          detail="Named reviewers and external validators must complete retained evidence; this UI cannot self-accept those gates."
          tone={pendingHumanEvidenceCount === 0 ? "good" : "bad"}
        />
        <DecisionSummaryItem
          label="Professional acceptance"
          value={`${accountantAcceptanceSummary.professionalSignOffRequiredCount} sign-offs`}
          detail={`${accountantAcceptanceSummary.manualHandoffScenarioCount} manual-handoff scenarios remain professionally owned.`}
          tone={accountantAcceptanceSummary.status === "accepted" ? "good" : "warn"}
        />
      </div>
      {blockers.length > 0 && (
        <ol className="mt-4 grid gap-3 lg:grid-cols-2" aria-label="Priority release blocker actions">
          {blockers.slice(0, 4).map((blocker) => (
            <li key={blocker.code} className="rounded-md border border-red-200 bg-red-50 p-3 dark:border-red-900/70 dark:bg-red-950/30">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <p className="text-sm font-semibold text-red-950 dark:text-red-100">{blocker.blockingIssue}</p>
                <StatusBadge tone="bad">Risk {blocker.riskRank}</StatusBadge>
              </div>
              <p className="mt-2 text-xs leading-5 text-red-900 dark:text-red-200">{blocker.nextAction}</p>
              <p className="mt-2 text-[11px] text-red-800 dark:text-red-300">Owner: {blocker.ownerRole}</p>
            </li>
          ))}
        </ol>
      )}
      {blockers.length > 4 && (
        <p className="mt-3 text-xs text-[var(--muted-foreground)]">
          {blockers.length - 4} additional blocking {blockers.length - 4 === 1 ? "item is" : "items are"} retained in the full release blocker register.
        </p>
      )}
    </ReviewPanel>
  );
}

export function DecisionSummaryItem({
  label,
  value,
  detail,
  tone,
}: {
  label: string;
  value: string;
  detail: string;
  tone: "good" | "warn" | "bad" | "info" | "default";
}) {
  return (
    <div className="min-w-0 p-4">
      <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">{value}</p>
      <div className="mt-3">
        <StatusBadge tone={tone}>{tone === "good" ? "Clear" : tone === "bad" ? "Blocked" : tone === "info" ? "Evidenced" : "Required"}</StatusBadge>
      </div>
      <p className="mt-3 text-xs leading-5 text-[var(--muted-foreground)]">{detail}</p>
    </div>
  );
}
