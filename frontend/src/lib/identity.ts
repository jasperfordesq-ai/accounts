import { ApiError } from "@/lib/api";
import {
  parseUserAdministrationList,
  parseUserAdministrationSummary,
  parseUserProvisioningResult,
} from "@/lib/apiContracts";

const CSRF_COOKIE = "accounts_csrf";
const CSRF_HEADER = "X-CSRF-Token";

export type PlatformRole = "Owner" | "Accountant" | "Reviewer" | "Client";

export interface UserAdministrationSummary {
  userId: number;
  email: string;
  displayName: string;
  role: PlatformRole;
  isActive: boolean;
  mustChangePassword: boolean;
  isLocked: boolean;
  lockedUntilUtc: string | null;
  mfaEnabled: boolean;
  companyIds: number[];
  inviteAcceptedAtUtc: string | null;
  deactivatedAtUtc: string | null;
  offboardedAtUtc: string | null;
  sessionVersion: number;
}

export interface UserProvisioningResult {
  user: UserAdministrationSummary;
  actionToken: string;
  expiresAtUtc: string;
}

function csrfToken(): string | undefined {
  if (typeof document === "undefined") return undefined;
  const prefix = `${CSRF_COOKIE}=`;
  const item = document.cookie.split(";").map((part) => part.trim()).find((part) => part.startsWith(prefix));
  return item ? decodeURIComponent(item.slice(prefix.length)) : undefined;
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  const token = csrfToken();
  const response = await fetch(path, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { [CSRF_HEADER]: token } : {}),
      ...init?.headers,
    },
  });
  if (!response.ok) {
    const body = await response.text().catch(() => "");
    throw new ApiError(response.status, response.statusText, body);
  }
  return response;
}

export async function listUsers(): Promise<UserAdministrationSummary[]> {
  const response = await request("/api/admin/users");
  return parseUserAdministrationList(await response.json()) as UserAdministrationSummary[];
}

export async function inviteUser(input: { email: string; displayName: string; role: PlatformRole; companyIds: number[] }): Promise<UserProvisioningResult> {
  const response = await request("/api/admin/users/invite", { method: "POST", body: JSON.stringify(input) });
  return parseUserProvisioningResult(await response.json()) as UserProvisioningResult;
}

export async function createUser(input: { email: string; displayName: string; role: PlatformRole; temporaryPassword: string; companyIds: number[] }): Promise<UserAdministrationSummary> {
  const response = await request("/api/admin/users", { method: "POST", body: JSON.stringify(input) });
  return parseUserAdministrationSummary(await response.json()) as UserAdministrationSummary;
}

async function updateUser(path: string, method: "POST" | "PUT", body?: unknown): Promise<UserAdministrationSummary> {
  const response = await request(path, { method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  return parseUserAdministrationSummary(await response.json()) as UserAdministrationSummary;
}

export const setUserActive = (userId: number, active: boolean) => updateUser(`/api/admin/users/${userId}/active`, "PUT", { active });
export const unlockUser = (userId: number) => updateUser(`/api/admin/users/${userId}/unlock`, "POST");
export const changeUserRole = (userId: number, role: PlatformRole) => updateUser(`/api/admin/users/${userId}/role`, "PUT", { role });
export const setUserCompanies = (userId: number, companyIds: number[]) => updateUser(`/api/admin/users/${userId}/companies`, "PUT", { companyIds });
export const revokeUserSessions = (userId: number) => updateUser(`/api/admin/users/${userId}/revoke-sessions`, "POST");
export const offboardUser = (userId: number) => updateUser(`/api/admin/users/${userId}/offboard`, "POST");

export async function beginPasswordReset(userId: number): Promise<UserProvisioningResult> {
  const response = await request(`/api/admin/users/${userId}/password-reset`, { method: "POST" });
  return parseUserProvisioningResult(await response.json()) as UserProvisioningResult;
}

export async function acceptInvitation(token: string, newPassword: string): Promise<void> {
  await request("/api/auth/invitations/accept", { method: "POST", body: JSON.stringify({ token, newPassword }) });
}

export async function completePasswordReset(token: string, newPassword: string): Promise<void> {
  await request("/api/auth/recovery/complete", { method: "POST", body: JSON.stringify({ token, newPassword }) });
}
