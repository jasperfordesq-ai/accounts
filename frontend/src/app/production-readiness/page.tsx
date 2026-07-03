"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { AlertCircle, ArrowLeft, RefreshCw } from "lucide-react";
import { Button } from "@heroui/react";
import { SkeletonBlock, SkeletonLine } from "@/components/Skeleton";
import { ProductionReadinessWorkbench } from "@/components/readiness/ProductionReadinessWorkbench";
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
        <ProductionReadinessSkeleton />
      ) : error ? (
        <div className="rounded-md border border-red-200 bg-red-50 p-4 text-red-800 dark:border-red-900 dark:bg-red-950/40 dark:text-red-100">
          <div className="flex items-start gap-3">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
            <div>
              <h1 className="text-base font-semibold">Production readiness could not be loaded</h1>
              <p className="mt-1 text-sm leading-6">{error}</p>
              <Button className="mt-4" variant="secondary" size="sm" onPress={loadReport}>
                <RefreshCw className="h-4 w-4" />
                Retry
              </Button>
            </div>
          </div>
        </div>
      ) : report ? (
        <ProductionReadinessWorkbench report={report} />
      ) : (
        <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4 text-sm text-[var(--muted-foreground)]">
          No production readiness report was returned.
        </div>
      )}
    </div>
  );
}

function ProductionReadinessSkeleton() {
  return (
    <div className="space-y-6" aria-label="Loading production readiness checklist">
      <section className="border-b border-[var(--border)] pb-5">
        <SkeletonLine className="h-7 w-80" />
        <SkeletonLine className="mt-3 h-4 w-full max-w-3xl" />
      </section>
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {[...Array(4)].map((_, index) => (
          <SkeletonBlock key={index} className="h-20 rounded-md" />
        ))}
      </div>
      <SkeletonBlock className="h-72 rounded-md" />
      <div className="grid gap-4 xl:grid-cols-2">
        <SkeletonBlock className="h-64 rounded-md" />
        <SkeletonBlock className="h-64 rounded-md" />
      </div>
    </div>
  );
}
