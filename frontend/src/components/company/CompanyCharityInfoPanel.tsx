"use client";

import { Button, Spinner } from "@heroui/react";
import { Heart, Pencil, Plus, Save, X } from "lucide-react";
import type { ReactNode } from "react";
import type { CharityInfo } from "@/lib/api";
import { MoneyField, ReviewPanel, StatusBadge } from "@/components/workbench";

interface CompanyCharityInfoPanelProps {
  charityInfo: CharityInfo | null;
  charityForm: CharityInfo;
  editing: boolean;
  saving: boolean;
  canWrite?: boolean;
  onStartEdit: () => void;
  onCancelEdit: () => void;
  onSave: () => void;
  onFormChange: (value: CharityInfo) => void;
}

export function CompanyCharityInfoPanel({
  charityInfo,
  charityForm,
  editing,
  saving,
  canWrite = true,
  onStartEdit,
  onCancelEdit,
  onSave,
  onFormChange,
}: CompanyCharityInfoPanelProps) {
  const statusTone = charityInfo ? "info" : "warn";
  const governanceEvidenceReady = Boolean(
    charityInfo?.governanceCodeCompliant !== null
    && charityInfo?.governanceEvidenceReference
    && charityInfo?.governanceReviewedBy
    && charityInfo?.governanceReviewedAtUtc
    && charityInfo?.governanceEvidenceArtifactSha256,
  );

  return (
    <ReviewPanel
      title="Charity Reporting"
      description="Charities Regulator and SORP facts for CLG charity workflows."
      actions={
        <>
          <StatusBadge tone={statusTone}>
            {charityInfo ? "Charity profile recorded" : "Not recorded"}
          </StatusBadge>
          {charityInfo && (
            <StatusBadge tone={governanceEvidenceReady ? "good" : "warn"}>
              {governanceEvidenceReady ? "Governance evidence retained" : "Governance evidence required"}
            </StatusBadge>
          )}
          {!canWrite && <StatusBadge>Read only</StatusBadge>}
          {canWrite && !editing && charityInfo && (
            <Button variant="outline" size="sm" onPress={onStartEdit} aria-label="Edit charity reporting">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
          )}
        </>
      }
    >
      {editing && canWrite ? (
        <CharityEditForm
          charityForm={charityForm}
          saving={saving}
          onChange={onFormChange}
          onCancelEdit={onCancelEdit}
          onSave={onSave}
        />
      ) : charityInfo ? (
        <CharitySummary charityInfo={charityInfo} />
      ) : (
        <EmptyCharityState canWrite={canWrite} onStartEdit={onStartEdit} />
      )}
    </ReviewPanel>
  );
}

function CharitySummary({ charityInfo }: { charityInfo: CharityInfo }) {
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <SummaryBlock label="Charity Number" value={charityInfo.charityNumber || "-"} />
        <SummaryBlock label="Charity legal form" value={charityInfo.charityType || "Not answered"} />
        <SummaryBlock label="Gross Income" value={<MoneyField value={charityInfo.grossIncome} />} />
        <SummaryBlock
          label="Governance Code"
          value={charityInfo.governanceCodeCompliant === null
            ? "Answer required"
            : charityInfo.governanceCodeCompliant ? "Yes" : "No"}
          tone={charityInfo.governanceCodeCompliant === null ? "warn" : "good"}
        />
      </div>

      <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-sm">
        <p className="font-semibold text-[var(--foreground)]">Period-specific SORP decision</p>
        <p className="mt-1 text-[var(--muted-foreground)]">
          Framework, tier and automated-support eligibility are decided for each accounting period from its start date,
          legal form and annual gross income. They are not user-selectable company facts.
        </p>
        {charityInfo.governanceEvidenceReference && (
          <p className="mt-2 text-[var(--foreground)]">
            Governance evidence: {charityInfo.governanceEvidenceReference}
            {charityInfo.governanceReviewedBy ? ` - reviewed by ${charityInfo.governanceReviewedBy}` : ""}
          </p>
        )}
      </div>

      {(charityInfo.charitableObjectives || charityInfo.principalActivities) && (
        <div className="grid gap-3 lg:grid-cols-2">
          {charityInfo.charitableObjectives && (
            <NarrativeBlock label="Objectives" value={charityInfo.charitableObjectives} />
          )}
          {charityInfo.principalActivities && (
            <NarrativeBlock label="Principal Activities" value={charityInfo.principalActivities} />
          )}
        </div>
      )}

      <div className="flex flex-wrap gap-2">
        <StatusBadge tone={charityInfo.trusteeRemunerationPaid ? "warn" : "good"}>
          {charityInfo.trusteeRemunerationPaid ? "Trustee remuneration" : "No trustee remuneration"}
        </StatusBadge>
        {charityInfo.trusteeRemunerationPaid && (
          <StatusBadge tone="warn">
            <MoneyField value={charityInfo.trusteeRemunerationAmount} />
          </StatusBadge>
        )}
        <StatusBadge tone={charityInfo.hasInternationalTransfers ? "warn" : "good"}>
          {charityInfo.hasInternationalTransfers ? "International transfers" : "No international transfers"}
        </StatusBadge>
      </div>

      {charityInfo.internationalTransferDetails && (
        <NarrativeBlock label="Transfer Details" value={charityInfo.internationalTransferDetails} />
      )}
    </div>
  );
}

