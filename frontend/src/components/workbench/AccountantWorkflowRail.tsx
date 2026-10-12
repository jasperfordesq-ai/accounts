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
type AccountantWorkflowStageId = "setup" | "import" | "classify" | "year-end" | "statements" | "notes" | "review" | "filing";
type AccountantWorkflowStageOverride = Partial<Pick<WorkflowItem, "detail" | "href" | "state">>;

interface AccountantWorkflowRailProps {
  /**
   * "progress" (default) shows per-stage state and is meant to be driven by `stageOverrides` from real
   * evidence. "guide" lists the stages only, with no completion state, for views that have no single
   * period in focus (firm dashboard, company page).
   */
  mode?: "progress" | "guide";
  activeStage?: AccountantWorkflowStage;
  title?: string;
  stageOverrides?: Partial<Record<AccountantWorkflowStageId, AccountantWorkflowStageOverride>>;
}

export function AccountantWorkflowRail({
  mode = "progress",
  activeStage = "Setup",
  title = "Accounts workflow",
  stageOverrides = {},
}: AccountantWorkflowRailProps) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <p className="mb-3 text-xs leading-5 text-[var(--muted-foreground)]">
        {mode === "guide"
          ? "Open a period to see its progress through these stages."
          : "Start with company setup, then move period work through evidence, statements, review and filing."}
      </p>
      <WorkflowRail
        title={title}
        guide={mode === "guide"}
        items={buildAccountantWorkflowItems(activeStage, mode === "guide" ? {} : stageOverrides)}
      />
    </div>
  );
}

function buildAccountantWorkflowItems(
  activeStage: AccountantWorkflowStage,
  stageOverrides: Partial<Record<AccountantWorkflowStageId, AccountantWorkflowStageOverride>>,
): WorkflowItem[] {
  const activeIndex = accountantWorkflowStages.indexOf(activeStage);
  const applyOverride = (item: WorkflowItem): WorkflowItem => ({
    ...item,
    ...stageOverrides[item.id as AccountantWorkflowStageId],
  });

  return [
    applyOverride({
      id: "setup",
      label: "Setup",
      detail: "Company profile, officers and statutory facts",
      state: stageState(0, activeIndex),
      icon: <Shield className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    }),
    applyOverride({
      id: "import",
      label: "Import",
      detail: "Trial balance and transaction evidence",
      state: stageState(1, activeIndex),
      icon: <Upload className="h-4 w-4 shrink-0 text-sky-600 dark:text-sky-300" />,
    }),
    applyOverride({
      id: "classify",
      label: "Classify",
      detail: "Size, regime, audit and exclusions",
      state: stageState(2, activeIndex),
      icon: <Scale className="h-4 w-4 shrink-0 text-blue-600 dark:text-blue-300" />,
    }),
    applyOverride({
      id: "year-end",
      label: "Year-End",
      detail: "Accruals, tax, directors and checks",
      state: stageState(3, activeIndex),
      icon: <ClipboardList className="h-4 w-4 shrink-0 text-purple-600 dark:text-purple-300" />,
    }),
    applyOverride({
      id: "statements",
      label: "Statements",
      detail: "Primary statements and balances",
      state: stageState(4, activeIndex),
      icon: <FileText className="h-4 w-4 shrink-0 text-cyan-600 dark:text-cyan-300" />,
    }),
    applyOverride({
      id: "notes",
      label: "Notes",
      detail: "Disclosures and statutory wording",
      state: stageState(5, activeIndex),
      icon: <Eye className="h-4 w-4 shrink-0 text-indigo-600 dark:text-indigo-300" />,
    }),
    applyOverride({
      id: "review",
      label: "Review",
      detail: "Evidence, sources and directors' approval",
      state: stageState(6, activeIndex),
      icon: <UserCheck className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    }),
    applyOverride({
      id: "filing",
      label: "Filing",
      detail: "CRO/Revenue pack states and receipts",
      state: stageState(7, activeIndex),
      icon: <Download className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    }),
  ];
}

function stageState(index: number, activeIndex: number): WorkflowItem["state"] {
  if (index < activeIndex) return "done";
  if (index === activeIndex) return "active";
  return "todo";
}
