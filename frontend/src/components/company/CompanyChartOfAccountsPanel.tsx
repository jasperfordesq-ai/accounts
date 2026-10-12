"use client";

import { useState, type ReactNode } from "react";
import { Button, Spinner } from "@heroui/react";
import { toast } from "sonner";
import type { Company } from "@/lib/api";
import { adoptCharityChart, chartAdoptionRefusal, getChartAdoptionPlan, type ChartAdoptionPlan } from "@/lib/chartAdoption";
import { isRecentMfaRequired } from "@/lib/filingEvidence";
import { formatDateIE } from "@/lib/format";
import { ReviewPanel, StatusBadge } from "@/components/workbench";
import { RecentVerificationPrompt, errorMessage } from "@/components/period/FilingEvidenceShared";

const VARIANT_LABELS = {
  GenericTrading: "Standard trading company",
  CharityClg: "Charity / company limited by guarantee",
} as const;

export const ARCHIVED_RULES_NOTE = "Imports skip transaction rules that point at archived accounts; point them at a new account if you still need them.";

export const TRANSACTIONS_NOT_MOVED = "Your transactions are not moved. Re-categorise them to the new accounts yourself.";
export const ADOPTION_IS_ONE_WAY =
  "Adopting the charity / CLG chart is one-way: there is no automatic way to change back to the standard trading chart.";

/**
 * The company's chart of accounts. An Owner can move a company on the standard trading chart to the
 * charity / CLG chart: the plan is reviewed first, and only exactly that plan is executed.
 */
