import { Card, ProgressBar, ProgressBarFill, ProgressBarTrack } from "@heroui/react";
import { AlertTriangle, CheckCircle2, FileText } from "lucide-react";
import type { ReadinessScore } from "@/lib/api";

export function StatementsReadinessPanel({ readiness }: { readiness: ReadinessScore | null }) {
  return (
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
      <Card.Header>
        <Card.Title className="text-gray-900 dark:text-gray-100">Filing Readiness</Card.Title>
        <Card.Description>Assessment of whether the accounts are ready for filing</Card.Description>
      </Card.Header>
      <Card.Content>
        {readiness ? (
          <div className="space-y-6">
            <div className="grid grid-cols-1 gap-6 sm:grid-cols-2">
              <ReadinessProgress
                label="Completeness"
                value={readiness.completenessPercent}
                ariaLabel="Completeness"
              />
              <ReadinessProgress
                label="Filing Readiness"
                value={readiness.filingReadinessPercent}
                ariaLabel="Filing readiness"
              />
            </div>

            <BalanceStatus balances={readiness.balanceSheetBalances} />

            {readiness.missingItems.length > 0 && (
              <ReadinessIssueList
                title="Missing Items"
                issues={readiness.missingItems}
                tone="bad"
              />
            )}

            {readiness.warnings.length > 0 && (
              <ReadinessIssueList
                title="Warnings"
                issues={readiness.warnings}
                tone="warn"
              />
            )}
          </div>
        ) : (
          <StatementsReadinessEmptyState />
        )}
      </Card.Content>
    </Card>
  );
}

function ReadinessProgress({
  label,
  value,
  ariaLabel,
}: {
  label: string;
  value: number;
  ariaLabel: string;
}) {
  return (
    <div>
      <div className="flex items-center justify-between gap-3 text-sm mb-2">
        <span className="text-gray-600 dark:text-gray-400">{label}</span>
        <span className="font-medium text-gray-900 dark:text-gray-100">{value}%</span>
      </div>
      <ProgressBar value={value} minValue={0} maxValue={100} aria-label={ariaLabel} color={value >= 80 ? "success" : "warning"}>
        <ProgressBarTrack>
          <ProgressBarFill />
        </ProgressBarTrack>
      </ProgressBar>
    </div>
  );
}

function BalanceStatus({ balances }: { balances: boolean }) {
  return (
    <div className="flex items-center gap-2 text-sm">
      {balances ? (
        <>
          <CheckCircle2 className="w-5 h-5 text-emerald-600" />
          <span className="text-emerald-700 dark:text-emerald-400 font-medium">Balance sheet balances</span>
        </>
      ) : (
        <>
          <AlertTriangle className="w-5 h-5 text-amber-500" />
          <span className="text-amber-700 dark:text-amber-400 font-medium">Balance sheet does not balance</span>
        </>
      )}
    </div>
  );
}

function ReadinessIssueList({
  title,
  issues,
  tone,
}: {
  title: string;
  issues: string[];
  tone: "bad" | "warn";
}) {
  const textClass = tone === "bad"
    ? "text-red-700 dark:text-red-400"
    : "text-amber-700 dark:text-amber-400";

  return (
    <div>
      <h4 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">{title}</h4>
      <ul className="space-y-1.5">
        {issues.map((issue, index) => (
          <li key={`${issue}-${index}`} className={`flex items-start gap-2 text-sm ${textClass}`}>
            {tone === "bad" ? (
              <span className="w-1.5 h-1.5 rounded-full bg-red-400 mt-1.5 shrink-0" />
            ) : (
              <AlertTriangle className="w-4 h-4 text-amber-500 mt-0.5 shrink-0" />
            )}
            {issue}
          </li>
        ))}
      </ul>
    </div>
  );
}

function StatementsReadinessEmptyState() {
  return (
    <div className="text-center py-8">
      <FileText className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
      <p className="text-sm text-[var(--muted-foreground)]">Readiness data is not available yet.</p>
      <p className="text-xs text-[var(--muted-foreground)] mt-1">Complete the year-end process and generate adjustments first.</p>
    </div>
  );
}
