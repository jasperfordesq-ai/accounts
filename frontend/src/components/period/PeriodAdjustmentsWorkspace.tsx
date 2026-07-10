"use client";

import { Button, Card, Chip, Spinner } from "@heroui/react";
import { Calculator, CheckCircle2, RefreshCw } from "lucide-react";
import type { ReactNode } from "react";

import type { Adjustment, AdjustmentSummary } from "@/lib/api";
import { ReadOnlyNotice } from "@/components/workbench";

interface PeriodAdjustmentsWorkspaceProps {
  canWrite?: boolean;
  canApprove?: boolean;
  adjustments: Adjustment[];
  adjSummary: AdjustmentSummary | null;
  loadingAdjustments: boolean;
  generatingAdj: boolean;
  approvingId: number | null;
  adjFilterApproved: string;
  adjFilterType: string;
  onGenerateAdjustments: () => void | Promise<void>;
  onRefreshAdjustments: () => void | Promise<void>;
  onApproveAdjustment: (adjustmentId: number) => void | Promise<void>;
  onFilterApprovedChange: (value: string) => void;
  onFilterTypeChange: (value: string) => void;
}

export function PeriodAdjustmentsWorkspace({
  canWrite = false,
  canApprove = false,
  adjustments,
  adjSummary,
  loadingAdjustments,
  generatingAdj,
  approvingId,
  adjFilterApproved,
  adjFilterType,
  onGenerateAdjustments,
  onRefreshAdjustments,
  onApproveAdjustment,
  onFilterApprovedChange,
  onFilterTypeChange,
}: PeriodAdjustmentsWorkspaceProps) {
  return (
    <div className="space-y-6">
      {!canWrite && (
        <ReadOnlyNotice
          subject="period adjustments"
          detail={canApprove
            ? "You can inspect the retained entries and approve pending adjustments; generating or editing entries requires Owner or Accountant access."
            : undefined}
        />
      )}
      <Card className="shadow-sm border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
        <Card.Header>
          <Card.Title className="text-gray-900 dark:text-gray-100">Period Adjustments</Card.Title>
          <Card.Description>
            Generate and review adjustments for depreciation, prepayments, accruals, and other year-end entries
          </Card.Description>
        </Card.Header>
        <Card.Content>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            {canWrite && <Button variant="primary" onPress={onGenerateAdjustments} isDisabled={generatingAdj}>
              {generatingAdj ? (
                <>
                  <Spinner size="sm" className="mr-2" />
                  Generating...
                </>
              ) : (
                <>
                  <Calculator className="mr-1 h-4 w-4" />
                  Generate Adjustments
                </>
              )}
            </Button>}
            <Button variant="ghost" size="sm" onPress={onRefreshAdjustments} isDisabled={loadingAdjustments}>
              <RefreshCw className={`mr-1 h-4 w-4 ${loadingAdjustments ? "animate-spin" : ""}`} />
              Refresh
            </Button>
          </div>
        </Card.Content>
      </Card>

      <AdjustmentSummaryCard adjSummary={adjSummary} />

      <Card className="shadow-sm border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
        <Card.Header>
          <Card.Title className="text-gray-900 dark:text-gray-100">Adjustment Details</Card.Title>
          <Card.Description>
            {adjustments.length} adjustment{adjustments.length !== 1 ? "s" : ""} found
          </Card.Description>
        </Card.Header>
        <Card.Content>
          <AdjustmentFilters
            adjFilterApproved={adjFilterApproved}
            adjFilterType={adjFilterType}
            loadingAdjustments={loadingAdjustments}
            onFilterApprovedChange={onFilterApprovedChange}
            onFilterTypeChange={onFilterTypeChange}
            onRefreshAdjustments={onRefreshAdjustments}
          />

          {loadingAdjustments ? (
            <div className="flex items-center justify-center py-8">
              <Spinner size="sm" />
            </div>
          ) : adjustments.length === 0 ? (
            <div className="py-8 text-center">
              <Calculator className="mx-auto mb-3 h-10 w-10 text-gray-300 dark:text-gray-600" />
              <p className="text-sm text-gray-500 dark:text-gray-400">
                {canWrite
                  ? "No adjustments yet. Use Generate Adjustments to create them."
                  : "No adjustment evidence has been recorded for this period."}
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              {adjustments.map((adjustment) => (
                <AdjustmentReviewCard
                  canApprove={canApprove}
                  key={adjustment.id}
                  adjustment={adjustment}
                  approvingId={approvingId}
                  onApproveAdjustment={onApproveAdjustment}
                />
              ))}
            </div>
          )}
        </Card.Content>
      </Card>
    </div>
  );
}

