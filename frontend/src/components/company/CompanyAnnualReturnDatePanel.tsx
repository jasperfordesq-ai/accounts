"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, Spinner } from "@heroui/react";
import { CalendarClock, ChevronDown, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import {
  getAnnualReturnDateHistory,
  recordAnnualReturnDate,
  type AnnualReturnDateRecord,
  type AnnualReturnDateSource,
  type Company,
} from "@/lib/api";
import { formatDateIE } from "@/lib/format";
import { ReviewPanel, StatusBadge } from "@/components/workbench";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";

const SOURCES: Array<{ value: AnnualReturnDateSource; label: string }> = [
  { value: "CroRecord", label: "CRO CORE record" },
  { value: "BroughtForward", label: "Brought forward on B1" },
  { value: "ExtendedB73", label: "Extended on B73" },
  { value: "CourtOrder", label: "Court order" },
  { value: "ManualOverride", label: "Manual reviewer override" },
];

export function CompanyAnnualReturnDatePanel({
  company,
  canWrite,
  onChanged,
}: {
  company: Company;
  canWrite: boolean;
  onChanged: () => void | Promise<void>;
}) {
  const [history, setHistory] = useState<AnnualReturnDateRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [annualReturnDate, setAnnualReturnDate] = useState(company.annualReturnDate ?? "");
  const [effectiveFrom, setEffectiveFrom] = useState(company.annualReturnDate ?? "");
  const [source, setSource] = useState<AnnualReturnDateSource>("CroRecord");
  const [evidenceReference, setEvidenceReference] = useState("");
  const [evidenceSha256, setEvidenceSha256] = useState("");
  const [changeReason, setChangeReason] = useState("");
  useUnsavedChanges(editing && Boolean(
    annualReturnDate !== (company.annualReturnDate ?? "")
    || effectiveFrom !== (company.annualReturnDate ?? "")
    || evidenceReference
    || evidenceSha256
    || changeReason
    || source !== "CroRecord",
  ));

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setHistory(await getAnnualReturnDateHistory(company.id));
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to load ARD evidence history");
    } finally {
      setLoading(false);
    }
  }, [company.id]);

  useEffect(() => { void load(); }, [load]);

  const currentEvidence = useMemo(
    () => history.find((record) => record.annualReturnDate === company.annualReturnDate) ?? history[0],
    [company.annualReturnDate, history],
  );

  function beginEdit() {
    setAnnualReturnDate(company.annualReturnDate ?? "");
    setEffectiveFrom(company.annualReturnDate ?? "");
    setSource("CroRecord");
    setEvidenceReference("");
    setEvidenceSha256("");
    setChangeReason("");
    setEditing(true);
  }

  async function save() {
    const reasonRequired = Boolean(company.annualReturnDate) || source === "ManualOverride";
    if (!annualReturnDate || !effectiveFrom || !evidenceReference.trim()) {
      toast.error("Exact ARD, effective date and retained evidence reference are required");
      return;
    }
    if (effectiveFrom > annualReturnDate) {
      toast.error("ARD effective date cannot be after the exact ARD");
      return;
    }
    if (reasonRequired && changeReason.trim().length < 20) {
      toast.error("Give a specific ARD change reason of at least 20 characters");
      return;
    }
    if (evidenceSha256 && !/^[a-f0-9]{64}$/i.test(evidenceSha256.trim())) {
      toast.error("Evidence SHA-256 must contain exactly 64 hexadecimal characters");
      return;
    }
    if (source === "ManualOverride" && !/^[a-f0-9]{64}$/i.test(evidenceSha256.trim())) {
      toast.error("A manual ARD override requires the SHA-256 of retained evidence");
      return;
    }

    setSaving(true);
    try {
      await recordAnnualReturnDate(company.id, {
        annualReturnDate,
        effectiveFrom,
        source,
        evidenceReference: evidenceReference.trim(),
        evidenceSha256: evidenceSha256.trim() || undefined,
        changeReason: changeReason.trim() || undefined,
      });
      toast.success("Exact Annual Return Date evidence recorded");
      setEditing(false);
      await Promise.all([load(), onChanged()]);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to record Annual Return Date evidence");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ReviewPanel
      title="Annual Return Date evidence"
      description="Exact CRO date, effective history and retained reviewer evidence used by B1 deadline calculations."
      actions={(
        <div className="flex items-center gap-2">
          <StatusBadge tone={company.annualReturnDate && currentEvidence ? "good" : "bad"}>
            {company.annualReturnDate && currentEvidence ? "Evidence retained" : "Deadline calculation blocked"}
          </StatusBadge>
          {canWrite && !editing && (
            <Button size="sm" variant="outline" onPress={beginEdit}>
              {company.annualReturnDate ? "Change ARD" : "Confirm exact ARD"}
            </Button>
          )}
        </div>
      )}
    >
      <div className="space-y-4">
        <div className="grid gap-3 md:grid-cols-3">
          <EvidenceMetric
            label="Current exact ARD"
            value={company.annualReturnDate ? formatDateIE(company.annualReturnDate) : "Not confirmed"}
            warning={!company.annualReturnDate}
          />
          <EvidenceMetric
            label="Effective from"
            value={currentEvidence ? formatDateIE(currentEvidence.effectiveFrom) : "No retained evidence"}
            warning={!currentEvidence}
          />
          <EvidenceMetric
            label="Evidence source"
            value={currentEvidence ? sourceLabel(currentEvidence.source) : "CORE confirmation required"}
            warning={!currentEvidence}
          />
        </div>

        {currentEvidence ? (
          <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-950 dark:border-emerald-900 dark:bg-emerald-950/30 dark:text-emerald-100">
            <div className="flex items-start gap-2">
              <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" />
              <div>
                <p className="font-semibold">{currentEvidence.evidenceReference}</p>
                <p className="mt-1 text-xs leading-5">
                  Recorded by {currentEvidence.recordedByDisplayName} on {formatDateTime(currentEvidence.recordedAtUtc)}.
                  Integrity {shortHash(currentEvidence.recordSha256)}.
                </p>
              </div>
            </div>
          </div>
        ) : (
          <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
            Month-only legacy data is not converted into a guessed day. Confirm the exact date shown by CRO CORE before relying on filing deadlines.
          </div>
        )}

        {editing && (
          <div className="space-y-4 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
            <div className="grid gap-4 md:grid-cols-3">
              <Field label="Exact ARD *">
                <input type="date" value={annualReturnDate} onChange={(event) => setAnnualReturnDate(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Effective from *">
                <input type="date" value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Change source *">
                <select value={source} onChange={(event) => setSource(event.target.value as AnnualReturnDateSource)} className={inputClass}>
                  {SOURCES.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </Field>
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Retained evidence reference *">
                <input value={evidenceReference} onChange={(event) => setEvidenceReference(event.target.value)} maxLength={300} placeholder="CORE extract, B1/B73, court order, or workpaper reference" className={inputClass} />
              </Field>
              <Field label={source === "ManualOverride" ? "Evidence SHA-256 *" : "Evidence SHA-256 (recommended)"}>
                <input value={evidenceSha256} onChange={(event) => setEvidenceSha256(event.target.value)} maxLength={64} spellCheck={false} placeholder="64 hexadecimal characters" className={`${inputClass} font-mono`} />
              </Field>
            </div>
            <Field label={company.annualReturnDate || source === "ManualOverride" ? "Change reason *" : "Initial review note"}>
              <textarea value={changeReason} onChange={(event) => setChangeReason(event.target.value)} maxLength={1000} rows={3} placeholder="Explain what changed and why this evidence supports the exact date." className={`${inputClass} resize-y`} />
            </Field>
            <p className="text-xs leading-5 text-[var(--muted-foreground)]">
              This records workflow evidence only. It does not submit a B1, B73 or court application to the CRO.
            </p>
            <div className="flex justify-end gap-2">
              <Button size="sm" variant="ghost" isDisabled={saving} onPress={() => setEditing(false)}>Cancel</Button>
              <Button size="sm" variant="primary" aria-label="Record ARD evidence" isDisabled={saving} onPress={() => void save()}>
                {saving ? <Spinner size="sm" /> : "Record ARD evidence"}
              </Button>
            </div>
          </div>
        )}

        {loading ? (
          <div className="flex items-center gap-2 text-sm text-[var(--muted-foreground)]"><Spinner size="sm" /> Loading retained ARD history</div>
        ) : history.length > 0 ? (
          <details className="group rounded-md border border-[var(--border)] bg-[var(--surface)]">
            <summary className="flex cursor-pointer list-none items-center justify-between px-4 py-3 text-sm font-semibold text-[var(--foreground)]">
              <span>{history.length} retained ARD {history.length === 1 ? "record" : "records"}</span>
              <ChevronDown className="h-4 w-4 transition group-open:rotate-180" />
            </summary>
            <div className="divide-y divide-[var(--border)] border-t border-[var(--border)]">
              {history.map((record) => (
                <div key={record.id} className="grid gap-2 px-4 py-3 text-xs md:grid-cols-[160px_170px_minmax(0,1fr)]">
                  <div className="font-semibold text-[var(--foreground)]">{formatDateIE(record.annualReturnDate)}</div>
                  <div className="text-[var(--muted-foreground)]">{sourceLabel(record.source)}</div>
                  <div className="min-w-0 text-[var(--muted-foreground)]">
                    <span className="font-medium text-[var(--foreground)]">{record.evidenceReference}</span>
                    {record.changeReason ? ` — ${record.changeReason}` : ""}
                  </div>
                </div>
              ))}
            </div>
          </details>
        ) : null}
      </div>
    </ReviewPanel>
  );
}

function EvidenceMetric({ label, value, warning = false }: { label: string; value: string; warning?: boolean }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <div className="flex items-center gap-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]"><CalendarClock className="h-3.5 w-3.5" />{label}</div>
      <p className={`mt-2 text-sm font-semibold ${warning ? "text-red-700 dark:text-red-200" : "text-[var(--foreground)]"}`}>{value}</p>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block text-sm font-medium text-[var(--foreground)]"><span className="mb-1.5 block">{label}</span>{children}</label>;
}

function sourceLabel(source: AnnualReturnDateSource) {
  return SOURCES.find((option) => option.value === source)?.label ?? source;
}

function shortHash(hash: string) {
  return `${hash.slice(0, 10)}…${hash.slice(-8)}`;
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString("en-IE", { dateStyle: "medium", timeStyle: "short" });
}

const inputClass = "w-full rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 py-2 text-sm text-[var(--foreground)] outline-none transition focus:border-[var(--ring)] focus:ring-2 focus:ring-teal-100 dark:focus:ring-teal-900/40";
