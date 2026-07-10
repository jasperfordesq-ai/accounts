"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
} from "react";
import { useRouter } from "next/navigation";

export type UnsavedNavigationTrigger = "link" | "push" | "replace" | "back" | "popstate";

export interface UnsavedChangesContextValue {
  updateBlocker: (id: symbol, isDirty: boolean) => void;
  removeBlocker: (id: symbol) => void;
  requestNavigation: (action: () => void, trigger: UnsavedNavigationTrigger) => boolean;
  navigateAfterSave: (action: () => void, trigger: UnsavedNavigationTrigger) => void;
}

export const UnsavedChangesContext = createContext<UnsavedChangesContextValue | null>(null);

/**
 * Registers one editable surface with the application-level navigation guard.
 * Multiple mounted editors are supported; navigation remains blocked until every
 * registered surface is clean or the user explicitly confirms discarding drafts.
 */
export function useUnsavedChanges(isDirty: boolean) {
  const context = useContext(UnsavedChangesContext);
  const blockerId = useRef(Symbol("unsaved-changes-blocker"));

  useEffect(() => {
    context?.updateBlocker(blockerId.current, isDirty);
  }, [context, isDirty]);

  useEffect(() => {
    const id = blockerId.current;
    return () => {
      context?.removeBlocker(id);
    };
  }, [context]);
}

/**
 * Guards a user action that will navigate only after it succeeds, such as sign-out.
 * This prevents the action itself from destroying the current draft before the user
 * has accepted the navigation warning.
 */
export function useUnsavedNavigationGuard() {
  const context = useContext(UnsavedChangesContext);
  return useCallback((action: () => void, trigger: UnsavedNavigationTrigger = "push") => {
    if (context) return context.requestNavigation(action, trigger);
    action();
    return true;
  }, [context]);
}

/**
 * App Router wrapper for programmatic navigation. Link navigation is handled by
 * the provider's capture listener; push/replace/back calls must use this wrapper.
 * The *AfterSave variants are deliberately named escape hatches for a mutation
 * that has already persisted or deleted the draft represented by the current page.
 */
export function useGuardedRouter() {
  const router = useRouter();
  const context = useContext(UnsavedChangesContext);

  const request = useCallback((action: () => void, trigger: UnsavedNavigationTrigger) => {
    if (context) return context.requestNavigation(action, trigger);
    action();
    return true;
  }, [context]);

  const afterSave = useCallback((action: () => void, trigger: UnsavedNavigationTrigger) => {
    if (context) context.navigateAfterSave(action, trigger);
    else action();
  }, [context]);

  return useMemo(() => ({
    ...router,
    push: (href: string, options?: Parameters<typeof router.push>[1]) =>
      request(() => options === undefined ? router.push(href) : router.push(href, options), "push"),
    replace: (href: string, options?: Parameters<typeof router.replace>[1]) =>
      request(() => options === undefined ? router.replace(href) : router.replace(href, options), "replace"),
    back: () => request(() => router.back(), "back"),
    pushAfterSave: (href: string, options?: Parameters<typeof router.push>[1]) =>
      afterSave(() => options === undefined ? router.push(href) : router.push(href, options), "push"),
    replaceAfterSave: (href: string, options?: Parameters<typeof router.replace>[1]) =>
      afterSave(() => options === undefined ? router.replace(href) : router.replace(href, options), "replace"),
  }), [afterSave, request, router]);
}
