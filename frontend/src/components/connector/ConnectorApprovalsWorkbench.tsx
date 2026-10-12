"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Button, Input, Label, TextField } from "@heroui/react";
import { RefreshCw, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { ConfirmModal } from "@/components/ConfirmModal";
import {
  PageShell,
  StatusBadge,
  WorkbenchEmptyState,
  WorkbenchErrorState,
  WorkbenchLoadingState,
} from "@/components/workbench";
import { ApiError } from "@/lib/api";
import {
  apiErrorDetail,
  approveConnectorAction,
  getConnectorApprovals,
  type ConnectorApproval,
} from "@/lib/connector";

export const APPROVALS_REFRESH_INTERVAL_MS = 10_000;

function remainingLabel(expiresAtUtc: string, now: number) {
  const remainingMs = Date.parse(expiresAtUtc) - now;
  if (remainingMs <= 0) return "Expired";
  const totalSeconds = Math.floor(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `Expires in ${minutes}:${String(seconds).padStart(2, "0")}`;
}

function loadErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Pending approvals could not be loaded.";
}

/**
 * Web counterpart of `filingbridge-mcp approve`: lists the irreversible agent actions waiting for
 * the signed-in person and grants one at a time with their password and authenticator code. The
 * approval binds to the exact request the agent made; the agent then repeats the call itself.
 */
export function ConnectorApprovalsWorkbench() {
  const [approvals, setApprovals] = useState<ConnectorApproval[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [highlightId, setHighlightId] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [pending, setPending] = useState<ConnectorApproval | null>(null);
  const [password, setPassword] = useState("");
  const [totpCode, setTotpCode] = useState("");
  const [granting, setGranting] = useState(false);
  const [grantError, setGrantError] = useState<string | null>(null);
  const highlightedRowRef = useRef<HTMLLIElement | null>(null);
  const scrolledToHighlight = useRef(false);

  const load = useCallback(async (initial = false) => {
    if (initial) setLoading(true);
    try {
      const next = await getConnectorApprovals();
      setApprovals(next);
      setLoadError(null);
    } catch (error) {
      setLoadError(loadErrorMessage(error));
    } finally {
      if (initial) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const id = new URLSearchParams(window.location.search).get("id");
    queueMicrotask(() => setHighlightId(id ? id.toLowerCase() : null));
    void load(true);
    const refresh = window.setInterval(() => void load(), APPROVALS_REFRESH_INTERVAL_MS);
    const tick = window.setInterval(() => setNow(Date.now()), 1000);
    return () => {
      window.clearInterval(refresh);
      window.clearInterval(tick);
    };
  }, [load]);

  useEffect(() => {
    if (scrolledToHighlight.current || !highlightedRowRef.current) return;
    scrolledToHighlight.current = true;
    highlightedRowRef.current.scrollIntoView({ block: "center" });
  }, [approvals, highlightId]);

  function openApproval(approval: ConnectorApproval) {
    setPending(approval);
    setPassword("");
    setTotpCode("");
    setGrantError(null);
  }

  function closeApproval() {
    if (granting) return;
    setPending(null);
    setPassword("");
    setTotpCode("");
    setGrantError(null);
  }

  async function grant() {
    if (!pending || granting) return;
    setGranting(true);
    setGrantError(null);
    try {
      const result = await approveConnectorAction(pending.approvalId, password, totpCode.trim());
      toast.success(`Approved: ${result.summary}. The agent can now repeat the action.`);
      setPending(null);
      setPassword("");
      setTotpCode("");
      await load();
    } catch (error) {
      const detail = apiErrorDetail(error);
      setGrantError(
        detail.message
          ?? (error instanceof ApiError && error.status === 429
            ? "Too many approval attempts. Wait a minute, then try again."
            : error instanceof Error ? error.message : "The approval was not granted."),
      );
      setPassword("");
    } finally {
      setGranting(false);
    }
  }

  const highlightMissing = Boolean(highlightId)
    && !loading
    && !loadError
    && !approvals.some((approval) => approval.approvalId.toLowerCase() === highlightId);

  return (
    <PageShell
      title="Agent approvals"
      subtitle="Irreversible actions requested by your connected AI agents wait here until you approve them with your password and authenticator code."
      backHref="/"
      backLabel="Dashboard"
      actions={(
        <Button variant="outline" size="sm" onPress={() => void load()}>
          <RefreshCw className="h-4 w-4" />
          Refresh
        </Button>
      )}
    >
      {loading ? (
        <WorkbenchLoadingState title="Loading approvals" description="Checking for agent actions waiting for you." />
      ) : loadError ? (
        <WorkbenchErrorState title="Approvals could not be loaded" description={loadError} onRetry={() => load(true)} />
      ) : (
        <div className="space-y-4">
          {highlightMissing && (
            <div role="status" className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
              The linked approval is no longer waiting. It may have expired, already been used, or belong to a
              different account. Ask the agent to try the action again for a fresh approval.
            </div>
          )}
          {approvals.length === 0 ? (
            <WorkbenchEmptyState
              title="No approvals waiting"
              description="Approvals come from AI agents you have connected to FilingBridge. When an agent asks to delete a record, approve a filing step or make another irreversible change, the request appears here (and the agent gives you a link to it). This list refreshes every 10 seconds."
            />
          ) : (
            <ul className="space-y-3" aria-label="Pending agent approvals">
              {approvals.map((approval) => {
                const highlighted = approval.approvalId.toLowerCase() === highlightId;
                const expired = Date.parse(approval.expiresAtUtc) <= now;
                return (
                  <li
                    key={approval.approvalId}
                    ref={highlighted ? highlightedRowRef : undefined}
                    aria-current={highlighted ? "true" : undefined}
                    data-highlighted={highlighted ? "true" : undefined}
                    className={`rounded-lg border p-4 ${highlighted
                      ? "border-emerald-500 bg-emerald-50 ring-2 ring-emerald-500/30 dark:bg-emerald-950/30"
                      : "border-[var(--border)] bg-[var(--surface)]"}`}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                      <div className="min-w-0 space-y-1">
                        <p className="font-semibold text-gray-900 dark:text-gray-100">{approval.summary}</p>
                        <p className="break-all font-mono text-xs text-[var(--muted-foreground)]">
                          {approval.method} {approval.routePattern}
                          {approval.resourceId ? ` (${approval.resourceId})` : ""}
                        </p>
                        <div className="flex flex-wrap items-center gap-2 pt-1 text-sm">
                          {approval.approvedAtUtc
                            ? <StatusBadge tone="good">Approved, waiting for the agent</StatusBadge>
                            : <StatusBadge tone={expired ? "bad" : "warn"}>{expired ? "Expired" : "Waiting for you"}</StatusBadge>}
                          <span className="text-[var(--muted-foreground)]" aria-live="off">
                            {remainingLabel(approval.expiresAtUtc, now)}
                          </span>
                        </div>
                      </div>
                      {!approval.approvedAtUtc && (
                        <Button
                          variant="primary"
                          size="sm"
                          isDisabled={expired}
                          onPress={() => openApproval(approval)}
                          aria-label={`Approve ${approval.summary}`}
                        >
                          <ShieldCheck className="h-4 w-4" />
                          Approve
                        </Button>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}

      <ConfirmModal
        open={pending !== null}
        title="Approve this agent action?"
        description={pending ? `${pending.summary}. The approval covers this exact request once and expires in a few minutes.` : ""}
        confirmLabel="Approve action"
        variant="warning"
        loading={granting}
        confirmDisabled={!password || !totpCode.trim()}
        onConfirm={() => void grant()}
        onCancel={closeApproval}
      >
        <form
          className="space-y-3"
          onSubmit={(event) => {
            event.preventDefault();
            if (password && totpCode.trim()) void grant();
          }}
        >
          {grantError && (
            <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
              {grantError}
            </div>
          )}
          <TextField fullWidth>
            <Label>Password</Label>
            <Input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} disabled={granting} required />
          </TextField>
          <TextField fullWidth>
            <Label>Authenticator code</Label>
            <Input inputMode="numeric" autoComplete="one-time-code" value={totpCode} onChange={(event) => setTotpCode(event.target.value)} disabled={granting} required />
          </TextField>
          <button type="submit" className="sr-only" tabIndex={-1} aria-hidden="true">Submit approval</button>
        </form>
      </ConfirmModal>
    </PageShell>
  );
}
