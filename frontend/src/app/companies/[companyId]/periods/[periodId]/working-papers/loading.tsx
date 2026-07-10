import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading accountant working papers"
      description="Checking the retained candidate-bound schedules, source trails, exceptions, and tax bridge."
    />
  );
}