export function CompanyChartOfAccountsPanel({
  company,
  canAdopt,
  onAdopted,
}: {
  company: Company;
  /** Owner only: the backend refuses every other role. */
  canAdopt: boolean;
  onAdopted?: () => void | Promise<void>;
}) {
  const [plan, setPlan] = useState<ChartAdoptionPlan | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsVerification, setNeedsVerification] = useState(false);

  const variant = company.chartOfAccountsVariant ?? "GenericTrading";
  // Only a company limited by guarantee may adopt the charity / CLG chart (the server refuses others too).
  const canStart = canAdopt && variant === "GenericTrading" && company.companyType === "CompanyLimitedByGuarantee";

  async function review() {
    setBusy(true);
    setError(null);
    try {
      setPlan(await getChartAdoptionPlan(company.id));
      setAcknowledged(false);
    } catch (failure) {
      setError(errorMessage(failure, "The chart of accounts plan could not be prepared"));
    } finally {
      setBusy(false);
    }
  }

  async function adopt() {
    if (!plan) return;
    setBusy(true);
    setError(null);
    try {
      await adoptCharityChart(company.id, plan, acknowledged);
      setNeedsVerification(false);
      setPlan(null);
      toast.success("Charity / CLG chart of accounts adopted");
      await onAdopted?.();
    } catch (failure) {
      const refusal = chartAdoptionRefusal(failure);
      if (isRecentMfaRequired(failure)) {
        setNeedsVerification(true);
      } else if (refusal) {
        setPlan(refusal.plan);
        setAcknowledged(false);
        setError(refusal.error);
      } else {
        setError(errorMessage(failure, "The chart of accounts could not be adopted"));
      }
    } finally {
      setBusy(false);
    }
  }

  const blocked = (plan?.blockedBy.length ?? 0) > 0;
  const needsAcknowledgement = (plan?.staleFilingArtifacts.length ?? 0) > 0;

  return (
    <ReviewPanel
      title="Chart of accounts"
      description="The list of accounts transactions are categorised to and the statements are built from."
      actions={<StatusBadge tone="info">{VARIANT_LABELS[variant]}</StatusBadge>}
    >
      <div className="space-y-3 text-sm text-[var(--foreground)]">
        {canStart && !plan && (
          <div className="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
            <p className="text-[var(--muted-foreground)]">
              A company limited by guarantee can move to the charity / CLG chart. You review the plan before anything changes.
            </p>
            <Button size="sm" variant="outline" aria-label="Adopt the charity / CLG chart" isDisabled={busy} onPress={() => void review()}>
              {busy ? <Spinner size="sm" /> : "Adopt the charity / CLG chart"}
            </Button>
          </div>
        )}

        {plan && (
          <div className="space-y-4 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
            <p className="font-semibold">{TRANSACTIONS_NOT_MOVED}</p>

            {blocked && (
              <div role="alert" className="rounded-md border border-red-200 bg-red-50 p-3 text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
                <p className="font-medium">The chart cannot be adopted yet:</p>
                <ul className="mt-1 list-disc pl-5">
                  {plan.blockedBy.map((reason) => <li key={reason}>{reason}</li>)}
                </ul>
              </div>
            )}

            <PlanSection title="Renamed in place (same account)">
              {plan.renames.map((row) => (
                <li key={row.categoryId}>{row.fromCode} {row.fromName} → {row.toCode} {row.toName}</li>
              ))}
            </PlanSection>

            <PlanSection title="Kept as legacy accounts (still selectable; re-categorise from them)">
              {plan.legacyRecodes.map((row) => (
                <li key={row.categoryId}>
                  {row.fromCode} → {row.toCode} {row.toName}: {countLabel(row.transactionCount, "transaction")}
                  {row.journalCount > 0 && `, ${countLabel(row.journalCount, "journal")}`}
                  {row.openingBalanceCount > 0 && `, ${countLabel(row.openingBalanceCount, "opening balance")}`}
                  <RuleCount count={row.ruleCount} />
                </li>
              ))}
            </PlanSection>

            <PlanSection title="Kept unchanged">
              {plan.retained.map((row) => (
                <li key={row.categoryId}>{row.code} {row.name}: {countLabel(row.transactionCount, "transaction")}</li>
              ))}
            </PlanSection>

            <PlanSection title={`Archived: no entries, hidden from pickers (${plan.archives.length})`} collapsed>
              {plan.archives.map((row) => (
                <li key={row.categoryId}>
                  {row.fromCode === row.toCode ? row.fromCode : `${row.fromCode} → ${row.toCode}`} {row.name}
                  <RuleCount count={row.ruleCount} />
                </li>
              ))}
            </PlanSection>

            <PlanSection title={`New charity / CLG accounts (${plan.inserts.length})`} collapsed>
              {plan.inserts.map((row) => <li key={row.code}>{row.code} {row.name}</li>)}
            </PlanSection>

            {plan.archives.some((row) => row.ruleCount > 0) && (
              <p className="text-[var(--muted-foreground)]">{ARCHIVED_RULES_NOTE}</p>
            )}

            {needsAcknowledgement && (
              <div className="space-y-2 rounded-md border border-amber-200 bg-amber-50 p-3 text-amber-950 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-100">
                <p className="font-medium">These periods&apos; CRO filing pack and directors&apos; approval (s.324) become stale and must be regenerated and approved again:</p>
                <ul className="list-disc pl-5">
                  {plan.staleFilingArtifacts.map((item) => (
                    <li key={item.periodId}>{formatDateIE(item.periodStart)} – {formatDateIE(item.periodEnd)} ({item.status})</li>
                  ))}
                </ul>
                <label className="flex items-start gap-2">
                  <input
                    type="checkbox"
                    className="workbench-checkbox mt-0.5"
                    checked={acknowledged}
                    onChange={(event) => setAcknowledged(event.target.checked)}
                  />
                  <span>I understand these filing artefacts become stale.</span>
                </label>
              </div>
            )}

            <p className="font-medium">{ADOPTION_IS_ONE_WAY}</p>

            <div className="flex justify-end gap-2">
              <Button size="sm" variant="outline" isDisabled={busy} onPress={() => { setPlan(null); setError(null); setNeedsVerification(false); }}>
                Cancel
              </Button>
              <Button
                size="sm"
                variant="primary"
                aria-label="Adopt chart of accounts"
                isDisabled={busy || blocked || (needsAcknowledgement && !acknowledged)}
                onPress={() => void adopt()}
              >
                {busy ? <Spinner size="sm" /> : "Adopt chart"}
              </Button>
            </div>
          </div>
        )}

        {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
        {error && (
          <p role="alert" className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
            {error}
          </p>
        )}
      </div>
    </ReviewPanel>
  );
}

function PlanSection({ title, collapsed = false, children }: { title: string; collapsed?: boolean; children: ReactNode[] }) {
  if (children.length === 0) return null;
  const list = <ul className="mt-1 list-disc pl-5">{children}</ul>;
  return collapsed ? (
    <details>
      <summary className="cursor-pointer font-medium">{title}</summary>
      {list}
    </details>
  ) : (
    <div>
      <p className="font-medium">{title}</p>
      {list}
    </div>
  );
}

function RuleCount({ count }: { count: number }) {
  if (count === 0) return null;
  return <> ({count === 1 ? "1 transaction rule points here" : `${count} transaction rules point here`})</>;
}

function countLabel(count: number, noun: string) {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}
