import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { WorkbenchEmptyState } from "@/components/workbench";

export default function NotFound() {
  return (
    <WorkbenchEmptyState
      title="Workspace not found"
      description="The requested company, accounting period or filing workspace could not be found."
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
