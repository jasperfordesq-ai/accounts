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
import { Spinner } from "@heroui/react";
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
  login as loginRequest,
  logout as logoutRequest,
  type AuthUser,
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

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  logoutError: string | null;
  login: (email: string, password: string) => Promise<AuthUser>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<AuthUser>;
  logout: () => Promise<void>;
  refresh: () => Promise<void>;
  isOwner: boolean;
  canWriteWorkingPapers: boolean;
  canReview: boolean;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function roleIs(user: AuthUser | null, roles: string[]) {
  return user ? roles.includes(user.role) : false;
}

function FullPageSpinner() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-[var(--background)] text-[var(--foreground)]">
      <div className="flex items-center gap-3 text-sm text-gray-600 dark:text-gray-300">
        <Spinner size="sm" />
        <span>Loading account session</span>
      </div>
    </div>
  );
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const authTransitionRef = useRef(0);

  const isLoginPage = pathname === "/login";
  const isPasswordChangePage = pathname === "/change-password";

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const transitionId = ++authTransitionRef.current;
    setLoading(true);

    try {
      const currentUser = await getCurrentUser(signal);
      if (authTransitionRef.current === transitionId) {
        setUser(currentUser);
        setLogoutError(null);
      }
    } catch (err) {
      if (signal?.aborted) return;

      if (authTransitionRef.current === transitionId && err instanceof ApiError && err.status === 401) {
        setUser(null);
        setLogoutError(null);
      }
    } finally {
      if (!signal?.aborted && authTransitionRef.current === transitionId) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);

    return () => controller.abort();
  }, [pathname, refresh]);

  useEffect(() => {
    function handleSessionExpired(event: Event) {
      const detail = (event as CustomEvent<SessionExpiredEventDetail>).detail;
      const returnTo = detail?.returnTo ?? currentPathWithSearch();
      ++authTransitionRef.current;
      setUser(null);
      setLogoutError(null);
      setLoading(false);
      if (!isLoginPage) {
        router.replace(loginRouteForReturnTo(returnTo));
      }
    }

    window.addEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired);
  }, [isLoginPage, router]);

  useEffect(() => {
    if (loading) return;

    if (!user && !isLoginPage) {
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

    if (user && !user.mustChangePassword && isPasswordChangePage) {
      router.replace(returnToFromLocation(
        typeof window === "undefined" ? undefined : new URLSearchParams(window.location.search)
      ));
    }
  }, [isLoginPage, isPasswordChangePage, loading, router, user]);

  const login = useCallback(async (email: string, password: string) => {
    const transitionId = ++authTransitionRef.current;

    try {
      const nextUser = await loginRequest(email, password);
      if (authTransitionRef.current === transitionId) {
        setUser(nextUser);
        setLogoutError(null);
      }
      return nextUser;
    } finally {
      if (authTransitionRef.current === transitionId) {
        setLoading(false);
      }
    }
  }, []);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    const transitionId = ++authTransitionRef.current;
    const returnTo = currentPathWithSearch();

    try {
      const nextUser = await changePasswordRequest(currentPassword, newPassword);
      if (authTransitionRef.current === transitionId) {
        setUser(nextUser);
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
    setLogoutError(null);

    try {
      await logoutRequest();
      if (authTransitionRef.current === transitionId) {
        setUser(null);
        setLoading(false);
        setLogoutError(null);
        router.replace("/login");
      }
    } catch (err) {
      if (authTransitionRef.current !== transitionId) return;

      if (shouldClearLocalSessionAfterLogout(err)) {
        setUser(null);
        setLoading(false);
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
    () => ({
      user,
      loading,
      logoutError,
      login,
      changePassword,
      logout,
      refresh,
      isOwner: roleIs(user, ["Owner"]),
      canWriteWorkingPapers: roleIs(user, ["Owner", "Accountant"]),
      canReview: roleIs(user, ["Owner", "Reviewer"]),
    }),
    [changePassword, loading, login, logout, logoutError, refresh, user],
  );

  if (!isLoginPage && loading) {
    return (
      <AuthContext.Provider value={value}>
        <FullPageSpinner />
      </AuthContext.Provider>
    );
  }

  if (!isLoginPage && !loading && !user) {
    return (
      <AuthContext.Provider value={value}>
        <FullPageSpinner />
      </AuthContext.Provider>
    );
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return context;
}
