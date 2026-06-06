import { ApiError } from "@/lib/api";

export interface AuthUser {
  userId: number;
  tenantId: number;
  tenantName: string;
  email: string;
  displayName: string;
  role: "Owner" | "Accountant" | "Reviewer" | "Client" | string;
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
  const res = await fetch("/api/auth/logout", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(res.status, res.statusText, body);
  }
}

export async function getCurrentUser(): Promise<AuthUser> {
  const res = await fetch("/api/auth/me", {
    method: "GET",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  });

  return readAuthResponse(res);
}
