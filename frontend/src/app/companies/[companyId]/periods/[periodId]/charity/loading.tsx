import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Charity workspace"
      description="Preparing charity profile, SoFA evidence, trustees report status and annual-return workflow state."
    />
  );
}
