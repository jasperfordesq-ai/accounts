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
      title="Company workspace could not be loaded"
      description={error.message || "Refresh the company workspace and verify your access before continuing."}
      onRetry={reset}
    />
  );
}
