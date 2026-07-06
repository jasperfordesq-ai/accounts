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
  DataGrid,
  MetricStrip,
  PageShell,
  ReleaseBlockerSummary,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
} from "@/components/workbench";

export function ProductionReadinessWorkbench({ report }: { report: ProductionReadinessReport }) {
  const assuranceActions = report.assuranceActions ?? [];
  const completionTracks = report.completionTracks ?? [];
  const accountantAcceptanceCriteria = report.accountantAcceptanceCriteria ?? [];
  const statutoryRuleMatrix = report.statutoryRuleMatrix ?? [];
  const statutoryRulesCoverage = report.statutoryRulesCoverage ?? [];
  const auditabilityControls = report.auditabilityControls ?? [];
  const auditEvidenceTimeline = report.auditEvidenceTimeline ?? [];
  const monitoringControls = report.monitoringControls ?? [];
  const dependencyPolicyControls = report.dependencyPolicyControls ?? [];
  const deploymentSafetyControls = report.deploymentSafetyControls ?? [];
  const releaseBlockerRegister = report.releaseBlockerRegister ?? [];
  const releaseReviewChecklist = report.releaseReviewChecklist ?? [];
  const releaseVerificationManifest = report.releaseVerificationManifest ?? [];
  const sourceLawTraceability = report.sourceLawTraceability ?? [];
  const sourceLawReviewLedger = report.sourceLawReviewLedger ?? [];
  const sourceLawMaintenanceProtocol = report.sourceLawMaintenanceProtocol;
  const visualQaCoverage = report.visualQaCoverage;
  const assurancePacket = report.assurancePacket;
  const accountantAcceptanceSummary = report.accountantAcceptanceSummary;
  const accountantWorkflowWalkthroughProtocol = report.accountantWorkflowWalkthroughProtocol;
  const accountantJourneyAcceptanceChecklist = report.accountantJourneyAcceptanceChecklist ?? [];
  const hardenedAreas = report.areas.filter((area) => area.status === "hardened").length;
  const coveredScenarios = report.goldenFilingCorpus.filter((scenario) => scenario.coverageStatus === "covered").length;
  const enforcedGates = report.operationalGates.filter((gate) => gate.status === "enforced").length;
  const completedAssuranceActions = assuranceActions.filter((action) => action.status === "complete").length;
  const statusTone = report.overallStatus === "ready" ? "good" : "warn";
  const releaseReady = report.overallStatus === "ready" && assurancePacket.openCriticalActions === 0;
  const openVisualRouteReviews = visualQaCoverage?.routeAudits.filter((audit) => audit.reviewStatus !== "accepted") ?? [];
  const visualScreenshotsRequiringReview = openVisualRouteReviews.reduce(
    (total, audit) => total + audit.screenshotCount,
    0,
  );

  return (
    <PageShell
      title="Production Readiness Checklist"
      subtitle="Accountant-facing evidence for statutory rules, golden filing coverage, unsupported paths, operational controls, and professional review gates."
      backHref="/"
      backLabel="Dashboard"
      meta={
        <>
          <StatusBadge tone={statusTone}>{formatStatus(report.overallStatus)}</StatusBadge>
          <span className="text-xs text-[var(--muted-foreground)]">Generated {formatDateTime(report.generatedAt)}</span>
        </>
      }
    >
      <MetricStrip
        metrics={[
          { label: "Companies in database", value: `${report.companiesInDatabase} companies`, tone: "default" },
          { label: "Periods in database", value: `${report.periodsInDatabase} periods`, tone: "default" },
          { label: "Hardened areas", value: `${hardenedAreas}/${report.areas.length}`, tone: hardenedAreas === report.areas.length ? "good" : "warn" },
          { label: "Assurance actions", value: `${completedAssuranceActions}/${assuranceActions.length}`, tone: completedAssuranceActions === assuranceActions.length ? "good" : "warn" },
        ]}
      />

      <ReviewPanel
        title="Release decision summary"
        description="The top-line release call before a reviewer drills into source law, golden scenarios, visual evidence and operational controls."
        actions={
          <StatusBadge tone={releaseReady ? "good" : "bad"}>
            {releaseReady ? "Ready for controlled release" : "Do not use for real filings"}
          </StatusBadge>
        }
      >
        <div className="grid overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] md:grid-cols-4 md:divide-x divide-y md:divide-y-0 divide-[var(--border)]">
          <DecisionSummaryItem
            label="Critical blockers"
            value={`${assurancePacket.openCriticalActions} critical ${assurancePacket.openCriticalActions === 1 ? "blocker" : "blockers"}`}
            detail={assurancePacket.releaseBlockers[0] ?? "No release blockers reported."}
            tone={assurancePacket.openCriticalActions === 0 ? "good" : "bad"}
          />
          <DecisionSummaryItem
            label="Golden corpus covered"
            value={`${assurancePacket.goldenCorpusCovered} of ${assurancePacket.goldenCorpusTotal} scenarios`}
            detail="Backend output evidence for PDF text, iXBRL, tax, notes, readiness and filing gates."
            tone={assurancePacket.goldenCorpusCovered === assurancePacket.goldenCorpusTotal ? "good" : "warn"}
          />
          <DecisionSummaryItem
            label="Visual QA evidence"
            value={`${assurancePacket.visualQaExpectedScreenshots} required screenshots`}
            detail="Light and dark desktop/mobile screenshots for the accountant workflow routes."
            tone="info"
          />
          <DecisionSummaryItem
            label="Accountant acceptance"
            value={`${accountantAcceptanceSummary.professionalSignOffRequiredCount} professional ${accountantAcceptanceSummary.professionalSignOffRequiredCount === 1 ? "sign-off" : "sign-offs"}`}
            detail={`${accountantAcceptanceSummary.manualHandoffScenarioCount} manual handoff ${accountantAcceptanceSummary.manualHandoffScenarioCount === 1 ? "scenario" : "scenarios"}`}
            tone="warn"
          />
        </div>
        <div className="mt-3 grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-xs leading-5 text-[var(--muted-foreground)] md:grid-cols-3">
          <div>
            <p className="font-semibold uppercase text-[var(--muted-foreground)]">Automated verifiers</p>
            <p className="mt-1 text-sm font-medium text-[var(--foreground)]">
              {accountantAcceptanceSummary.automatedVerifierCount} automated {accountantAcceptanceSummary.automatedVerifierCount === 1 ? "verifier" : "verifiers"}
            </p>
          </div>
          <div>
            <p className="font-semibold uppercase text-[var(--muted-foreground)]">Blocking scenarios</p>
            <p className="mt-1 break-words text-sm font-medium text-[var(--foreground)]">
              {accountantAcceptanceSummary.releaseBlockingScenarioCodes.join(", ") || "None"}
            </p>
          </div>
          <div>
            <p className="font-semibold uppercase text-[var(--muted-foreground)]">Acceptance status</p>
            <div className="mt-1">
              <StatusBadge tone={accountantAcceptanceSummary.status === "accepted" ? "good" : "warn"}>
                {formatStatus(accountantAcceptanceSummary.status)}
              </StatusBadge>
            </div>
          </div>
        </div>
      </ReviewPanel>

      <ReviewPanel
        title="Production assurance packet"
        description="A deterministic release-evidence fingerprint tying source law, golden corpus coverage, statutory rules, visual QA and operational gates together."
        actions={<StatusBadge tone={assurancePacket.status === "ready" ? "good" : "warn"}>{formatPacketStatus(assurancePacket.status)}</StatusBadge>}
      >
        <div className="grid gap-4 xl:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)]">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Packet id</p>
            <code className="mt-1 block break-all text-xs leading-5 text-[var(--foreground)]">{assurancePacket.packetId}</code>
            <p className="mt-2 break-all text-xs text-[var(--muted-foreground)]">{assurancePacket.packetVersion}</p>
            <p className="mt-2 break-all text-xs text-[var(--muted-foreground)]">
              Source hash {assurancePacket.sourceLawSnapshotHash}
            </p>
          </div>

          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <PacketMetric
              label="Golden corpus"
              value={`${assurancePacket.goldenCorpusCovered}/${assurancePacket.goldenCorpusTotal}`}
              tone={assurancePacket.goldenCorpusCovered === assurancePacket.goldenCorpusTotal ? "good" : "warn"}
              status={assurancePacket.goldenCorpusCovered === assurancePacket.goldenCorpusTotal ? "Covered" : "Partial"}
            />
            <PacketMetric
              label="Visual QA"
              value={`${assurancePacket.visualQaExpectedScreenshots} screenshots`}
              tone="info"
              status="Expected"
            />
            <PacketMetric
              label="Required gates"
              value={`${assurancePacket.requiredOperationalGates}`}
              tone="warn"
              status="Required"
            />
            <PacketMetric
              label="Open critical actions"
              value={`${assurancePacket.openCriticalActions}`}
              tone={assurancePacket.openCriticalActions === 0 ? "good" : "bad"}
              status={assurancePacket.openCriticalActions === 0 ? "Clear" : "Open"}
            />
          </div>
        </div>

        <div className="mt-4 grid gap-4 xl:grid-cols-2">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Evidence items</p>
            <div className="mt-2">
              <CompactList items={assurancePacket.evidenceItems} />
            </div>
          </div>
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Release blockers</p>
            <div className="mt-2">
              {assurancePacket.releaseBlockers.length === 0 ? (
                <EmptyLine label="No release blockers." />
              ) : (
                <CompactList items={assurancePacket.releaseBlockers} />
              )}
            </div>
          </div>
        </div>
      </ReviewPanel>

      <ReviewPanel
        title="Source-law maintenance"
        description="Release gate for keeping CRO, Revenue, FRC and charity guidance aligned with the pinned source-law snapshot before real filing use."
        actions={<StatusBadge tone={sourceLawMaintenanceProtocol.status === "complete" ? "good" : "warn"}>{formatStatus(sourceLawMaintenanceProtocol.status)}</StatusBadge>}
      >
        <div className="grid gap-4 xl:grid-cols-[minmax(0,0.85fr)_minmax(0,1.15fr)]">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Protocol</p>
                <code className="mt-1 block break-all text-xs text-[var(--foreground)]">{sourceLawMaintenanceProtocol.protocolVersion}</code>
              </div>
              <StatusBadge tone="bad">Release gate</StatusBadge>
            </div>
            <div className="mt-3 space-y-3 text-xs leading-5 text-[var(--muted-foreground)]">
              <div>
                <p className="font-semibold uppercase text-[var(--foreground)]">Owner</p>
                <p>{sourceLawMaintenanceProtocol.ownerRole}</p>
              </div>
              <div>
                <p className="font-semibold uppercase text-[var(--foreground)]">Cadence</p>
                <p>{sourceLawMaintenanceProtocol.reviewCadence}</p>
                <p className="mt-1">Next review due {formatDate(sourceLawMaintenanceProtocol.nextReviewDue)}</p>
              </div>
              <div>
                <p className="font-semibold uppercase text-[var(--foreground)]">Sign-off gate</p>
                <code className="break-all text-[11px] text-[var(--foreground)]">{sourceLawMaintenanceProtocol.signOffGate}</code>
              </div>
              <p>{sourceLawMaintenanceProtocol.failurePolicy}</p>
            </div>
          </div>

          <div className="grid gap-3 md:grid-cols-2">
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
              <p className="mb-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]">Required evidence</p>
              <CompactList items={sourceLawMaintenanceProtocol.requiredEvidence} />
            </div>
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
              <p className="mb-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]">Acceptance criteria</p>
              <CompactList items={sourceLawMaintenanceProtocol.acceptanceCriteria} />
            </div>
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 md:col-span-2">
              <p className="mb-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]">Monitored source ids</p>
              <CodeStack items={sourceLawMaintenanceProtocol.monitoredSourceIds} />
              <p className="mt-3 text-xs leading-5 text-[var(--muted-foreground)]">{sourceLawMaintenanceProtocol.changeDetection}</p>
            </div>
          </div>
        </div>
      </ReviewPanel>

      <ReviewPanel
        title="Source-law review ledger"
        description="Per-source release evidence proving every pinned legal source has a named owner, review checks, and accountant sign-off before generated packs are used."
        actions={<StatusBadge tone="bad">{sourceLawReviewLedger.length} blocking reviews</StatusBadge>}
      >
        <DataGrid
          caption="Source-law review ledger"
          filterPlaceholder="Filter source-law review ledger"
          emptyState="No source-law review ledger entries"
          columns={["Source", "Owner", "Pinned date", "Release gate", "Review checks", "Required evidence"]}
          rows={sourceLawReviewLedger.map((entry) => ({
            id: entry.sourceId,
            tone: entry.blocksRelease ? "bad" : "warn",
            sortValues: [
              entry.title,
              entry.ownerRole,
              entry.pinnedEffectiveDate,
              entry.releaseChecklistCode,
              entry.reviewChecks.join(" "),
              entry.requiredEvidence.join(" "),
            ],
            searchText: [
              entry.sourceId,
              entry.title,
              entry.url,
              entry.ownerRole,
              entry.pinnedEffectiveDate,
              entry.releaseChecklistCode,
              ...entry.reviewChecks,
              ...entry.requiredEvidence,
            ].join(" "),
            cells: [
              <div key="source" className="min-w-56 whitespace-normal">
                <a
                  href={entry.url}
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex max-w-sm items-start gap-1.5 whitespace-normal break-words text-sm font-medium text-emerald-700 hover:text-emerald-800 dark:text-emerald-300 dark:hover:text-emerald-200"
                >
                  {entry.title}
                  <ExternalLink className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                </a>
                <code className="mt-2 block break-all rounded border border-[var(--border)] bg-[var(--surface-subtle)] px-2 py-1 text-[11px] text-[var(--muted-foreground)]">
                  {entry.sourceId}
                </code>
              </div>,
              <span key="owner" className="text-[var(--muted-foreground)]">{entry.ownerRole}</span>,
              <span key="pinned-date" className="text-[var(--muted-foreground)]">{formatDate(entry.pinnedEffectiveDate)}</span>,
              <div key="gate" className="space-y-2">
                <CodeStack items={[entry.releaseChecklistCode]} />
                <StatusBadge tone={entry.blocksRelease ? "bad" : "warn"}>
                  {entry.blocksRelease ? "Blocks release" : "Advisory"}
                </StatusBadge>
              </div>,
              <CompactList key="review-checks" items={entry.reviewChecks} />,
              <CompactList key="required-evidence" items={entry.requiredEvidence} />,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Next assurance actions"
        description="Priority-ranked work that must be evidenced before the platform can be treated as production-ready for real statutory accounts."
        actions={<StatusBadge tone="warn">{assuranceActions.length - completedAssuranceActions} open</StatusBadge>}
      >
        <DataGrid
          caption="Next assurance actions"
          filterPlaceholder="Filter assurance actions"
          emptyState="No matching assurance actions"
          defaultSort={{ columnIndex: 2, direction: "asc" }}
          columns={["Action", "Owner", "Risk", "Stage", "Evidence required", "Status"]}
          rows={assuranceActions.map((action) => ({
            id: action.code,
            tone: action.status === "complete" ? "good" : action.priority === "critical" ? "bad" : action.status === "in-progress" ? "info" : "warn",
            sortValues: [
              action.label,
              action.owner,
              action.riskRank,
              action.evidenceStage,
              action.evidenceRequired,
              action.status,
            ],
            searchText: [
              action.label,
              action.owner,
              action.priority,
              action.riskRank,
              action.evidenceStage,
              action.status,
              action.detail,
              action.evidenceRequired,
            ].join(" "),
            cells: [
              <div key="action" className="min-w-48 whitespace-normal">
                <p className="font-medium">{action.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{action.detail}</p>
              </div>,
              <span key="owner" className="text-[var(--muted-foreground)]">{action.owner}</span>,
              <StatusBadge key="risk" tone={riskTone(action.riskRank)}>Risk {action.riskRank}</StatusBadge>,
              <div key="stage" className="min-w-36">
                <StatusBadge tone={priorityTone(action.priority)}>{formatStatus(action.priority)}</StatusBadge>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{action.evidenceStage}</code>
              </div>,
              <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{action.evidenceRequired}</span>,
              <StatusBadge key="status" tone={action.status === "complete" ? "good" : action.status === "in-progress" ? "info" : "warn"}>
                {formatStatus(action.status)}
              </StatusBadge>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Release blocker register"
        description="Track-owned production blockers tied to assurance actions, release checklist evidence and the next action required to clear them."
        actions={<StatusBadge tone={releaseBlockerRegister.some((blocker) => blocker.blocksRelease) ? "bad" : "good"}>{releaseBlockerRegister.length} blockers</StatusBadge>}
      >
        <div className="mb-4">
          <ReleaseBlockerSummary blockers={releaseBlockerRegister} actionHref="" />
        </div>
        <DataGrid
          caption="Release blocker register"
          filterPlaceholder="Filter release blocker register"
          emptyState="No release blockers"
          defaultSort={{ columnIndex: 2, direction: "asc" }}
          columns={["Blocker", "Track", "Risk", "Evidence", "Next action", "Linked gate"]}
          rows={releaseBlockerRegister.map((blocker) => ({
            id: blocker.code,
            tone: blocker.blocksRelease ? "bad" : "warn",
            sortValues: [
              blocker.blockingIssue,
              blocker.trackLabel,
              blocker.riskRank,
              blocker.evidenceArtifact,
              blocker.nextAction,
              blocker.releaseChecklistCode,
            ],
            searchText: [
              blocker.code,
              blocker.trackCode,
              blocker.trackLabel,
              blocker.ownerRole,
              blocker.severity,
              blocker.riskRank,
              blocker.blockingIssue,
              blocker.requiredEvidence,
              blocker.nextAction,
              blocker.sourceActionCode,
              blocker.releaseChecklistCode,
              blocker.operationalGateCode,
              blocker.evidenceArtifact,
            ].join(" "),
            cells: [
              <div key="blocker" className="min-w-48 whitespace-normal">
                <p className="font-medium">{blocker.blockingIssue}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{blocker.code}</code>
              </div>,
              <div key="track" className="min-w-36 whitespace-normal">
                <p className="font-medium">{blocker.trackLabel}</p>
                <p className="mt-1 text-xs text-[var(--muted-foreground)]">{blocker.ownerRole}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{blocker.trackCode}</code>
              </div>,
              <div key="risk" className="space-y-1">
                <StatusBadge tone={priorityTone(blocker.severity)}>{formatStatus(blocker.severity)}</StatusBadge>
                <StatusBadge tone={riskTone(blocker.riskRank)}>Risk {blocker.riskRank}</StatusBadge>
              </div>,
              <div key="evidence" className="min-w-52 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
                <p className="font-medium text-[var(--foreground)]">{blocker.requiredEvidence}</p>
                <code className="mt-1 block break-all text-[11px]">{blocker.evidenceArtifact}</code>
              </div>,
              <span key="next" className="min-w-52 whitespace-normal text-[var(--muted-foreground)]">{blocker.nextAction}</span>,
              <div key="gate" className="space-y-1">
                <code className="block break-all text-[11px] text-[var(--muted-foreground)]">{blocker.sourceActionCode}</code>
                <code className="block break-all text-[11px] text-[var(--muted-foreground)]">{blocker.releaseChecklistCode}</code>
                {blocker.operationalGateCode ? (
                  <code className="block break-all text-[11px] text-[var(--muted-foreground)]">{blocker.operationalGateCode}</code>
                ) : (
                  <span className="text-xs text-[var(--muted-foreground)]">Checklist evidence only</span>
                )}
              </div>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Production completion map"
        description="The remaining production work split into backend code, frontend UI/UX and frontend code, with evidence and release actions tied together."
        actions={<StatusBadge tone={completionTracks.every((track) => track.status === "complete") ? "good" : "warn"}>{completionTracks.length} tracks</StatusBadge>}
      >
        <DataGrid
          caption="Production completion map"
          filterPlaceholder="Filter production completion map"
          emptyState="No completion tracks"
          columns={["Track", "Completion criteria", "Current evidence", "Next actions", "Assurance links", "Status"]}
          rows={completionTracks.map((track) => ({
            id: track.code,
            tone: track.status === "complete" ? "good" : track.status === "in-progress" ? "info" : track.status === "blocked" ? "bad" : "warn",
            searchText: [
              track.code,
              track.label,
              track.ownerRole,
              track.status,
              ...track.completionCriteria,
              ...track.currentEvidence,
              ...track.nextActions,
              ...track.assuranceActionCodes,
            ].join(" "),
            cells: [
              <div key="track" className="min-w-40 whitespace-normal">
                <p className="font-medium">{track.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{track.ownerRole}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{track.code}</code>
              </div>,
              <CompactList key="criteria" items={track.completionCriteria} />,
              <CompactList key="evidence" items={track.currentEvidence} />,
              <CompactList key="next-actions" items={track.nextActions} />,
              <CodeStack key="links" items={track.assuranceActionCodes} />,
              <StatusBadge key="status" tone={track.status === "complete" ? "good" : track.status === "in-progress" ? "info" : track.status === "blocked" ? "bad" : "warn"}>
                {formatStatus(track.status)}
              </StatusBadge>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Release review checklist"
        description="Role-owned evidence checklist tying release blockers to assurance actions, operational gates, audit events and CI artifacts."
        actions={<StatusBadge tone={releaseReviewChecklist.some((item) => item.blocksRelease) ? "bad" : "good"}>{releaseReviewChecklist.filter((item) => item.blocksRelease).length} blocking</StatusBadge>}
      >
        <DataGrid
          caption="Release review checklist"
          filterPlaceholder="Filter release checklist"
          emptyState="No release checklist items"
          columns={["Gate", "Owner", "Evidence artifact", "Linked controls", "Audit evidence", "Status"]}
          rows={releaseReviewChecklist.map((item) => ({
            id: item.code,
            tone: item.blocksRelease ? "bad" : item.status === "complete" ? "good" : "warn",
            searchText: [
              item.code,
              item.label,
              item.ownerRole,
              item.status,
              item.evidenceArtifact,
              item.assuranceActionCode,
              item.operationalGateCode,
              ...item.auditEventCodes,
              item.detail,
            ].join(" "),
            cells: [
              <div key="gate" className="min-w-48 whitespace-normal">
                <p className="font-medium">{item.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{item.detail}</p>
              </div>,
              <span key="owner" className="text-[var(--muted-foreground)]">{item.ownerRole}</span>,
              <code key="artifact" className="break-all text-[11px] text-[var(--foreground)]">{item.evidenceArtifact}</code>,
              <div key="controls" className="space-y-1">
                <code className="block break-all text-[11px] text-[var(--muted-foreground)]">{item.assuranceActionCode}</code>
                {item.operationalGateCode ? (
                  <code className="block break-all text-[11px] text-[var(--muted-foreground)]">{item.operationalGateCode}</code>
                ) : (
                  <span className="text-xs text-[var(--muted-foreground)]">No app gate</span>
                )}
              </div>,
              item.auditEventCodes.length > 0 ? (
                <CodeStack key="audit" items={item.auditEventCodes} />
              ) : (
                <span key="audit" className="text-xs text-[var(--muted-foreground)]">CI artifact</span>
              ),
              <StatusBadge key="status" tone={item.blocksRelease ? "bad" : item.status === "complete" ? "good" : "warn"}>
                {item.blocksRelease ? "Blocks release" : formatStatus(item.status)}
              </StatusBadge>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Release verification manifest"
        description="Executable commands and manual fallbacks that produce the release evidence pack when CI is unavailable, skipped, or environment-gated."
        actions={<StatusBadge tone={releaseVerificationManifest.some((item) => item.blocksRelease) ? "bad" : "good"}>{releaseVerificationManifest.filter((item) => item.blocksRelease).length} blocking checks</StatusBadge>}
      >
        <DataGrid
          caption="Release verification manifest"
          filterPlaceholder="Filter release verification manifest"
          emptyState="No release verification commands"
          defaultSort={{ columnIndex: 4, direction: "desc" }}
          columns={["Verification", "Owner", "Command", "CI scope", "Evidence linkage", "Fallback"]}
          rows={releaseVerificationManifest.map((item) => ({
            id: item.code,
            tone: item.blocksRelease ? "bad" : item.runsInDefaultCi ? "good" : "warn",
            sortValues: [
              item.label,
              item.ownerRole,
              item.command,
              item.ciScope,
              item.blocksRelease ? 1 : 0,
              item.manualFallback,
            ],
            searchText: [
              item.code,
              item.label,
              item.ownerRole,
              item.command,
              item.ciScope,
              item.evidenceArtifact,
              item.releaseChecklistEvidenceArtifact,
              item.manualFallback,
            ].join(" "),
            cells: [
              <div key="verification" className="min-w-48 whitespace-normal">
                <p className="font-medium">{item.label}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{item.code}</code>
              </div>,
              <span key="owner" className="text-[var(--muted-foreground)]">{item.ownerRole}</span>,
              <code key="command" className="block min-w-56 whitespace-normal break-all rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-2 text-[11px] leading-5 text-[var(--foreground)]">
                {item.command}
              </code>,
              <div key="scope" className="space-y-1">
                <StatusBadge tone={ciScopeTone(item.ciScope)}>{item.ciScope}</StatusBadge>
                <StatusBadge tone={item.runsInDefaultCi ? "good" : "warn"}>
                  {item.runsInDefaultCi ? "Default CI" : "Manual evidence"}
                </StatusBadge>
              </div>,
              <div key="evidence" className="min-w-48 space-y-1">
                <code className="block break-all text-[11px] text-[var(--foreground)]">{item.evidenceArtifact}</code>
                <p className="text-xs text-[var(--muted-foreground)]">Checklist: {item.releaseChecklistEvidenceArtifact}</p>
                {item.blocksRelease && <StatusBadge tone="bad">Blocks release</StatusBadge>}
              </div>,
              <span key="fallback" className="whitespace-normal text-[var(--muted-foreground)]">{item.manualFallback}</span>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Accountant workflow walkthrough"
        description="Named qualified-accountant walkthrough protocol for taking the seeded golden companies through the live accountant journey."
        actions={<StatusBadge tone="warn">{formatStatus(accountantWorkflowWalkthroughProtocol.status)}</StatusBadge>}
      >
        <div className="grid gap-4 xl:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)]">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Reviewer</p>
                <p className="mt-1 text-sm font-semibold text-[var(--foreground)]">
                  {accountantWorkflowWalkthroughProtocol.reviewerRole}
                </p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">
                  {accountantWorkflowWalkthroughProtocol.protocolVersion}
                </code>
              </div>
              <StatusBadge tone="bad">Blocks release</StatusBadge>
            </div>
            <div className="mt-4 space-y-3 text-xs leading-5 text-[var(--muted-foreground)]">
              <div>
                <p className="mb-1 font-semibold uppercase text-[var(--foreground)]">Sign-off gate</p>
                <code className="break-all text-[11px] text-[var(--foreground)]">
                  {accountantWorkflowWalkthroughProtocol.signOffGate}
                </code>
              </div>
              <div>
                <p className="mb-1 font-semibold uppercase text-[var(--foreground)]">Seeded scenarios</p>
                <div className="flex flex-wrap gap-1.5">
                  {accountantWorkflowWalkthroughProtocol.seededScenarioCodes.map((code) => (
                    <StatusBadge key={code} tone="default">{code}</StatusBadge>
                  ))}
                </div>
              </div>
              <p>{accountantWorkflowWalkthroughProtocol.failurePolicy}</p>
            </div>
          </div>
          <div className="grid gap-3 lg:grid-cols-3">
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
              <p className="mb-2 text-xs font-semibold uppercase text-[var(--foreground)]">Route sequence</p>
              <CompactList items={accountantWorkflowWalkthroughProtocol.routeSequence} />
            </div>
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
              <p className="mb-2 text-xs font-semibold uppercase text-[var(--foreground)]">Acceptance criteria</p>
              <CompactList items={accountantWorkflowWalkthroughProtocol.acceptanceCriteria} />
            </div>
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
              <p className="mb-2 text-xs font-semibold uppercase text-[var(--foreground)]">Required evidence</p>
              <CompactList items={accountantWorkflowWalkthroughProtocol.requiredEvidence} />
            </div>
          </div>
        </div>
      </ReviewPanel>

      <ReviewPanel
        title="Accountant journey acceptance checklist"
        description="Route-by-route acceptance evidence tying the live accountant workbench journey to seeded golden scenarios and visual smoke artifacts."
        actions={<StatusBadge tone="warn">{accountantJourneyAcceptanceChecklist.length} routes</StatusBadge>}
      >
        <DataGrid
          caption="Accountant journey acceptance checklist"
          filterPlaceholder="Filter accountant journey acceptance"
          emptyState="No matching accountant journey acceptance entries"
          columns={["Route", "Workflow", "Seeded scenarios", "Visual evidence", "Acceptance criteria", "Required evidence", "Status"]}
          rows={accountantJourneyAcceptanceChecklist.map((item) => ({
            id: item.routeCode,
            tone: item.status === "accepted" ? "good" : "warn",
            searchText: [
              item.routeCode,
              item.routeLabel,
              item.routeKey,
              item.status,
              item.signOffGate,
              ...item.workflowStages,
              ...item.seededScenarioCodes,
              ...item.visualArtifactNames,
              ...item.acceptanceCriteria,
              ...item.requiredEvidence,
            ].join(" "),
            cells: [
              <div key="route" className="min-w-44 whitespace-normal">
                <p className="font-medium">{item.routeLabel}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{item.routeCode}</code>
                <p className="mt-1 text-xs text-[var(--muted-foreground)]">Gate: {item.signOffGate}</p>
              </div>,
              <div key="workflow" className="flex min-w-44 flex-wrap gap-1.5">
                {item.workflowStages.map((stage) => (
                  <StatusBadge key={stage} tone="default">{stage}</StatusBadge>
                ))}
              </div>,
              <CodeStack key="seeded-scenarios" items={item.seededScenarioCodes} />,
              <CodeStack key="visual-artifacts" items={item.visualArtifactNames} />,
              <CompactList key="acceptance-criteria" items={item.acceptanceCriteria} />,
              <CompactList key="required-evidence" items={item.requiredEvidence} />,
              <StatusBadge key="status" tone={item.status === "accepted" ? "good" : "warn"}>
                {formatStatus(item.status)}
              </StatusBadge>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Accountant acceptance criteria"
        description="Scenario-by-scenario acceptance gates a named qualified accountant must review before generated packs are trusted for real statutory use."
        actions={<StatusBadge tone="warn">{accountantAcceptanceCriteria.length} required</StatusBadge>}
      >
        <DataGrid
          caption="Accountant acceptance criteria"
          filterPlaceholder="Filter accountant acceptance criteria"
          emptyState="No matching accountant acceptance criteria"
          columns={["Scenario", "Review scope", "Required evidence", "Sign-off gate", "Automated evidence", "Sources", "Status"]}
          rows={accountantAcceptanceCriteria.map((criterion) => ({
            id: criterion.scenarioCode,
            tone: criterion.acceptanceStatus.includes("manual-handoff") ? "warn" : "info",
            searchText: [
              criterion.scenarioCode,
              criterion.label,
              criterion.acceptanceStatus,
              criterion.requiredSignOffGate,
              ...criterion.reviewScope,
              ...criterion.requiredEvidence,
              ...criterion.evidenceVerifiers.flatMap((verifier) => [
                verifier.name,
                verifier.command,
                verifier.ciScope,
                verifier.environment,
              ]),
              ...criterion.sources.map((source) => source.title),
            ].join(" "),
            cells: [
              <div key="scenario" className="min-w-48 whitespace-normal">
                <p className="font-medium">{criterion.label}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{criterion.scenarioCode}</code>
              </div>,
              <CompactList key="scope" items={criterion.reviewScope} />,
              <CompactList key="evidence" items={criterion.requiredEvidence} />,
              <span key="gate" className="whitespace-normal text-[var(--muted-foreground)]">{criterion.requiredSignOffGate}</span>,
              <VerifierScopeList key="acceptance-verifiers" verifiers={criterion.evidenceVerifiers} label="Acceptance verifier" />,
              <SourceLinkList key="sources" sources={criterion.sources} />,
              <StatusBadge key="status" tone={criterion.acceptanceStatus.includes("manual-handoff") ? "warn" : "info"}>
                {formatStatus(criterion.acceptanceStatus)}
              </StatusBadge>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Production auditability"
        description="Controls proving who changed data, who approved outputs, what evidence was present, what was generated, and how the audit chain is checked."
        actions={<StatusBadge tone={auditabilityControls.every((control) => control.required) ? "good" : "warn"}>{auditabilityControls.length} controls</StatusBadge>}
      >
        <DataGrid
          caption="Production auditability"
          filterPlaceholder="Filter auditability controls"
          emptyState="No matching auditability controls"
          columns={["Control", "Enforcement", "Evidence captured", "Verification", "Audit events"]}
          rows={auditabilityControls.map((control) => ({
            id: control.code,
            tone: control.required ? "good" : "warn",
            searchText: [
              control.label,
              control.enforcement,
              control.evidenceCaptured,
              control.verification,
              ...control.auditEventCodes,
            ].join(" "),
            cells: [
              <div key="control" className="min-w-48 whitespace-normal">
                <p className="font-medium">{control.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{control.required ? "Required production control" : "Advisory control"}</p>
              </div>,
              <span key="enforcement" className="whitespace-normal text-[var(--muted-foreground)]">{control.enforcement}</span>,
              <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{control.evidenceCaptured}</span>,
              <span key="verification" className="whitespace-normal text-[var(--muted-foreground)]">{control.verification}</span>,
              <CodeStack key="events" items={control.auditEventCodes} />,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Audit evidence timeline"
        description="Chronological evidence capture points proving when data changed, outputs were generated, professional approval happened, and external validation evidence was present."
        actions={<StatusBadge tone="info">{auditEvidenceTimeline.length} capture points</StatusBadge>}
      >
        <DataGrid
          caption="Audit evidence timeline"
          filterPlaceholder="Filter audit evidence timeline"
          emptyState="No audit evidence timeline entries"
          columns={["Stage", "Evidence question", "Captured when", "Actor", "Verification", "Audit events", "Blocking gates"]}
          rows={auditEvidenceTimeline.map((entry) => ({
            id: entry.code,
            tone: entry.blockingGateCodes.some((gate) => gate.includes("qualified-accountant") || gate.includes("external-ros")) ? "warn" : "info",
            searchText: [
              entry.code,
              entry.stage,
              entry.evidenceQuestion,
              entry.capturedWhen,
              entry.requiredActor,
              entry.verification,
              ...entry.auditEventCodes,
              ...entry.blockingGateCodes,
            ].join(" "),
            cells: [
              <div key="stage" className="min-w-44 whitespace-normal">
                <p className="font-medium">{entry.stage}</p>
                <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{entry.code}</code>
              </div>,
              <span key="question" className="whitespace-normal text-[var(--foreground)]">{entry.evidenceQuestion}</span>,
              <span key="when" className="whitespace-normal text-[var(--muted-foreground)]">{entry.capturedWhen}</span>,
              <span key="actor" className="whitespace-normal text-[var(--muted-foreground)]">{entry.requiredActor}</span>,
              <span key="verification" className="whitespace-normal text-[var(--muted-foreground)]">{entry.verification}</span>,
              <CodeStack key="events" items={entry.auditEventCodes} />,
              <CodeStack key="gates" items={entry.blockingGateCodes} />,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Production monitoring"
        description="Operational controls proving production errors are routed, logs are structured, and every safe error response can be traced back to server evidence."
        actions={<StatusBadge tone={monitoringControls.every((control) => control.required) ? "good" : "warn"}>{monitoringControls.length} controls</StatusBadge>}
      >
        <DataGrid
          caption="Production monitoring"
          filterPlaceholder="Filter monitoring controls"
          emptyState="No matching monitoring controls"
          columns={["Control", "Provider", "Safety gate", "Alert route", "Evidence captured", "Verification", "Failure policy", "Status"]}
          rows={monitoringControls.map((control) => ({
            id: control.code,
            tone: control.required ? "good" : "warn",
            searchText: [
              control.label,
              control.provider,
              control.productionSafetyGate,
              control.alertRoute,
              control.evidenceCaptured,
              control.verification,
              control.failurePolicy,
            ].join(" "),
            cells: [
              <div key="control" className="min-w-48 whitespace-normal">
                <p className="font-medium">{control.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{control.required ? "Required production control" : "Advisory control"}</p>
              </div>,
              <span key="provider" className="whitespace-normal text-[var(--muted-foreground)]">{control.provider}</span>,
              <code key="gate" className="break-all rounded border border-[var(--border)] bg-[var(--surface-subtle)] px-2 py-1 text-[11px] text-[var(--muted-foreground)]">
                {control.productionSafetyGate}
              </code>,
              <span key="alert-route" className="whitespace-normal text-[var(--muted-foreground)]">{control.alertRoute}</span>,
              <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{control.evidenceCaptured}</span>,
              <span key="verification" className="whitespace-normal text-[var(--muted-foreground)]">{control.verification}</span>,
              <span key="failure-policy" className="whitespace-normal text-[var(--muted-foreground)]">{control.failurePolicy}</span>,
              <StatusBadge key="status" tone={control.required ? "good" : "warn"}>
                {control.required ? "Required" : "Advisory"}
              </StatusBadge>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Dependency policy controls"
        description="Release controls proving frontend, backend and CI dependency hygiene is reproducible, audited, and fails closed before production use."
        actions={<StatusBadge tone={dependencyPolicyControls.every((control) => control.required) ? "good" : "warn"}>{dependencyPolicyControls.length} controls</StatusBadge>}
      >
        <DataGrid
          caption="Dependency policy controls"
          filterPlaceholder="Filter dependency controls"
          emptyState="No matching dependency controls"
          columns={["Control", "Enforcement", "Evidence captured", "Verification", "Failure policy"]}
          rows={dependencyPolicyControls.map((control) => ({
            id: control.code,
            tone: control.required ? "good" : "warn",
            searchText: [
              control.label,
              control.enforcement,
              control.evidenceCaptured,
              control.verification,
              control.failurePolicy,
            ].join(" "),
            cells: [
              <div key="control" className="min-w-48 whitespace-normal">
                <p className="font-medium">{control.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{control.required ? "Required release control" : "Advisory release control"}</p>
              </div>,
              <span key="enforcement" className="whitespace-normal text-[var(--muted-foreground)]">{control.enforcement}</span>,
              <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{control.evidenceCaptured}</span>,
              <span key="verification" className="whitespace-normal text-[var(--muted-foreground)]">{control.verification}</span>,
              <span key="failure-policy" className="whitespace-normal text-[var(--muted-foreground)]">{control.failurePolicy}</span>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Deployment safety controls"
        description="Release controls proving migrations, demo data and backup restoration are handled deliberately before production filing packs are trusted."
        actions={<StatusBadge tone={deploymentSafetyControls.every((control) => control.required) ? "good" : "warn"}>{deploymentSafetyControls.length} controls</StatusBadge>}
      >
        <DataGrid
          caption="Deployment safety controls"
          filterPlaceholder="Filter deployment safety controls"
          emptyState="No matching deployment safety controls"
          columns={["Control", "Enforcement", "Evidence captured", "Verification", "Failure policy"]}
          rows={deploymentSafetyControls.map((control) => ({
            id: control.code,
            tone: control.required ? "good" : "warn",
            searchText: [
              control.label,
              control.enforcement,
              control.evidenceCaptured,
              control.verification,
              control.failurePolicy,
            ].join(" "),
            cells: [
              <div key="control" className="min-w-48 whitespace-normal">
                <p className="font-medium">{control.label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{control.required ? "Required deployment control" : "Advisory deployment control"}</p>
              </div>,
              <span key="enforcement" className="whitespace-normal text-[var(--muted-foreground)]">{control.enforcement}</span>,
              <span key="evidence" className="whitespace-normal text-[var(--muted-foreground)]">{control.evidenceCaptured}</span>,
              <span key="verification" className="whitespace-normal text-[var(--muted-foreground)]">{control.verification}</span>,
              <span key="failure-policy" className="whitespace-normal text-[var(--muted-foreground)]">{control.failurePolicy}</span>,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Statutory rules matrix"
        description="Accountant-readable filing paths, required evidence, outputs, fail-closed gates, and source references for the supported and unsupported Irish company workflows."
        actions={<StatusBadge tone="info">{statutoryRuleMatrix.length} paths</StatusBadge>}
      >
        <DataGrid
          caption="Statutory rules matrix"
          filterPlaceholder="Filter rules, regimes, gates or sources"
          emptyState="No matching statutory rule paths"
          columns={["Company path", "Regime", "Evidence", "Outputs", "Gates", "Sources"]}
          rows={statutoryRuleMatrix.map((row) => ({
            id: row.code,
            tone: supportTone(row.supportLevel),
            searchText: [
              row.companyScope,
              row.sizeOrRegime,
              row.supportLevel,
              ...row.requiredEvidence,
              ...row.requiredOutputs,
              ...row.manualHandoffGates,
              ...row.sources.map((source) => source.title),
            ].join(" "),
            cells: [
              <div key="path" className="min-w-44 whitespace-normal">
                <p className="font-medium">{row.companyScope}</p>
                <StatusBadge tone={supportTone(row.supportLevel)}>{formatStatus(row.supportLevel)}</StatusBadge>
              </div>,
              <span key="regime" className="whitespace-normal text-[var(--muted-foreground)]">{row.sizeOrRegime}</span>,
              <CompactList key="evidence" items={row.requiredEvidence} />,
              <CompactList key="outputs" items={row.requiredOutputs} />,
              <CompactList key="gates" items={row.manualHandoffGates} />,
              <SourceLinkList key="sources" sources={row.sources} />,
            ],
          }))}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Statutory rules coverage"
        description="Granular rule families, edge cases, executable tests, and legal sources proving the accounting engine is covered beyond the four golden paths."
        actions={<StatusBadge tone={statutoryRulesCoverage.every((item) => item.coverageStatus === "covered") ? "good" : "warn"}>{statutoryRulesCoverage.length} rule families</StatusBadge>}
      >
        <DataGrid
          caption="Statutory rules coverage"
          filterPlaceholder="Filter rule coverage"
          emptyState="No matching statutory rule coverage"
          columns={["Rule family", "Decision under test", "Edge cases", "Automated verifiers", "Sources", "Status"]}
          rows={statutoryRulesCoverage.map((coverage) => ({
            id: coverage.code,
            tone: coverage.coverageStatus === "covered" ? "good" : "warn",
            searchText: [
              coverage.ruleFamily,
              coverage.decisionUnderTest,
              coverage.coverageStatus,
              ...coverage.edgeCases,
              ...coverage.automatedVerifierNames,
              ...coverage.sources.map((source) => source.title),
            ].join(" "),
            cells: [
              <span key="family" className="font-medium">{coverage.ruleFamily}</span>,
              <span key="decision" className="whitespace-normal text-[var(--muted-foreground)]">{coverage.decisionUnderTest}</span>,
              <CompactList key="edge-cases" items={coverage.edgeCases} />,
              <CodeStack key="verifiers" items={coverage.automatedVerifierNames} />,
              <SourceLinkList key="sources" sources={coverage.sources} />,
              <StatusBadge key="status" tone={coverage.coverageStatus === "covered" ? "good" : "warn"}>
                {formatStatus(coverage.coverageStatus)}
              </StatusBadge>,
            ],
          }))}
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
                <p className="mt-1 break-all text-xs font-medium text-[var(--foreground)]">{visualQaCoverage.manifestFileName}</p>
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
              <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
                <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Layout checks</p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {visualQaCoverage.layoutChecks.map((check) => (
                    <StatusBadge key={check} tone="good">{formatStatus(check)}</StatusBadge>
                  ))}
                </div>
              </div>
              <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Visual review protocol</p>
                    <p className="mt-1 text-sm font-medium text-[var(--foreground)]">{visualQaCoverage.reviewProtocol.reviewerRole}</p>
                    <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{visualQaCoverage.reviewProtocol.protocolVersion}</code>
                  </div>
                  <StatusBadge tone="warn">{formatStatus(visualQaCoverage.reviewProtocol.status)}</StatusBadge>
                </div>
                <div className="mt-3 grid gap-3 text-xs leading-5 text-[var(--muted-foreground)]">
                  <div>
                    <p className="mb-1 font-semibold uppercase text-[var(--foreground)]">Sign-off gate</p>
                    <code className="break-all text-[11px] text-[var(--foreground)]">{visualQaCoverage.reviewProtocol.signOffGate}</code>
                  </div>
                  <div>
                    <p className="mb-1 font-semibold uppercase text-[var(--foreground)]">Required evidence</p>
                    <CompactList items={visualQaCoverage.reviewProtocol.requiredEvidence} />
                  </div>
                  <div>
                    <p className="mb-1 font-semibold uppercase text-[var(--foreground)]">Acceptance criteria</p>
                    <CompactList items={visualQaCoverage.reviewProtocol.acceptanceCriteria} />
                  </div>
                  <p>{visualQaCoverage.reviewProtocol.failurePolicy}</p>
                </div>
              </div>
            </div>

            <DataGrid
              caption="Visual QA routes"
              filterPlaceholder="Filter visual QA routes"
              emptyState="No matching visual QA routes"
              columns={["Route", "Capture key", "Workflow stages", "Required text", "Viewport evidence", "Tab action"]}
              rows={visualQaCoverage.routes.map((route) => ({
                id: route.code,
                tone: route.openFilingTab ? "info" : "default",
                searchText: [
                  route.label,
                  route.routeKey,
                  route.description,
                  route.requiredText,
                  ...route.workflowStages,
                  route.openFilingTab ? "filing tab" : "initial view",
                ].join(" "),
                cells: [
                  <div key="route" className="min-w-44 whitespace-normal">
                    <p className="font-medium">{route.label}</p>
                    <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{route.description}</p>
                  </div>,
                  <code key="route-key" className="break-all text-[11px] text-[var(--foreground)]">{route.routeKey}</code>,
                  <div key="workflow-stages" className="flex min-w-44 flex-wrap gap-1.5">
                    {route.workflowStages.map((stage) => (
                      <StatusBadge key={stage} tone="default">{stage}</StatusBadge>
                    ))}
                  </div>,
                  <span key="required-text" className="whitespace-normal text-[var(--muted-foreground)]">{route.requiredText}</span>,
                  <span key="viewport-evidence" className="text-[var(--muted-foreground)]">
                    {visualQaCoverage.themes.length * visualQaCoverage.viewports.length} screenshots
                  </span>,
                  <StatusBadge key="tab-action" tone={route.openFilingTab ? "info" : "default"}>
                    {route.openFilingTab ? "Open filing tab" : "Initial view"}
                  </StatusBadge>,
                ],
              }))}
            />

            <div className="mt-4">
              <div className="mb-3 flex flex-col gap-2 border-b border-[var(--border)] pb-3 md:flex-row md:items-end md:justify-between">
                <div>
                  <h3 className="text-sm font-semibold text-[var(--foreground)]">Visual route review board</h3>
                  <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                    Route-level visual sign-off queue for accountant workflow hierarchy, table scanability, contrast, mobile density and route states.
                  </p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <StatusBadge tone={openVisualRouteReviews.length === 0 ? "good" : "warn"}>
                    {openVisualRouteReviews.length} {openVisualRouteReviews.length === 1 ? "route review" : "route reviews"} open
                  </StatusBadge>
                  <StatusBadge tone={visualScreenshotsRequiringReview === 0 ? "good" : "warn"}>
                    {visualScreenshotsRequiringReview} screenshots requiring review
                  </StatusBadge>
                  <StatusBadge tone="info">{visualQaCoverage.reviewProtocol.signOffGate}</StatusBadge>
                </div>
              </div>
              <DataGrid
                caption="Visual route review board"
                filterPlaceholder="Filter visual route review board"
                emptyState="No matching visual route reviews"
                columns={["Route", "Evidence package", "Reviewer checks", "Required sign-off", "Status"]}
                rows={visualQaCoverage.routeAudits.map((audit) => ({
                  id: audit.routeCode,
                  tone: audit.reviewStatus === "accepted" ? "good" : "warn",
                  searchText: [
                    audit.label,
                    audit.routeCode,
                    audit.routeKey,
                    audit.reviewStatus,
                    visualQaCoverage.reviewProtocol.signOffGate,
                    ...audit.workflowStages,
                    ...audit.reviewChecks,
                    ...visualQaCoverage.reviewProtocol.requiredEvidence,
                  ].join(" "),
                  cells: [
                    <div key="route" className="min-w-44 whitespace-normal">
                      <p className="font-medium">{audit.label}</p>
                      <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{audit.routeCode}</code>
                    </div>,
                    <div key="evidence" className="min-w-48 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
                      <p className="font-medium text-[var(--foreground)]">{audit.screenshotCount} screenshots</p>
                      <p>{visualQaCoverage.manifestFileName}</p>
                      <p>{audit.workflowStages.join(", ")}</p>
                    </div>,
                    <div key="checks" className="flex min-w-52 flex-wrap gap-1.5">
                      {audit.reviewChecks.map((check) => (
                        <StatusBadge key={check} tone="info">{formatStatus(check)}</StatusBadge>
                      ))}
                    </div>,
                    <div key="sign-off" className="min-w-48 whitespace-normal">
                      <CodeStack items={[visualQaCoverage.reviewProtocol.signOffGate]} />
                      <div className="mt-2">
                        <CompactList items={visualQaCoverage.reviewProtocol.requiredEvidence.slice(0, 2)} />
                      </div>
                    </div>,
                    <StatusBadge key="status" tone={audit.reviewStatus === "accepted" ? "good" : "warn"}>
                      {formatStatus(audit.reviewStatus)}
                    </StatusBadge>,
                  ],
                }))}
              />
            </div>

            <div className="mt-4">
              <DataGrid
                caption="Route audit summary"
                filterPlaceholder="Filter visual route audits"
                emptyState="No matching visual route audits"
                columns={["Route", "Screenshots", "Workflow stages", "Review checks", "Status"]}
                rows={visualQaCoverage.routeAudits.map((audit) => ({
                  id: audit.routeCode,
                  tone: audit.reviewStatus === "accepted" ? "good" : "warn",
                  searchText: [
                    audit.label,
                    audit.routeCode,
                    audit.routeKey,
                    audit.reviewStatus,
                    ...audit.workflowStages,
                    ...audit.reviewChecks,
                  ].join(" "),
                  cells: [
                    <div key="route" className="min-w-44 whitespace-normal">
                      <p className="font-medium">{audit.label}</p>
                      <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{audit.routeCode}</code>
                    </div>,
                    <span key="screenshots" className="text-[var(--muted-foreground)]">
                      {audit.screenshotCount} screenshots
                    </span>,
                    <div key="workflow-stages" className="flex min-w-44 flex-wrap gap-1.5">
                      {audit.workflowStages.map((stage) => (
                        <StatusBadge key={stage} tone="default">{stage}</StatusBadge>
                      ))}
                    </div>,
                    <div key="review-checks" className="flex min-w-48 flex-wrap gap-1.5">
                      {audit.reviewChecks.map((check) => (
                        <StatusBadge key={check} tone="info">{formatStatus(check)}</StatusBadge>
                      ))}
                    </div>,
                    <StatusBadge key="status" tone={audit.reviewStatus === "accepted" ? "good" : "warn"}>
                      {formatStatus(audit.reviewStatus)}
                    </StatusBadge>,
                  ],
                }))}
              />
            </div>

            <div className="mt-4">
              <DataGrid
                caption="Visual QA artifact manifest"
                filterPlaceholder="Filter visual QA artifacts"
                emptyState="No matching visual QA artifacts"
                columns={["Artifact", "Route", "Theme", "Viewport", "Capture target", "Review"]}
                rows={visualQaCoverage.artifacts.map((artifact) => ({
                  id: `${artifact.routeCode}-${artifact.theme}-${artifact.viewportName}`,
                  tone: artifact.reviewStatus === "accepted" ? "good" : "warn",
                  searchText: [
                    artifact.fileName,
                    artifact.artifactPath,
                    artifact.routeCode,
                    artifact.routeKey,
                    artifact.theme,
                    artifact.viewportName,
                    artifact.requiredText,
                    artifact.reviewStatus,
                    ...artifact.layoutChecks,
                  ].join(" "),
                  cells: [
                    <div key="artifact" className="min-w-52 whitespace-normal">
                      <p className="font-medium">{artifact.fileName}</p>
                      <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{artifact.artifactPath}</code>
                    </div>,
                    <code key="route" className="break-all text-[11px] text-[var(--foreground)]">{artifact.routeCode}</code>,
                    <StatusBadge key="theme" tone="default">{formatStatus(artifact.theme)}</StatusBadge>,
                    <span key="viewport" className="text-[var(--muted-foreground)]">{artifact.viewportName}</span>,
                    <div key="target" className="min-w-48 whitespace-normal text-xs leading-5 text-[var(--muted-foreground)]">
                      <p className="font-medium text-[var(--foreground)]">{artifact.requiredText}</p>
                      <p>Capture key {artifact.routeKey}</p>
                      <p>{artifact.openFilingTab ? "Open filing tab before capture" : "Capture initial view"}</p>
                      <p>{artifact.layoutChecks.map(formatStatus).join(", ")}</p>
                    </div>,
                    <StatusBadge key="review" tone={artifact.reviewStatus === "accepted" ? "good" : "warn"}>
                      {formatStatus(artifact.reviewStatus)}
                    </StatusBadge>,
                  ],
                }))}
              />
            </div>
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
        <DataGrid
          caption="Golden filing corpus"
          filterPlaceholder="Filter golden scenarios"
          emptyState="No matching golden filing scenarios"
          columns={["Scenario", "Fixture", "Company scope", "Expected outcome", "Evidence tests", "Verifier scope", "Assertions", "Status"]}
          rows={report.goldenFilingCorpus.map((scenario) => ({
            id: scenario.code,
            tone: scenario.coverageStatus === "covered" ? "good" : "warn",
            searchText: [
              scenario.label,
              scenario.fixture.legalName,
              scenario.fixture.companyType,
              scenario.fixture.expectedSizeClass,
              scenario.fixture.expectedRegime,
              scenario.companyScope,
              scenario.expectedOutcome,
              scenario.coverageStatus,
              ...scenario.evidenceTestNames,
              ...scenario.evidenceVerifiers.flatMap((verifier) => [
                verifier.ciScope,
                verifier.environment,
                verifier.evidenceLevel,
                verifier.command,
              ]),
              ...scenario.assertions,
            ].join(" "),
            cells: [
              <span key="label" className="font-medium">{scenario.label}</span>,
              <div key="fixture" className="space-y-1 text-xs text-[var(--muted-foreground)]">
                <p className="font-semibold text-[var(--foreground)]">{scenario.fixture.legalName}</p>
                <p>{scenario.fixture.periodStart} to {scenario.fixture.periodEnd}</p>
                <p>{scenario.fixture.companyType} / {scenario.fixture.expectedSizeClass} / {scenario.fixture.expectedRegime}</p>
                <p>{scenario.fixture.auditExempt ? "Audit exempt" : "Audit required"}; {scenario.fixture.manualProfessionalReviewRequired ? "manual review required" : "standard review gate"}</p>
              </div>,
              <span key="scope" className="whitespace-normal text-[var(--muted-foreground)]">{scenario.companyScope}</span>,
              <span key="outcome" className="text-[var(--muted-foreground)]">{formatStatus(scenario.expectedOutcome)}</span>,
              <CodeStack key="tests" items={scenario.evidenceTestNames} />,
              <VerifierScopeList key="verifiers" verifiers={scenario.evidenceVerifiers} />,
              <AssertionList key="assertions" items={scenario.assertions} />,
              <StatusBadge key="status" tone={scenario.coverageStatus === "covered" ? "good" : "warn"}>
                {formatStatus(scenario.coverageStatus)}
              </StatusBadge>,
            ],
          }))}
        />

        <div className="mt-5 space-y-3">
          <div>
            <h3 className="text-sm font-semibold text-[var(--foreground)]">Golden evidence pack</h3>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
              The concrete generated artifacts, decision gates, expected values and statutory sources each scenario must prove.
            </p>
          </div>
          <DataGrid
            caption="Golden evidence pack"
            filterPlaceholder="Filter evidence packs"
            emptyState="No matching evidence packs"
            columns={["Scenario", "Output artifacts", "Decision gates", "Expected value checks", "Expected outputs", "Expected proof points", "Sources"]}
            rows={report.goldenFilingCorpus.map((scenario) => ({
              id: `${scenario.code}-evidence`,
              tone: scenario.coverageStatus === "covered" ? "good" : "warn",
              searchText: [
                scenario.label,
                ...scenario.evidencePack.outputArtifacts,
                ...scenario.evidencePack.decisionGates,
                ...scenario.evidencePack.expectedValueChecks,
                ...scenario.evidencePack.expectedOutputs.pdfTextMarkers,
                ...scenario.evidencePack.expectedOutputs.ixbrlRequiredTags,
                scenario.evidencePack.expectedOutputs.filingReadinessState,
                scenario.evidencePack.expectedOutputs.expectedCorporationTax.toString(),
                ...scenario.evidencePack.expectedOutputs.requiredNotes,
                ...scenario.evidencePack.expectedOutputs.filingGateStates,
                scenario.evidencePack.expectedOutputs.signOffPacketState,
                ...scenario.evidencePack.expectedProofPoints.flatMap((proof) => [
                  proof.area,
                  proof.expectedEvidence,
                  proof.automatedVerifier,
                ]),
                ...scenario.evidencePack.sourceReferences.map((source) => source.title),
              ].join(" "),
              cells: [
                <span key="scenario" className="font-medium">{scenario.label}</span>,
                <CompactList key="artifacts" items={scenario.evidencePack.outputArtifacts} />,
                <CompactList key="gates" items={scenario.evidencePack.decisionGates} />,
                <CompactList key="values" items={scenario.evidencePack.expectedValueChecks} />,
                <ExpectedOutputsList key="expected-outputs" outputs={scenario.evidencePack.expectedOutputs} />,
                <ProofPointList key="proof-points" proofPoints={scenario.evidencePack.expectedProofPoints} />,
                <SourceLinkList key="sources" sources={scenario.evidencePack.sourceReferences} />,
              ],
            }))}
          />
        </div>

        <div className="mt-5 space-y-3">
          <div>
            <h3 className="text-sm font-semibold text-[var(--foreground)]">Golden evidence ledger</h3>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
              Accountant-facing ledger tying each sample company to its verifier, expected outputs, source ids, readiness state and release gate.
            </p>
          </div>
          <DataGrid
            caption="Golden evidence ledger"
            filterPlaceholder="Filter golden evidence ledger"
            emptyState="No matching ledger entries"
            columns={["Scenario", "Fixture", "Verifier", "Artifacts", "Expected state", "Sources", "Release gate"]}
            rows={report.goldenEvidenceLedger.map((entry) => ({
              id: `${entry.scenarioCode}-ledger`,
              tone: entry.blocksRelease ? "warn" : "good",
              searchText: [
                entry.scenarioCode,
                entry.label,
                entry.fixtureLegalName,
                entry.companyType,
                entry.expectedOutcome,
                entry.coverageStatus,
                entry.acceptanceStatus,
                entry.requiredSignOffGate,
                entry.filingReadinessState,
                entry.signOffPacketState,
                entry.expectedCorporationTax.toString(),
                ...entry.automatedVerifierNames,
                ...entry.outputArtifacts,
                ...entry.decisionGates,
                ...entry.expectedValueChecks,
                ...entry.proofPointAreas,
                ...entry.sourceIds,
              ].join(" "),
              sortValues: [
                entry.label,
                entry.fixtureLegalName,
                entry.automatedVerifierNames.join(" "),
                entry.outputArtifacts.length,
                entry.filingReadinessState,
                entry.sourceIds.join(" "),
                entry.acceptanceStatus,
              ],
              cells: [
                <div key="scenario" className="min-w-40">
                  <p className="font-medium text-[var(--foreground)]">{entry.label}</p>
                  <code className="mt-1 block break-all text-[11px] text-[var(--muted-foreground)]">{entry.scenarioCode}</code>
                </div>,
                <div key="fixture" className="min-w-48 text-xs leading-5 text-[var(--muted-foreground)]">
                  <p className="font-semibold text-[var(--foreground)]">{entry.fixtureLegalName}</p>
                  <p>{entry.companyType}</p>
                  <p>{formatStatus(entry.expectedOutcome)} / {formatStatus(entry.coverageStatus)}</p>
                </div>,
                <CodeStack key="verifier" items={entry.automatedVerifierNames} />,
                <CompactList key="artifacts" items={entry.outputArtifacts} />,
                <div key="state" className="min-w-48 space-y-1 text-xs leading-5 text-[var(--muted-foreground)]">
                  <p className="font-medium text-[var(--foreground)]">Expected CT: {formatCurrency(entry.expectedCorporationTax)}</p>
                  <p>{entry.filingReadinessState}</p>
                  <p>{entry.signOffPacketState}</p>
                  <p>{entry.expectedValueChecks.join(", ")}</p>
                </div>,
                <CodeStack key="sources" items={entry.sourceIds} />,
                <div key="gate" className="min-w-48 space-y-2">
                  <StatusBadge tone={entry.blocksRelease ? "warn" : "good"}>
                    {formatStatus(entry.acceptanceStatus)}
                  </StatusBadge>
                  <p className="text-xs leading-5 text-[var(--muted-foreground)]">{entry.requiredSignOffGate}</p>
                </div>,
              ],
            }))}
          />
        </div>
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
          actions={<StatusBadge tone="info">{formatPinnedSources(report.sourceLawSnapshot.sourceCount)}</StatusBadge>}
        >
          <div className="mb-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Snapshot fingerprint</p>
            <code className="mt-1 block break-all text-xs leading-5 text-[var(--foreground)]">
              {report.sourceLawSnapshot.contentHash}
            </code>
          </div>
          <DataGrid
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
          <div className="mt-4">
          <DataGrid
            columns={["Traceability", "Used by", "Release gates", "Pinned"]}
            rows={sourceLawTraceability.map((entry) => [
              <span key="title" className="font-medium">{entry.title}</span>,
              <span key="used" className="text-[var(--muted-foreground)]">
                {entry.usedBy.slice(0, 3).join(", ")}
                {entry.usedBy.length > 3 ? ` +${entry.usedBy.length - 3} more` : ""}
              </span>,
              <CodeStack key="release-gates" items={entry.releaseGateCodes} />,
              <StatusBadge key="pinned" tone={entry.inSnapshot ? "good" : "bad"}>
                {entry.inSnapshot ? "Snapshot" : "Missing"}
              </StatusBadge>,
            ])}
            />
          </div>
        </ReviewPanel>
      </div>
    </PageShell>
  );
}

function DecisionSummaryItem({
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

function VerifierScopeList({
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

function ExpectedOutputsList({
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

function ExpectedOutputGroup({ label, items }: { label: string; items: string[] }) {
  return (
    <div>
      <p className="text-[11px] font-semibold uppercase text-[var(--foreground)]">{label}</p>
      <CompactList items={items} />
    </div>
  );
}

function ProofPointList({
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

function PacketMetric({
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

function toneIconClass(tone: "good" | "warn" | "bad" | "info" | "default") {
  if (tone === "good") return "mt-0.5 shrink-0 text-emerald-600 dark:text-emerald-300";
  if (tone === "bad") return "mt-0.5 shrink-0 text-red-600 dark:text-red-300";
  if (tone === "warn") return "mt-0.5 shrink-0 text-amber-600 dark:text-amber-300";
  if (tone === "info") return "mt-0.5 shrink-0 text-sky-600 dark:text-sky-300";
  return "mt-0.5 shrink-0 text-[var(--muted-foreground)]";
}

function priorityTone(priority: string): "good" | "warn" | "bad" | "info" | "default" {
  if (priority === "critical") return "bad";
  if (priority === "high") return "warn";
  return "default";
}

function riskTone(riskRank: number): "good" | "warn" | "bad" | "info" | "default" {
  if (riskRank <= 5) return "bad";
  if (riskRank <= 20) return "warn";
  if (riskRank <= 40) return "info";
  return "default";
}

function supportTone(supportLevel: string): "good" | "warn" | "bad" | "info" | "default" {
  if (supportLevel === "supported") return "good";
  if (supportLevel === "supported-with-review") return "info";
  if (supportLevel === "manual-handoff") return "warn";
  if (supportLevel === "unsupported") return "bad";
  return "default";
}

function ciScopeTone(scope: string): "good" | "warn" | "bad" | "info" | "default" {
  if (scope === "default-ci") return "good";
  if (scope === "environment-gated") return "warn";
  if (scope === "manual-release") return "bad";
  return "default";
}

function formatStatus(value: string) {
  const words = value
    .split("-")
    .filter(Boolean)
    .join(" ");

  return words.charAt(0).toUpperCase() + words.slice(1);
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(value);
}

function formatPacketStatus(value: string) {
  return value === "ready" ? "Packet ready" : `Packet ${formatStatus(value).toLowerCase()}`;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("en-IE", { day: "2-digit", month: "short", year: "numeric" }).format(date);
}

function formatPinnedSources(count: number) {
  return `${count} pinned source${count === 1 ? "" : "s"}`;
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
