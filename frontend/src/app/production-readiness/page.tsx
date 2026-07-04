"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ProductionReadinessWorkbench } from "@/components/readiness/ProductionReadinessWorkbench";
import { WorkbenchEmptyState, WorkbenchErrorState, WorkbenchLoadingState } from "@/components/workbench";
import { getProductionReadinessReport, type ProductionReadinessReport } from "@/lib/api";

export default function ProductionReadinessPage() {
  const [report, setReport] = useState<ProductionReadinessReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadReport = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getProductionReadinessReport();
      setReport(data);
    } catch (err) {
      setReport(null);
      setError(err instanceof Error ? err.message : "Failed to load production readiness report");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadReport();
  }, [loadReport]);

  return (
    <div className="animate-fade-in space-y-5">
      <Link
        href="/"
        className="inline-flex items-center gap-2 text-sm font-medium text-[var(--muted-foreground)] hover:text-[var(--foreground)]"
      >
        <ArrowLeft className="h-4 w-4" />
        Dashboard
      </Link>

      {loading ? (
        <WorkbenchLoadingState
          title="Loading production readiness"
          description="Preparing statutory source checks, filing gates and accountant sign-off evidence."
        />
      ) : error ? (
        <WorkbenchErrorState
          title="Production readiness could not be loaded"
          description={error}
          onRetry={loadReport}
        />
      ) : report ? (
        <ProductionReadinessWorkbench report={report} />
      ) : (
        <WorkbenchEmptyState
          title="No production readiness report"
          description="The API returned no report. Run the readiness checks before treating the platform as production-ready."
        />
      )}
    </div>
  );
}
