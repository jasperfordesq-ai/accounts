"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { usePathname, useRouter } from "next/navigation";
import { Button } from "@heroui/react";
import { toast } from "sonner";
import {
  ApiError,
  dispatchSessionExpired,
  SESSION_EXPIRED_EVENT,
  type SessionExpiredEventDetail,
} from "@/lib/api";
import {
  getCurrentUser,
  changePassword as changePasswordRequest,
  completeMfaChallenge as completeMfaChallengeRequest,
  isMfaChallenge,
  login as loginRequest,
  logout as logoutRequest,
  reauthenticate as reauthenticateRequest,
  type AuthUser,
  type MfaChallenge,
  type MfaCompletion,
} from "@/lib/auth";
import {
  logoutFailureMessage,
  shouldClearLocalSessionAfterLogout,
} from "@/lib/logoutSession";
import {
  changePasswordRouteForReturnTo,
  currentPathWithSearch,
  loginRouteForReturnTo,
  returnToFromLocation,
} from "@/lib/navigation";
import { PermissionDeniedPanel, WorkbenchLoadingState } from "@/components/workbench";
import { canAccessRoute, permissionsForRole, routePolicyForPath, type RolePermissions } from "@/lib/permissions";

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  revalidating: boolean;
  authServiceError: string | null;
  logoutError: string | null;
  login: (email: string, password: string) => Promise<AuthUser | MfaChallenge>;
  completeMfaChallenge: (challengeToken: string, totpCode?: string, recoveryCode?: string) => Promise<MfaCompletion>;
  reauthenticate: (password: string, totpCode: string) => Promise<AuthUser>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<AuthUser>;
  logout: () => Promise<void>;
  refresh: () => Promise<void>;
  permissions: RolePermissions;
  isOwner: boolean;
  canRead: boolean;
  canCreateCompany: boolean;
  canDeleteCompany: boolean;
  canWriteWorkingPapers: boolean;
  canReadInternalWorkingPapers: boolean;
  canReview: boolean;
  canApprove: boolean;
  canReviewReleaseEvidence: boolean;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function FullPageSpinner() {
  return (
    <div className="min-h-screen bg-[var(--background)] px-4 py-6 text-[var(--foreground)] sm:px-6 lg:px-8">
      <div className="mx-auto max-w-5xl">
        <WorkbenchLoadingState />
      </div>
    </div>
  );
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [revalidating, setRevalidating] = useState(false);
  const [authServiceError, setAuthServiceError] = useState<string | null>(null);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const authTransitionRef = useRef(0);
  const initializedRef = useRef(false);
  const refreshInFlightRef = useRef(false);

  const isLoginPage = pathname === "/login";
  const isPasswordChangePage = pathname === "/change-password";
  const isActionTokenPage = pathname === "/accept-invite" || pathname === "/reset-password";
  const isPublicPage = isLoginPage || isActionTokenPage || pathname === "/about";

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (refreshInFlightRef.current) return;
    refreshInFlightRef.current = true;
    const isInitialCheck = !initializedRef.current;
    const transitionId = ++authTransitionRef.current;
    if (isInitialCheck) setLoading(true);
    else setRevalidating(true);

    try {
      const currentUser = await getCurrentUser(signal);
      if (authTransitionRef.current === transitionId) {
        setUser(currentUser);
        setAuthServiceError(null);
        setLogoutError(null);
      }
    } catch (err) {
      if (signal?.aborted) return;

      if (authTransitionRef.current === transitionId && err instanceof ApiError && err.status === 401) {
        setUser(null);
        setAuthServiceError(null);
        setLogoutError(null);
      } else if (authTransitionRef.current === transitionId) {
        setAuthServiceError("Authentication service is temporarily unavailable. Your current work has been retained.");
      }
    } finally {
      refreshInFlightRef.current = false;
      initializedRef.current = true;
      if (!signal?.aborted && authTransitionRef.current === transitionId) {
        setLoading(false);
        setRevalidating(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);

    return () => controller.abort();
  }, [refresh]);

  useEffect(() => {
    const revalidateVisibleSession = () => {
      if (document.visibilityState === "visible") void refresh();
    };
    window.addEventListener("focus", revalidateVisibleSession);
    document.addEventListener("visibilitychange", revalidateVisibleSession);
    return () => {
      window.removeEventListener("focus", revalidateVisibleSession);
      document.removeEventListener("visibilitychange", revalidateVisibleSession);
    };
  }, [refresh]);

  useEffect(() => {
    function handleSessionExpired(event: Event) {
      const detail = (event as CustomEvent<SessionExpiredEventDetail>).detail;
      const returnTo = detail?.returnTo ?? currentPathWithSearch();
      ++authTransitionRef.current;
      setUser(null);
      setAuthServiceError(null);
      setLogoutError(null);
      setLoading(false);
      setRevalidating(false);
      if (!isPublicPage) {
        router.replace(loginRouteForReturnTo(returnTo));
      }
    }

    window.addEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired);
  }, [isPublicPage, router]);

  useEffect(() => {
    if (loading) return;

    if (!user && !isPublicPage && !authServiceError) {
      router.replace(loginRouteForReturnTo(currentPathWithSearch()));
      return;
    }

    if (user && isLoginPage) {
      const returnTo = returnToFromLocation(
        typeof window === "undefined" ? undefined : new URLSearchParams(window.location.search)
      );
      router.replace(user.mustChangePassword ? changePasswordRouteForReturnTo(returnTo) : returnTo);
      return;
    }

    if (user?.mustChangePassword && !isPasswordChangePage) {
      router.replace(changePasswordRouteForReturnTo(currentPathWithSearch()));
      return;
    }

  }, [authServiceError, isLoginPage, isPasswordChangePage, isPublicPage, loading, router, user]);

  const login = useCallback(async (email: string, password: string) => {
    const transitionId = ++authTransitionRef.current;
    setRevalidating(false);

    try {
      const nextUser = await loginRequest(email, password);
      if (authTransitionRef.current === transitionId && !isMfaChallenge(nextUser)) {
        setUser(nextUser);
        setAuthServiceError(null);
        setLogoutError(null);
      }
      return nextUser;
    } finally {
      if (authTransitionRef.current === transitionId) {
        setLoading(false);
      }
    }
  }, []);

  const completeMfaChallenge = useCallback(async (challengeToken: string, totpCode?: string, recoveryCode?: string) => {
    const transitionId = ++authTransitionRef.current;
    setRevalidating(false);
    try {
      const completion = await completeMfaChallengeRequest(challengeToken, totpCode, recoveryCode);
      if (authTransitionRef.current === transitionId) {
        setUser(completion.user);
        setAuthServiceError(null);
        setLogoutError(null);
      }
      return completion;
    } finally {
      if (authTransitionRef.current === transitionId) setLoading(false);
    }
  }, []);

  const reauthenticate = useCallback(async (password: string, totpCode: string) => {
    const nextUser = await reauthenticateRequest(password, totpCode);
    setUser(nextUser);
    setAuthServiceError(null);
    return nextUser;
  }, []);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    const transitionId = ++authTransitionRef.current;
    setRevalidating(false);
    const returnTo = currentPathWithSearch();

    try {
      const nextUser = await changePasswordRequest(currentPassword, newPassword);
      if (authTransitionRef.current === transitionId) {
        setUser(nextUser);
        setAuthServiceError(null);
        setLogoutError(null);
      }
      return nextUser;
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        dispatchSessionExpired(returnTo);
      }
      throw err;
    } finally {
      if (authTransitionRef.current === transitionId) {
        setLoading(false);
      }
    }
  }, []);

  const logout = useCallback(async () => {
    const transitionId = ++authTransitionRef.current;
    setRevalidating(false);
    setLogoutError(null);

    try {
      await logoutRequest();
      if (authTransitionRef.current === transitionId) {
        setUser(null);
        setLoading(false);
        setAuthServiceError(null);
        setLogoutError(null);
        router.replace("/login");
      }
    } catch (err) {
      if (authTransitionRef.current !== transitionId) return;

      if (shouldClearLocalSessionAfterLogout(err)) {
        setUser(null);
        setLoading(false);
        setAuthServiceError(null);
        setLogoutError(null);
        router.replace("/login");
        return;
      }

      const message = logoutFailureMessage(err);
      setLoading(false);
      setLogoutError(message);
      toast.error(message);
    }
  }, [router]);

  const value = useMemo<AuthContextValue>(
    () => {
      const permissions = permissionsForRole(user?.role);
      return {
        user,
        loading,
        revalidating,
        authServiceError,
        logoutError,
        login,
        completeMfaChallenge,
        reauthenticate,
        changePassword,
        logout,
        refresh,
        permissions,
        isOwner: permissions.canManageUsers,
        canRead: permissions.canRead,
        canCreateCompany: permissions.canCreateCompany,
        canDeleteCompany: permissions.canDeleteCompany,
        canWriteWorkingPapers: permissions.canWriteWorkingPapers,
        canReadInternalWorkingPapers: permissions.canReadInternalWorkingPapers,
        canReview: permissions.canReview,
        canApprove: permissions.canApprove,
        canReviewReleaseEvidence: permissions.canReviewReleaseEvidence,
      };
    },
    [authServiceError, changePassword, completeMfaChallenge, loading, login, logout, logoutError, reauthenticate, refresh, revalidating, user],
  );

  if (!isPublicPage && loading) {
    return (
      <AuthContext.Provider value={value}>
        <FullPageSpinner />
      </AuthContext.Provider>
    );
  }

  if (!isPublicPage && !loading && !user) {
    if (authServiceError) {
      return (
        <AuthContext.Provider value={value}>
          <main className="mx-auto w-full max-w-3xl px-4 py-10 sm:px-6">
            <PermissionDeniedPanel
              title="Authentication service unavailable"
              description="The session could not be checked. The app has not treated this infrastructure failure as a sign-out."
              actions={(
                <Button variant="primary" onPress={() => void refresh()}>
                  Retry session check
                </Button>
              )}
            />
          </main>
        </AuthContext.Provider>
      );
    }
    return (
      <AuthContext.Provider value={value}>
        <FullPageSpinner />
      </AuthContext.Provider>
    );
  }

  if (user?.mustChangePassword && !isPasswordChangePage) {
    return (
      <AuthContext.Provider value={value}>
        <FullPageSpinner />
      </AuthContext.Provider>
    );
  }

  if (!isPublicPage && user && !canAccessRoute(user.role, pathname)) {
    const policy = routePolicyForPath(pathname);
    const isReleaseEvidenceRoute = policy?.id === "production-readiness";
    const isInternalWorkingPaperRoute = policy?.requiredPermission === "canReadInternalWorkingPapers";
    return (
      <AuthContext.Provider value={value}>
        <main className="mx-auto w-full max-w-3xl px-4 py-10 sm:px-6">
          <PermissionDeniedPanel
            title={isReleaseEvidenceRoute
              ? "Release-review permission required"
              : isInternalWorkingPaperRoute
                ? "Internal-working-paper permission required"
                : "Owner permission required"}
            description={isReleaseEvidenceRoute
              ? "Internal production evidence is limited to Owners and explicitly assigned Reviewers."
              : isInternalWorkingPaperRoute
                ? "Internal accountant working papers are limited to Owners, Accountants and explicitly assigned Reviewers."
              : policy?.id === "new-company"
                ? "Company creation is Owner-only. Return to the dashboard to continue with companies already assigned to you."
                : policy?.id === "user-administration"
                  ? "User administration is Owner-only. Ask a firm Owner to change access or company assignments."
                : "Your role does not permit access to this workspace."}
          />
        </main>
      </AuthContext.Provider>
    );
  }

  return (
    <AuthContext.Provider value={value}>
      {authServiceError && user && (
        <div
          role="status"
          aria-live="polite"
          className="no-print border-b border-amber-300 bg-amber-50 px-4 py-2 text-center text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950/70 dark:text-amber-100"
        >
          {authServiceError}{" "}
          <button type="button" className="font-semibold underline underline-offset-2" onClick={() => void refresh()}>
            Retry
          </button>
        </div>
      )}
      {revalidating && <span role="status" className="sr-only">Checking account session</span>}
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return context;
}
