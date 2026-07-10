"use client";

import { use, useCallback, useEffect, useState } from "react";
import { toast } from "sonner";
import { AccountantWorkingPaperWorkbench } from "@/components/period/AccountantWorkingPaperWorkbench";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import { useAuth } from "@/components/AuthProvider";
import {
  ApiError,
  generateAccountantWorkingPaperPack,
  getAccountantWorkingPaperPack,
  type AccountantWorkingPaperPack,
} from "@/lib/api";

export default function AccountantWorkingPapersPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const { canWriteWorkingPapers, canReadInternalWorkingPapers } = useAuth();
  const canView = canReadInternalWorkingPapers;
  const [pack, setPack] = useState<AccountantWorkingPaperPack | null>(null);
  const [loading, setLoading] = useState(canView);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!canView) {
      setPack(null);
      setError(null);
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      setPack(await getAccountantWorkingPaperPack(Number(companyId), Number(periodId)));
      setError(null);
    } catch (loadError) {
      if (loadError instanceof ApiError && loadError.status === 404) {
        setPack(null);
        setError(null);
      } else {
        setError(loadError instanceof Error ? loadError.message : "The retained working papers could not be loaded.");
      }
    } finally {
      setLoading(false);
    }
  }, [canView, companyId, periodId]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const generate = useCallback(async () => {
    if (!canWriteWorkingPapers) return;
    setGenerating(true);
    try {
      const generated = await generateAccountantWorkingPaperPack(Number(companyId), Number(periodId));
      setPack(generated);
      setError(null);
      toast.success("Retained accountant working-paper pack generated");
    } catch (generationError) {
      const message = generationError instanceof Error
        ? generationError.message
        : "The working-paper pack could not be generated.";
      setError(message);
      toast.error(message);
    } finally {
      setGenerating(false);
    }
  }, [canWriteWorkingPapers, companyId, periodId]);

  if (loading && !pack) return <PeriodWorkspaceSkeleton />;

  return (
    <AccountantWorkingPaperWorkbench
      companyId={companyId}
      periodId={periodId}
      pack={pack}
      canView={canView}
      canGenerate={canWriteWorkingPapers}
      generating={generating}
      error={error}
      onGenerate={generate}
      onRetry={load}
    />
  );
}
