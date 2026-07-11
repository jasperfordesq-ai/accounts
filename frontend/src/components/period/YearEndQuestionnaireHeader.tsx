"use client";

import Link from "next/link";
import { Card, Chip } from "@heroui/react";
import { ArrowLeft } from "lucide-react";

import { Breadcrumbs } from "@/components/Breadcrumbs";

interface YearEndQuestionnaireHeaderProps {
  companyId: number | string;
  periodId: number | string;
  companyName: string;
  periodLabel: string;
  backHref: string;
  completedCount: number;
  totalSections: number;
}

export function YearEndQuestionnaireHeader({
  companyId,
  periodId,
  companyName,
  periodLabel,
  backHref,
  completedCount,
  totalSections,
}: YearEndQuestionnaireHeaderProps) {
  const progressPercent = totalSections > 0
    ? Math.round((completedCount / totalSections) * 100)
    : 0;

  return (
    <>
      <Breadcrumbs
        items={[
          { label: companyName, href: `/companies/${companyId}` },
          { label: periodLabel, href: `/companies/${companyId}/periods/${periodId}` },
          { label: "Year-End" },
        ]}
      />

      <div className="mb-6">
        <Link
          href={backHref}
          className="inline-flex items-center gap-1.5 text-sm text-emerald-700 hover:text-emerald-800 dark:text-emerald-400 dark:hover:text-emerald-300 mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Year-End Questionnaire
        </h1>
        <p className="text-sm text-[var(--muted-foreground)] mt-1">
          {companyName} - {periodLabel}
        </p>
      </div>

      <div className="mb-6">
        <Card className="bg-white dark:bg-neutral-900 shadow-sm border border-gray-200 dark:border-neutral-700">
          <Card.Content className="p-4">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-300">
                Progress
              </span>
              <Chip
                color={completedCount >= totalSections - 2 ? "success" : completedCount >= Math.floor(totalSections / 2) ? "warning" : "default"}
                variant="soft"
                size="sm"
              >
                {completedCount} of {totalSections} sections completed
              </Chip>
            </div>
            <div
              role="progressbar"
              aria-label="Year-end questionnaire progress"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={progressPercent}
              className="w-full bg-gray-200 dark:bg-neutral-700 rounded-full h-2.5"
            >
              <div
                className="bg-emerald-500 h-2.5 rounded-full transition-all"
                style={{ width: `${progressPercent}%` }}
              />
            </div>
          </Card.Content>
        </Card>
      </div>
    </>
  );
}
