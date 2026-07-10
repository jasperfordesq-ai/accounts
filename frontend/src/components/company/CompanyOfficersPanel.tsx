"use client";

import { Button } from "@heroui/react";
import { Pencil, Plus, Save, Trash2, UserRound, X } from "lucide-react";
import type { Officer } from "@/lib/api";
import { DataGrid, ReviewPanel, StatusBadge } from "@/components/workbench";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

interface CompanyOfficersPanelProps {
  officers?: Officer[];
  showAddOfficer: boolean;
  newOfficerName: string;
  newOfficerRole: string;
  newOfficerAppointedDate?: string;
  editingOfficerId: number | null;
  editOfficerName: string;
  editOfficerRole: string;
  editOfficerAppointedDate?: string;
  editOfficerResignedDate?: string;
  savingOfficer: boolean;
  canWrite?: boolean;
  onShowAddOfficer: () => void;
  onNewOfficerNameChange: (value: string) => void;
  onNewOfficerRoleChange: (value: string) => void;
  onNewOfficerAppointedDateChange?: (value: string) => void;
  onCancelAddOfficer: () => void;
  onAddOfficer: () => void;
  onStartEditOfficer: (officer: Officer) => void;
  onEditOfficerNameChange: (value: string) => void;
  onEditOfficerRoleChange: (value: string) => void;
  onEditOfficerAppointedDateChange?: (value: string) => void;
  onEditOfficerResignedDateChange?: (value: string) => void;
  onSaveOfficer: () => void;
  onCancelEditOfficer: () => void;
  onDeleteOfficer: (officerId: number) => void | Promise<void>;
}

const officerRoles = ["Director", "Secretary", "CompanySecretary"];

