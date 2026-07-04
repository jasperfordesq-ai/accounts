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
      title="Production readiness could not be loaded"
      description={error.message || "Refresh the readiness report before relying on release evidence."}
      onRetry={reset}
    />
  );
}
