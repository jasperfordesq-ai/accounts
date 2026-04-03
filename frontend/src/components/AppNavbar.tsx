"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Button } from "@heroui/react";
import { Building2, LayoutDashboard, Plus, Menu, X } from "lucide-react";
import { ThemeToggle } from "./ThemeToggle";

export function AppNavbar() {
  const pathname = usePathname();
  const [mobileOpen, setMobileOpen] = useState(false);

  const isActive = (path: string) => pathname === path;

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
          </div>
        </div>
      )}
    </nav>
  );
}
