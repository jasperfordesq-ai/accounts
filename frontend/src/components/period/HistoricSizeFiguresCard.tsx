"use client";

import { useState } from "react";
import { Button, Card, Chip, Spinner } from "@heroui/react";
import { toast } from "sonner";
import { RecentVerificationPrompt } from "@/components/period/FilingEvidenceShared";
import { isRecentMfaRequired } from "@/lib/filingEvidence";
import {
  HISTORIC_FIGURES_BADGE,
  HISTORIC_FIGURES_PROVENANCE,
  recordHistoricSizeFigures,
  type HistoricSizeFiguresInput,
} from "@/lib/historicSizeFigures";

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

type ThresholdElectionDate = HistoricSizeFiguresInput["thresholdElectionEffectiveFrom"];

// A typed zero is a real answer; an empty box is not a silent zero.
function parseNonNegative(value: string, integer = false): number | undefined {
  if (value.trim() === "") return undefined;
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0 || (integer && !Number.isInteger(parsed))) return undefined;
  return parsed;
}

type AttestedClass = NonNullable<HistoricSizeFiguresInput["attestedEffectiveClass"]>;

/**
 * Keys in a period's figures from the accounts filed for it, with the PDF as evidence. The
 * recent-verification prompt is rendered beside these controls, never inside a form.
 */
