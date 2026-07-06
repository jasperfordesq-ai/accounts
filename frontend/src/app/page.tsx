"use client";

import { useEffect, useState } from "react";
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
import { DashboardWorkbench } from "@/components/dashboard/DashboardWorkbench";

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
    <DashboardWorkbench
      companies={companies}
      deadlines={deadlines}
      isOwner={isOwner}
      readinessReport={readinessReport}
      readinessError={readinessError}
      error={error}
    />
  );
}
