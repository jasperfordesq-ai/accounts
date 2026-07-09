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
import { DataGrid, ReviewPanel, StatusBadge } from "@/components/workbench";

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
        <div className="grid min-w-0 grid-cols-1 gap-3 md:grid-cols-4">
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
  const humanReleaseEvidence = report.humanReleaseEvidence ?? [];
  const pendingHumanEvidenceCount = humanReleaseEvidence.filter((item) => item.blocksRelease).length;
  const humanEvidenceTemplateCount = humanReleaseEvidence.length;

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
      <div className="grid min-w-0 grid-cols-1 gap-3 md:grid-cols-4">
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

      <div className="mt-4 grid min-w-0 grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(320px,0.8fr)]">
        <div className="min-w-0 space-y-4">
          <DataGrid
            columns={["Golden scenario", "Fixture", "Scope", "Outcome", "Evidence tests", "Status"]}
            rows={report.goldenFilingCorpus.map((scenario) => [
              <span key="label" className="font-medium">{scenario.label}</span>,
              <div key="fixture" className="max-w-sm space-y-1 whitespace-normal text-xs text-[var(--muted-foreground)]">
                <p className="font-semibold text-[var(--foreground)]">{scenario.fixture.legalName}</p>
                <p>{scenario.fixture.periodStart} to {scenario.fixture.periodEnd}</p>
                <p>{scenario.fixture.expectedSizeClass} / {scenario.fixture.expectedRegime}</p>
              </div>,
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

          <section aria-label="Legal basis snapshots" className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Legal basis snapshots</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                  Fixture, filing basis, source IDs and professional gates that must remain aligned before accountant sign-off.
                </p>
              </div>
              <StatusBadge tone="info">{report.goldenFilingCorpus.length} snapshots</StatusBadge>
            </div>

            <div className="mt-3 grid gap-2">
              {report.goldenFilingCorpus.slice(0, 3).map((scenario) => (
                <div key={scenario.code} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-[var(--foreground)]">{scenario.label}</p>
                      <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                        {scenario.legalBasisSnapshot.legalBasis}
                      </p>
                    </div>
                    <StatusBadge tone={scenario.legalBasisSnapshot.manualProfessionalReviewRequired ? "warn" : "good"}>
                      {scenario.legalBasisSnapshot.manualProfessionalReviewRequired ? "Manual review" : "Standard gate"}
                    </StatusBadge>
                  </div>
                  <div className="mt-3 grid min-w-0 grid-cols-1 gap-2 text-xs leading-5 text-[var(--muted-foreground)] sm:grid-cols-2">
                    <LedgerFact>{scenario.legalBasisSnapshot.companyType} / {scenario.legalBasisSnapshot.sizeClass} / {scenario.legalBasisSnapshot.electedRegime}</LedgerFact>
                    <LedgerFact>Sources: {scenario.legalBasisSnapshot.sourceIds.join(", ")}</LedgerFact>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section aria-label="Golden evidence ledger" className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Golden evidence ledger</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                  Expected outputs, source traces and sign-off states for release-blocking accountant review.
                </p>
              </div>
              <StatusBadge tone="warn">{report.goldenEvidenceLedger.length} entries</StatusBadge>
            </div>

            <div className="mt-3 grid gap-2">
              {report.goldenEvidenceLedger.map((entry) => (
                <div key={entry.scenarioCode} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-[var(--foreground)]">{entry.label}</p>
                      <p className="mt-1 text-xs text-[var(--muted-foreground)]">{entry.fixtureLegalName}</p>
                    </div>
                    <StatusBadge tone={entry.blocksRelease ? "warn" : "good"}>
                      {formatStatus(entry.acceptanceStatus)}
                    </StatusBadge>
                  </div>

                  <div className="mt-3 grid min-w-0 grid-cols-1 gap-2 text-xs leading-5 text-[var(--muted-foreground)] sm:grid-cols-2 xl:grid-cols-3">
                    <LedgerFact>Readiness: {entry.filingReadinessState}</LedgerFact>
                    <LedgerFact>Expected CT: {formatCurrency(entry.expectedCorporationTax)}</LedgerFact>
                    <LedgerFact>Sign-off: {entry.signOffPacketState}</LedgerFact>
                    <LedgerFact>Sources: {entry.sourceIds.join(", ")}</LedgerFact>
                    <LedgerFact>Checks: {entry.expectedValueChecks.join(", ")}</LedgerFact>
                    <LedgerFact>Artifacts: {entry.outputArtifacts.join(", ")}</LedgerFact>
                  </div>
                </div>
              ))}
            </div>
          </section>
        </div>

        <div className="min-w-0 space-y-3">
          <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Human evidence closeout</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                  Complete reviewer templates, verify release evidence, then verify the final artifact pack for the same candidate.
                </p>
              </div>
              <StatusBadge tone={pendingHumanEvidenceCount > 0 ? "bad" : "good"}>
                {pendingHumanEvidenceCount} pending
              </StatusBadge>
            </div>
            <div className="mt-3 space-y-2 text-xs leading-5">
              <CloseoutStep label={`${humanEvidenceTemplateCount} templates`} value="Docs/release-evidence/*.md" />
              <CloseoutStep label="Verifier" value="scripts/verify-release-evidence.ps1" />
              <CloseoutStep label="Completion" value={`${humanEvidenceTemplateCount} accepted humanEvidenceCompletion rows`} />
              <CloseoutStep label="Final pack" value="scripts/verify-release-artifact-pack.ps1" />
            </div>
          </div>

          <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <div className="flex items-center justify-between gap-2">
              <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Completion tracks</p>
              <StatusBadge tone={report.completionTracks.every((track) => track.status === "complete") ? "good" : "warn"}>
                {report.completionTracks.length} tracks
              </StatusBadge>
            </div>
            <div className="mt-3 space-y-2">
              {report.completionTracks.map((track) => (
                <div key={track.code} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
                  <div className="flex items-center justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-sm font-medium text-[var(--foreground)]">{track.label}</p>
                      <p className="mt-1 text-xs text-[var(--muted-foreground)]">{track.ownerRole}</p>
                    </div>
                    <StatusBadge tone={completionTrackTone(track.status)}>{formatStatus(track.status)}</StatusBadge>
                  </div>
                  {track.nextActions.length > 0 && (
                    <div className="mt-3 space-y-1">
                      {track.nextActions.slice(0, 2).map((action) => (
                        <p key={action} className="rounded border border-[var(--border)] bg-[var(--surface)] px-2 py-1.5 text-xs leading-5 text-[var(--foreground)]">
                          {action}
                        </p>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
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

          <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
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

function CloseoutStep({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex min-w-0 items-start justify-between gap-2 rounded border border-[var(--border)] bg-[var(--surface-subtle)] px-2 py-1.5">
      <span className="shrink-0 font-medium text-[var(--foreground)]">{label}</span>
      <code className="min-w-0 break-all text-right text-[11px] text-[var(--muted-foreground)]">{value}</code>
    </div>
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

function LedgerFact({ children }: { children: ReactNode }) {
  return (
    <p className="min-w-0 rounded border border-[var(--border)] bg-[var(--surface)] px-2 py-1.5 font-medium text-[var(--foreground)]">
      {children}
    </p>
  );
}

function formatStatus(value: string) {
  const words = value
    .split("-")
    .filter(Boolean)
    .join(" ");

  return words.charAt(0).toUpperCase() + words.slice(1);
}

function completionTrackTone(status: string) {
  if (status === "complete") {
    return "good";
  }

  if (status === "in-progress") {
    return "info";
  }

  return "warn";
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}
