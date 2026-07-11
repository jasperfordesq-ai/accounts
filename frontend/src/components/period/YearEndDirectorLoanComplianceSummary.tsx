"use client";

import { Chip } from "@heroui/react";
import type { DirectorLoanCompliance } from "@/lib/api";

interface YearEndDirectorLoanComplianceSummaryProps {
  compliance: DirectorLoanCompliance | null;
}

export function YearEndDirectorLoanComplianceSummary({ compliance }: YearEndDirectorLoanComplianceSummaryProps) {
  if (!compliance || compliance.loans.length === 0) return null;

  const ready = compliance.signOffPacket.readyForFinalOutput;
  return (
    <section className="space-y-4" aria-labelledby="director-loan-compliance-heading">
      <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-start">
        <div>
          <h3 id="director-loan-compliance-heading" className="text-sm font-semibold text-gray-900 dark:text-gray-100">
            Director-loan statutory evidence
          </h3>
          <p className="mt-1 text-xs text-[var(--muted-foreground)]">
            Sections 236, 239–245, 307 and 308 evidence. Arrangement review does not replace release-level qualified-accountant approval.
          </p>
        </div>
        <Chip variant="soft" size="sm" color={ready ? "success" : "danger"}>
          {ready ? "Arrangement evidence accepted" : "Final output blocked"}
        </Chip>
      </div>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <MetricTile label="Closing exposure" value={formatCurrency(compliance.totalDirectorLoans)} />
        <MetricTile label="Maximum aggregate exposure" value={formatCurrency(compliance.aggregateMaximumExposure)} />
        <MetricTile label="Section 236 presumed interest" value={formatCurrency(compliance.section236PresumedInterest)} />
        <MetricTile label="Open blockers" value={compliance.blockingIssues.length.toString()} />
      </div>

      {compliance.blockingIssues.length > 0 && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 dark:border-red-800 dark:bg-red-950/30" role="alert">
          <p className="text-sm font-semibold text-red-900 dark:text-red-200">Resolve before generating final statutory output</p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-red-800 dark:text-red-300">
            {compliance.blockingIssues.map((issue) => <li key={issue}>{issue}</li>)}
          </ul>
        </div>
      )}

      {compliance.warnings.length > 0 && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 dark:border-amber-700 dark:bg-amber-950/20">
          <p className="text-sm font-semibold text-amber-900 dark:text-amber-200">Professional-review warnings</p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-amber-800 dark:text-amber-300">
            {compliance.warnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        </div>
      )}

      <div className="space-y-2" aria-label="Arrangement compliance decisions">
        {compliance.loans.map((loan) => (
          <article key={loan.id} className="rounded-lg border border-gray-200 p-3 dark:border-neutral-700 dark:bg-neutral-800/40">
            <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-start">
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{loan.counterpartyName}</p>
                <p className="text-xs text-[var(--muted-foreground)]">
                  {humanise(loan.arrangementType)} · {humanise(loan.complianceBasis)} · maximum {formatCurrency(loan.maxDuringYear)}
                </p>
              </div>
              <Chip variant="soft" size="sm" color={loan.readyForFinalOutput ? "success" : "danger"}>
                {loan.readyForFinalOutput ? "Accepted" : `${loan.blockingIssues.length} blocker${loan.blockingIssues.length === 1 ? "" : "s"}`}
              </Chip>
            </div>
            {loan.complianceBasis === "Section240BelowTenPercent" && (
              <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-3">
                <Value label="Relevant assets" value={loan.relevantAssets === undefined ? "Not retained" : formatCurrency(loan.relevantAssets)} />
                <Value label="Strict 10% threshold" value={loan.section240Threshold === undefined ? "Not available" : formatCurrency(loan.section240Threshold)} />
                <Value label="Threshold result" value={loan.section240StrictlyBelowThreshold ? "Strictly below" : "Not strictly below"} />
              </dl>
            )}
            <p className="mt-2 text-xs text-gray-600 dark:text-gray-300">
              Review: {humanise(loan.reviewDecision)}{loan.reviewedBy ? ` by ${loan.reviewedBy}` : ""}
              {loan.reviewedAtUtc ? ` on ${new Date(loan.reviewedAtUtc).toLocaleString("en-IE")}` : ""}.
            </p>
          </article>
        ))}
      </div>

      <details className="rounded-lg border border-gray-200 p-3 text-xs dark:border-neutral-700">
        <summary className="cursor-pointer font-medium text-gray-800 dark:text-gray-200">Current statutory source links</summary>
        <ul className="mt-2 grid gap-1 sm:grid-cols-2">
          {compliance.legalSources.map((source) => (
            <li key={source.code}>
              <a href={source.url} target="_blank" rel="noreferrer" className="text-emerald-700 underline underline-offset-2 dark:text-emerald-300">
                {source.title} (opens in a new tab)
              </a>
            </li>
          ))}
        </ul>
      </details>
    </section>
  );
}

function MetricTile({ label, value }: { label: string; value: string }) {
  return <div className="rounded-lg border border-gray-200 p-3 dark:border-neutral-700 dark:bg-neutral-800/50"><p className="text-xs text-[var(--muted-foreground)]">{label}</p><p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{value}</p></div>;
}

function Value({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-[var(--muted-foreground)]">{label}</dt><dd className="font-medium text-gray-900 dark:text-gray-100">{value}</dd></div>;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

function humanise(value: string): string {
  return value.replace(/([a-z])([A-Z0-9])/g, "$1 $2").replace(/Section(\d+)/, "Section $1");
}
