"use client";

import { Card, Chip, ProgressBar, ProgressBarFill, ProgressBarTrack } from "@heroui/react";
import { ArrowRight, BarChart3 } from "lucide-react";
import { ActionLink } from "@/components/workbench";

import type { YearEndSummary } from "@/lib/api";

interface PeriodYearEndWorkspaceProps {
  yearEnd: YearEndSummary | null;
  questionnaireHref: string;
}

export function PeriodYearEndWorkspace({ yearEnd, questionnaireHref }: PeriodYearEndWorkspaceProps) {
  return (
    <div className="space-y-6">
      <Card className="shadow-sm border border-emerald-200 dark:border-emerald-800 bg-emerald-50/30 dark:bg-emerald-900/10">
        <Card.Content className="p-5">
          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Year-End Questionnaire</h3>
              <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
                Walk through all 9 sections to capture debtors, creditors, assets, payroll, tax, and more.
              </p>
            </div>
            <ActionLink href={questionnaireHref} variant="primary" size="md">
              Open Year-End Questionnaire
              <ArrowRight className="ml-1.5 h-4 w-4" />
            </ActionLink>
          </div>
        </Card.Content>
      </Card>

      {yearEnd && <YearEndCompletenessCard yearEnd={yearEnd} />}

      <div className="grid gap-4 md:grid-cols-2">
        <SummaryCard
          title="Debtors"
          value={yearEnd ? formatCurrency(yearEnd.debtors.total) : "--"}
          subtitle={yearEnd ? `${yearEnd.debtors.count} entries` : "No data"}
        />
        <SummaryCard
          title="Creditors"
          value={yearEnd ? formatCurrency(yearEnd.creditors.total) : "--"}
          subtitle={yearEnd ? `${yearEnd.creditors.count} entries` : "No data"}
        />
        <SummaryCard
          title="Fixed Assets"
          value={yearEnd ? formatCurrency(yearEnd.fixedAssets.totalCost) : "--"}
          subtitle={yearEnd ? `${yearEnd.fixedAssets.count} assets` : "No data"}
        />
        <SummaryCard
          title="Inventory"
          value={yearEnd ? formatCurrency(yearEnd.inventory.totalValue) : "--"}
          subtitle={yearEnd ? `${yearEnd.inventory.count} items` : "No data"}
        />
        <SummaryCard
          title="Loans"
          value={yearEnd ? formatCurrency(yearEnd.loans.totalBalance) : "--"}
          subtitle={yearEnd ? `${yearEnd.loans.count} loans` : "No data"}
        />
        <SummaryCard
          title="Payroll"
          value={yearEnd?.payroll ? formatCurrency(yearEnd.payroll.grossWages) : "--"}
          subtitle={yearEnd?.payroll ? `${yearEnd.payroll.staffCount} staff` : "Not applicable"}
        />
        <SummaryCard
          title="Tax Liabilities"
          value={yearEnd ? formatCurrency(yearEnd.taxes.totalLiability) : "--"}
          subtitle={yearEnd ? `${yearEnd.taxes.count} items` : "No data"}
        />
        <SummaryCard
          title="Dividends"
          value={yearEnd ? formatCurrency(yearEnd.dividends.total) : "--"}
          subtitle={yearEnd ? `${yearEnd.dividends.count} distributions` : "No data"}
        />
      </div>

      {!yearEnd && (
        <Card className="shadow-sm border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
          <Card.Content className="py-8 text-center">
            <BarChart3 className="mx-auto mb-3 h-10 w-10 text-gray-300 dark:text-gray-600" />
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Year-end summary data is not yet available for this period.
            </p>
            <p className="mt-1 text-xs text-gray-400 dark:text-gray-500">
              Complete the import and categorisation steps first.
            </p>
          </Card.Content>
        </Card>
      )}
    </div>
  );
}

function YearEndCompletenessCard({ yearEnd }: { yearEnd: YearEndSummary }) {
  return (
    <Card className="shadow-sm border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
      <Card.Header>
        <Card.Title className="text-gray-900 dark:text-gray-100">Year-End Completeness</Card.Title>
        <Card.Description>
          {yearEnd.completeness.completed} of {yearEnd.completeness.total} items completed
        </Card.Description>
      </Card.Header>
      <Card.Content>
        <div className="mb-2 flex items-center justify-between text-sm">
          <span className="text-gray-600 dark:text-gray-400">Progress</span>
          <span className="font-medium text-gray-900 dark:text-gray-100">{yearEnd.completeness.score}%</span>
        </div>
        <ProgressBar
          value={yearEnd.completeness.score}
          minValue={0}
          maxValue={100}
          aria-label="Year-end completeness"
          color={yearEnd.completeness.score >= 80 ? "success" : yearEnd.completeness.score >= 50 ? "warning" : "danger"}
        >
          <ProgressBarTrack>
            <ProgressBarFill />
          </ProgressBarTrack>
        </ProgressBar>
        {yearEnd.completeness.incomplete.length > 0 && (
          <div className="mt-4">
            <p className="mb-2 text-xs font-medium text-gray-500 dark:text-gray-400">Incomplete items:</p>
            <div className="flex flex-wrap gap-1.5">
              {yearEnd.completeness.incomplete.map((item) => (
                <Chip key={item} color="warning" variant="soft" size="sm">
                  {item}
                </Chip>
              ))}
            </div>
          </div>
        )}
      </Card.Content>
    </Card>
  );
}

function SummaryCard({ title, value, subtitle }: { title: string; value: string; subtitle: string }) {
  return (
    <Card className="shadow-sm border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
      <Card.Content className="p-4">
        <p className="text-xs font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400">{title}</p>
        <p className="mt-1 text-2xl font-bold text-gray-900 dark:text-gray-100">{value}</p>
        <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">{subtitle}</p>
      </Card.Content>
    </Card>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
