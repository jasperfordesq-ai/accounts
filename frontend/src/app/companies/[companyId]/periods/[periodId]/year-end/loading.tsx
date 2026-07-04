import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Year-end workspace"
      description="Preparing accruals, prepayments, loans, payroll, tax, dividends and review confirmations."
    />
  );
}
