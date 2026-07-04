import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Classification workspace"
      description="Preparing size thresholds, regime eligibility, audit-exemption evidence and source-law references."
    />
  );
}
