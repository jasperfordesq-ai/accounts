"use client";

import {
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { ConfirmModal } from "@/components/ConfirmModal";
import {
  UnsavedChangesContext,
  type UnsavedChangesContextValue,
  type UnsavedNavigationTrigger,
} from "@/lib/useUnsavedChanges";

const HISTORY_GUARD_STATE = "__accountsUnsavedChangesGuard";

interface PendingNavigation {
  action: () => void;
  trigger: UnsavedNavigationTrigger;
}

function currentHistoryState(): Record<string, unknown> {
  const state = window.history.state;
  return state && typeof state === "object" ? state as Record<string, unknown> : {};
}

function isNavigatingAnchor(event: MouseEvent): HTMLAnchorElement | null {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
    return null;
  }
  const target = event.target;
  if (!(target instanceof Element)) return null;
  const anchor = target.closest<HTMLAnchorElement>("a[href]");
  if (!anchor || anchor.hasAttribute("download")) return null;
  const targetName = anchor.getAttribute("target");
  if (targetName && targetName.toLowerCase() !== "_self") return null;

  const href = anchor.getAttribute("href");
  if (!href || href.startsWith("#")) return null;
  try {
    const destination = new URL(anchor.href, window.location.href);
    const current = new URL(window.location.href);
    if (destination.href === current.href) return null;
    if (destination.origin === current.origin
      && destination.pathname === current.pathname
      && destination.search === current.search
      && destination.hash !== current.hash) {
      return null;
    }
  } catch {
    return null;
  }
  return anchor;
}

