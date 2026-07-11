import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { WorkbenchEmptyState } from "@/components/workbench";

export default function CompanyNotFound() {
  return (
    <WorkbenchEmptyState
      title="Company workspace not found"
      description="The requested company record could not be found or is not available to this workspace."
      actions={
        <Link
          href="/"
          className="inline-flex min-h-10 items-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 text-sm font-semibold text-[var(--foreground)] shadow-sm transition hover:bg-[var(--surface-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
        >
          <ArrowLeft className="h-4 w-4" />
          Return to dashboard
        </Link>
      }
    />
  );
}
