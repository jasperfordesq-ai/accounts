"use client";

import { useEffect, useRef } from "react";
import { Button, Spinner } from "@heroui/react";
import { AlertTriangle } from "lucide-react";

interface ConfirmModalProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: "danger" | "warning" | "default";
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmModal({
  open,
  title,
  description,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  variant = "default",
  loading = false,
  onConfirm,
  onCancel,
}: ConfirmModalProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (open) {
      cancelRef.current?.focus();
      const handleEsc = (e: KeyboardEvent) => {
        if (e.key === "Escape" && !loading) onCancel();
      };
      document.addEventListener("keydown", handleEsc);
      document.body.style.overflow = "hidden";
      return () => {
        document.removeEventListener("keydown", handleEsc);
        document.body.style.overflow = "";
      };
    }
  }, [open, loading, onCancel]);

  if (!open) return null;

  const iconColor =
    variant === "danger"
      ? "text-red-500"
      : variant === "warning"
        ? "text-amber-500"
        : "text-gray-500";

  const confirmVariant = variant === "danger" ? "danger" : "primary";

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/40 animate-backdrop"
        onClick={loading ? undefined : onCancel}
        aria-hidden="true"
      />
      {/* Modal */}
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        aria-describedby="confirm-desc"
        className="relative bg-white dark:bg-neutral-900 rounded-xl shadow-xl border border-gray-200 dark:border-neutral-700 w-full max-w-md mx-4 p-6 animate-slide-down"
      >
        <div className="flex items-start gap-4">
          <div className={`shrink-0 p-2 rounded-full bg-gray-100 dark:bg-neutral-800 ${iconColor}`}>
            <AlertTriangle className="w-5 h-5" />
          </div>
          <div className="flex-1 min-w-0">
            <h2
              id="confirm-title"
              className="text-base font-semibold text-gray-900 dark:text-gray-100"
            >
              {title}
            </h2>
            <p
              id="confirm-desc"
              className="text-sm text-gray-500 dark:text-gray-400 mt-1"
            >
              {description}
            </p>
          </div>
        </div>
        <div className="flex justify-end gap-3 mt-6">
          <Button
            ref={cancelRef}
            variant="outline"
            size="sm"
            onPress={onCancel}
            isDisabled={loading}
          >
            {cancelLabel}
          </Button>
          <Button
            variant={confirmVariant}
            size="sm"
            onPress={onConfirm}
            isDisabled={loading}
          >
            {loading ? (
              <>
                <Spinner size="sm" className="mr-1.5" />
                Processing...
              </>
            ) : (
              confirmLabel
            )}
          </Button>
        </div>
      </div>
    </div>
  );
}