function CharityEditForm({
  charityForm,
  saving,
  onChange,
  onCancelEdit,
  onSave,
}: {
  charityForm: CharityInfo;
  saving: boolean;
  onChange: (value: CharityInfo) => void;
  onCancelEdit: () => void;
  onSave: () => void;
}) {
  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_12rem_14rem]">
        <TextField
          label="Charity number"
          value={charityForm.charityNumber || ""}
          onChange={(value) => onChange({ ...charityForm, charityNumber: value })}
        />
        <NumberField
          label="Gross income"
          value={charityForm.grossIncome}
          onChange={(value) => onChange({ ...charityForm, grossIncome: value })}
        />
        <TextField
          label="Charity legal form"
          value={charityForm.charityType || ""}
          onChange={(value) => onChange({ ...charityForm, charityType: value })}
        />
      </div>

      <TextareaField
        label="Charitable objectives"
        value={charityForm.charitableObjectives || ""}
        onChange={(value) => onChange({ ...charityForm, charitableObjectives: value })}
      />
      <TextareaField
        label="Principal activities"
        value={charityForm.principalActivities || ""}
        onChange={(value) => onChange({ ...charityForm, principalActivities: value })}
      />

      <div className="space-y-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
        <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
          Has the charity complied with the Charities Governance Code?
          <select
            value={charityForm.governanceCodeCompliant === null ? "" : charityForm.governanceCodeCompliant ? "yes" : "no"}
            onChange={(event) => onChange({
              ...charityForm,
              governanceCodeCompliant: event.target.value === "" ? null : event.target.value === "yes",
            })}
            className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
          >
            <option value="">Select an explicit answer</option>
            <option value="yes">Yes</option>
            <option value="no">No</option>
          </select>
        </label>
        <TextareaField
          label="Governance review note"
          value={charityForm.governanceCodeNote || ""}
          onChange={(value) => onChange({ ...charityForm, governanceCodeNote: value })}
        />
        <TextField
          label="Governance evidence reference"
          value={charityForm.governanceEvidenceReference || ""}
          onChange={(value) => onChange({ ...charityForm, governanceEvidenceReference: value })}
        />
        <EvidenceFileField charityForm={charityForm} onChange={onChange} />
        {charityForm.governanceEvidenceArtifactSha256 && !charityForm.governanceEvidenceArtifact && (
          <p className="text-xs text-emerald-700 dark:text-emerald-300">
            Existing retained artifact: {charityForm.governanceEvidenceArtifactSha256.slice(0, 16)}...
          </p>
        )}
      </div>

      <div className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 lg:grid-cols-2">
        <CheckboxField
          label="Trustee remuneration paid"
          checked={charityForm.trusteeRemunerationPaid}
          onChange={(checked) => onChange({ ...charityForm, trusteeRemunerationPaid: checked })}
        />
        <CheckboxField
          label="International transfers"
          checked={charityForm.hasInternationalTransfers}
          onChange={(checked) => onChange({ ...charityForm, hasInternationalTransfers: checked })}
        />
      </div>

      {charityForm.trusteeRemunerationPaid && (
        <NumberField
          label="Trustee remuneration amount"
          value={charityForm.trusteeRemunerationAmount}
          onChange={(value) => onChange({ ...charityForm, trusteeRemunerationAmount: value })}
        />
      )}

      {charityForm.hasInternationalTransfers && (
        <TextareaField
          label="International transfer details"
          value={charityForm.internationalTransferDetails || ""}
          onChange={(value) => onChange({ ...charityForm, internationalTransferDetails: value })}
        />
      )}

      <div className="flex flex-wrap items-center justify-end gap-2 border-t border-[var(--border)] pt-3">
        <Button variant="ghost" size="sm" onPress={onCancelEdit} isDisabled={saving} aria-label="Cancel charity reporting edit">
          <X className="h-3.5 w-3.5" />
          Cancel
        </Button>
        <Button variant="primary" size="sm" onPress={onSave} isDisabled={saving} aria-label="Save charity reporting">
          {saving ? <Spinner size="sm" /> : <Save className="h-3.5 w-3.5" />}
          Save Charity Reporting
        </Button>
      </div>
    </div>
  );
}

