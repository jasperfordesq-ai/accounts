"use client";

import { useCallback, useState } from "react";
import { ConfirmModal } from "@/components/ConfirmModal";

export interface DestructiveActionRequest {
  recordLabel: string;
  consequence: string;
  onConfirm: () => void | Promise<void>;
  confirmLabel?: string;
  successAnnouncement?: string;
  failureAnnouncement?: string;
}

export function useDestructiveActionConfirmation() {
  const [pending, setPending] = useState<DestructiveActionRequest | null>(null);
  const [loading, setLoading] = useState(false);
  const [announcement, setAnnouncement] = useState("");

  const requestDestructiveAction = useCallback((request: DestructiveActionRequest) => {
    setAnnouncement("");
    setPending(request);
  }, []);

  const cancel = useCallback(() => {
    if (loading) return;
    if (pending) {
      setAnnouncement(`Removal cancelled. ${sentenceCaseLabel(pending.recordLabel)} was kept.`);
    }
    setPending(null);
  }, [loading, pending]);

  const confirm = useCallback(async () => {
    if (!pending || loading) return;

    setLoading(true);
    try {
      await pending.onConfirm();
      setAnnouncement(pending.successAnnouncement ?? `${sentenceCaseLabel(pending.recordLabel)} was removed.`);
      setPending(null);
    } catch {
      setAnnouncement(
        pending.failureAnnouncement
          ?? `${sentenceCaseLabel(pending.recordLabel)} was not removed. The retained record is unchanged.`,
      );
      setPending(null);
    } finally {
      setLoading(false);
    }
  }, [loading, pending]);

  return {
    requestDestructiveAction,
    destructiveActionConfirmation: (
      <>
        <p className="sr-only" role="status" aria-live="polite" aria-atomic="true">
          {announcement}
        </p>
        <ConfirmModal
          open={pending !== null}
          title={pending ? `Remove ${pending.recordLabel}?` : "Remove record?"}
          description={pending?.consequence ?? "This record will be removed."}
          confirmLabel={pending?.confirmLabel ?? "Remove record"}
          cancelLabel="Keep record"
          variant="danger"
          dialogRole="alertdialog"
          loading={loading}
          onConfirm={() => void confirm()}
          onCancel={cancel}
        />
      </>
    ),
  };
}

function sentenceCaseLabel(value: string) {
  return value.length === 0 ? value : `${value[0].toUpperCase()}${value.slice(1)}`;
}
