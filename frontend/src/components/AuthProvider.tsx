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
import {
  getCurrentUser,
  login as loginRequest,
  logout as logoutRequest,
  type AuthUser,
} from "@/lib/auth";

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<AuthUser>;
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
  const authTransitionRef = useRef(0);

  const isLoginPage = pathname === "/login";

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const transitionId = ++authTransitionRef.current;
    setLoading(true);

    try {
      const currentUser = await getCurrentUser(signal);
      if (authTransitionRef.current === transitionId) {
        setUser(currentUser);
      }
    } catch {
      if (signal?.aborted) return;

      if (authTransitionRef.current === transitionId) {
        setUser(null);
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
    if (loading) return;

    if (!user && !isLoginPage) {
      router.replace("/login");
      return;
    }

    if (user && isLoginPage) {
      router.replace("/");
    }
  }, [isLoginPage, loading, router, user]);

  const login = useCallback(async (email: string, password: string) => {
    const transitionId = ++authTransitionRef.current;

    try {
      const nextUser = await loginRequest(email, password);
      if (authTransitionRef.current === transitionId) {
        setUser(nextUser);
      }
      return nextUser;
    } finally {
      if (authTransitionRef.current === transitionId) {
        setLoading(false);
      }
    }
  }, []);

  const logout = useCallback(async () => {
    const transitionId = ++authTransitionRef.current;

    try {
      await logoutRequest();
    } catch {
      // The local shell should still clear stale session state if the server session is gone.
    } finally {
      if (authTransitionRef.current === transitionId) {
        setUser(null);
        setLoading(false);
      }
      router.replace("/login");
    }
  }, [router]);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      loading,
      login,
      logout,
      refresh,
      isOwner: roleIs(user, ["Owner"]),
      canWriteWorkingPapers: roleIs(user, ["Owner", "Accountant"]),
      canReview: roleIs(user, ["Owner", "Reviewer"]),
    }),
    [loading, login, logout, refresh, user],
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