function EmptyCharityState({
  canWrite,
  onStartEdit,
}: {
  canWrite: boolean;
  onStartEdit: () => void;
}) {
  return (
    <div className="rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-10 text-center">
      <Heart className="mx-auto h-9 w-9 text-[var(--muted-foreground)]" />
      <p className="mt-3 text-sm font-medium text-[var(--foreground)]">No charity information recorded</p>
      <p className="mt-1 text-sm text-[var(--muted-foreground)]">
        Record the charity number, legal form, annual income and retained governance review evidence before filing review.
      </p>
      {canWrite && (
        <Button variant="primary" size="sm" onPress={onStartEdit} className="mt-4" aria-label="Add charity reporting">
          <Plus className="h-3.5 w-3.5" />
          Add Charity Reporting
        </Button>
      )}
    </div>
  );
}

function EvidenceFileField({
  charityForm,
  onChange,
}: {
  charityForm: CharityInfo;
  onChange: (value: CharityInfo) => void;
}) {
  const readFile = async (file?: File) => {
    if (!file) {
      onChange({ ...charityForm, governanceEvidenceArtifact: undefined });
      return;
    }
    const bytes = new Uint8Array(await file.arrayBuffer());
    let binary = "";
    for (const byte of bytes) binary += String.fromCharCode(byte);
    onChange({ ...charityForm, governanceEvidenceArtifact: btoa(binary) });
  };

  return (
    <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
      Governance review artifact
      <input
        type="file"
        accept=".pdf,.png,.jpg,.jpeg,.txt,.doc,.docx"
        onChange={(event) => void readFile(event.target.files?.[0])}
        className="mt-1 block min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm normal-case text-[var(--foreground)] file:mr-3 file:rounded file:border-0 file:bg-emerald-700 file:px-3 file:py-1 file:text-xs file:font-semibold file:text-white"
      />
      <span className="mt-1 block font-normal normal-case text-[var(--muted-foreground)]">
        Retained with a SHA-256 hash and the signed-in reviewer identity and UTC review time.
      </span>
    </label>
  );
}

function SummaryBlock({
  label,
  value,
  tone = "default",
}: {
  label: string;
  value: ReactNode;
  tone?: "default" | "good" | "warn";
}) {
  const toneClass = tone === "good"
    ? "text-emerald-700 dark:text-emerald-300"
    : tone === "warn"
      ? "text-amber-700 dark:text-amber-300"
      : "text-[var(--foreground)]";

  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <div className={`mt-1 text-sm font-semibold ${toneClass}`}>{value}</div>
    </div>
  );
}

function NarrativeBlock({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <p className="mt-2 text-sm leading-6 text-[var(--foreground)]">{value}</p>
    </div>
  );
}

function TextField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
      {label}
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
      />
    </label>
  );
}

function NumberField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
}) {
  return (
    <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
      {label}
      <input
        type="number"
        value={Number.isFinite(value) ? value : ""}
        onChange={(event) => onChange(Number(event.target.value))}
        className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
      />
    </label>
  );
}

function TextareaField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
      {label}
      <textarea
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 min-h-24 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm normal-case leading-6 text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
      />
    </label>
  );
}

function CheckboxField({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex min-h-10 items-center gap-2 text-sm font-medium text-[var(--foreground)]">
      <input
        type="checkbox"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        className="rounded border-[var(--border)] text-emerald-700 focus:ring-emerald-500"
      />
      {label}
    </label>
  );
}
