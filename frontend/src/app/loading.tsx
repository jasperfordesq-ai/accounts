import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading accountant workspace"
      description="Preparing statutory evidence, deadlines and filing workflow state."
    />
  );
}
