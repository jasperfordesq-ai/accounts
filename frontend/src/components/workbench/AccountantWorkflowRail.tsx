import {
  ClipboardList,
  Download,
  Eye,
  FileText,
  Scale,
  Shield,
  Upload,
  UserCheck,
} from "lucide-react";
import { WorkflowRail, type WorkflowItem } from "@/components/workbench";

export const accountantWorkflowStages = [
  "Setup",
  "Import",
  "Classify",
  "Year-End",
  "Statements",
  "Notes",
  "Review",
  "Filing",
] as const;

export type AccountantWorkflowStage = typeof accountantWorkflowStages[number];

interface AccountantWorkflowRailProps {
  activeStage?: AccountantWorkflowStage;
  title?: string;
}

export function AccountantWorkflowRail({
  activeStage = "Setup",
  title = "Accountant Workflow",
}: AccountantWorkflowRailProps) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <p className="mb-3 text-xs leading-5 text-[var(--muted-foreground)]">
        Start with company setup, then move period work through evidence, statements, review and filing.
      </p>
      <WorkflowRail title={title} items={buildAccountantWorkflowItems(activeStage)} />
    </div>
  );
}

function buildAccountantWorkflowItems(activeStage: AccountantWorkflowStage): WorkflowItem[] {
  const activeIndex = accountantWorkflowStages.indexOf(activeStage);

  return [
    {
      id: "setup",
      label: "Setup",
      detail: "Company profile, officers and statutory facts",
      state: stageState(0, activeIndex),
      icon: <Shield className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "import",
      label: "Import",
      detail: "Trial balance and transaction evidence",
      state: stageState(1, activeIndex),
      icon: <Upload className="h-4 w-4 shrink-0 text-sky-600 dark:text-sky-300" />,
    },
    {
      id: "classify",
      label: "Classify",
      detail: "Size, regime, audit and exclusions",
      state: stageState(2, activeIndex),
      icon: <Scale className="h-4 w-4 shrink-0 text-blue-600 dark:text-blue-300" />,
    },
    {
      id: "year-end",
      label: "Year-End",
      detail: "Accruals, tax, directors and checks",
      state: stageState(3, activeIndex),
      icon: <ClipboardList className="h-4 w-4 shrink-0 text-purple-600 dark:text-purple-300" />,
    },
    {
      id: "statements",
      label: "Statements",
      detail: "Primary statements and balances",
      state: stageState(4, activeIndex),
      icon: <FileText className="h-4 w-4 shrink-0 text-cyan-600 dark:text-cyan-300" />,
    },
    {
      id: "notes",
      label: "Notes",
      detail: "Disclosures and statutory wording",
      state: stageState(5, activeIndex),
      icon: <Eye className="h-4 w-4 shrink-0 text-indigo-600 dark:text-indigo-300" />,
    },
    {
      id: "review",
      label: "Review",
      detail: "Evidence, sources and accountant sign-off",
      state: stageState(6, activeIndex),
      icon: <UserCheck className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "filing",
      label: "Filing",
      detail: "CRO/Revenue pack states and receipts",
      state: stageState(7, activeIndex),
      icon: <Download className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
  ];
}

function stageState(index: number, activeIndex: number): WorkflowItem["state"] {
  if (index < activeIndex) return "done";
  if (index === activeIndex) return "active";
  return "todo";
}
