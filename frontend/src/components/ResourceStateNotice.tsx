"use client";

import { Button, Spinner } from "@heroui/react";
import { AlertTriangle, RefreshCw } from "lucide-react";
import type { ResourceState } from "@/lib/resourceState";

export function ResourceStateNotice({
  state,
  label,
  onRetry,
  compact = false,
}: {
  state: ResourceState;
  label: string;
  onRetry?: () => void | Promise<void>;
  compact?: boolean;
}) {
  if (state.status === "loaded" || state.status === "empty") return null;

  if (state.status === "loading" || state.status === "stale/retrying") {
    return (
      <div
        className={`flex items-center gap-2 rounded-md border border-sky-200 bg-sky-50 text-sm text-sky-800 dark:border-sky-800 dark:bg-sky-950/30 dark:text-sky-200 ${compact ? "px-3 py-2" : "px-4 py-3"}`}
        role="status"
      >
        <Spinner size="sm" />
        {state.status === "stale/retrying" ? `Refreshing ${label}; retained data may be stale.` : `Loading ${label}…`}
      </div>
    );
  }

  return (
    <div
      className={`flex flex-col gap-3 rounded-md border border-amber-300 bg-amber-50 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100 sm:flex-row sm:items-center sm:justify-between ${compact ? "px-3 py-2" : "px-4 py-3"}`}
      role="alert"
    >
      <div className="flex min-w-0 items-start gap-2">
        <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
        <div>
          <p className="font-semibold">{label} unavailable</p>
          <p className="text-xs leading-5">
            {state.error ?? "Required data could not be loaded."}
            {state.hasRetainedData ? " Retained data remains visible but cannot support a new professional confirmation." : ""}
          </p>
        </div>
      </div>
      {onRetry && (
        <Button variant="outline" size="sm" onPress={onRetry}>
          <RefreshCw className="h-4 w-4" />
          Retry failed resource
        </Button>
      )}
    </div>
  );
}