export function CompanyOfficersPanel({
  officers = [],
  showAddOfficer,
  newOfficerName,
  newOfficerRole,
  newOfficerAppointedDate = "",
  editingOfficerId,
  editOfficerName,
  editOfficerRole,
  editOfficerAppointedDate = "",
  editOfficerResignedDate = "",
  savingOfficer,
  canWrite = true,
  onShowAddOfficer,
  onNewOfficerNameChange,
  onNewOfficerRoleChange,
  onNewOfficerAppointedDateChange = () => {},
  onCancelAddOfficer,
  onAddOfficer,
  onStartEditOfficer,
  onEditOfficerNameChange,
  onEditOfficerRoleChange,
  onEditOfficerAppointedDateChange = () => {},
  onEditOfficerResignedDateChange = () => {},
  onSaveOfficer,
  onCancelEditOfficer,
  onDeleteOfficer,
}: CompanyOfficersPanelProps) {
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  return (
    <>
      <ReviewPanel
      title="Officers & Signatories"
      description="Directors, secretary and statutory signatory records."
      actions={
        <>
          <StatusBadge tone={officers.length > 0 ? "info" : "warn"}>
            {officers.length} {officers.length === 1 ? "officer" : "officers"}
          </StatusBadge>
          {canWrite && (
            <Button variant="primary" size="sm" onPress={onShowAddOfficer}>
              <Plus className="h-3.5 w-3.5" />
              Add officer
            </Button>
          )}
        </>
      }
    >
      {showAddOfficer && canWrite && (
        <div className="mb-4 grid gap-3 rounded-md border border-emerald-200 bg-emerald-50 p-3 dark:border-emerald-800 dark:bg-emerald-950/30 md:grid-cols-[minmax(14rem,1fr)_12rem_11rem_auto_auto] md:items-end">
          <OfficerNameField
            label="New officer"
            value={newOfficerName}
            onChange={onNewOfficerNameChange}
            onEnter={onAddOfficer}
            autoFocus
          />
          <OfficerRoleField
            label="Role"
            value={newOfficerRole}
            onChange={onNewOfficerRoleChange}
          />
          <OfficerDateField
            label="Appointed"
            value={newOfficerAppointedDate}
            onChange={onNewOfficerAppointedDateChange}
          />
          <Button variant="primary" size="sm" onPress={onAddOfficer} isDisabled={savingOfficer || !newOfficerName.trim() || (newOfficerRole === "Director" && !newOfficerAppointedDate)}>
            <Save className="h-3.5 w-3.5" />
            Save
          </Button>
          <Button variant="ghost" size="sm" onPress={onCancelAddOfficer} isDisabled={savingOfficer}>
            <X className="h-3.5 w-3.5" />
            Cancel
          </Button>
        </div>
      )}

      {officers.length === 0 ? (
        <div className="rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-10 text-center">
          <UserRound className="mx-auto h-9 w-9 text-[var(--muted-foreground)]" />
          <p className="mt-3 text-sm font-medium text-[var(--foreground)]">No officers recorded</p>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">
            Add directors and secretary records before sign-off.
          </p>
        </div>
      ) : (
        <DataGrid
          columns={["Officer", "Role", "Status", "Actions"]}
          mobilePresentation="cards"
          sortableColumns={[true, true, true, false]}
          rows={officers.map((officer) => {
            const isEditing = editingOfficerId === officer.id;
            const officerStatus = officer.resignedDate ? "Resigned" : isEditing ? "Editing" : "Active";

            return {
              id: officer.id ?? `${officer.name}-${officer.role}`,
              searchText: `${officer.name} ${officer.role} ${officerStatus}`,
              sortValues: [officer.name, officer.role, officerStatus, null],
              cells: [
              isEditing ? (
                <div key="name" className="min-w-0 space-y-2 sm:min-w-64">
                  <OfficerNameField
                    label="Edit officer name"
                    value={editOfficerName}
                    onChange={onEditOfficerNameChange}
                    onEnter={onSaveOfficer}
                    onEscape={onCancelEditOfficer}
                    compact
                    autoFocus
                  />
                  <div className="grid gap-2 sm:grid-cols-2">
                    <OfficerDateField
                      label="Appointed"
                      value={editOfficerAppointedDate}
                      onChange={onEditOfficerAppointedDateChange}
                      compact
                    />
                    <OfficerDateField
                      label="Resigned"
                      value={editOfficerResignedDate}
                      onChange={onEditOfficerResignedDateChange}
                      compact
                    />
                  </div>
                </div>
              ) : (
                <OfficerIdentity key="name" officer={officer} />
              ),
              isEditing ? (
                <OfficerRoleField
                  key="role"
                  label="Edit officer role"
                  value={editOfficerRole}
                  onChange={onEditOfficerRoleChange}
                  compact
                />
              ) : (
                <StatusBadge key="role" tone={roleTone(officer.role)}>
                  {officer.role}
                </StatusBadge>
              ),
              <StatusBadge key="status" tone={officer.resignedDate ? "warn" : isEditing ? "warn" : "good"}>
                {officerStatus}
              </StatusBadge>,
              <OfficerActions
                key="actions"
                officer={officer}
                isEditing={isEditing}
                canWrite={canWrite}
                savingOfficer={savingOfficer}
                canSave={Boolean(editOfficerName.trim()) && (editOfficerRole !== "Director" || Boolean(editOfficerAppointedDate))}
                onStartEditOfficer={onStartEditOfficer}
                onSaveOfficer={onSaveOfficer}
                onCancelEditOfficer={onCancelEditOfficer}
                onDeleteOfficer={(officerId) => requestDestructiveAction({
                  recordLabel: `officer ${officer.name}`,
                  consequence: `This permanently removes ${officer.name}'s ${officer.role} appointment record and may affect statutory reports, signatories and charity trustee evidence. The removal cannot be undone.`,
                  onConfirm: () => onDeleteOfficer(officerId),
                  successAnnouncement: `Officer ${officer.name} was removed.`,
                })}
              />,
              ],
            };
          })}
        />
      )}
      </ReviewPanel>
      {destructiveActionConfirmation}
    </>
  );
}

