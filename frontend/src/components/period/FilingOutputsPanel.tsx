import { Button, Spinner } from "@heroui/react";
import { CheckCircle2, Circle, Download, FileText } from "lucide-react";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

export interface FilingOutputChecklist {
  transactionsCategorised: boolean;
  adjustmentsReviewed: boolean;
  balanceSheetBalances: boolean;
  filingReadinessComplete: boolean;
  accountsPdfGenerated: boolean;
  croPackAndSignatureGenerated: boolean;
}

interface FilingOutputsPanelProps {
  canRead?: boolean;
  canGenerate?: boolean;
  filingRegimeReady: boolean;
  downloadingDocument: string | null;
  checklist: FilingOutputChecklist;
  onDownloadAgmPack: () => void | Promise<void>;
  onDownloadCroFilingPack: () => void | Promise<void>;
  onDownloadSignaturePage: () => void | Promise<void>;
  onDownloadIxbrl: () => void | Promise<void>;
}

const checklistLabels: { key: keyof FilingOutputChecklist; label: string }[] = [
  { key: "transactionsCategorised", label: "All transactions imported and categorised" },
  { key: "adjustmentsReviewed", label: "Year-end adjustments generated and reviewed" },
  { key: "balanceSheetBalances", label: "Balance sheet balances" },
  { key: "filingReadinessComplete", label: "Filing readiness at 100%" },
  { key: "accountsPdfGenerated", label: "CRO accounts PDF generated" },
  { key: "croPackAndSignatureGenerated", label: "CRO filing pack and signature page generated" },
];

export function FilingOutputsPanel({
  canRead = false,
  canGenerate = false,
  filingRegimeReady,
  downloadingDocument,
  checklist,
  onDownloadAgmPack,
  onDownloadCroFilingPack,
  onDownloadSignaturePage,
  onDownloadIxbrl,
}: FilingOutputsPanelProps) {
  const completeCount = checklistLabels.filter((item) => checklist[item.key]).length;
  const generatedEvidenceCount = [
    checklist.accountsPdfGenerated,
    checklist.croPackAndSignatureGenerated,
  ].filter(Boolean).length;
  const openChecklistCount = checklistLabels.length - completeCount;
  const anyDownloadInProgress = downloadingDocument !== null;
  const outputStates = [
    { title: "AGM Pack", isBlocked: !canRead || anyDownloadInProgress },
    { title: "CRO Filing Pack", isBlocked: !canGenerate || anyDownloadInProgress || !filingRegimeReady },
    { title: "Signature Page", isBlocked: !canGenerate || anyDownloadInProgress || !filingRegimeReady },
    { title: "iXBRL review prototype", isBlocked: !canRead || anyDownloadInProgress },
  ];
  const availableOutputs = outputStates.filter((output) => !output.isBlocked).map((output) => output.title);
  const blockedOutputs = outputStates.filter((output) => output.isBlocked).map((output) => output.title);
  const nextOutputGate = !canRead
    ? "Read access is required to inspect filing outputs"
    : anyDownloadInProgress
      ? "Wait for the current output to finish preparing"
      : !canGenerate
        ? "Owner or Accountant access is required to generate the CRO filing pack and signature page"
        : !filingRegimeReady
          ? "Complete filing regime before CRO downloads"
          : completeCount === checklistLabels.length
            ? "All filing outputs are available for review"
            : "Complete open filing checklist items before final use";

  return (
    <ReviewPanel
      title="Filing outputs"
      description="Generated accounts packs, statutory filing files and final pre-filing evidence."
      actions={
        <StatusBadge tone={completeCount === checklistLabels.length ? "good" : "warn"}>
          {completeCount} of {checklistLabels.length} checklist items complete
        </StatusBadge>
      }
    >
      <div className="space-y-4">
        <OutputReadinessSummary
          availableOutputs={availableOutputs}
          blockedOutputs={blockedOutputs}
          nextOutputGate={nextOutputGate}
        />

        <OutputEvidenceDocket
          generatedEvidenceCount={generatedEvidenceCount}
          generatedEvidenceTotal={2}
          openChecklistCount={openChecklistCount}
          nextOutputGate={nextOutputGate}
        />

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <OutputDownloadTile
              canDownload={canRead}
              title="AGM Pack"
              description="Full statutory accounts for AGM approval"
              buttonLabel="Download AGM PDF"
              isLoading={downloadingDocument === "AGM pack"}
              isDisabled={anyDownloadInProgress}
              onDownload={onDownloadAgmPack}
            />
            <OutputDownloadTile
              canDownload={canGenerate}
              title="CRO Filing Pack"
              description="Abridged accounts for CRO filing"
              buttonLabel="Download CRO PDF"
              isLoading={downloadingDocument === "CRO filing pack"}
              isDisabled={anyDownloadInProgress || !filingRegimeReady}
              gateLabel={filingRegimeReady ? undefined : "Filing regime required"}
              onDownload={onDownloadCroFilingPack}
            />
            <OutputDownloadTile
              canDownload={canGenerate}
              title="Signature Page"
              description="Typeset signatures for CRO (s.347)"
              buttonLabel="Download Signature PDF"
              isLoading={downloadingDocument === "signature page"}
              isDisabled={anyDownloadInProgress || !filingRegimeReady}
              gateLabel={filingRegimeReady ? undefined : "Filing regime required"}
              onDownload={onDownloadSignaturePage}
            />
            <OutputDownloadTile
              canDownload={canRead}
              title="iXBRL review prototype"
              description="Incomplete draft for accountant review only; filing-ready generation is disabled and requires manual handoff"
              buttonLabel="Download draft review XHTML"
              isLoading={downloadingDocument === "draft iXBRL review prototype"}
              isDisabled={anyDownloadInProgress}
              onDownload={onDownloadIxbrl}
            />
          </div>

          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
            <h3 className="text-sm font-semibold text-[var(--foreground)]">Filing checklist</h3>
            <ul className="mt-3 space-y-2">
              {checklistLabels.map((item) => (
                <ChecklistRow key={item.key} label={item.label} done={checklist[item.key]} />
              ))}
            </ul>
          </div>
        </div>
      </div>
    </ReviewPanel>
  );
}

