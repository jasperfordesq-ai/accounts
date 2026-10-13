"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Button, Card } from "@heroui/react";
import { Bot, ChevronLeft, ChevronRight, PlugZap, Unplug } from "lucide-react";
import { toast } from "sonner";
import { useAuth } from "@/components/AuthProvider";
import { ConfirmModal } from "@/components/ConfirmModal";
import {
  HorizontalScrollRegion,
  PageShell,
  StatusBadge,
  WorkbenchEmptyState,
  WorkbenchErrorState,
  WorkbenchLoadingState,
} from "@/components/workbench";
import {
  getConnectorActivity,
  getConnectorSessions,
  revokeConnectorSession,
  type ConnectorActivityPage,
  type ConnectorSession,
} from "@/lib/connector";
import { listUsers, type UserAdministrationSummary } from "@/lib/identity";

export const ACTIVITY_PAGE_SIZE = 25;

const selectClass = "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 dark:bg-neutral-950 dark:text-gray-100 sm:w-80";

function formatTimestamp(value: string | null) {
  if (!value) return "Never";
  return new Date(value).toLocaleString("en-IE", { dateStyle: "medium", timeStyle: "short" });
}

function agentName(session: ConnectorSession) {
  return session.deviceLabel?.trim() || "Unnamed agent";
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

/**
 * Connected AI agents for the signed-in person (or, for an Owner, any firm user): what each agent
 * may do, when it was last used, disconnecting it, and the activity log the API keeps for agents.
 */
export function ConnectorAgentsWorkbench() {
  const { user, isOwner } = useAuth();
  const [origin, setOrigin] = useState("");
  const [remoteResource, setRemoteResource] = useState<string | null>(null);
  const [users, setUsers] = useState<UserAdministrationSummary[]>([]);
  const [usersError, setUsersError] = useState<string | null>(null);
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [sessions, setSessions] = useState<ConnectorSession[]>([]);
  const [sessionsLoading, setSessionsLoading] = useState(true);
  const [sessionsError, setSessionsError] = useState<string | null>(null);
  const [activity, setActivity] = useState<ConnectorActivityPage | null>(null);
  const [activityPage, setActivityPage] = useState(1);
  const [activityError, setActivityError] = useState<string | null>(null);
  const [activityLoading, setActivityLoading] = useState(true);
  const [pendingDisconnect, setPendingDisconnect] = useState<ConnectorSession | null>(null);
  const [disconnecting, setDisconnecting] = useState(false);

  // An Owner looking at someone else passes that user's id; everyone else (and an Owner looking at
  // their own agents) uses the signed-in identity the API resolves itself.
  const targetUserId = isOwner && selectedUserId && selectedUserId !== user?.userId ? selectedUserId : null;
  const viewingSelf = targetUserId === null;
  const targetName = viewingSelf
    ? "you"
    : users.find((candidate) => candidate.userId === targetUserId)?.displayName ?? "this user";

  useEffect(() => {
    // The connection URL must be the MCP resource the server issues tokens for (for example the
    // Tailscale origin after `connector enable --origin tailscale`), not whatever address this page
    // was opened on. The browser's own origin is only a fallback when discovery is unavailable.
    let cancelled = false;
    fetch("/.well-known/oauth-protected-resource/mcp", { cache: "no-store" })
      .then((response) => (response.ok ? response.json() : null))
      .then((metadata: unknown) => {
        if (cancelled) return;
        const resource = typeof metadata === "object" && metadata !== null
          ? (metadata as { resource?: unknown }).resource
          : undefined;
        if (typeof resource === "string" && /^https?:\/\/[^/]+\/mcp$/.test(resource)) {
          setRemoteResource(resource);
        } else {
          setOrigin(window.location.origin);
        }
      })
      .catch(() => {
        if (!cancelled) setOrigin(window.location.origin);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!isOwner) return;
    let cancelled = false;
    listUsers()
      .then((next) => { if (!cancelled) setUsers(next); })
      .catch((error: unknown) => { if (!cancelled) setUsersError(errorMessage(error, "Firm users could not be loaded.")); });
    return () => {
      cancelled = true;
    };
  }, [isOwner]);

  // Each load takes a number; only the latest one may touch state. An Owner switching users (or
  // paging) can leave an earlier request in flight, and its late answer must not replace the view
  // of whoever is selected now.
  const sessionsRequest = useRef(0);
  const activityRequest = useRef(0);

  const loadSessions = useCallback(async () => {
    const request = ++sessionsRequest.current;
    setSessionsLoading(true);
    try {
      const next = await getConnectorSessions(targetUserId);
      if (request !== sessionsRequest.current) return;
      setSessions(next);
      setSessionsError(null);
    } catch (error) {
      if (request !== sessionsRequest.current) return;
      setSessionsError(errorMessage(error, "Connected agents could not be loaded."));
    } finally {
      if (request === sessionsRequest.current) setSessionsLoading(false);
    }
  }, [targetUserId]);

  const loadActivity = useCallback(async (page: number) => {
    const request = ++activityRequest.current;
    setActivityLoading(true);
    try {
      const next = await getConnectorActivity({ userId: targetUserId, page, pageSize: ACTIVITY_PAGE_SIZE });
      if (request !== activityRequest.current) return;
      setActivity(next);
      setActivityError(null);
    } catch (error) {
      if (request !== activityRequest.current) return;
      setActivityError(errorMessage(error, "Agent activity could not be loaded."));
    } finally {
      if (request === activityRequest.current) setActivityLoading(false);
    }
  }, [targetUserId]);

  /** Shows another user's agents: the previous user's cards and activity go at once, not on arrival. */
  function selectUser(userId: number | null) {
    const nextTarget = userId && userId !== user?.userId ? userId : null;
    if (nextTarget === targetUserId) {
      setSelectedUserId(userId);
      return;
    }
    sessionsRequest.current += 1;
    activityRequest.current += 1;
    setSelectedUserId(userId);
    setActivityPage(1);
    setSessions([]);
    setSessionsError(null);
    setSessionsLoading(true);
    setActivity(null);
    setActivityError(null);
    setActivityLoading(true);
  }

  useEffect(() => {
    queueMicrotask(() => {
      void loadSessions();
    });
  }, [loadSessions]);

  useEffect(() => {
    queueMicrotask(() => {
      void loadActivity(activityPage);
    });
  }, [loadActivity, activityPage]);

  async function disconnect() {
    if (!pendingDisconnect || disconnecting) return;
    setDisconnecting(true);
    try {
      // The agent belongs to the user on its own card, whoever the picker shows by now.
      const owner = pendingDisconnect.userId === user?.userId ? null : pendingDisconnect.userId;
      await revokeConnectorSession(pendingDisconnect.familyId, owner);
      toast.success(`${agentName(pendingDisconnect)} has been disconnected.`);
      setPendingDisconnect(null);
      await loadSessions();
    } catch (error) {
      toast.error(errorMessage(error, "The agent could not be disconnected."));
    } finally {
      setDisconnecting(false);
    }
  }

  const remoteUrl = remoteResource ?? (origin ? `${origin}/mcp` : "/mcp");

  return (
    <PageShell
      title="Connected agents"
      subtitle="AI agents act as the person who connected them, inside that person's role and company assignments. Disconnect any agent you no longer use."
      backHref="/"
      backLabel="Dashboard"
    >
      <div className="space-y-5">
        {isOwner && (
          <Card>
            <Card.Content>
              <label className="block">
                <span className="mb-1 block text-sm font-medium">Show agents connected by</span>
                <select
                  className={selectClass}
                  value={selectedUserId ?? user?.userId ?? ""}
                  onChange={(event) => selectUser(Number(event.target.value) || null)}
                >
                  {user && <option value={user.userId}>{user.displayName} (you)</option>}
                  {users
                    .filter((candidate) => candidate.userId !== user?.userId)
                    .map((candidate) => (
                      <option key={candidate.userId} value={candidate.userId}>
                        {candidate.displayName} ({candidate.role})
                      </option>
                    ))}
                </select>
              </label>
              {usersError && <p role="alert" className="mt-2 text-sm text-red-700 dark:text-red-300">{usersError}</p>}
            </Card.Content>
          </Card>
        )}

        <section aria-labelledby="connected-agents-heading" className="space-y-3">
          <h2 id="connected-agents-heading" className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            {viewingSelf ? "Your agents" : `Agents connected by ${targetName}`}
          </h2>
          {sessionsLoading && sessions.length === 0 ? (
            <WorkbenchLoadingState title="Loading connected agents" description="Checking which agents hold a live connection." />
          ) : sessionsError ? (
            <WorkbenchErrorState title="Connected agents could not be loaded" description={sessionsError} onRetry={loadSessions} />
          ) : sessions.length === 0 ? (
            <WorkbenchEmptyState
              title="No connected agents"
              description={viewingSelf
                ? "You have not connected an AI agent yet. See below for how to connect one."
                : `${targetName} has no connected agents.`}
            />
          ) : (
            <ul className="grid gap-3 lg:grid-cols-2" aria-label="Connected agents">
              {sessions.map((session) => (
                <li key={session.familyId} className="rounded-lg border border-[var(--border)] bg-[var(--surface)] p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="flex items-center gap-2 font-semibold text-gray-900 dark:text-gray-100">
                        <Bot className="h-4 w-4 shrink-0" aria-hidden="true" />
                        <span className="truncate">{agentName(session)}</span>
                      </p>
                      {session.clientVersion && (
                        <p className="text-xs text-[var(--muted-foreground)]">{session.clientVersion}</p>
                      )}
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      onPress={() => setPendingDisconnect(session)}
                      aria-label={`Disconnect ${agentName(session)}`}
                    >
                      <Unplug className="h-4 w-4" />
                      Disconnect
                    </Button>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <StatusBadge tone={session.accessLevel === "Read" ? "info" : session.accessLevel === "Admin" ? "warn" : "default"}>
                      {session.accessLevel} access
                    </StatusBadge>
                    <StatusBadge tone={session.dataScope === "Full" ? "warn" : "good"}>
                      Personal data {session.dataScope.toLowerCase()}
                    </StatusBadge>
                  </div>
                  <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
                    <dt className="text-[var(--muted-foreground)]">Connected</dt>
                    <dd>{formatTimestamp(session.connectedAtUtc)}</dd>
                    <dt className="text-[var(--muted-foreground)]">Last used</dt>
                    <dd>{formatTimestamp(session.lastUsedAtUtc)}</dd>
                    <dt className="text-[var(--muted-foreground)]">Expires</dt>
                    <dd>{formatTimestamp(session.expiresAtUtc)}</dd>
                  </dl>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section aria-labelledby="agent-activity-heading" className="space-y-3">
          <h2 id="agent-activity-heading" className="text-lg font-semibold text-gray-900 dark:text-gray-100">Agent activity</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Every agent change, every refused request and every read of personal data, newest first.
          </p>
          {activityError ? (
            <WorkbenchErrorState title="Agent activity could not be loaded" description={activityError} onRetry={() => loadActivity(activityPage)} />
          ) : activityLoading && !activity ? (
            <WorkbenchLoadingState title="Loading agent activity" description="Reading the agent activity log." />
          ) : activity && activity.data.length === 0 && activityPage === 1 ? (
            <WorkbenchEmptyState title="No agent activity yet" description="Agent activity appears here once a connected agent makes a change or is refused." />
          ) : activity ? (
            <>
              <HorizontalScrollRegion label="Agent activity table">
                <table className="w-full min-w-[44rem] text-left text-sm" aria-busy={activityLoading}>
                  <caption className="sr-only">Agent activity, page {activity.page}</caption>
                  <thead className="border-b border-[var(--border)] text-xs uppercase text-[var(--muted-foreground)]">
                    <tr>
                      <th scope="col" className="px-3 py-2">When</th>
                      <th scope="col" className="px-3 py-2">Request</th>
                      <th scope="col" className="px-3 py-2">Company</th>
                      <th scope="col" className="px-3 py-2">Result</th>
                      <th scope="col" className="px-3 py-2">Access</th>
                      <th scope="col" className="px-3 py-2">Reason</th>
                    </tr>
                  </thead>
                  <tbody>
                    {activity.data.map((row) => (
                      <tr key={row.id} className="border-b border-[var(--border)] align-top">
                        <td className="whitespace-nowrap px-3 py-2">{formatTimestamp(row.occurredAtUtc)}</td>
                        <td className="px-3 py-2 font-mono text-xs break-all">
                          {row.method} {row.routePattern}
                          {row.resourceId ? ` (${row.resourceId})` : ""}
                        </td>
                        <td className="px-3 py-2">
                          {row.companyId ? <Link className="underline underline-offset-2" href={`/companies/${row.companyId}`}>#{row.companyId}</Link> : "-"}
                        </td>
                        <td className="px-3 py-2">
                          <StatusBadge tone={row.statusCode < 400 ? "good" : row.statusCode === 428 ? "warn" : "bad"}>{row.statusCode}</StatusBadge>
                        </td>
                        <td className="px-3 py-2">{row.accessLevel} / {row.dataScope}</td>
                        <td className="px-3 py-2">{row.reason ?? "-"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </HorizontalScrollRegion>
              <nav aria-label="Agent activity pages" className="flex items-center justify-between gap-3">
                <Button
                  variant="outline"
                  size="sm"
                  isDisabled={activityPage <= 1 || activityLoading}
                  onPress={() => setActivityPage((page) => Math.max(1, page - 1))}
                >
                  <ChevronLeft className="h-4 w-4" />
                  Newer
                </Button>
                <span className="text-sm text-[var(--muted-foreground)]">Page {activity.page}</span>
                <Button
                  variant="outline"
                  size="sm"
                  isDisabled={!activity.hasMore || activityLoading}
                  onPress={() => setActivityPage((page) => page + 1)}
                >
                  Older
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </nav>
            </>
          ) : null}
        </section>

        <Card>
          <Card.Header>
            <div>
              <Card.Title className="flex items-center gap-2"><PlugZap className="h-5 w-5" aria-hidden="true" /> Connect an agent</Card.Title>
              <Card.Description>
                Agents sign in as you and choose an access level when they connect. Irreversible actions always wait for
                your approval on the <Link className="underline underline-offset-2" href="/settings/approvals">Approvals</Link> page.
              </Card.Description>
            </div>
          </Card.Header>
          <Card.Content className="space-y-3 text-sm">
            <div>
              <p className="font-medium">Remote connector (Claude.ai, Claude Code, Codex)</p>
              <p className="text-[var(--muted-foreground)]">Add this address as a remote MCP server. Your agent opens a FilingBridge consent page where you sign in and choose its access.</p>
              <code className="mt-1 block break-all rounded bg-gray-50 p-2 font-mono text-xs dark:bg-neutral-950" data-testid="mcp-remote-url">{remoteUrl}</code>
              <code className="mt-1 block break-all rounded bg-gray-50 p-2 font-mono text-xs dark:bg-neutral-950">claude mcp add --transport http filingbridge {remoteUrl}</code>
            </div>
            <div>
              <p className="font-medium">Command-line connector</p>
              <p className="text-[var(--muted-foreground)]">
                For agents that run tools locally, sign in once in a terminal with the <code className="font-mono">filingbridge-mcp</code> connector
                (<code className="font-mono">filingbridge-mcp connect --base-url {origin || "<this address>"}</code>), then add <code className="font-mono">filingbridge-mcp serve</code> to
                your AI client. See the connector README for options.
              </p>
            </div>
          </Card.Content>
        </Card>
      </div>

      <ConfirmModal
        open={pendingDisconnect !== null}
        title={pendingDisconnect ? `Disconnect ${agentName(pendingDisconnect)}?` : "Disconnect agent?"}
        description="The agent loses access immediately and must be connected again, with a fresh sign-in, before it can act for this account."
        confirmLabel="Disconnect agent"
        variant="danger"
        loading={disconnecting}
        dialogRole="alertdialog"
        onConfirm={() => void disconnect()}
        onCancel={() => { if (!disconnecting) setPendingDisconnect(null); }}
      />
    </PageShell>
  );
}
