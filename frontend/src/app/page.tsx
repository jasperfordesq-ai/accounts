"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  Card,
  Button,
} from "@heroui/react";
import {
  Building2,
  Calendar,
  Plus,
  AlertCircle,
  CheckCircle2,
  Clock,
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

  // Compute quick stats from companies
  const totalCompanies = companies.length;
  const totalPeriods = companies.reduce(
    (sum, c) => sum + (c.periodCount || 0),
    0
  );
  const tradingCompanies = companies.filter((c) => c.isTrading).length;
  const dormantCompanies = companies.filter((c) => c.isDormant).length;

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
        <AccountantDashboardQueue companies={companies} deadlines={deadlines} />
      </div>

      {/* Quick Stats Bar */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-emerald-50 dark:bg-emerald-900/30 p-2 rounded-lg">
                <Building2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {totalCompanies}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Companies
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-blue-50 dark:bg-blue-900/30 p-2 rounded-lg">
                <Calendar className="w-5 h-5 text-blue-600 dark:text-blue-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {totalPeriods}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Periods
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-purple-50 dark:bg-purple-900/30 p-2 rounded-lg">
                <CheckCircle2 className="w-5 h-5 text-purple-600 dark:text-purple-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {tradingCompanies}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Trading
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-amber-50 dark:bg-amber-900/30 p-2 rounded-lg">
                <Clock className="w-5 h-5 text-amber-600 dark:text-amber-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {dormantCompanies}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Dormant
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>
      </div>

      <DashboardCompanyDirectory companies={companies} deadlines={deadlines} isOwner={isOwner} />
    </div>
  );
}