function OutputEvidenceDocket({
  generatedEvidenceCount,
  generatedEvidenceTotal,
  openChecklistCount,
  nextOutputGate,
}: {
  generatedEvidenceCount: number;
  generatedEvidenceTotal: number;
  openChecklistCount: number;
  nextOutputGate: string;
}) {
  return (
    <section
      aria-label="Output evidence docket"
      className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-4 md:grid-cols-3"
    >
      <OutputDocketItem
        label="Generated filing evidence"
        value={`${generatedEvidenceCount} of ${generatedEvidenceTotal} generated`}
        detail="CRO accounts PDF plus filing pack/signature evidence."
        tone={generatedEvidenceCount === generatedEvidenceTotal ? "good" : "warn"}
      />
      <OutputDocketItem
        label="Open checklist evidence"
        value={`${openChecklistCount} open`}
        detail={openChecklistCount === 0 ? "Every filing checklist item is complete." : "Open evidence remains before final use."}
        tone={openChecklistCount === 0 ? "good" : "warn"}
      />
      <OutputDocketItem
        label="Final-use gate"
        value={nextOutputGate}
        detail="Generated outputs stay draft evidence until this gate is clear."
        tone={openChecklistCount === 0 ? "good" : "info"}
      />
    </section>
  );
}

function OutputDocketItem({
  label,
  value,
  detail,
  tone,
}: {
  label: string;
  value: string;
  detail: string;
  tone: "good" | "warn" | "info";
}) {
  return (
    <div className="min-w-0">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
        <StatusBadge tone={tone}>{tone === "good" ? "Clear" : tone === "warn" ? "Open" : "Gate"}</StatusBadge>
      </div>
      <p className="mt-2 text-sm font-semibold leading-5 text-[var(--foreground)]">{value}</p>
      <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{detail}</p>
    </div>
  );
}

function OutputReadinessSummary({
  availableOutputs,
  blockedOutputs,
  nextOutputGate,
}: {
  availableOutputs: string[];
  blockedOutputs: string[];
  nextOutputGate: string;
}) {
  return (
    <section
      aria-label="Output readiness"
      className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 md:grid-cols-3"
    >
      <OutputReadinessItem
        label="What can I download now?"
        badge={formatOutputCount(availableOutputs.length, "available")}
        badgeTone="good"
        value={formatOutputList(availableOutputs, "No outputs available")}
      />
      <OutputReadinessItem
        label="What is blocked?"
        badge={formatOutputCount(blockedOutputs.length, "blocked")}
        badgeTone={blockedOutputs.length > 0 ? "warn" : "good"}
        value={formatOutputList(blockedOutputs, "Nothing blocked")}
      />
      <OutputReadinessItem label="What must I do next?" badge="Next" badgeTone="info" value={nextOutputGate} />
    </section>
  );
}

function OutputReadinessItem({
  label,
  badge,
  badgeTone,
  value,
}: {
  label: string;
  badge: string;
  badgeTone: "good" | "warn" | "info";
  value: string;
}) {
  return (
    <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</h3>
        <StatusBadge tone={badgeTone}>{badge}</StatusBadge>
      </div>
      <p className="mt-2 text-sm font-medium leading-5 text-[var(--foreground)]">{value}</p>
    </div>
  );
}

function formatOutputList(outputs: string[], emptyLabel: string) {
  return outputs.length > 0 ? outputs.join(", ") : emptyLabel;
}

function formatOutputCount(count: number, label: string) {
  return `${count} ${label}`;
}

function OutputDownloadTile({
  canDownload,
  title,
  description,
  buttonLabel,
  isLoading,
  isDisabled,
  gateLabel,
  onDownload,
}: {
  canDownload: boolean;
  title: string;
  description: string;
  buttonLabel: string;
  isLoading: boolean;
  isDisabled: boolean;
  gateLabel?: string;
  onDownload: () => void | Promise<void>;
}) {
  return (
    <div className="flex min-w-0 flex-col gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
      <div className="flex items-start gap-3">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md border border-[var(--border)] bg-[var(--surface-subtle)]">
          <FileText className="h-5 w-5 text-[var(--muted-foreground)]" />
        </div>
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-sm font-semibold text-[var(--foreground)]">{title}</h3>
            {gateLabel && <StatusBadge tone="warn">{gateLabel}</StatusBadge>}
          </div>
          <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{description}</p>
        </div>
      </div>
      {canDownload && (
        <Button
          variant="outline"
          size="sm"
          isDisabled={isDisabled}
          className="mt-auto w-fit"
          onPress={() => {
            void onDownload();
          }}
        >
          {isLoading ? <Spinner size="sm" className="mr-2" /> : <Download className="mr-1 h-4 w-4" />}
          {isLoading ? "Preparing..." : buttonLabel}
        </Button>
      )}
    </div>
  );
}

function ChecklistRow({ label, done }: { label: string; done: boolean }) {
  return (
    <li className="flex items-center justify-between gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2">
      <span className="flex min-w-0 items-center gap-2 text-sm text-[var(--foreground)]">
        {done ? (
          <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />
        ) : (
          <Circle className="h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
        )}
        <span className="min-w-0">{label}</span>
      </span>
      <StatusBadge tone={done ? "good" : "warn"}>{done ? "Complete" : "Open"}</StatusBadge>
    </li>
  );
}
