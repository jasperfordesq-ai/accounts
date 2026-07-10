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
      title="Accountant working papers could not be loaded"
      description={error.message || "Refresh the internal working-paper workspace before review."}
      onRetry={reset}
    />
  );
}
