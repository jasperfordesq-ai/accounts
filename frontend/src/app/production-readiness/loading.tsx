import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Production readiness"
      description="Preparing source-law evidence, golden corpus coverage, visual QA status and operational gates."
    />
  );
}