export function HistoricSizeFiguresCard({
  companyId,
  periodId,
  initialElection,
  onCancel,
  onRecorded,
}: {
  companyId: number;
  periodId: number;
  /** The dependent period's threshold election, when known. */
  initialElection?: ThresholdElectionDate | null;
  onCancel: () => void;
  onRecorded: () => Promise<void>;
}) {
  const [turnover, setTurnover] = useState("");
  const [balanceSheetTotal, setBalanceSheetTotal] = useState("");
  const [avgEmployees, setAvgEmployees] = useState("");
  const [election, setElection] = useState<ThresholdElectionDate>(initialElection ?? "2024-01-01");
  const [sourceReference, setSourceReference] = useState("");
  const [evidence, setEvidence] = useState<File | null>(null);
  const [attestedClass, setAttestedClass] = useState<AttestedClass | "">("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsVerification, setNeedsVerification] = useState(false);

  const parsedTurnover = parseNonNegative(turnover);
  const parsedBalanceSheetTotal = parseNonNegative(balanceSheetTotal);
  const parsedAvgEmployees = parseNonNegative(avgEmployees, true);
  const trimmedSource = sourceReference.trim();
  const valid = parsedTurnover !== undefined
    && parsedBalanceSheetTotal !== undefined
    && parsedAvgEmployees !== undefined
    && trimmedSource.length > 0
    && trimmedSource.length <= 300
    && evidence !== null;

  async function record() {
    if (!valid || parsedTurnover === undefined || parsedBalanceSheetTotal === undefined || parsedAvgEmployees === undefined || !evidence) return;
    setSaving(true);
    setError(null);
    try {
      await recordHistoricSizeFigures(companyId, periodId, {
        turnover: parsedTurnover,
        balanceSheetTotal: parsedBalanceSheetTotal,
        avgEmployees: parsedAvgEmployees,
        thresholdElectionEffectiveFrom: election,
        sourceReference: trimmedSource,
        evidence,
        attestedEffectiveClass: attestedClass === "" ? null : attestedClass,
      });
      setNeedsVerification(false);
      toast.success("Historic figures recorded from the filed accounts");
      await onRecorded();
    } catch (failure) {
      if (isRecentMfaRequired(failure)) {
        setNeedsVerification(true);
      } else {
        setError(failure instanceof Error ? failure.message : "The historic figures could not be recorded");
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card className="shadow-sm border border-amber-200 dark:border-amber-800 bg-white dark:bg-neutral-900 mb-6">
      <Card.Header>
        <Card.Title className="flex flex-wrap items-center gap-2">
          Figures from the filed accounts
          <Chip color="warning" variant="soft" size="sm">{HISTORIC_FIGURES_BADGE}</Chip>
        </Card.Title>
        <Card.Description>{HISTORIC_FIGURES_PROVENANCE}</Card.Description>
      </Card.Header>
      <Card.Content>
        <div className="space-y-4">
          <p className="text-xs leading-5 text-[var(--muted-foreground)]">
            They are used only as the preceding year in the next period&apos;s size test (Companies Act 2014, s.280A). Take
            turnover from the full members&apos; accounts: abridged or micro CRO filings may leave out the profit and loss account.
          </p>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="historic-turnover" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Turnover from the filed accounts
              </label>
              <input id="historic-turnover" type="number" min={0} step="0.01" className={inputClass} value={turnover}
                onChange={(e) => setTurnover(e.target.value)} />
            </div>
            <div>
              <label htmlFor="historic-balance-sheet-total" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Balance sheet total from the filed accounts
              </label>
              <input id="historic-balance-sheet-total" type="number" min={0} step="0.01" className={inputClass} value={balanceSheetTotal}
                onChange={(e) => setBalanceSheetTotal(e.target.value)} />
            </div>
            <div>
              <label htmlFor="historic-average-employees" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Average employees from the filed accounts
              </label>
              <input id="historic-average-employees" type="number" min={0} step="1" className={inputClass} value={avgEmployees}
                onChange={(e) => setAvgEmployees(e.target.value)} />
            </div>
            <div>
              <label htmlFor="historic-threshold-election" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                2024 threshold adjustment election
              </label>
              <select id="historic-threshold-election" className={inputClass} value={election}
                onChange={(e) => setElection(e.target.value as ThresholdElectionDate)}>
                <option value="2024-01-01">Apply adjusted thresholds from financial years beginning in 2024</option>
                <option value="2023-01-01">Elect adjusted thresholds from financial years beginning in 2023</option>
              </select>
            </div>
            <div className="sm:col-span-2">
              <label htmlFor="historic-source-reference" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Which filed accounts
              </label>
              <input id="historic-source-reference" type="text" maxLength={300} className={inputClass}
                placeholder="e.g. Members' accounts for the year ended 31 July 2025, filed with the CRO"
                value={sourceReference} onChange={(e) => setSourceReference(e.target.value)} />
            </div>
            <div>
              <label htmlFor="historic-evidence" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                PDF of the filed accounts
              </label>
              <input id="historic-evidence" type="file" accept="application/pdf,.pdf" className="block w-full text-sm"
                onChange={(e) => setEvidence(e.target.files?.[0] ?? null)} />
            </div>
            <div>
              <label htmlFor="historic-attested-class" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                Size class the company qualified for in that year (attested by you; unlocks the carry-forward rule)
              </label>
              <select id="historic-attested-class" className={inputClass} value={attestedClass}
                aria-describedby="historic-attested-class-hint"
                onChange={(e) => setAttestedClass(e.target.value as AttestedClass | "")}>
                <option value="">None attested</option>
                <option value="Micro">Micro</option>
                <option value="Small">Small</option>
                <option value="Medium">Medium</option>
                <option value="Large">Large</option>
              </select>
              <p id="historic-attested-class-hint" className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                Optional. Attest a class only if the filed accounts relied on it: it lets the next year keep that class for one year
                even if its own figures exceed the limits. Without it, both years&apos; figures must meet the size conditions.
              </p>
            </div>
          </div>
          <p role="note" className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
            Recording historic figures marks this period as history filed outside FilingBridge. It can&apos;t then be classified,
            finalised or filed here unless an Owner retires the historic figures: they are kept as evidence, and the period
            can then be classified normally.
          </p>
          <div className="flex flex-wrap gap-3">
            <Button
              variant="primary"
              aria-label="Record historic figures"
              onPress={() => void record()}
              isDisabled={saving || !valid}
            >
              {saving ? <Spinner size="sm" /> : "Record historic figures"}
            </Button>
            <Button variant="outline" onPress={onCancel} isDisabled={saving}>
              Cancel
            </Button>
          </div>
          {error && (
            <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
              {error}
            </div>
          )}
          {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
        </div>
      </Card.Content>
    </Card>
  );
}
