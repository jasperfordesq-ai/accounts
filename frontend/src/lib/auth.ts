import { ApiError } from "@/lib/api";

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
  role: "Owner" | "Accountant" | "Reviewer" | "Client" | string;
  allowedCompanyIds: number[];
  mustChangePassword: boolean;
}

async function readAuthResponse(res: Response): Promise<AuthUser> {
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(res.status, res.statusText, body);
  }

  return res.json();
}

export async function login(email: string, password: string): Promise<AuthUser> {
  const res = await fetch("/api/auth/login", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ email, password }),
  });

  return readAuthResponse(res);
}

export async function logout(): Promise<void> {
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
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<AuthUser> {
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
}

export async function getCurrentUser(signal?: AbortSignal): Promise<AuthUser> {
  const res = await fetch("/api/auth/me", {
    method: "GET",
    credentials: "include",
    signal,
    headers: {
      "Content-Type": "application/json",
    },
  });

  return readAuthResponse(res);
}
