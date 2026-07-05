"use client";

import { useCallback, useEffect, useState } from "react";
import { ProductionReadinessWorkbench } from "@/components/readiness/ProductionReadinessWorkbench";
import { PageShell, WorkbenchEmptyState, WorkbenchErrorState, WorkbenchLoadingState } from "@/components/workbench";
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
      {loading ? (
        <PageShell
          title="Production Readiness Checklist"
          subtitle="Accountant-facing evidence for statutory rules, golden filing coverage, unsupported paths, operational controls, and professional review gates."
          backHref="/"
          backLabel="Dashboard"
        >
          <WorkbenchLoadingState
            title="Loading production readiness"
            description="Preparing statutory source checks, filing gates and accountant sign-off evidence."
          />
        </PageShell>
      ) : error ? (
        <PageShell
          title="Production Readiness Checklist"
          subtitle="Accountant-facing evidence for statutory rules, golden filing coverage, unsupported paths, operational controls, and professional review gates."
          backHref="/"
          backLabel="Dashboard"
        >
          <WorkbenchErrorState
            title="Production readiness could not be loaded"
            description={error}
            onRetry={loadReport}
          />
        </PageShell>
      ) : report ? (
        <ProductionReadinessWorkbench report={report} />
      ) : (
        <PageShell
          title="Production Readiness Checklist"
          subtitle="Accountant-facing evidence for statutory rules, golden filing coverage, unsupported paths, operational controls, and professional review gates."
          backHref="/"
          backLabel="Dashboard"
        >
          <WorkbenchEmptyState
            title="No production readiness report"
            description="The API returned no report. Run the readiness checks before treating the platform as production-ready."
          />
        </PageShell>
      )}
    </div>
  );
}
