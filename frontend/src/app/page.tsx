"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  Button,
} from "@heroui/react";
import {
  Plus,
  AlertCircle,
} from "lucide-react";
import {
  getCompanies,
  getProductionReadinessReport,
  getUpcomingDeadline,
  type Company,
  type FilingDeadline,
  type ProductionReadinessReport,
} from "@/lib/api";
import { DashboardSkeleton } from "@/components/Skeleton";
import { useAuth } from "@/components/AuthProvider";
import { ProductionReadinessPanel } from "@/components/ProductionReadinessPanel";
import { AccountantDashboardQueue } from "@/components/dashboard/AccountantDashboardQueue";
import { DashboardCompanyDirectory } from "@/components/dashboard/DashboardCompanyDirectory";
import { DashboardPracticeSummary } from "@/components/dashboard/DashboardPracticeSummary";

export default function Dashboard() {
  const { isOwner } = useAuth();
  const [companies, setCompanies] = useState<Company[]>([]);
  const [deadlines, setDeadlines] = useState<Record<number, FilingDeadline | null>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [readinessReport, setReadinessReport] = useState<ProductionReadinessReport | null>(null);
  const [readinessError, setReadinessError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    async function loadDashboard() {
      try {
        const [data, report] = await Promise.all([
          getCompanies(),
          getProductionReadinessReport().catch((err) => {
            if (active) {
              setReadinessError(err instanceof Error ? err.message : "Failed to load production readiness");
            }
            return null;
          }),
        ]);

        if (!active) return;
        setCompanies(data);
        setReadinessReport(report);
        // Fetch upcoming deadlines for each company
        const deadlineMap: Record<number, FilingDeadline | null> = {};
        await Promise.all(
          data.map(async (c) => {
            try {
              const d = await getUpcomingDeadline(c.id);
              deadlineMap[c.id] = d && "dueDate" in d ? (d as FilingDeadline) : null;
            } catch {
              deadlineMap[c.id] = null;
            }
          })
        );
        if (!active) return;
        setDeadlines(deadlineMap);
      } catch (err) {
        if (active) setError(err instanceof Error ? err.message : "Failed to load companies");
      } finally {
        if (active) setLoading(false);
      }
    }

    loadDashboard();
    return () => {
      active = false;
    };
  }, []);

  if (loading) return <DashboardSkeleton />;

  return (
    <div className="animate-fade-in">
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
            Dashboard
          </h1>
          <p className="text-gray-500 dark:text-gray-400 mt-1">
            Irish Accounts Platform overview
          </p>
        </div>
        {isOwner && (
          <Link href="/companies/new">
            <Button variant="primary" size="sm">
              <Plus className="w-4 h-4" />
              Add Company
            </Button>
          </Link>
        )}
      </div>

      {/* Error state */}
      {error && (
        <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400 mb-6 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          {error}
        </div>
      )}

      <div className="mb-8">
        <ProductionReadinessPanel report={readinessReport} error={readinessError} />
      </div>

      <div className="mb-8">
        <AccountantDashboardQueue
          companies={companies}
          deadlines={deadlines}
          productionReleaseBlockers={readinessReport?.releaseBlockerRegister ?? []}
        />
      </div>

      <div className="mb-8">
        <DashboardPracticeSummary companies={companies} deadlines={deadlines} />
      </div>

      <DashboardCompanyDirectory companies={companies} deadlines={deadlines} isOwner={isOwner} />
    </div>
  );
}
