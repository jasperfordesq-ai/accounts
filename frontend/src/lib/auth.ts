import { ApiError } from "@/lib/api";
import {
  ApiContractError,
  parseAuthUser,
  parseMfaChallenge,
  parseMfaCompletion,
} from "@/lib/apiContracts";
import { reportClientMonitoringEvent } from "@/lib/clientMonitoring";

const ACCOUNTS_CSRF_COOKIE = "accounts_csrf";
const CSRF_HEADER = "X-CSRF-Token";

function readCsrfToken(): string | undefined {
  if (typeof document === "undefined") return undefined;

  const prefix = `${ACCOUNTS_CSRF_COOKIE}=`;
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));

  return cookie ? decodeURIComponent(cookie.slice(prefix.length)) : undefined;
}

export interface AuthUser {
  userId: number;
  tenantId: number;
  tenantName: string;
  email: string;
  displayName: string;
  role: "Owner" | "Accountant" | "Reviewer" | "Client";
  allowedCompanyIds: number[];
  mustChangePassword: boolean;
  mfaVerified?: boolean;
  mfaMethod?: "totp" | "recovery" | null;
}

export interface MfaChallenge {
  challengeToken: string;
  requiresEnrollment: boolean;
  expiresAtUtc: string;
  enrollmentSecret: string | null;
  otpAuthUri: string | null;
}

export interface MfaCompletion {
  user: AuthUser;
  recoveryCodes: string[];
}

export function isMfaChallenge(value: AuthUser | MfaChallenge): value is MfaChallenge {
  return "challengeToken" in value;
}

async function readAuthResponse(res: Response): Promise<AuthUser> {
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(res.status, res.statusText, body);
  }

  const payload: unknown = await res.json();
  return parseAuthUser(payload);
}

async function withAuthMonitoring<T>(operation: () => Promise<T>): Promise<T> {
  try {
    return await operation();
  } catch (error) {
    const providerRelevantFailure =
      (error instanceof ApiError && (error.status >= 500 || error.status === 429)) ||
      error instanceof ApiContractError ||
      error instanceof TypeError;
    const wasAborted = typeof DOMException !== "undefined" && error instanceof DOMException && error.name === "AbortError";
    if (providerRelevantFailure && !wasAborted) {
      void reportClientMonitoringEvent("auth-service-unavailable");
    }
    throw error;
  }
}

export async function login(email: string, password: string): Promise<AuthUser | MfaChallenge> {
  return withAuthMonitoring(async () => {
    const res = await fetch("/api/auth/login", {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ email, password }),
    });

    if (res.status === 202) {
      const payload: unknown = await res.json();
      return parseMfaChallenge(payload);
    }
    return readAuthResponse(res);
  });
}

export async function completeMfaChallenge(
  challengeToken: string,
  totpCode?: string,
  recoveryCode?: string,
): Promise<MfaCompletion> {
  return withAuthMonitoring(async () => {
    const res = await fetch("/api/auth/mfa/challenge", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ challengeToken, totpCode: totpCode || null, recoveryCode: recoveryCode || null }),
    });
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new ApiError(res.status, res.statusText, body);
    }
    const payload: unknown = await res.json();
    return parseMfaCompletion(payload);
  });
}

export async function reauthenticate(password: string, totpCode: string): Promise<AuthUser> {
  return withAuthMonitoring(async () => {
    const csrfToken = readCsrfToken();
    const res = await fetch("/api/auth/reauthenticate", {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        ...(csrfToken ? { [CSRF_HEADER]: csrfToken } : {}),
      },
      body: JSON.stringify({ password, totpCode }),
    });
    return readAuthResponse(res);
  });
}

export async function logout(): Promise<void> {
  return withAuthMonitoring(async () => {
    const csrfToken = readCsrfToken();
    const res = await fetch("/api/auth/logout", {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        ...(csrfToken ? { [CSRF_HEADER]: csrfToken } : {}),
      },
    });

    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new ApiError(res.status, res.statusText, body);
    }
  });
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<AuthUser> {
  return withAuthMonitoring(async () => {
    const csrfToken = readCsrfToken();
    const res = await fetch("/api/auth/password", {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        ...(csrfToken ? { [CSRF_HEADER]: csrfToken } : {}),
      },
      body: JSON.stringify({ currentPassword, newPassword }),
    });

    return readAuthResponse(res);
  });
}

export async function getCurrentUser(signal?: AbortSignal): Promise<AuthUser> {
  return withAuthMonitoring(async () => {
    const res = await fetch("/api/auth/me", {
      method: "GET",
      credentials: "include",
      signal,
      headers: {
        "Content-Type": "application/json",
      },
    });

    return readAuthResponse(res);
  });
}
