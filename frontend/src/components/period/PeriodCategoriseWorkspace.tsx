"use client";

import { Button, Card, Chip, ProgressBar, ProgressBarFill, ProgressBarTrack, Spinner } from "@heroui/react";
import { RefreshCw, Settings } from "lucide-react";
import type { ReactNode } from "react";
import type { AccountCategory, BankAccount, ImportedTransaction, TransactionRule } from "@/lib/api";

interface TransactionRuleForm {
  pattern: string;
  categoryId: string;
  priority: string;
}

interface PeriodCategoriseWorkspaceProps {
  transactions: ImportedTransaction[];
  transactionTotal: number;
  categorisedCount: number;
  uncategorisedCount: number;
  loadingTransactions: boolean;
  categorisingId: number | null;
  categories: AccountCategory[];
  bankAccounts: BankAccount[];
  transactionRules: TransactionRule[];
  selectedTransactionIds: number[];
  visibleTransactionIds: number[];
  allVisibleTransactionsSelected: boolean;
  bulkCategoryId: string;
  bulkCategorising: boolean;
  ruleForm: TransactionRuleForm;
  savingRule: boolean;
  deletingRuleId: number | null;
  txFilterStatus: string;
  txFilterCategory: string;
  txFilterBank: string;
  txFilterSearch: string;
  onRefresh: () => void | Promise<void>;
  onRuleFormChange: (form: TransactionRuleForm) => void;
  onCreateRule: () => void | Promise<void>;
  onDeleteRule: (ruleId: number) => void | Promise<void>;
  onBulkCategoryChange: (categoryId: string) => void;
  onBulkCategorise: () => void | Promise<void>;
  onFilterStatusChange: (value: string) => void;
  onFilterCategoryChange: (value: string) => void;
  onFilterBankChange: (value: string) => void;
  onSearchInputChange: (value: string) => void;
  onSelectVisibleTransactions: (selected: boolean) => void;
  onToggleTransactionSelection: (transactionId: number, selected: boolean) => void;
  onCategoriseTransaction: (transactionId: number, categoryId: number) => void | Promise<void>;
}

