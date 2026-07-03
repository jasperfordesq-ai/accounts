"use client";

import { Button } from "@heroui/react";
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

  return (
    <ReviewPanel
      title="Charity Reporting"
      description="Charities Regulator and SORP facts for CLG charity workflows."
      actions={
        <>
          <StatusBadge tone={statusTone}>
            {charityInfo ? `Tier ${charityInfo.sorpTier}` : "Not recorded"}
          </StatusBadge>
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
        <SummaryBlock label="SORP Tier" value={`Tier ${charityInfo.sorpTier}`} />
        <SummaryBlock label="Gross Income" value={<MoneyField value={charityInfo.grossIncome} />} />
        <SummaryBlock
          label="Governance Code"
          value={charityInfo.governanceCodeCompliant ? "Governance confirmed" : "Not confirmed"}
          tone={charityInfo.governanceCodeCompliant ? "good" : "warn"}
        />
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
      <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_12rem_10rem]">
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
        <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
          SORP tier
          <select
            value={charityForm.sorpTier}
            onChange={(event) => onChange({ ...charityForm, sorpTier: Number(event.target.value) })}
            className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
          >
            <option value={1}>Tier 1</option>
            <option value={2}>Tier 2</option>
          </select>
        </label>
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

      <div className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 lg:grid-cols-3">
        <CheckboxField
          label="Governance Code compliant"
          checked={charityForm.governanceCodeCompliant}
          onChange={(checked) => onChange({ ...charityForm, governanceCodeCompliant: checked })}
        />
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
          <Save className="h-3.5 w-3.5" />
          {saving ? "Saving..." : "Save Charity Reporting"}
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
        Record charity number, SORP tier and governance confirmations before filing review.
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