export function UnsavedChangesProvider({ children }: { children: ReactNode }) {
  const guardId = useId();
  const blockers = useRef(new Map<symbol, boolean>());
  const dirtyRef = useRef(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [historyRecentering, setHistoryRecentering] = useState(false);
  const pendingNavigation = useRef<PendingNavigation | null>(null);
  const historyGuardArmed = useRef(false);
  const popContinuation = useRef<(() => void) | null>(null);
  const bypassNextAnchorClick = useRef(false);

  const publishDirtyState = useCallback(() => {
    const next = Array.from(blockers.current.values()).some(Boolean);
    dirtyRef.current = next;
    setHasUnsavedChanges(next);
  }, []);

  const updateBlocker = useCallback((id: symbol, isDirty: boolean) => {
    if (blockers.current.get(id) === isDirty) return;
    blockers.current.set(id, isDirty);
    publishDirtyState();
  }, [publishDirtyState]);

  const removeBlocker = useCallback((id: symbol) => {
    if (!blockers.current.delete(id)) return;
    publishDirtyState();
  }, [publishDirtyState]);

  const currentEntryIsGuard = useCallback(() =>
    currentHistoryState()[HISTORY_GUARD_STATE] === guardId, [guardId]);

  const armHistoryGuard = useCallback(() => {
    if (historyGuardArmed.current || !dirtyRef.current) return;
    window.history.pushState(
      { ...currentHistoryState(), [HISTORY_GUARD_STATE]: guardId },
      "",
      window.location.href,
    );
    historyGuardArmed.current = true;
  }, [guardId]);

  const runAfterHistoryGuardReleased = useCallback((action: () => void) => {
    if (historyGuardArmed.current && currentEntryIsGuard()) {
      historyGuardArmed.current = false;
      popContinuation.current = action;
      window.history.back();
      return;
    }
    historyGuardArmed.current = false;
    action();
  }, [currentEntryIsGuard]);

  const openConfirmation = useCallback((action: () => void, trigger: UnsavedNavigationTrigger) => {
    if (pendingNavigation.current) return false;
    pendingNavigation.current = { action, trigger };
    setDialogOpen(true);
    return false;
  }, []);

  const requestNavigation = useCallback((action: () => void, trigger: UnsavedNavigationTrigger) => {
    if (!dirtyRef.current) {
      action();
      return true;
    }
    return openConfirmation(action, trigger);
  }, [openConfirmation]);

  const navigateAfterSave = useCallback((action: () => void) => {
    pendingNavigation.current = null;
    setDialogOpen(false);
    setHistoryRecentering(false);
    runAfterHistoryGuardReleased(action);
  }, [runAfterHistoryGuardReleased]);

  const cancelNavigation = useCallback(() => {
    const pending = pendingNavigation.current;
    pendingNavigation.current = null;
    setDialogOpen(false);
    setHistoryRecentering(false);

    if (pending?.trigger === "popstate") {
      if (currentEntryIsGuard()) {
        historyGuardArmed.current = true;
      } else {
        popContinuation.current = () => {
          historyGuardArmed.current = true;
        };
        window.history.forward();
      }
    }
  }, [currentEntryIsGuard]);

  const confirmNavigation = useCallback(() => {
    const pending = pendingNavigation.current;
    pendingNavigation.current = null;
    setDialogOpen(false);
    setHistoryRecentering(false);
    if (!pending) return;

    const execute = () => {
      if (pending.trigger === "back" || pending.trigger === "popstate") {
        popContinuation.current = () => undefined;
      }
      pending.action();
    };
    runAfterHistoryGuardReleased(execute);
  }, [runAfterHistoryGuardReleased]);

  useEffect(() => {
    if (hasUnsavedChanges) armHistoryGuard();
    else runAfterHistoryGuardReleased(() => undefined);
  }, [armHistoryGuard, hasUnsavedChanges, runAfterHistoryGuardReleased]);

  useEffect(() => {
    if (!hasUnsavedChanges) return;
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, [hasUnsavedChanges]);

  useEffect(() => {
    const handleClick = (event: MouseEvent) => {
      if (bypassNextAnchorClick.current) {
        bypassNextAnchorClick.current = false;
        return;
      }
      if (!dirtyRef.current) return;
      const anchor = isNavigatingAnchor(event);
      if (!anchor) return;

      event.preventDefault();
      event.stopPropagation();
      requestNavigation(() => {
        if (!anchor.isConnected) return;
        bypassNextAnchorClick.current = true;
        anchor.click();
      }, "link");
    };
    document.addEventListener("click", handleClick, true);
    return () => document.removeEventListener("click", handleClick, true);
  }, [requestNavigation]);

  useEffect(() => {
    const handlePopState = () => {
      const continuation = popContinuation.current;
      if (continuation) {
        popContinuation.current = null;
        queueMicrotask(continuation);
        return;
      }
      if (!dirtyRef.current) {
        historyGuardArmed.current = false;
        return;
      }
      if (pendingNavigation.current) {
        historyGuardArmed.current = false;
        if (currentEntryIsGuard()) {
          historyGuardArmed.current = true;
          setHistoryRecentering(false);
        } else {
          setHistoryRecentering(true);
          popContinuation.current = () => {
            historyGuardArmed.current = true;
            setHistoryRecentering(false);
          };
          window.history.forward();
        }
        return;
      }

      historyGuardArmed.current = false;
      // Return to the sentinel before showing the warning. This keeps repeated
      // browser-Back presses behind the same guard instead of allowing a second
      // popstate to escape while the confirmation is open.
      popContinuation.current = () => {
        historyGuardArmed.current = true;
        setHistoryRecentering(false);
        openConfirmation(() => window.history.back(), "popstate");
      };
      window.history.forward();
    };
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, [currentEntryIsGuard, openConfirmation]);

  const contextValue = useMemo<UnsavedChangesContextValue>(() => ({
    updateBlocker,
    removeBlocker,
    requestNavigation,
    navigateAfterSave,
  }), [navigateAfterSave, removeBlocker, requestNavigation, updateBlocker]);

  return (
    <UnsavedChangesContext.Provider value={contextValue}>
      {children}
      <ConfirmModal
        open={dialogOpen}
        dialogRole="alertdialog"
        title="Leave without saving?"
        description="You have unsaved changes. Stay on this page to keep editing, or leave and discard those drafts."
        confirmLabel="Leave and discard"
        cancelLabel="Stay and keep editing"
        variant="warning"
        loading={historyRecentering}
        onConfirm={confirmNavigation}
        onCancel={cancelNavigation}
      />
    </UnsavedChangesContext.Provider>
  );
}
