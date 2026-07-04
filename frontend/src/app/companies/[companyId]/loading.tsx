import { WorkbenchLoadingState } from "@/components/workbench";

export default function Loading() {
  return (
    <WorkbenchLoadingState
      title="Loading Company workspace"
      description="Preparing company profile, officers, charity facts, filing profile and accounting periods."
    />
  );
}
