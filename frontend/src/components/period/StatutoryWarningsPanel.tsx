import { AlertTriangle, FileText } from "lucide-react";
import type { AuditExemptionJeopardy } from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

interface StatutoryWarningsPanelProps {
  jeopardy: AuditExemptionJeopardy | null;
  section307Note: string | null;
}

export function StatutoryWarningsPanel({ jeopardy, section307Note }: StatutoryWarningsPanelProps) {
  if (!jeopardy?.warning && !section307Note) return null;

  const warningTone = jeopardy?.hasLostExemption ? "bad" : "warn";

  return (
    <ReviewPanel
      title="Statutory warnings"
      description="Audit-exemption and Companies Act disclosures that need reviewer attention before filing."
      actions={
        <StatusBadge tone={warningTone}>
          {jeopardy?.hasLostExemption ? "Statutory block" : "Review required"}
        </StatusBadge>
      }
    >
      <div className="space-y-3">
        {jeopardy?.warning && (
          <div className={`rounded-md border p-4 ${jeopardy.hasLostExemption ? "border-red-200 bg-red-50 text-red-800 dark:border-red-900 dark:bg-red-950/40 dark:text-red-100" : "border-amber-200 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-100"}`}>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div className="min-w-0">
                <h3 className="flex items-center gap-2 text-sm font-semibold">
                  <AlertTriangle className="h-4 w-4 shrink-0" />
                  {jeopardy.hasLostExemption ? "Audit exemption lost" : "Audit exemption at risk"}
                </h3>
                <p className="mt-1 text-xs opacity-85">{jeopardy.lateFilingCount} late filings</p>
              </div>
              <StatusBadge tone={warningTone}>
                {jeopardy.hasLostExemption ? "Manual review required" : "Monitor"}
              </StatusBadge>
            </div>
            <p className="mt-3 text-sm leading-6">{jeopardy.warning}</p>
          </div>
        )}

        {section307Note && (
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
            <h3 className="flex items-center gap-2 text-sm font-semibold text-[var(--foreground)]">
              <FileText className="h-4 w-4 text-[var(--muted-foreground)]" />
              s.307 Director Loan Disclosure
            </h3>
            <pre className="mt-3 whitespace-pre-wrap font-sans text-sm leading-6 text-[var(--foreground)]">
              {section307Note}
            </pre>
          </div>
        )}
      </div>
    </ReviewPanel>
  );
}