function AdjustmentSummaryCard({ adjSummary }: { adjSummary: AdjustmentSummary | null }) {
  const countMetrics = adjSummary
    ? [
        {
          label: "Auto-Generated",
          value: adjSummary.autoGenerated,
          tileClassName: "bg-blue-50 dark:bg-blue-900/20",
          valueClassName: "text-blue-700 dark:text-blue-400",
        },
        {
          label: "Manual",
          value: adjSummary.manual,
          tileClassName: "bg-purple-50 dark:bg-purple-900/20",
          valueClassName: "text-purple-700 dark:text-purple-400",
        },
        {
          label: "Pending Approval",
          value: adjSummary.pendingApproval,
          tileClassName: "bg-amber-50 dark:bg-amber-900/20",
          valueClassName: "text-amber-700 dark:text-amber-400",
        },
        {
          label: "Approved",
          value: adjSummary.approved,
          tileClassName: "bg-emerald-50 dark:bg-emerald-900/20",
          valueClassName: "text-emerald-700 dark:text-emerald-400",
        },
      ]
    : ["Auto-Generated", "Manual", "Pending Approval", "Approved"].map((label) => ({
        label,
        value: 0,
        tileClassName: "bg-gray-50 dark:bg-neutral-800",
        valueClassName: "text-gray-900 dark:text-gray-100",
      }));

  return (
    <Card className="shadow-sm border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
      <Card.Header>
        <Card.Title className="text-gray-900 dark:text-gray-100">Adjustment Summary</Card.Title>
      </Card.Header>
      <Card.Content>
        <div className="space-y-4">
          <div className="grid gap-4 md:grid-cols-4">
            {countMetrics.map((metric) => (
              <div key={metric.label} className={`rounded-lg p-4 text-center ${metric.tileClassName}`}>
                <p className={`text-2xl font-bold ${metric.valueClassName}`}>{metric.value}</p>
                <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">{metric.label}</p>
              </div>
            ))}
          </div>
          {adjSummary && (
            <div className="grid gap-4 md:grid-cols-2">
              <ImpactMetric label="Total Impact on Profit" value={adjSummary.totalImpactOnProfit} />
              <ImpactMetric label="Total Impact on Assets" value={adjSummary.totalImpactOnAssets} />
            </div>
          )}
        </div>
      </Card.Content>
    </Card>
  );
}