function OfficerIdentity({ officer }: { officer: Officer }) {
  return (
    <div className="min-w-0 sm:min-w-48">
      <p className="font-medium text-[var(--foreground)]">{officer.name}</p>
      {officer.appointedDate && (
        <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
          Appointed {new Intl.DateTimeFormat("en-IE", { day: "2-digit", month: "short", year: "numeric" }).format(new Date(officer.appointedDate))}
        </p>
      )}
      {!officer.appointedDate && officer.role === "Director" && (
        <p className="mt-1 text-xs font-medium text-amber-700 dark:text-amber-300">
          Appointment date required for charity trustee-period review
        </p>
      )}
    </div>
  );
}

function OfficerActions({
  officer,
  isEditing,
  canWrite,
  savingOfficer,
  canSave,
  onStartEditOfficer,
  onSaveOfficer,
  onCancelEditOfficer,
  onDeleteOfficer,
}: {
  officer: Officer;
  isEditing: boolean;
  canWrite: boolean;
  savingOfficer: boolean;
  canSave: boolean;
  onStartEditOfficer: (officer: Officer) => void;
  onSaveOfficer: () => void;
  onCancelEditOfficer: () => void;
  onDeleteOfficer: (officerId: number) => void | Promise<void>;
}) {
  if (!canWrite) {
    return <span className="text-xs text-[var(--muted-foreground)]">Read only</span>;
  }

  if (isEditing) {
    return (
      <div className="flex min-w-28 items-center gap-2">
        <Button variant="primary" size="sm" isIconOnly onPress={onSaveOfficer} isDisabled={savingOfficer || !canSave} aria-label="Save officer">
          <Save className="h-3.5 w-3.5" />
        </Button>
        <Button variant="ghost" size="sm" isIconOnly onPress={onCancelEditOfficer} isDisabled={savingOfficer} aria-label="Cancel editing">
          <X className="h-3.5 w-3.5" />
        </Button>
      </div>
    );
  }

  return (
    <div className="flex min-w-28 items-center gap-2">
      <Button variant="ghost" size="sm" isIconOnly onPress={() => onStartEditOfficer(officer)} aria-label={`Edit ${officer.name}`}>
        <Pencil className="h-3.5 w-3.5" />
      </Button>
      {officer.id && (
        <Button variant="ghost" size="sm" isIconOnly onPress={() => onDeleteOfficer(officer.id!)} aria-label={`Remove ${officer.name}`}>
          <Trash2 className="h-3.5 w-3.5 text-red-600 dark:text-red-300" />
        </Button>
      )}
    </div>
  );
}

function OfficerNameField({
  label,
  value,
  onChange,
  onEnter,
  onEscape,
  compact = false,
  autoFocus = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  onEnter: () => void;
  onEscape?: () => void;
  compact?: boolean;
  autoFocus?: boolean;
}) {
  return (
    <label className={`block text-xs font-semibold uppercase ${compact ? "text-[var(--muted-foreground)]" : "text-emerald-900 dark:text-emerald-100"}`}>
      {label}
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") onEnter();
          if (event.key === "Escape") onEscape?.();
        }}
        className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
        autoFocus={autoFocus}
      />
    </label>
  );
}

function OfficerRoleField({
  label,
  value,
  onChange,
  compact = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  compact?: boolean;
}) {
  return (
    <label className={`block text-xs font-semibold uppercase ${compact ? "text-[var(--muted-foreground)]" : "text-emerald-900 dark:text-emerald-100"}`}>
      {label}
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
      >
        {officerRoles.map((role) => (
          <option key={role} value={role}>
            {role}
          </option>
        ))}
      </select>
    </label>
  );
}

function OfficerDateField({
  label,
  value,
  onChange,
  compact = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  compact?: boolean;
}) {
  return (
    <label className={`block text-xs font-semibold uppercase ${compact ? "text-[var(--muted-foreground)]" : "text-emerald-900 dark:text-emerald-100"}`}>
      {label}
      <input
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm normal-case text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500"
      />
    </label>
  );
}

function roleTone(role: string): "default" | "good" | "warn" | "bad" | "info" {
  if (role === "Director") return "good";
  if (role === "Secretary") return "info";
  if (role === "Chairperson") return "warn";
  return "default";
}
