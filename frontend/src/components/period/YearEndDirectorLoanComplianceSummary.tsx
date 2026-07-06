"use client";

import { Chip } from "@heroui/react";

import type { DirectorLoanCompliance } from "@/lib/api";

interface YearEndDirectorLoanComplianceSummaryProps {
  compliance: DirectorLoanCompliance | null;
}

export function YearEndDirectorLoanComplianceSummary({
  compliance,
}: YearEndDirectorLoanComplianceSummaryProps) {
  if (!compliance || compliance.loans.length === 0) {
    return null;
  }

  return (
    <div className="space-y-4" aria-label="s.236 / overdrawn-DLA compliance summary">
      <p className="sr-only">s.236 / overdrawn-DLA compliance summary</p>
      {compliance.warning && (
        <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-700 p-3">
          <p className="text-sm font-medium text-amber-800 dark:text-amber-300">
            {compliance.warning}
          </p>
        </div>
      )}

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <MetricTile label="Total Loans" value={formatCurrency(compliance.totalDirectorLoans)} />
        <MetricTile label="Net Assets" value={formatCurrency(compliance.netAssets)} />
        <MetricTile label="10% Threshold" value={formatCurrency(compliance.thresholdAmount)} />
        <div className="rounded-lg border border-gray-200 dark:border-neutral-700 p-3 dark:bg-neutral-800/50">
          <p className="text-xs text-gray-500 dark:text-gray-400">Status</p>
          <Chip variant="soft" size="sm" color={compliance.exceedsThreshold ? "danger" : "success"}>
            {compliance.exceedsThreshold ? "Exceeds Threshold" : "Within Limits"}
          </Chip>
        </div>
      </div>

      {compliance.sapRequired && (
        <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700 p-3">
          <p className="text-sm font-medium text-red-800 dark:text-red-300">
            Shareholder Approval Process (SAP) required under s.239 Companies Act 2014
          </p>
        </div>
      )}
    </div>
  );
}

function MetricTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-gray-200 dark:border-neutral-700 p-3 dark:bg-neutral-800/50">
      <p className="text-xs text-gray-500 dark:text-gray-400">{label}</p>
      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{value}</p>
    </div>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
