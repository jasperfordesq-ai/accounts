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
  filingRegimeReady,
  downloadingDocument,
  checklist,
  onDownloadAgmPack,
  onDownloadCroFilingPack,
  onDownloadSignaturePage,
  onDownloadIxbrl,
}: FilingOutputsPanelProps) {
  const completeCount = checklistLabels.filter((item) => checklist[item.key]).length;
  const anyDownloadInProgress = downloadingDocument !== null;

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
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <OutputDownloadTile
            title="AGM Pack"
            description="Full statutory accounts for AGM approval"
            buttonLabel="Download AGM PDF"
            isLoading={downloadingDocument === "AGM pack"}
            isDisabled={anyDownloadInProgress}
            onDownload={onDownloadAgmPack}
          />
          <OutputDownloadTile
            title="CRO Filing Pack"
            description="Abridged accounts for CRO filing"
            buttonLabel="Download CRO PDF"
            isLoading={downloadingDocument === "CRO filing pack"}
            isDisabled={anyDownloadInProgress || !filingRegimeReady}
            gateLabel={filingRegimeReady ? undefined : "Filing regime required"}
            onDownload={onDownloadCroFilingPack}
          />
          <OutputDownloadTile
            title="Signature Page"
            description="Typeset signatures for CRO (s.347)"
            buttonLabel="Download Signature PDF"
            isLoading={downloadingDocument === "signature page"}
            isDisabled={anyDownloadInProgress || !filingRegimeReady}
            gateLabel={filingRegimeReady ? undefined : "Filing regime required"}
            onDownload={onDownloadSignaturePage}
          />
          <OutputDownloadTile
            title="iXBRL Filing"
            description="For Revenue Online Service (ROS) submission"
            buttonLabel="Download iXBRL"
            isLoading={downloadingDocument === "iXBRL filing"}
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
    </ReviewPanel>
  );
}

function OutputDownloadTile({
  title,
  description,
  buttonLabel,
  isLoading,
  isDisabled,
  gateLabel,
  onDownload,
}: {
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
