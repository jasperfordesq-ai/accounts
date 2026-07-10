import { ExternalLink, FileCheck2 } from "lucide-react";
import type { ReactNode } from "react";
import type { ProductionReadinessReport } from "@/lib/api";
import { StatusBadge } from "@/components/workbench";

export function EvidenceRow({
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
    <div className="grid min-w-0 grid-cols-1 gap-3 py-3 first:pt-0 last:pb-0 md:grid-cols-[minmax(0,1fr)_auto] md:items-start">
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

export function CodeStack({ items }: { items: string[] }) {
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

export function VerifierScopeList({
  verifiers,
  label,
}: {
  verifiers: ProductionReadinessReport["goldenFilingCorpus"][number]["evidenceVerifiers"];
  label?: string;
}) {
  return (
    <div className="max-w-lg space-y-2 whitespace-normal">
      {verifiers.map((verifier) => (
        <div key={verifier.name} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-2 text-xs leading-5">
          {label && <p className="mb-1 text-[11px] font-semibold uppercase text-[var(--foreground)]">{label}</p>}
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge tone={verifier.runsInDefaultCi ? "good" : "warn"}>
              {verifier.runsInDefaultCi ? "Default CI" : "Environment gated"}
            </StatusBadge>
            <span className="font-medium text-[var(--foreground)]">{formatStatus(verifier.ciScope)}</span>
          </div>
          <p className="mt-1 text-[var(--muted-foreground)]">{verifier.environment}</p>
          <code className="mt-1 block break-all rounded border border-[var(--border)] bg-[var(--surface)] px-2 py-1 text-[11px] text-[var(--muted-foreground)]">
            {verifier.command}
          </code>
        </div>
      ))}
    </div>
  );
}

export function AssertionList({ items }: { items: string[] }) {
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

export function CompactList({ items }: { items: string[] }) {
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

export function ExpectedOutputsList({
  outputs,
}: {
  outputs: ProductionReadinessReport["goldenFilingCorpus"][number]["evidencePack"]["expectedOutputs"];
}) {
  return (
    <div className="max-w-lg space-y-2 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
      <ExpectedOutputGroup label="PDF" items={outputs.pdfTextMarkers} />
      <ExpectedOutputGroup label="iXBRL" items={outputs.ixbrlRequiredTags} />
      <ExpectedOutputGroup label="Notes" items={outputs.requiredNotes} />
      <ExpectedOutputGroup label="Gates" items={outputs.filingGateStates} />
      <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-2">
        <p><span className="font-semibold text-[var(--foreground)]">Readiness:</span> {formatStatus(outputs.filingReadinessState)}</p>
        <p><span className="font-semibold text-[var(--foreground)]">Sign-off:</span> {formatStatus(outputs.signOffPacketState)}</p>
        <p><span className="font-semibold text-[var(--foreground)]">Expected CT:</span> {formatCurrency(outputs.expectedCorporationTax)}</p>
      </div>
    </div>
  );
}

export function ExpectedOutputGroup({ label, items }: { label: string; items: string[] }) {
  return (
    <div>
      <p className="text-[11px] font-semibold uppercase text-[var(--foreground)]">{label}</p>
      <CompactList items={items} />
    </div>
  );
}

export function ProofPointList({
  proofPoints,
}: {
  proofPoints: ProductionReadinessReport["goldenFilingCorpus"][number]["evidencePack"]["expectedProofPoints"];
}) {
  return (
    <ul className="max-w-lg space-y-2 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
      {proofPoints.map((proof) => (
        <li key={`${proof.area}-${proof.automatedVerifier}`} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-2">
          <div className="flex flex-wrap items-center gap-2">
            <code className="rounded border border-[var(--border)] bg-[var(--surface)] px-1.5 py-0.5 text-[11px] text-[var(--foreground)]">
              {proof.area}
            </code>
            {proof.required && <StatusBadge tone="good">Required</StatusBadge>}
          </div>
          <p className="mt-1 text-[var(--foreground)]">{proof.expectedEvidence}</p>
          <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{proof.automatedVerifier}</code>
        </li>
      ))}
    </ul>
  );
}

export function SourceLinkList({ sources }: { sources: ProductionReadinessReport["sourceLawSnapshot"]["sources"] }) {
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

export function EmptyLine({ label }: { label: string }) {
  return <p className="text-sm text-[var(--muted-foreground)]">{label}</p>;
}

export function PacketMetric({
  label,
  value,
  tone,
  status,
}: {
  label: string;
  value: string;
  tone: "good" | "warn" | "bad" | "info" | "default";
  status: string;
}) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <p className="text-sm font-semibold text-[var(--foreground)]">{label} {value}</p>
      <div className="mt-2">
        <StatusBadge tone={tone}>{status}</StatusBadge>
      </div>
    </div>
  );
}

export function toneIconClass(tone: "good" | "warn" | "bad" | "info" | "default") {
  if (tone === "good") return "mt-0.5 shrink-0 text-emerald-600 dark:text-emerald-300";
  if (tone === "bad") return "mt-0.5 shrink-0 text-red-600 dark:text-red-300";
  if (tone === "warn") return "mt-0.5 shrink-0 text-amber-600 dark:text-amber-300";
  if (tone === "info") return "mt-0.5 shrink-0 text-sky-600 dark:text-sky-300";
  return "mt-0.5 shrink-0 text-[var(--muted-foreground)]";
}

export function priorityTone(priority: string): "good" | "warn" | "bad" | "info" | "default" {
  if (priority === "critical") return "bad";
  if (priority === "high") return "warn";
  return "default";
}

export function riskTone(riskRank: number): "good" | "warn" | "bad" | "info" | "default" {
  if (riskRank <= 5) return "bad";
  if (riskRank <= 20) return "warn";
  if (riskRank <= 40) return "info";
  return "default";
}

export function scorecardTone(currentScore: number, targetScore: number): "good" | "warn" | "bad" | "info" | "default" {
  const ratio = targetScore > 0 ? currentScore / targetScore : 0;
  if (ratio >= 1) return "good";
  if (ratio >= 0.85) return "info";
  if (ratio >= 0.65) return "warn";
  return "bad";
}

type ScorecardCategory = ProductionReadinessReport["productionScorecard"]["categories"][number];

const SCORECARD_ASSURANCE_CLASSES = [
  { code: "code", label: "Code", tone: "info" },
  { code: "machine", label: "Machine assurance", tone: "default" },
  { code: "human-external", label: "Human / external", tone: "warn" },
] as const;

export function ScorecardControlLedger({ category }: { category: ScorecardCategory }) {
  return (
    <div className="mt-3 space-y-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3 xl:grid-cols-1 2xl:grid-cols-3">
        {SCORECARD_ASSURANCE_CLASSES.map((assuranceClass) => {
          const controls = category.controls.filter((control) => control.assuranceClass === assuranceClass.code);
          const passedWeight = controls
            .filter((control) => control.passed)
            .reduce((total, control) => total + control.weight, 0);
          const totalWeight = controls.reduce((total, control) => total + control.weight, 0);

          return (
            <div key={assuranceClass.code} className="rounded border border-[var(--border)] bg-[var(--surface-subtle)] p-2">
              <StatusBadge tone={assuranceClass.tone}>{assuranceClass.label}</StatusBadge>
              <p className="mt-1 text-xs font-semibold text-[var(--foreground)]">
                {formatScore(passedWeight)}/{formatScore(totalWeight)} points
              </p>
              <p className="text-[11px] text-[var(--muted-foreground)]">
                {controls.filter((control) => control.passed).length}/{controls.length} controls passed
              </p>
            </div>
          );
        })}
      </div>

      <details className="rounded border border-[var(--border)] bg-[var(--surface-subtle)]">
        <summary className="cursor-pointer px-3 py-2 text-xs font-semibold text-[var(--foreground)]">
          Review {category.controls.length} weighted controls
        </summary>
        <ul className="divide-y divide-[var(--border)] border-t border-[var(--border)]">
          {category.controls.map((control) => (
            <li key={control.code} className="space-y-2 px-3 py-3 text-xs leading-5">
              <div className="flex flex-wrap items-center gap-2">
                <StatusBadge tone={SCORECARD_ASSURANCE_CLASSES.find((item) => item.code === control.assuranceClass)?.tone ?? "default"}>
                  {SCORECARD_ASSURANCE_CLASSES.find((item) => item.code === control.assuranceClass)?.label ?? control.assuranceClass}
                </StatusBadge>
                <StatusBadge tone={control.passed ? "good" : "bad"}>{control.passed ? "Passed" : "Open"}</StatusBadge>
                <span className="font-semibold text-[var(--foreground)]">{formatScore(control.weight)} points</span>
              </div>
              <div>
                <p className="font-semibold text-[var(--foreground)]">{control.label}</p>
                <p className="text-[var(--muted-foreground)]">{control.evidence[0]}</p>
              </div>
              {!control.passed && (
                <p className="text-red-700 dark:text-red-300">
                  Blocking audit items: {control.blockingAuditItemIds.join(", ")}
                </p>
              )}
            </li>
          ))}
        </ul>
      </details>
    </div>
  );
}

export function supportTone(supportLevel: string): "good" | "warn" | "bad" | "info" | "default" {
  if (supportLevel === "supported") return "good";
  if (supportLevel === "supported-with-review") return "info";
  if (supportLevel === "manual-handoff") return "warn";
  if (supportLevel === "unsupported") return "bad";
  return "default";
}

export function ciScopeTone(scope: string): "good" | "warn" | "bad" | "info" | "default" {
  if (scope === "default-ci") return "good";
  if (scope === "environment-gated") return "warn";
  if (scope === "manual-release") return "bad";
  return "default";
}

export function closeoutStepTone(
  step: ProductionReadinessReport["humanReleaseEvidenceCloseout"][number],
  pendingHumanEvidenceCount: number,
): "good" | "warn" | "bad" | "info" | "default" {
  if (!step.blocksRelease) return "good";
  if (step.code === "pick-up-reviewer-workspace" && pendingHumanEvidenceCount > 0) return "bad";
  return "warn";
}

export function formatStatus(value: string) {
  const words = value
    .split("-")
    .filter(Boolean)
    .join(" ");

  return words.charAt(0).toUpperCase() + words.slice(1);
}

export function formatCurrency(value: number) {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(value);
}

export function formatScore(value: number) {
  return new Intl.NumberFormat("en-IE").format(value);
}

export function formatPacketStatus(value: string) {
  return value === "ready" ? "Packet ready" : `Packet ${formatStatus(value).toLowerCase()}`;
}

export function formatTaxonomyPeriodWindow(effectiveFrom: string, effectiveBefore: string) {
  return effectiveBefore.trim()
    ? `${effectiveFrom} to ${effectiveBefore}`
    : `${effectiveFrom} onward`;
}

export function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("en-IE", { day: "2-digit", month: "short", year: "numeric" }).format(date);
}

export function formatPinnedSources(count: number) {
  return `${count} pinned source${count === 1 ? "" : "s"}`;
}

export function formatDateTime(value: string) {
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