function ImpactMetric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-lg bg-gray-50 p-4 dark:bg-neutral-800">
      <p className="text-xs text-gray-500 dark:text-gray-400">{label}</p>
      <p className={`mt-1 text-lg font-bold ${value >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
        {formatCurrency(value)}
      </p>
    </div>
  );
}

function AdjustmentFilters({
  adjFilterApproved,
  adjFilterType,
  loadingAdjustments,
  onFilterApprovedChange,
  onFilterTypeChange,
  onRefreshAdjustments,
}: {
  adjFilterApproved: string;
  adjFilterType: string;
  loadingAdjustments: boolean;
  onFilterApprovedChange: (value: string) => void;
  onFilterTypeChange: (value: string) => void;
  onRefreshAdjustments: () => void | Promise<void>;
}) {
  return (
    <div className="mb-4 grid gap-3 md:grid-cols-3">
      <FilterField label="Approval status">
        <select
          value={adjFilterApproved}
          onChange={(event) => onFilterApprovedChange(event.target.value)}
          className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
        >
          <option value="">All adjustments</option>
          <option value="pending">Pending approval</option>
          <option value="approved">Approved</option>
        </select>
      </FilterField>
      <FilterField label="Source">
        <select
          value={adjFilterType}
          onChange={(event) => onFilterTypeChange(event.target.value)}
          className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
        >
          <option value="">Auto and manual</option>
          <option value="auto">Auto-generated</option>
          <option value="manual">Manual only</option>
        </select>
      </FilterField>
      <div className="flex items-end">
        <Button variant="outline" size="sm" onPress={onRefreshAdjustments} isDisabled={loadingAdjustments}>
          <RefreshCw className={`mr-1 h-4 w-4 ${loadingAdjustments ? "animate-spin" : ""}`} />
          Apply Filters
        </Button>
      </div>
    </div>
  );
}

function FilterField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">{label}</span>
      {children}
    </label>
  );
}

function AdjustmentReviewCard({
  canApprove,
  adjustment,
  approvingId,
  onApproveAdjustment,
}: {
  canApprove: boolean;
  adjustment: Adjustment;
  approvingId: number | null;
  onApproveAdjustment: (adjustmentId: number) => void | Promise<void>;
}) {
  return (
    <Card id={`adjustment-review-${adjustment.id}`} tabIndex={-1} className="border border-gray-100 bg-white outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 dark:border-neutral-700 dark:bg-neutral-900">
      <Card.Content className="p-4">
        <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div className="min-w-0 flex-1">
            <div className="mb-1 flex flex-wrap items-center gap-2">
              <p className="truncate text-sm font-medium text-gray-900 dark:text-gray-100">{adjustment.description}</p>
              <Chip color={adjustment.isAuto ? "default" : "accent"} variant="soft" size="sm">
                {adjustment.isAuto ? "Auto" : "Manual"}
              </Chip>
              <Chip color={adjustment.approvedBy ? "success" : "warning"} variant="soft" size="sm">
                {adjustment.approvedBy ? "Approved" : "Pending"}
              </Chip>
            </div>
            {adjustment.reason && (
              <p className="mb-1 text-xs text-gray-500 dark:text-gray-400">{adjustment.reason}</p>
            )}
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-gray-500 dark:text-gray-400">
              <AdjustmentAmount label="Amount" value={adjustment.amount} />
              <AdjustmentAmount label="Profit" value={adjustment.impactOnProfit} signed />
              <AdjustmentAmount label="Assets" value={adjustment.impactOnAssets} signed />
            </div>
            {adjustment.approvedBy && (
              <p className="mt-1 text-xs text-gray-400 dark:text-gray-500">
                Approved by {adjustment.approvedBy}
                {adjustment.approvedAt && ` on ${new Date(adjustment.approvedAt).toLocaleDateString("en-IE")}`}
              </p>
            )}
          </div>
          {canApprove && !adjustment.approvedBy && (
            <div className="shrink-0">
              <Button
                variant="outline"
                size="sm"
                aria-label={`Approve ${adjustment.description} adjustment`}
                onPress={() => onApproveAdjustment(adjustment.id)}
                isDisabled={approvingId === adjustment.id}
              >
                {approvingId === adjustment.id ? (
                  <Spinner size="sm" />
                ) : (
                  <>
                    <CheckCircle2 className="mr-1 h-4 w-4" />
                    Approve
                  </>
                )}
              </Button>
            </div>
          )}
        </div>
      </Card.Content>
    </Card>
  );
}

function AdjustmentAmount({ label, value, signed = false }: { label: string; value: number; signed?: boolean }) {
  const valueClassName = signed
    ? value >= 0
      ? "text-emerald-700 dark:text-emerald-400"
      : "text-red-700 dark:text-red-400"
    : "text-gray-700 dark:text-gray-300";

  return (
    <span>
      {label}:{" "}
      <span className={`font-medium ${valueClassName}`}>{formatCurrency(value)}</span>
    </span>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
