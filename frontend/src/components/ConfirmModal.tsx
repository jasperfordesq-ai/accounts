"use client";

import { useEffect, useId, useRef, type ReactNode } from "react";
import { Button, Spinner } from "@heroui/react";
import { AlertTriangle } from "lucide-react";

const openModalStack: symbol[] = [];
let bodyOverflowBeforeFirstModal = "";

interface ConfirmModalProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: "danger" | "warning" | "default";
  loading?: boolean;
  confirmDisabled?: boolean;
  dialogRole?: "dialog" | "alertdialog";
  children?: ReactNode;
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
  confirmDisabled = false,
  dialogRole = "dialog",
  children,
  onConfirm,
  onCancel,
}: ConfirmModalProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const modalRef = useRef<HTMLDivElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const loadingRef = useRef(loading);
  const onCancelRef = useRef(onCancel);
  const modalInstanceId = useRef(Symbol("confirm-modal"));
  const titleId = `${useId()}-title`;
  const descriptionId = `${useId()}-description`;

  useEffect(() => {
    loadingRef.current = loading;
    onCancelRef.current = onCancel;
  }, [loading, onCancel]);

  useEffect(() => {
    if (open) {
      const instanceId = modalInstanceId.current;
      if (openModalStack.length === 0) {
        bodyOverflowBeforeFirstModal = document.body.style.overflow;
      }
      openModalStack.push(instanceId);
      previouslyFocusedRef.current = document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
      if (cancelRef.current && !cancelRef.current.disabled) {
        cancelRef.current.focus();
      } else {
        modalRef.current?.focus();
      }
      const handleKeyDown = (event: KeyboardEvent) => {
        if (openModalStack.at(-1) !== instanceId) return;
        if (event.key === "Escape" && !loadingRef.current) {
          event.preventDefault();
          onCancelRef.current();
          return;
        }
        if (event.key !== "Tab") return;
        const focusable = Array.from(modalRef.current?.querySelectorAll<HTMLElement>(
          'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
        ) ?? []);
        if (focusable.length === 0) {
          event.preventDefault();
          modalRef.current?.focus();
          return;
        }
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const activeElement = document.activeElement;
        if (!modalRef.current?.contains(activeElement) || activeElement === modalRef.current) {
          event.preventDefault();
          (event.shiftKey ? last : first).focus();
        } else if (event.shiftKey && activeElement === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      };
      document.addEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "hidden";
      return () => {
        document.removeEventListener("keydown", handleKeyDown);
        const stackIndex = openModalStack.lastIndexOf(instanceId);
        if (stackIndex >= 0) openModalStack.splice(stackIndex, 1);
        if (openModalStack.length === 0) {
          document.body.style.overflow = bodyOverflowBeforeFirstModal;
        }
        if (previouslyFocusedRef.current?.isConnected) {
          previouslyFocusedRef.current.focus();
        }
      };
    }
  }, [open]);

  if (!open) return null;

  const iconColor =
    variant === "danger"
      ? "text-red-500"
      : variant === "warning"
        ? "text-amber-500"
        : "text-[var(--muted-foreground)]";

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
        ref={modalRef}
        role={dialogRole}
        aria-modal="true"
        aria-busy={loading}
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
        className="relative bg-white dark:bg-neutral-900 rounded-xl shadow-xl border border-gray-200 dark:border-neutral-700 w-full max-w-md mx-4 p-6 animate-slide-down"
      >
        <div className="flex items-start gap-4">
          <div className={`shrink-0 p-2 rounded-full bg-gray-100 dark:bg-neutral-800 ${iconColor}`}>
            <AlertTriangle className="w-5 h-5" />
          </div>
          <div className="flex-1 min-w-0">
            <h2
              id={titleId}
              className="text-base font-semibold text-gray-900 dark:text-gray-100"
            >
              {title}
            </h2>
            <p
              id={descriptionId}
              className="text-sm text-[var(--muted-foreground)] mt-1"
            >
              {description}
            </p>
          </div>
        </div>
        {children && <div className="mt-5">{children}</div>}
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
            isDisabled={loading || confirmDisabled}
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
