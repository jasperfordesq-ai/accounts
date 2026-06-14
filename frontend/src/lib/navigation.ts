type ReturnToSearchParams = Pick<URLSearchParams, "get">;

export function normaliseReturnTo(input?: string | null, fallback = "/"): string {
  const candidate = input?.trim() ?? "";
  if (!candidate || !candidate.startsWith("/") || candidate.startsWith("//")) {
    return fallback;
  }

  try {
    const parsed = new URL(candidate, "https://accounts.local");
    const pathname = parsed.pathname || "/";
    if (pathname === "/login" || pathname === "/change-password") {
      return fallback;
    }

    return `${pathname}${parsed.search}${parsed.hash}`;
  } catch {
    return fallback;
  }
}

export function currentPathWithSearch(): string {
  if (typeof window === "undefined") return "/";
  return normaliseReturnTo(`${window.location.pathname}${window.location.search}${window.location.hash}`);
}

export function returnToFromLocation(searchParams: ReturnToSearchParams | null | undefined): string {
  return normaliseReturnTo(searchParams?.get("returnTo"));
}

export function loginRouteForReturnTo(returnTo?: string | null): string {
  return `/login?returnTo=${encodeURIComponent(normaliseReturnTo(returnTo))}`;
}

export function changePasswordRouteForReturnTo(returnTo?: string | null): string {
  return `/change-password?returnTo=${encodeURIComponent(normaliseReturnTo(returnTo))}`;
}
