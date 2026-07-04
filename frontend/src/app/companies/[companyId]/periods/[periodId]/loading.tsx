import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Period workspace"
      description="Preparing workflow status, readiness blockers, accounting evidence and filing actions."
    />
  );
}
