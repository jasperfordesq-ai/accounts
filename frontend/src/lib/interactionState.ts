"use client";

import { useCallback, useMemo, useRef, useState } from "react";

export type SearchParamPatch = Record<string, string | number | null | undefined>;

/**
 * Build a same-route URL while preserving unrelated query parameters. Empty values are removed so
 * copied deep links stay compact and default state is not mistaken for an intentional filter.
 */
export function patchSearchHref(
  pathname: string,
  currentSearch: string | URLSearchParams,
  patch: SearchParamPatch,
): string {
  const params = new URLSearchParams(
    typeof currentSearch === "string" ? currentSearch.replace(/^\?/, "") : currentSearch.toString(),
  );

  for (const [key, value] of Object.entries(patch)) {
    if (value === null || value === undefined || value === "") params.delete(key);
    else params.set(key, String(value));
  }

  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}

export function enumSearchParam<T extends string>(
  searchParams: Pick<URLSearchParams, "get">,
  key: string,
  allowed: ReadonlySet<T>,
  fallback: T,
): T {
  const value = searchParams.get(key);
  return value && allowed.has(value as T) ? value as T : fallback;
}

export function positiveIntegerSearchParam(
  searchParams: Pick<URLSearchParams, "get">,
  key: string,
  fallback: number,
  allowed?: ReadonlySet<number>,
): number {
  const raw = searchParams.get(key);
  if (!raw || !/^\d+$/.test(raw)) return fallback;
  const value = Number(raw);
  if (!Number.isSafeInteger(value) || value < 1 || (allowed && !allowed.has(value))) return fallback;
  return value;
}

export function numericIdentifierSearchParam(
  searchParams: Pick<URLSearchParams, "get">,
  key: string,
): string {
  const value = searchParams.get(key)?.trim() ?? "";
  return /^\d+$/.test(value) && Number(value) > 0 ? value : "";
}

/** A request ticket is current only until the next request in the same sequence begins. */
export interface LatestRequestTicket {
  readonly id: number;
  isLatest(): boolean;
}

export interface LatestRequestSequence {
  begin(): LatestRequestTicket;
  invalidate(): void;
}

export function createLatestRequestSequence(): LatestRequestSequence {
  let current = 0;
  return {
    begin() {
      const id = ++current;
      return { id, isLatest: () => id === current };
    },
    invalidate() {
      current += 1;
    },
  };
}

export function useLatestRequestSequence(): LatestRequestSequence {
  const [sequence] = useState(createLatestRequestSequence);
  return sequence;
}

export type InteractionAnnouncementTone = "success" | "error" | "warning";

export interface InteractionAnnouncement {
  id: number;
  message: string;
  tone: InteractionAnnouncementTone;
}

export function useInteractionAnnouncements() {
  const nextId = useRef(0);
  const [announcement, setAnnouncement] = useState<InteractionAnnouncement>({
    id: 0,
    message: "",
    tone: "success",
  });

  const announce = useCallback((tone: InteractionAnnouncementTone, message: string) => {
    setAnnouncement({ id: ++nextId.current, message, tone });
  }, []);

  return useMemo(() => ({ announcement, announce }), [announce, announcement]);
}

export interface InteractionFocusSnapshot {
  restore(): void;
}

type FocusFallback = string | (() => HTMLElement | null) | null | undefined;

/**
 * Capture the initiating control and scroll position before an async mutation. Restoration uses
 * preventScroll and then reapplies the captured viewport so a refreshed row cannot jump the user.
 */
export function captureInteractionFocus(fallback?: FocusFallback): InteractionFocusSnapshot {
  if (typeof document === "undefined") return { restore() {} };

  const activeElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  const scrollX = typeof window !== "undefined" ? window.scrollX : 0;
  const scrollY = typeof window !== "undefined" ? window.scrollY : 0;
  let restored = false;

  return {
    restore() {
      if (restored) return;
      restored = true;

      const run = () => {
        const resolvedFallback = typeof fallback === "function"
          ? fallback()
          : typeof fallback === "string"
            ? document.getElementById(fallback)
            : null;
        const target = isFocusableInteractionTarget(activeElement) ? activeElement : resolvedFallback;
        if (target instanceof HTMLElement) target.focus({ preventScroll: true });
        if (typeof window !== "undefined" && typeof window.scrollTo === "function") {
          try {
            window.scrollTo({ left: scrollX, top: scrollY, behavior: "auto" });
          } catch {
            // Older browsers may not support the options form. Focus is still restored safely.
          }
        }
      };

      if (typeof requestAnimationFrame === "function") requestAnimationFrame(run);
      else setTimeout(run, 0);
    },
  };
}

function isFocusableInteractionTarget(element: HTMLElement | null): element is HTMLElement {
  if (!element?.isConnected) return false;
  if (element.matches(":disabled, [aria-disabled='true'], [inert], [inert] *")) return false;
  return true;
}
