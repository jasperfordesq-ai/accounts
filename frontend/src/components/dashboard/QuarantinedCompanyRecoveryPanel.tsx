"use client";

import { useState } from "react";
import { ArchiveRestore, ShieldCheck } from "lucide-react";
import { Button, Card } from "@heroui/react";
import { toast } from "sonner";
import { ConfirmModal } from "@/components/ConfirmModal";
import {
  recoverCompany,
  type QuarantinedCompanySummary,
} from "@/lib/api";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";

interface QuarantinedCompanyRecoveryPanelProps {
  companies: QuarantinedCompanySummary[];
  onRecovered: () => void | Promise<void>;
}

export function QuarantinedCompanyRecoveryPanel({
  companies,
  onRecovered,
}: QuarantinedCompanyRecoveryPanelProps) {
  const [selected, setSelected] = useState<QuarantinedCompanySummary | null>(null);
  const [confirmation, setConfirmation] = useState("");
  const [reason, setReason] = useState("");
  const [recovering, setRecovering] = useState(false);
  useUnsavedChanges(selected !== null && (confirmation !== "" || reason !== ""));

  if (companies.length === 0) return null;

  const close = () => {
    setSelected(null);
    setConfirmation("");
    setReason("");
  };

  const recover = async () => {
    if (!selected || confirmation !== selected.legalName || reason.trim().length < 20) return;
    setRecovering(true);
    try {
      await recoverCompany(selected.companyId, {
        confirmation,
        reason: reason.trim(),
      });
      toast.success(`${selected.legalName} was recovered with all retained records.`);
      close();
      await onRecovered();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Company recovery failed");
    } finally {
      setRecovering(false);
    }
  };

  return (
    <Card className="border border-amber-200 bg-amber-50/60 shadow-sm dark:border-amber-900 dark:bg-amber-950/20">
      <Card.Header>
        <Card.Title className="flex items-center gap-2">
          <ArchiveRestore className="h-5 w-5 text-amber-700 dark:text-amber-400" />
          Quarantined companies
        </Card.Title>
        <Card.Description>
          Hidden records remain intact. Recovery is Owner-only and creates another immutable evidence event.
        </Card.Description>
      </Card.Header>
      <Card.Content>
        <div className="divide-y divide-amber-200 dark:divide-amber-900">
          {companies.map((company) => (
            <div key={company.companyId} className="flex flex-col gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="font-semibold text-gray-900 dark:text-gray-100">{company.legalName}</p>
                <p className="mt-1 text-sm text-gray-600 dark:text-gray-400">{company.reason}</p>
                <p className="mt-1 text-xs text-gray-500 dark:text-gray-500">
                  Quarantined {new Date(company.quarantinedAtUtc).toLocaleString("en-IE")} by {company.quarantinedByDisplayName}
                  {` · Evidence ${company.evidenceSha256.slice(0, 12)}…`}
                </p>
              </div>
              <Button
                variant="outline"
                size="sm"
                onPress={() => {
                  setSelected(company);
                  setConfirmation("");
                  setReason("");
                }}
              >
                <ShieldCheck className="h-4 w-4" />
                Recover
              </Button>
            </div>
          ))}
        </div>
      </Card.Content>

      <ConfirmModal
        open={selected !== null}
        title="Recover Company"
        description={selected
          ? `Restore “${selected.legalName}” and every retained record to normal workspaces.`
          : "Restore this company."}
        confirmLabel="Recover Company"
        loading={recovering}
        confirmDisabled={!selected || confirmation !== selected.legalName || reason.trim().length < 20}
        onConfirm={recover}
        onCancel={close}
      >
        <div className="space-y-4">
          <div>
            <label htmlFor="recovery-confirmation" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
              Type the exact legal name to confirm
            </label>
            <input
              id="recovery-confirmation"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              autoComplete="off"
              className="mt-1.5 w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:border-neutral-600 dark:bg-neutral-800 dark:text-gray-100"
            />
          </div>
          <div>
            <label htmlFor="recovery-reason" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
              Recovery reason
            </label>
            <textarea
              id="recovery-reason"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              rows={3}
              maxLength={2000}
              placeholder="Give a specific reason (minimum 20 characters)."
              className="mt-1.5 w-full resize-y rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:border-neutral-600 dark:bg-neutral-800 dark:text-gray-100"
            />
          </div>
        </div>
      </ConfirmModal>
    </Card>
  );
}