export function PeriodCategoriseWorkspace({
  transactions,
  transactionTotal,
  categorisedCount,
  uncategorisedCount,
  loadingTransactions,
  categorisingId,
  categories,
  bankAccounts,
  transactionRules,
  selectedTransactionIds,
  visibleTransactionIds,
  allVisibleTransactionsSelected,
  bulkCategoryId,
  bulkCategorising,
  ruleForm,
  savingRule,
  deletingRuleId,
  txFilterStatus,
  txFilterCategory,
  txFilterBank,
  txFilterSearch,
  onRefresh,
  onRuleFormChange,
  onCreateRule,
  onDeleteRule,
  onBulkCategoryChange,
  onBulkCategorise,
  onFilterStatusChange,
  onFilterCategoryChange,
  onFilterBankChange,
  onSearchInputChange,
  onSelectVisibleTransactions,
  onToggleTransactionSelection,
  onCategoriseTransaction,
}: PeriodCategoriseWorkspaceProps) {
  return (
    <div className="space-y-6">
      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
        <Card.Header>
          <div className="flex items-center justify-between w-full">
            <div>
              <Card.Title className="text-gray-900 dark:text-gray-100">Categorisation Overview</Card.Title>
              <Card.Description>Review and categorise imported transactions</Card.Description>
            </div>
            <Button variant="ghost" size="sm" onPress={onRefresh} isDisabled={loadingTransactions}>
              <RefreshCw className={`w-4 h-4 mr-1 ${loadingTransactions ? "animate-spin" : ""}`} />
              Refresh
            </Button>
          </div>
        </Card.Header>
        <Card.Content>
          <div className="grid grid-cols-3 gap-4 mb-6">
            <MetricCard tone="default" value={transactionTotal} label="Total Transactions" />
            <MetricCard tone="success" value={categorisedCount} label="Categorised" />
            <MetricCard tone="warning" value={uncategorisedCount} label="Uncategorised" />
          </div>

          {transactionTotal > 0 && (
            <div className="mb-6">
              <div className="flex items-center justify-between text-sm mb-1.5">
                <span className="text-gray-600 dark:text-gray-400">Categorisation Progress</span>
                <span className="font-medium text-gray-900 dark:text-gray-100">
                  {Math.round((categorisedCount / transactionTotal) * 100)}%
                </span>
              </div>
              <ProgressBar
                value={categorisedCount}
                minValue={0}
                maxValue={transactionTotal || 1}
                aria-label="Categorisation progress"
                color={categorisedCount === transactionTotal ? "success" : "warning"}
              >
                <ProgressBarTrack>
                  <ProgressBarFill />
                </ProgressBarTrack>
              </ProgressBar>
            </div>
          )}

          <TransactionRulesPanel
            categories={categories}
            transactionRules={transactionRules}
            ruleForm={ruleForm}
            savingRule={savingRule}
            deletingRuleId={deletingRuleId}
            onRuleFormChange={onRuleFormChange}
            onCreateRule={onCreateRule}
            onDeleteRule={onDeleteRule}
          />

          {transactions.length > 0 && (
            <div className="mb-4 rounded-md border border-slate-200 bg-slate-50 p-3 dark:border-neutral-700 dark:bg-neutral-800/40">
              <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
                <div>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">Bulk categorisation</p>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {selectedTransactionIds.length} selected from {visibleTransactionIds.length} visible. Use this for
                    reviewed recurring items only.
                  </p>
                </div>
                <div className="flex flex-col gap-2 md:flex-row md:items-center">
                  <CategorySelect
                    value={bulkCategoryId}
                    categories={categories}
                    ariaLabel="Bulk category"
                    className="min-w-64"
                    onChange={onBulkCategoryChange}
                  />
                  <Button
                    variant="primary"
                    size="sm"
                    onPress={onBulkCategorise}
                    isDisabled={bulkCategorising || selectedTransactionIds.length === 0 || !bulkCategoryId}
                  >
                    {bulkCategorising ? <Spinner size="sm" /> : "Apply to Selected"}
                  </Button>
                </div>
              </div>
            </div>
          )}

          <TransactionFilters
            categories={categories}
            bankAccounts={bankAccounts}
            txFilterStatus={txFilterStatus}
            txFilterCategory={txFilterCategory}
            txFilterBank={txFilterBank}
            txFilterSearch={txFilterSearch}
            onFilterStatusChange={onFilterStatusChange}
            onFilterCategoryChange={onFilterCategoryChange}
            onFilterBankChange={onFilterBankChange}
            onSearchInputChange={onSearchInputChange}
          />

          <TransactionTable
            transactions={transactions}
            transactionTotal={transactionTotal}
            loadingTransactions={loadingTransactions}
            categories={categories}
            categorisingId={categorisingId}
            selectedTransactionIds={selectedTransactionIds}
            allVisibleTransactionsSelected={allVisibleTransactionsSelected}
            onSelectVisibleTransactions={onSelectVisibleTransactions}
            onToggleTransactionSelection={onToggleTransactionSelection}
            onCategoriseTransaction={onCategoriseTransaction}
          />
        </Card.Content>
      </Card>
    </div>
  );
}

