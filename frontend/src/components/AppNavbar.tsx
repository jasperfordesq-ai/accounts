"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Button } from "@heroui/react";
import { Building2, LayoutDashboard, LogOut, Menu, Plus, UserCircle, X } from "lucide-react";
import { useAuth } from "@/components/AuthProvider";
import { ThemeToggle } from "./ThemeToggle";

export function AppNavbar() {
  const pathname = usePathname();
  const { user, isOwner, logout, logoutError } = useAuth();
  const [mobileOpen, setMobileOpen] = useState(false);

  const isActive = (path: string) => pathname === path;

  if (pathname === "/login" || pathname === "/change-password") return null;

  async function handleLogout() {
    setMobileOpen(false);
    await logout();
  }

  return (
    <nav className="bg-white dark:bg-neutral-900 border-b border-gray-200 dark:border-neutral-700 px-6 py-3 no-print">
      <div className="max-w-7xl mx-auto flex items-center justify-between">
        <Link
          href="/"
          className="flex items-center gap-2 text-lg font-semibold text-gray-900 dark:text-gray-100"
        >
          <Building2 className="w-6 h-6 text-emerald-600 dark:text-emerald-400" />
          Irish Accounts
        </Link>

        {/* Desktop nav */}
        <div className="hidden md:flex items-center gap-2">
          <Link href="/">
            <Button
              variant={isActive("/") ? "secondary" : "ghost"}
              size="sm"
              aria-current={isActive("/") ? "page" : undefined}
            >
              <LayoutDashboard className="w-4 h-4" />
              Dashboard
            </Button>
          </Link>
          {isOwner && (
            <Link href="/companies/new">
              <Button
                variant={isActive("/companies/new") ? "secondary" : "primary"}
                size="sm"
                aria-current={isActive("/companies/new") ? "page" : undefined}
              >
                <Plus className="w-4 h-4" />
                New Company
              </Button>
            </Link>
          )}
          {user && (
            <div className="ml-2 flex max-w-xl items-center gap-2 border-l border-gray-200 pl-3 dark:border-neutral-700">
              <UserCircle className="h-5 w-5 shrink-0 text-gray-500 dark:text-gray-400" />
              <div className="min-w-0 leading-tight">
                <div className="truncate text-sm font-medium text-gray-900 dark:text-gray-100">
                  {user.displayName}
                </div>
                <div className="truncate text-xs text-gray-500 dark:text-gray-400">
                  {user.role} / {user.tenantName}
                </div>
              </div>
              {logoutError && (
                <div
                  role="status"
                  aria-live="polite"
                  className="hidden max-w-48 text-xs leading-snug text-red-600 dark:text-red-400 md:block xl:max-w-52"
                >
                  {logoutError}
                </div>
              )}
              <Button
                variant="ghost"
                size="sm"
                isIconOnly
                onPress={handleLogout}
                aria-label="Sign out"
              >
                <LogOut className="h-4 w-4" />
              </Button>
            </div>
          )}
          <div className="ml-1 border-l border-gray-200 dark:border-neutral-700 pl-2">
            <ThemeToggle />
          </div>
        </div>

        {/* Mobile hamburger */}
        <div className="flex md:hidden items-center gap-2">
          <ThemeToggle />
          <Button
            variant="ghost"
            size="sm"
            isIconOnly
            onPress={() => setMobileOpen(!mobileOpen)}
            aria-label={mobileOpen ? "Close menu" : "Open menu"}
            aria-expanded={mobileOpen}
          >
            {mobileOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
          </Button>
        </div>
      </div>

      {logoutError && user && (
        <div
          role="status"
          aria-live="polite"
          className="mx-auto mt-3 max-w-7xl rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs leading-snug text-red-700 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-300 md:hidden"
        >
          {logoutError}
        </div>
      )}

      {/* Mobile menu */}
      {mobileOpen && (
        <div className="md:hidden border-t border-gray-200 dark:border-neutral-700 mt-3 pt-3 pb-1 animate-slide-down">
          <div className="flex flex-col gap-1">
            <Link href="/" onClick={() => setMobileOpen(false)}>
              <Button
                variant={isActive("/") ? "secondary" : "ghost"}
                size="sm"
                className="w-full justify-start"
              >
                <LayoutDashboard className="w-4 h-4" />
                Dashboard
              </Button>
            </Link>
            {isOwner && (
              <Link href="/companies/new" onClick={() => setMobileOpen(false)}>
                <Button
                  variant="primary"
                  size="sm"
                  className="w-full justify-start"
                >
                  <Plus className="w-4 h-4" />
                  New Company
                </Button>
              </Link>
            )}
            {user && (
              <div className="mt-2 border-t border-gray-200 pt-3 dark:border-neutral-700">
                <div className="mb-2 flex items-start gap-2 px-2">
                  <UserCircle className="mt-0.5 h-5 w-5 shrink-0 text-gray-500 dark:text-gray-400" />
                  <div className="min-w-0 leading-tight">
                    <div className="truncate text-sm font-medium text-gray-900 dark:text-gray-100">
                      {user.displayName}
                    </div>
                    <div className="truncate text-xs text-gray-500 dark:text-gray-400">
                      {user.role} / {user.tenantName}
                    </div>
                  </div>
                </div>
                {logoutError && (
                  <div
                    role="status"
                    aria-live="polite"
                    className="mb-2 px-2 text-xs leading-snug text-red-600 dark:text-red-400"
                  >
                    {logoutError}
                  </div>
                )}
                <Button
                  variant="ghost"
                  size="sm"
                  className="w-full justify-start"
                  onPress={handleLogout}
                >
                  <LogOut className="w-4 h-4" />
                  Sign out
                </Button>
              </div>
            )}
          </div>
        </div>
      )}
    </nav>
  );
}
