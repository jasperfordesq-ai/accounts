import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Statements workspace"
      description="Preparing trial balance, profit and loss, balance sheet, tax computation and readiness checks."
    />
  );
}
