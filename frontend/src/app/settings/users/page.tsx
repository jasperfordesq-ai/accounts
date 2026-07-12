"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { Button, Card, Input, Label, Spinner, TextField } from "@heroui/react";
import { KeyRound, RefreshCw, ShieldCheck, UserPlus, Users } from "lucide-react";
import { useAuth } from "@/components/AuthProvider";
import { PageShell, StatusBadge, WorkbenchErrorState, WorkbenchLoadingState } from "@/components/workbench";
import { ConfirmModal } from "@/components/ConfirmModal";
import { ApiError, getCompanies, type Company } from "@/lib/api";
import {
  beginPasswordReset,
  changeUserRole,
  createUser,
  inviteUser,
  listUsers,
  offboardUser,
  revokeUserSessions,
  setUserActive,
  setUserCompanies,
  unlockUser,
  type PlatformRole,
  type UserAdministrationSummary,
} from "@/lib/identity";

const roles: PlatformRole[] = ["Owner", "Accountant", "Reviewer", "Client"];
const inputClass = "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-500/20 dark:bg-neutral-950 dark:text-gray-100";

export default function UserAdministrationPage() {
  const { reauthenticate } = useAuth();
  const [users, setUsers] = useState<UserAdministrationSummary[]>([]);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [assignments, setAssignments] = useState<Record<number, number[]>>({});
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionLink, setActionLink] = useState<{ label: string; value: string; expires: string } | null>(null);
  const [invite, setInvite] = useState({ email: "", displayName: "", role: "Client" as PlatformRole });
  const [createMode, setCreateMode] = useState(false);
  const [temporaryPassword, setTemporaryPassword] = useState("");
  const [reauthPassword, setReauthPassword] = useState("");
  const [reauthCode, setReauthCode] = useState("");
  const [pendingOffboard, setPendingOffboard] = useState<UserAdministrationSummary | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [nextUsers, nextCompanies] = await Promise.all([listUsers(), getCompanies()]);
      setUsers(nextUsers);
      setCompanies(nextCompanies);
      setAssignments(Object.fromEntries(nextUsers.map((user) => [user.userId, user.companyIds])));
    } catch (err) {
      setError(messageFor(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const activeOwners = useMemo(() => users.filter((user) => user.role === "Owner" && user.isActive && !user.offboardedAtUtc).length, [users]);

  async function run(key: string, operation: () => Promise<UserAdministrationSummary>, success: string) {
    setBusy(key); setError(null); setNotice(null); setActionLink(null);
    try {
      const updated = await operation();
      setUsers((current) => current.map((user) => user.userId === updated.userId ? updated : user));
      setAssignments((current) => ({ ...current, [updated.userId]: updated.companyIds }));
      setNotice(success);
    } catch (err) { setError(messageFor(err)); }
    finally { setBusy(null); }
  }

  async function provision(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy("provision"); setError(null); setNotice(null); setActionLink(null);
    try {
      if (createMode) {
        const created = await createUser({ ...invite, temporaryPassword, companyIds: [] });
        setUsers((current) => [...current, created].sort((a, b) => a.displayName.localeCompare(b.displayName)));
        setNotice("User created. Share the temporary password through an approved secure channel; the user must change it at sign-in.");
      } else {
        const result = await inviteUser({ ...invite, companyIds: [] });
        setUsers((current) => [...current, result.user].sort((a, b) => a.displayName.localeCompare(b.displayName)));
        const base = typeof window === "undefined" ? "" : window.location.origin;
        setActionLink({ label: "One-time invitation link", value: `${base}/accept-invite#token=${encodeURIComponent(result.actionToken)}`, expires: result.expiresAtUtc });
      }
      setInvite({ email: "", displayName: "", role: "Client" }); setTemporaryPassword("");
    } catch (err) { setError(messageFor(err)); }
    finally { setBusy(null); }
  }

  async function initiateReset(user: UserAdministrationSummary) {
    setBusy(`reset-${user.userId}`); setError(null); setNotice(null); setActionLink(null);
    try {
      const result = await beginPasswordReset(user.userId);
      setUsers((current) => current.map((item) => item.userId === result.user.userId ? result.user : item));
      const base = typeof window === "undefined" ? "" : window.location.origin;
      setActionLink({ label: `One-time reset link for ${user.displayName}`, value: `${base}/reset-password#token=${encodeURIComponent(result.actionToken)}`, expires: result.expiresAtUtc });
    } catch (err) { setError(messageFor(err)); }
    finally { setBusy(null); }
  }

  async function handleReauthenticate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy("reauth"); setError(null);
    try {
      await reauthenticate(reauthPassword, reauthCode);
      setReauthPassword(""); setReauthCode(""); setNotice("Recent TOTP verification recorded for sensitive administration actions.");
    } catch (err) { setError(messageFor(err)); }
    finally { setBusy(null); }
  }

  return (
    <PageShell title="User administration" subtitle="Provision all firm roles, enforce privileged MFA, manage company scope, and revoke access without database intervention." backHref="/" backLabel="Dashboard">
      {loading ? <WorkbenchLoadingState title="Loading firm users" description="Checking lifecycle state, lockout, MFA and company assignments." /> : (
        <div className="space-y-5">
          {error && <div role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/30 dark:text-red-200">{error}</div>}
          {notice && <div role="status" className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-900 dark:border-emerald-900 dark:bg-emerald-950/30 dark:text-emerald-100">{notice}</div>}
          {actionLink && (
            <div role="status" className="space-y-2 rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
              <p className="font-semibold">{actionLink.label}</p><p>This secret is displayed once and expires {new Date(actionLink.expires).toLocaleString("en-IE")}.</p>
              <code className="block break-all rounded bg-white p-2 font-mono text-xs dark:bg-neutral-950">{actionLink.value}</code>
            </div>
          )}

          <div className="grid gap-5 xl:grid-cols-[1.15fr_0.85fr]">
            <Card><Card.Header><div><Card.Title className="flex items-center gap-2"><UserPlus className="h-5 w-5" /> Provision a user</Card.Title><Card.Description>Invitation is preferred. Direct creation requires a strong temporary password and forces rotation.</Card.Description></div></Card.Header>
              <Card.Content><form className="grid gap-3 sm:grid-cols-2" onSubmit={provision}>
                <TextField><Label>Display name</Label><Input value={invite.displayName} onChange={(event) => setInvite((value) => ({ ...value, displayName: event.target.value }))} required /></TextField>
                <TextField><Label>Email</Label><Input type="email" value={invite.email} onChange={(event) => setInvite((value) => ({ ...value, email: event.target.value }))} required /></TextField>
                <label className="block"><span className="mb-1 block text-sm font-medium">Role</span><select className={inputClass} value={invite.role} onChange={(event) => setInvite((value) => ({ ...value, role: event.target.value as PlatformRole }))}>{roles.map((role) => <option key={role}>{role}</option>)}</select></label>
                <label className="flex items-center gap-2 self-end pb-2 text-sm"><input type="checkbox" className="workbench-checkbox" checked={createMode} onChange={(event) => setCreateMode(event.target.checked)} /> Create directly with temporary password</label>
                {createMode && <TextField><Label>Temporary password</Label><Input type="password" autoComplete="new-password" value={temporaryPassword} onChange={(event) => setTemporaryPassword(event.target.value)} required minLength={20} /></TextField>}
                <Button type="submit" variant="primary" isDisabled={busy === "provision" || (createMode && temporaryPassword.length < 20)}>{busy === "provision" ? <Spinner size="sm" /> : <UserPlus className="h-4 w-4" />}{createMode ? "Create user" : "Create invitation"}</Button>
              </form></Card.Content>
            </Card>

            <Card><Card.Header><div><Card.Title className="flex items-center gap-2"><KeyRound className="h-5 w-5" /> Recent verification</Card.Title><Card.Description>Role changes, offboarding, resets, unlocks and assignment changes require a recent password plus TOTP ceremony.</Card.Description></div></Card.Header>
              <Card.Content><form className="space-y-3" onSubmit={handleReauthenticate}>
                <TextField><Label>Current password</Label><Input type="password" autoComplete="current-password" value={reauthPassword} onChange={(event) => setReauthPassword(event.target.value)} required /></TextField>
                <TextField><Label>Authenticator code</Label><Input inputMode="numeric" autoComplete="one-time-code" value={reauthCode} onChange={(event) => setReauthCode(event.target.value)} required /></TextField>
                <Button type="submit" variant="secondary" isDisabled={busy === "reauth"}>{busy === "reauth" ? <Spinner size="sm" /> : <ShieldCheck className="h-4 w-4" />}Verify for sensitive actions</Button>
              </form></Card.Content>
            </Card>
          </div>

          <div className="flex items-center justify-between"><div><h2 className="flex items-center gap-2 text-lg font-semibold"><Users className="h-5 w-5" /> Firm users</h2><p className="text-sm text-gray-600 dark:text-gray-400">{users.length} accounts · {activeOwners} active Owners</p></div><Button variant="ghost" onPress={() => void load()}><RefreshCw className="h-4 w-4" />Refresh</Button></div>
          {users.length === 0 ? <WorkbenchErrorState title="No users available" description="At least one Owner should exist before this workspace is used." /> : users.map((user) => (
            <Card key={user.userId}><Card.Header><div className="flex w-full flex-wrap items-start justify-between gap-3"><div><Card.Title>{user.displayName}</Card.Title><Card.Description>{user.email}</Card.Description></div><div className="flex flex-wrap gap-2"><StatusBadge tone={user.isActive ? "good" : "bad"}>{user.offboardedAtUtc ? "Offboarded" : user.isActive ? "Active" : "Inactive"}</StatusBadge><StatusBadge tone={user.mfaEnabled ? "good" : AuthServiceRoleRequiresMfa(user.role) ? "warn" : "default"}>{user.mfaEnabled ? "MFA enabled" : AuthServiceRoleRequiresMfa(user.role) ? "MFA enrollment due" : "MFA optional"}</StatusBadge>{user.isLocked && <StatusBadge tone="bad">Locked</StatusBadge>}</div></div></Card.Header>
              <Card.Content className="space-y-4">
                <div className="grid gap-3 lg:grid-cols-[220px_1fr]">
                  <label><span className="mb-1 block text-sm font-medium">Role</span><select aria-label={`Role for ${user.displayName}`} className={inputClass} value={user.role} disabled={Boolean(user.offboardedAtUtc) || busy !== null} onChange={(event) => void run(`role-${user.userId}`, () => changeUserRole(user.userId, event.target.value as PlatformRole), "Role changed and previous sessions revoked.")}>{roles.map((role) => <option key={role}>{role}</option>)}</select></label>
                  <fieldset><legend className="mb-1 text-sm font-medium">Company assignments</legend><div className="flex flex-wrap gap-x-4 gap-y-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-700">{companies.map((company) => <label key={company.id} className="flex items-center gap-2 text-sm"><input type="checkbox" className="workbench-checkbox" checked={(assignments[user.userId] ?? []).includes(company.id)} disabled={Boolean(user.offboardedAtUtc)} onChange={(event) => setAssignments((current) => ({ ...current, [user.userId]: event.target.checked ? [...(current[user.userId] ?? []), company.id] : (current[user.userId] ?? []).filter((id) => id !== company.id) }))} />{company.legalName}</label>)}</div><Button className="mt-2" size="sm" variant="secondary" isDisabled={Boolean(user.offboardedAtUtc) || busy !== null} onPress={() => void run(`companies-${user.userId}`, () => setUserCompanies(user.userId, assignments[user.userId] ?? []), "Company assignments changed and previous sessions revoked.")}>Save assignments</Button></fieldset>
                </div>
                <div className="flex flex-wrap gap-2 border-t border-gray-200 pt-3 dark:border-neutral-700">
                  {user.isLocked && <Button size="sm" variant="secondary" onPress={() => void run(`unlock-${user.userId}`, () => unlockUser(user.userId), "Account unlocked.")}>Unlock</Button>}
                  {!user.offboardedAtUtc && <Button size="sm" variant="secondary" onPress={() => void run(`active-${user.userId}`, () => setUserActive(user.userId, !user.isActive), user.isActive ? "Account deactivated and sessions revoked." : "Account activated; MFA and role gates still apply.")}>{user.isActive ? "Deactivate" : "Activate"}</Button>}
                  {!user.offboardedAtUtc && <Button size="sm" variant="secondary" onPress={() => void initiateReset(user)}>Create reset link</Button>}
                  {!user.offboardedAtUtc && <Button size="sm" variant="secondary" onPress={() => void run(`revoke-${user.userId}`, () => revokeUserSessions(user.userId), "All existing sessions revoked.")}>Revoke sessions</Button>}
                  {!user.offboardedAtUtc && <Button size="sm" variant="danger" onPress={() => setPendingOffboard(user)}>Offboard</Button>}
                </div>
              </Card.Content>
            </Card>
          ))}
        </div>
      )}
      <ConfirmModal
        open={pendingOffboard !== null}
        title="Offboard this user?"
        description={pendingOffboard ? `${pendingOffboard.displayName} will lose all company assignments, action tokens and sessions. This lifecycle action cannot be reversed.` : ""}
        confirmLabel="Offboard user"
        variant="danger"
        dialogRole="alertdialog"
        loading={pendingOffboard !== null && busy === `offboard-${pendingOffboard.userId}`}
        onCancel={() => setPendingOffboard(null)}
        onConfirm={() => {
          if (!pendingOffboard) return;
          const target = pendingOffboard;
          void run(`offboard-${target.userId}`, () => offboardUser(target.userId), "Account offboarded; assignments, tokens and sessions revoked.")
            .then(() => setPendingOffboard(null));
        }}
      />
    </PageShell>
  );
}

function AuthServiceRoleRequiresMfa(role: PlatformRole) { return role === "Owner" || role === "Accountant" || role === "Reviewer"; }

function messageFor(error: unknown): string {
  if (error instanceof ApiError && error.status === 428) return "This action needs recent TOTP verification. Complete the Recent verification panel and retry.";
  if (error instanceof ApiError && error.status === 403) return "Owner access is required for user administration.";
  return error instanceof Error ? error.message : "The identity operation could not be completed.";
}
