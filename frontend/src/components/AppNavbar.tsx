"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Button } from "@heroui/react";
import { Building2, LayoutDashboard, Plus } from "lucide-react";

export function AppNavbar() {
  const pathname = usePathname();

  return (
    <nav className="bg-white border-b border-gray-200 px-6 py-3">
      <div className="max-w-7xl mx-auto flex items-center justify-between">
        <Link href="/" className="flex items-center gap-2 text-lg font-semibold text-gray-900">
          <Building2 className="w-6 h-6 text-emerald-600" />
          Irish Accounts
        </Link>
        <div className="flex items-center gap-3">
          <Link href="/">
            <Button
              variant={pathname === "/" ? "secondary" : "ghost"}
              size="sm"
            >
              <LayoutDashboard className="w-4 h-4" />
              Dashboard
            </Button>
          </Link>
          <Link href="/companies/new">
            <Button
              variant="primary"
              size="sm"
            >
              <Plus className="w-4 h-4" />
              New Company
            </Button>
          </Link>
        </div>
      </div>
    </nav>
  );
}
