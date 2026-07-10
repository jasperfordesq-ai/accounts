"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Button } from "@heroui/react";
import { Building2, KeyRound, LayoutDashboard, LogOut, Menu, Plus, UserCircle, UserCog, X } from "lucide-react";
import { useAuth } from "@/components/AuthProvider";
import { ActionLink } from "@/components/workbench";
import { useUnsavedNavigationGuard } from "@/lib/useUnsavedChanges";
import { ThemeToggle } from "./ThemeToggle";

export function AppNavbar() {
  const pathname = usePathname();
  const { user, canCreateCompany, isOwner, logout, logoutError } = useAuth();
  const [mobileOpen, setMobileOpen] = useState(false);
  const mobileMenuButtonRef = useRef<HTMLButtonElement>(null);
  const guardNavigation = useUnsavedNavigationGuard();

  const isActive = (path: string) => pathname === path;

  function handleLogout() {
    guardNavigation(() => {
      setMobileOpen(false);
      void logout();
    }, "replace");
  }

  useEffect(() => {
    if (!mobileOpen) return;

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setMobileOpen(false);
      mobileMenuButtonRef.current?.focus();
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [mobileOpen]);

  if (["/login", "/change-password", "/accept-invite", "/reset-password"].includes(pathname)) return null;

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
          <ActionLink
            href="/"
            variant={isActive("/") ? "secondary" : "ghost"}
            ariaCurrent={isActive("/") ? "page" : undefined}
          >
            <LayoutDashboard className="w-4 h-4" />
            Dashboard
          </ActionLink>
          {canCreateCompany && (
            <ActionLink
              href="/companies/new"
              variant={isActive("/companies/new") ? "secondary" : "primary"}
              ariaCurrent={isActive("/companies/new") ? "page" : undefined}
            >
              <Plus className="w-4 h-4" />
              New Company
            </ActionLink>
          )}
          {isOwner && (
            <ActionLink
              href="/settings/users"
              variant={isActive("/settings/users") ? "secondary" : "ghost"}
              ariaCurrent={isActive("/settings/users") ? "page" : undefined}
            >
              <UserCog className="h-4 w-4" />
              Users
            </ActionLink>
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
              <ActionLink
                href="/change-password"
                variant={isActive("/change-password") ? "secondary" : "ghost"}
                ariaLabel="Change password"
              >
                <KeyRound className="h-4 w-4" />
                <span className="hidden xl:inline">Password</span>
              </ActionLink>
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
            ref={mobileMenuButtonRef}
            variant="ghost"
            size="sm"
            isIconOnly
            onPress={() => setMobileOpen(!mobileOpen)}
            aria-label={mobileOpen ? "Close menu" : "Open menu"}
            aria-expanded={mobileOpen}
            aria-controls="primary-mobile-navigation"
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
        <div id="primary-mobile-navigation" role="group" aria-label="Mobile primary navigation" className="md:hidden border-t border-gray-200 dark:border-neutral-700 mt-3 pt-3 pb-1 animate-slide-down">
          <div className="flex flex-col gap-1">
            <ActionLink
              href="/"
              variant={isActive("/") ? "secondary" : "ghost"}
              className="w-full justify-start"
              ariaCurrent={isActive("/") ? "page" : undefined}
              onClick={() => setMobileOpen(false)}
            >
              <LayoutDashboard className="w-4 h-4" />
              Dashboard
            </ActionLink>
            {canCreateCompany && (
              <ActionLink
                href="/companies/new"
                variant="primary"
                className="w-full justify-start"
                ariaCurrent={isActive("/companies/new") ? "page" : undefined}
                onClick={() => setMobileOpen(false)}
              >
                <Plus className="w-4 h-4" />
                New Company
              </ActionLink>
            )}
            {isOwner && (
              <ActionLink
                href="/settings/users"
                variant={isActive("/settings/users") ? "secondary" : "ghost"}
                className="w-full justify-start"
                ariaCurrent={isActive("/settings/users") ? "page" : undefined}
                onClick={() => setMobileOpen(false)}
              >
                <UserCog className="h-4 w-4" />
                User administration
              </ActionLink>
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
                <ActionLink
                  href="/change-password"
                  variant="ghost"
                  className="w-full justify-start"
                  onClick={() => setMobileOpen(false)}
                >
                  <KeyRound className="h-4 w-4" />
                  Change password
                </ActionLink>
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
