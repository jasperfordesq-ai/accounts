"use client";

import { Button, Card } from "@heroui/react";
import { ArrowRight, ClipboardList, Eye, Heart } from "lucide-react";
import Link from "next/link";

import type { ReadinessScore } from "@/lib/api";

import { StatementsReadinessPanel } from "@/components/period/StatementsReadinessPanel";

interface PeriodStatementsWorkspaceProps {
  readiness: ReadinessScore | null;
  statementsHref: string;
  notesHref: string;
  charityHref: string;
  isCharity: boolean;
}

export function PeriodStatementsWorkspace({
  readiness,
  statementsHref,
  notesHref,
  charityHref,
  isCharity,
}: PeriodStatementsWorkspaceProps) {
  return (
    <div className="space-y-6">
      <StatementsReadinessPanel readiness={readiness} />

      <WorkflowLinkCard
        tone="blue"
        icon={<Eye className="h-5 w-5 text-blue-600 dark:text-blue-400" />}
        title="View Financial Statements"
        description="Preview trial balance, P&L, balance sheet, and tax computation."
        href={statementsHref}
        actionLabel="View Statements"
      />

      <WorkflowLinkCard
        tone="purple"
        icon={<ClipboardList className="h-5 w-5 text-purple-600 dark:text-purple-400" />}
        title="Manage Notes"
        description="Generate, edit, and manage notes to the financial statements."
        href={notesHref}
        actionLabel="Manage Notes"
      />

      {isCharity && (
        <WorkflowLinkCard
          tone="green"
          icon={<Heart className="h-5 w-5 text-green-600 dark:text-green-400" />}
          title="Charity Reporting (SoFA)"
          description="Statement of Financial Activities, fund accounting, and Trustees' Annual Report."
          href={charityHref}
          actionLabel="Open Charity Reporting"
        />
      )}
    </div>
  );
}

function WorkflowLinkCard({
  tone,
  icon,
  title,
  description,
  href,
  actionLabel,
}: {
  tone: "blue" | "purple" | "green";
  icon: React.ReactNode;
  title: string;
  description: string;
  href: string;
  actionLabel: string;
}) {
  const toneClassName = {
    blue: "border-blue-200 bg-blue-50/30 dark:border-blue-800 dark:bg-blue-900/10",
    purple: "border-purple-200 bg-purple-50/30 dark:border-purple-800 dark:bg-purple-900/10",
    green: "border-green-200 bg-green-50/30 dark:border-green-800 dark:bg-green-900/10",
  }[tone];

  return (
    <Card className={`shadow-sm ${toneClassName}`}>
      <Card.Content className="p-4">
        <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
          <div className="flex items-center gap-3">
            {icon}
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">{title}</h3>
              <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">{description}</p>
            </div>
          </div>
          <Link href={href}>
            <Button variant="outline" size="sm">
              {actionLabel}
              <ArrowRight className="ml-1.5 h-4 w-4" />
            </Button>
          </Link>
        </div>
      </Card.Content>
    </Card>
  );
}
