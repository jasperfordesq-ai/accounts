"use client";

import { useEffect } from "react";
import { WorkbenchErrorState } from "@/components/workbench";

export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <WorkbenchErrorState
      title="Charity workspace could not be loaded"
      description={error.message || "Refresh the charity workspace before relying on charity annual-return evidence."}
      onRetry={reset}
    />
  );
}
