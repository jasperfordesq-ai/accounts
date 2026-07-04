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
      title="Statements workspace could not be loaded"
      description={error.message || "Refresh the statements workspace before reviewing generated accounts."}
      onRetry={reset}
    />
  );
}
