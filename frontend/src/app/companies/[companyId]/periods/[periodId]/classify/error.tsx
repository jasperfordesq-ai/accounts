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
      title="Classification workspace could not be loaded"
      description={error.message || "Refresh the classification workspace before making a statutory size or regime decision."}
      onRetry={reset}
    />
  );
}