function TransactionRulesPanel({
  categories,
  transactionRules,
  ruleForm,
  savingRule,
  deletingRuleId,
  onRuleFormChange,
  onCreateRule,
  onDeleteRule,
}: {
  categories: AccountCategory[];
  transactionRules: TransactionRule[];
  ruleForm: TransactionRuleForm;
  savingRule: boolean;
  deletingRuleId: number | null;
  onRuleFormChange: (form: TransactionRuleForm) => void;
  onCreateRule: () => void | Promise<void>;
  onDeleteRule: (ruleId: number) => void | Promise<void>;
}) {
  return (
    <div className="mb-6 rounded-md border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800/40">
      <div className="mb-3 flex flex-col gap-1 md:flex-row md:items-center md:justify-between">
        <div>
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Transaction Rules</h3>
          <p className="text-xs text-gray-500 dark:text-gray-400">
            Match recurring bank descriptions to account categories for future imports.
          </p>
        </div>
        <Chip size="sm" variant="soft" color={transactionRules.length > 0 ? "success" : "default"}>
          {transactionRules.length} rule{transactionRules.length !== 1 ? "s" : ""}
        </Chip>
      </div>
      <div className="grid gap-3 md:grid-cols-12">
        <input
          value={ruleForm.pattern}
          onChange={(event) => onRuleFormChange({ ...ruleForm, pattern: event.target.value })}
          placeholder="Description contains..."
          className="md:col-span-5 rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
          aria-label="Rule pattern"
        />
        <CategorySelect
          value={ruleForm.categoryId}
          categories={categories}
          ariaLabel="Rule category"
          className="md:col-span-4"
          onChange={(categoryId) => onRuleFormChange({ ...ruleForm, categoryId })}
        />
        <input
          type="number"
          value={ruleForm.priority}
          onChange={(event) => onRuleFormChange({ ...ruleForm, priority: event.target.value })}
          className="md:col-span-1 rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
          aria-label="Rule priority"
        />
        <Button variant="outline" size="sm" onPress={onCreateRule} isDisabled={savingRule || categories.length === 0} className="md:col-span-2">
          {savingRule ? <Spinner size="sm" /> : "Add Rule"}
        </Button>
      </div>
      {transactionRules.length > 0 && (
        <div className="mt-4 overflow-hidden rounded-md border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
          {transactionRules.map((rule) => {
            const category = categories.find((cat) => cat.id === rule.categoryId);
            return (
              <div key={rule.id} className="grid grid-cols-12 items-center gap-2 border-b border-gray-100 px-3 py-2 text-xs last:border-b-0 dark:border-neutral-800">
                <div className="col-span-5 font-medium text-gray-900 dark:text-gray-100">{rule.pattern}</div>
                <div className="col-span-5 text-gray-600 dark:text-gray-300">
                  {category ? categoryLabel(category) : `Category ${rule.categoryId}`}
                </div>
                <div className="col-span-1 text-right text-gray-500 dark:text-gray-400">{rule.priority}</div>
                <div className="col-span-1 text-right">
                  <Button variant="ghost" size="sm" onPress={() => onDeleteRule(rule.id)} isDisabled={deletingRuleId === rule.id}>
                    {deletingRuleId === rule.id ? <Spinner size="sm" /> : "Delete"}
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function TransactionFilters({
  categories,
  bankAccounts,
  txFilterStatus,
  txFilterCategory,
  txFilterBank,
  txFilterSearch,
  onFilterStatusChange,
  onFilterCategoryChange,
  onFilterBankChange,
  onSearchInputChange,
}: {
  categories: AccountCategory[];
  bankAccounts: BankAccount[];
  txFilterStatus: string;
  txFilterCategory: string;
  txFilterBank: string;
  txFilterSearch: string;
  onFilterStatusChange: (value: string) => void;
  onFilterCategoryChange: (value: string) => void;
  onFilterBankChange: (value: string) => void;
  onSearchInputChange: (value: string) => void;
}) {
  return (
    <div className="grid grid-cols-4 gap-3 mb-4">
      <FilterField label="Status">
        <select
          className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
          value={txFilterStatus}
          onChange={(event) => onFilterStatusChange(event.target.value)}
        >
          <option value="">All</option>
          <option value="uncategorised">Uncategorised</option>
        </select>
      </FilterField>
      <FilterField label="Category">
        <CategorySelect
          value={txFilterCategory}
          categories={categories}
          ariaLabel="Filter category"
          allLabel="All Categories"
          onChange={onFilterCategoryChange}
        />
      </FilterField>
      <FilterField label="Bank Account">
        <select
          className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
          value={txFilterBank}
          onChange={(event) => onFilterBankChange(event.target.value)}
        >
          <option value="">All Accounts</option>
          {bankAccounts.map((bankAccount) => (
            <option key={bankAccount.id} value={bankAccount.id}>
              {bankAccount.name} {bankAccount.iban ? `(${bankAccount.iban})` : ""}
            </option>
          ))}
        </select>
      </FilterField>
      <FilterField label="Search">
        <input
          type="text"
          placeholder="Search description..."
          className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
          defaultValue={txFilterSearch}
          onChange={(event) => onSearchInputChange(event.target.value)}
        />
      </FilterField>
    </div>
  );
}

function TransactionTable({
  transactions,
  transactionTotal,
  loadingTransactions,
  categories,
  categorisingId,
  selectedTransactionIds,
  allVisibleTransactionsSelected,
  onSelectVisibleTransactions,
  onToggleTransactionSelection,
  onCategoriseTransaction,
}: {
  transactions: ImportedTransaction[];
  transactionTotal: number;
  loadingTransactions: boolean;
  categories: AccountCategory[];
  categorisingId: number | null;
  selectedTransactionIds: number[];
  allVisibleTransactionsSelected: boolean;
  onSelectVisibleTransactions: (selected: boolean) => void;
  onToggleTransactionSelection: (transactionId: number, selected: boolean) => void;
  onCategoriseTransaction: (transactionId: number, categoryId: number) => void | Promise<void>;
}) {
  if (loadingTransactions) {
    return (
      <div className="flex items-center justify-center py-8">
        <Spinner size="sm" />
      </div>
    );
  }

  if (transactions.length === 0) {
    return (
      <div className="text-center py-8">
        <Settings className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
        <p className="text-sm text-gray-400 dark:text-gray-500 italic">
          Import transactions to begin categorisation
        </p>
      </div>
    );
  }

  return (
    <div className="border border-gray-200 dark:border-neutral-700 rounded-lg overflow-hidden">
      <div className="grid grid-cols-12 gap-2 bg-gray-50 dark:bg-neutral-800 border-b border-gray-200 dark:border-neutral-700 px-4 py-2.5 text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wide">
        <div className="col-span-1">
          <input
            type="checkbox"
            checked={allVisibleTransactionsSelected}
            onChange={(event) => onSelectVisibleTransactions(event.target.checked)}
            aria-label="Select visible transactions"
          />
        </div>
        <div className="col-span-2">Date</div>
        <div className="col-span-3">Description</div>
        <div className="col-span-2 text-right">Amount</div>
        <div className="col-span-3">Category</div>
        <div className="col-span-1 text-center">Conf.</div>
      </div>
      <div className="divide-y divide-gray-100 dark:divide-neutral-700">
        {transactions.map((transaction, index) => (
          <div
            key={transaction.id}
            className={`grid grid-cols-12 gap-2 px-4 py-3 text-sm hover:bg-gray-50 dark:hover:bg-neutral-800/50 items-center transition-colors ${
              index % 2 === 1 ? "bg-gray-50/50 dark:bg-neutral-800/25" : ""
            }`}
          >
            <div className="col-span-1">
              <input
                type="checkbox"
                checked={selectedTransactionIds.includes(transaction.id)}
                onChange={(event) => onToggleTransactionSelection(transaction.id, event.target.checked)}
                aria-label={`Select ${transaction.description}`}
              />
            </div>
            <div className="col-span-2 text-gray-600 dark:text-gray-400">
              {new Date(transaction.date).toLocaleDateString("en-IE")}
            </div>
            <div className="col-span-3 text-gray-900 dark:text-gray-100 truncate" title={transaction.description}>
              {transaction.description}
            </div>
            <div className={`col-span-2 text-right font-medium font-mono ${transaction.amount >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
              {formatCurrency(transaction.amount)}
            </div>
            <div className="col-span-3">
              <div className="flex items-center gap-2">
                <CategorySelect
                  value={transaction.categoryId ?? ""}
                  categories={categories}
                  ariaLabel={`Categorise ${transaction.description}`}
                  placeholder="Uncategorised"
                  disabled={categories.length === 0 || categorisingId === transaction.id}
                  className="min-w-0 flex-1 rounded-md px-2 py-1.5 text-xs"
                  onChange={(value) => {
                    if (value) void onCategoriseTransaction(transaction.id, Number(value));
                  }}
                />
                {categorisingId === transaction.id && <Spinner size="sm" />}
              </div>
              {transaction.manualOverride && (
                <p className="mt-1 text-[11px] text-blue-600 dark:text-blue-400">Manual</p>
              )}
            </div>
            <div className="col-span-1 text-center text-xs text-gray-400 dark:text-gray-500">
              {transaction.confidenceScore != null ? `${Math.round(transaction.confidenceScore * 100)}%` : "--"}
            </div>
          </div>
        ))}
      </div>
      {transactionTotal > transactions.length && (
        <div className="bg-gray-50 dark:bg-neutral-800 border-t border-gray-200 dark:border-neutral-700 px-4 py-2.5 text-xs text-gray-500 dark:text-gray-400 text-center">
          Showing {transactions.length} of {transactionTotal} transactions
        </div>
      )}
    </div>
  );
}

function CategorySelect({
  value,
  categories,
  ariaLabel,
  className,
  placeholder = "Choose category",
  allLabel,
  disabled = false,
  onChange,
}: {
  value: string | number;
  categories: AccountCategory[];
  ariaLabel: string;
  className?: string;
  placeholder?: string;
  allLabel?: string;
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <select
      value={value}
      onChange={(event) => onChange(event.target.value)}
      disabled={disabled}
      className={`rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100 ${className ?? ""}`}
      aria-label={ariaLabel}
    >
      <option value="">{allLabel ?? placeholder}</option>
      {categories.map((category) => (
        <option key={category.id} value={category.id}>
          {categoryLabel(category)}
        </option>
      ))}
    </select>
  );
}

function FilterField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <label className="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">{label}</label>
      {children}
    </div>
  );
}

function MetricCard({
  tone,
  value,
  label,
}: {
  tone: "default" | "success" | "warning";
  value: number;
  label: string;
}) {
  const classes = {
    default: "bg-gray-50 dark:bg-neutral-800 text-gray-900 dark:text-gray-100",
    success: "bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-400",
    warning: "bg-amber-50 dark:bg-amber-900/20 text-amber-700 dark:text-amber-400",
  }[tone];

  return (
    <div className={`rounded-lg p-4 text-center ${classes}`}>
      <p className="text-2xl font-bold">{value}</p>
      <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{label}</p>
    </div>
  );
}

function categoryLabel(category: AccountCategory): string {
  return category.code ? `${category.code} - ${category.name}` : category.name;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
