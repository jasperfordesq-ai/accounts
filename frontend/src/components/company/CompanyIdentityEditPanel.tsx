"use client";

import { Button, Spinner } from "@heroui/react";
import { Building2, Save, X } from "lucide-react";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

export interface CompanyEditFormValues {
  legalName: string;
  tradingName: string;
  croNumber: string;
  taxReference: string;
  companyType: string;
  isTrading: boolean;
  isDormant: boolean;
}

interface CompanyIdentityEditPanelProps {
  form: CompanyEditFormValues;
  saving: boolean;
  onFormChange: (value: CompanyEditFormValues) => void;
  onSave: () => void;
  onCancel: () => void;
}

const companyTypeOptions = [
  { value: "Private", label: "LTD - Private company limited by shares" },
  { value: "PrivateUnlimited", label: "Unlimited company" },
  { value: "DesignatedActivityCompany", label: "DAC - Designated activity company" },
  { value: "CompanyLimitedByGuarantee", label: "CLG - Company limited by guarantee" },
  { value: "PublicLimitedCompany", label: "PLC - Public limited company" },
];

export function CompanyIdentityEditPanel({
  form,
  saving,
  onFormChange,
  onSave,
  onCancel,
}: CompanyIdentityEditPanelProps) {
  return (
    <ReviewPanel
      title="Edit Company Identity"
      description="Legal identity, CRO references and trading flags used across statutory workflows."
      actions={<StatusBadge tone="warn">Draft changes</StatusBadge>}
    >
      <div className="space-y-4">
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_13rem]">
          <TextField
            label="Legal Name"
            required
            value={form.legalName}
            onChange={(value) => onFormChange({ ...form, legalName: value })}
          />
          <TextField
            label="Trading Name"
            value={form.tradingName}
            placeholder="Trading Name (optional)"
            onChange={(value) => onFormChange({ ...form, tradingName: value })}
          />
          <TextField
            label="CRO Number"
            value={form.croNumber}
            onChange={(value) => onFormChange({ ...form, croNumber: value })}
          />
        </div>

        <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          <TextField
            label="Tax Reference"
            value={form.taxReference}
            onChange={(value) => onFormChange({ ...form, taxReference: value })}
          />
          <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
            Company Type
            <select
              value={form.companyType}
              onChange={(event) => onFormChange({ ...form, companyType: event.target.value })}
              aria-label="Company Type"
              className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
            >
              {companyTypeOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
          <div className="min-w-0">
            <div className="flex items-center gap-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]">
              <Building2 className="h-4 w-4" />
              Workflow Flags
            </div>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
              These flags feed statutory profile cues and filing readiness warnings.
            </p>
          </div>
          <div className="flex flex-wrap gap-4">
            <CheckboxField
              label="Trading"
              ariaLabel="Is Trading"
              checked={form.isTrading}
              onChange={(checked) => onFormChange({ ...form, isTrading: checked })}
            />
            <CheckboxField
              label="Dormant"
              ariaLabel="Is Dormant"
              checked={form.isDormant}
              onChange={(checked) => onFormChange({ ...form, isDormant: checked })}
            />
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-end gap-2 border-t border-[var(--border)] pt-3">
          <Button variant="ghost" size="sm" onPress={onCancel} isDisabled={saving} aria-label="Cancel company editing">
            <X className="h-3.5 w-3.5" />
            Cancel
          </Button>
          <Button variant="primary" size="sm" onPress={onSave} isDisabled={saving} aria-label="Save Changes to company">
            {saving ? <Spinner size="sm" /> : <Save className="h-3.5 w-3.5" />}
            Save Changes
          </Button>
        </div>
      </div>
    </ReviewPanel>
  );
}

function TextField({
  label,
  value,
  onChange,
  placeholder,
  required = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  required?: boolean;
}) {
  return (
    <label className="block text-xs font-semibold uppercase text-[var(--muted-foreground)]">
      {label}{required ? " *" : ""}
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder ?? label}
        aria-label={label}
        className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
      />
    </label>
  );
}

function CheckboxField({
  label,
  ariaLabel,
  checked,
  onChange,
}: {
  label: string;
  ariaLabel: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex min-h-10 items-center gap-2 text-sm font-medium text-[var(--foreground)]">
      <input
        type="checkbox"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        aria-label={ariaLabel}
        className="rounded border-[var(--border)] text-emerald-700 focus:ring-emerald-500"
      />
      {label}
    </label>
  );
}
