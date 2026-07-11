"use client";

import { Button, Card, Chip, ProgressBar, ProgressBarFill, ProgressBarTrack, Spinner } from "@heroui/react";
import { RefreshCw, Settings } from "lucide-react";
import { cloneElement, useId, type ReactElement } from "react";
import type {
  AccountCategory,
  BankAccount,
  ImportedTransaction,
  TransactionRule,
  TransactionSortDirection,
  TransactionSortField,
} from "@/lib/api";
import { DataGrid, ReadOnlyNotice } from "@/components/workbench";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

interface TransactionRuleForm {
  pattern: string;
  categoryId: string;
  priority: string;
}

interface PeriodCategoriseWorkspaceProps {
  canWrite?: boolean;
  transactions: ImportedTransaction[];
  transactionTotal: number;
  filteredTransactionTotal: number;
  categorisedCount: number;
  uncategorisedCount: number;
  transactionPage: number;
  transactionPageSize: number;
  transactionPageCount: number;
  transactionSortBy: TransactionSortField;
  transactionSortDirection: TransactionSortDirection;
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
  selectionAnnouncement?: string;
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
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  onSortByChange: (sortBy: TransactionSortField) => void;
  onSortDirectionChange: (sortDirection: TransactionSortDirection) => void;
  onSelectVisibleTransactions: (selected: boolean) => void;
  onToggleTransactionSelection: (transactionId: number, selected: boolean) => void;
  onCategoriseTransaction: (transactionId: number, categoryId: number) => void | Promise<void>;
}

export function PeriodCategoriseWorkspace({
  canWrite = true,
  transactions,
  transactionTotal,
  filteredTransactionTotal,
  categorisedCount,
  uncategorisedCount,
  transactionPage,
  transactionPageSize,
  transactionPageCount,
  transactionSortBy,
  transactionSortDirection,
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
  selectionAnnouncement = "",
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
  onPageChange,
  onPageSizeChange,
  onSortByChange,
  onSortDirectionChange,
  onSelectVisibleTransactions,
  onToggleTransactionSelection,
  onCategoriseTransaction,
}: PeriodCategoriseWorkspaceProps) {
  const selectedOnCurrentPage = visibleTransactionIds.filter((id) => selectedTransactionIds.includes(id)).length;

  return (
    <div className="space-y-6">
      {!canWrite && <ReadOnlyNotice subject="transaction categorisation" />}
      <p className="sr-only" aria-live="polite" aria-atomic="true">
        {selectionAnnouncement}
      </p>
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
            canWrite={canWrite}
            categories={categories}
            transactionRules={transactionRules}
            ruleForm={ruleForm}
            savingRule={savingRule}
            deletingRuleId={deletingRuleId}
            onRuleFormChange={onRuleFormChange}
            onCreateRule={onCreateRule}
            onDeleteRule={onDeleteRule}
          />

          {canWrite && transactions.length > 0 && (
            <div className="mb-4 rounded-md border border-slate-200 bg-slate-50 p-3 dark:border-neutral-700 dark:bg-neutral-800/40">
              <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
                <div>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">Bulk categorisation</p>
                  <p className="text-xs text-[var(--muted-foreground)]">
                    {selectedTransactionIds.length} selected across pages; {selectedOnCurrentPage} of {visibleTransactionIds.length}
                    {" "}on this page. Select current page never selects every matching result. Changing a filter clears the selection.
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
                    aria-label="Apply to Selected transactions"
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
            transactionSortBy={transactionSortBy}
            transactionSortDirection={transactionSortDirection}
            onFilterStatusChange={onFilterStatusChange}
            onFilterCategoryChange={onFilterCategoryChange}
            onFilterBankChange={onFilterBankChange}
            onSearchInputChange={onSearchInputChange}
            onSortByChange={onSortByChange}
            onSortDirectionChange={onSortDirectionChange}
          />

          <TransactionTable
            canWrite={canWrite}
            transactions={transactions}
            transactionTotal={transactionTotal}
            filteredTransactionTotal={filteredTransactionTotal}
            transactionPage={transactionPage}
            transactionPageSize={transactionPageSize}
            transactionPageCount={transactionPageCount}
            loadingTransactions={loadingTransactions}
            categories={categories}
            categorisingId={categorisingId}
            selectedTransactionIds={selectedTransactionIds}
            allVisibleTransactionsSelected={allVisibleTransactionsSelected}
            onSelectVisibleTransactions={onSelectVisibleTransactions}
            onToggleTransactionSelection={onToggleTransactionSelection}
            onCategoriseTransaction={onCategoriseTransaction}
            onPageChange={onPageChange}
            onPageSizeChange={onPageSizeChange}
          />
        </Card.Content>
      </Card>
    </div>
  );
}

function TransactionRulesPanel({
  canWrite,
  categories,
  transactionRules,
  ruleForm,
  savingRule,
  deletingRuleId,
  onRuleFormChange,
  onCreateRule,
  onDeleteRule,
}: {
  canWrite: boolean;
  categories: AccountCategory[];
  transactionRules: TransactionRule[];
  ruleForm: TransactionRuleForm;
  savingRule: boolean;
  deletingRuleId: number | null;
  onRuleFormChange: (form: TransactionRuleForm) => void;
  onCreateRule: () => void | Promise<void>;
  onDeleteRule: (ruleId: number) => void | Promise<void>;
}) {
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  return (
    <div className="mb-6 rounded-md border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800/40">
      <div className="mb-3 flex flex-col gap-1 md:flex-row md:items-center md:justify-between">
        <div>
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Transaction Rules</h3>
          <p className="text-xs text-[var(--muted-foreground)]">
            Match recurring bank descriptions to account categories for future imports.
          </p>
        </div>
        <Chip size="sm" variant="soft" color={transactionRules.length > 0 ? "success" : "default"}>
          {transactionRules.length} rule{transactionRules.length !== 1 ? "s" : ""}
        </Chip>
      </div>
      {canWrite && <div className="grid gap-3 md:grid-cols-12">
        <input
          value={ruleForm.pattern}
          onChange={(event) => onRuleFormChange({ ...ruleForm, pattern: event.target.value })}
          placeholder="Description contains..."
          className="rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100 md:col-span-5"
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
          className="rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100 md:col-span-1"
          aria-label="Rule priority"
        />
        <Button variant="outline" size="sm" aria-label="Add Rule for transaction matching" onPress={onCreateRule} isDisabled={savingRule || categories.length === 0} className="md:col-span-2">
          {savingRule ? <Spinner size="sm" /> : "Add Rule"}
        </Button>
      </div>}
      {transactionRules.length > 0 && (
        <ul className="mt-4 overflow-hidden rounded-md border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900" aria-label="Transaction rules">
          {transactionRules.map((rule) => {
            const category = categories.find((cat) => cat.id === rule.categoryId);
            return (
              <li key={rule.id} className="grid gap-2 border-b border-gray-100 px-3 py-3 text-xs last:border-b-0 dark:border-neutral-800 md:grid-cols-12 md:items-center">
                <div className="font-medium text-gray-900 dark:text-gray-100 md:col-span-5">{rule.pattern}</div>
                <div className="text-gray-600 dark:text-gray-300 md:col-span-5">
                  {category ? categoryLabel(category) : `Category ${rule.categoryId}`}
                </div>
                <div className="text-[var(--muted-foreground)] md:col-span-1 md:text-right">Priority {rule.priority}</div>
                {canWrite && (
                  <div className="md:col-span-1 md:text-right">
                    <Button variant="ghost" size="sm" aria-label={`Delete rule ${rule.pattern}`} onPress={() => requestDestructiveAction({
                      recordLabel: `transaction rule "${rule.pattern}"`,
                      consequence: `This permanently removes the rule that assigns matching transactions to ${category ? categoryLabel(category) : `category ${rule.categoryId}`}. Existing categorisations remain, but future imports will no longer use this rule.`,
                      onConfirm: () => onDeleteRule(rule.id),
                      successAnnouncement: `Transaction rule ${rule.pattern} was removed.`,
                    })} isDisabled={deletingRuleId === rule.id}>
                      {deletingRuleId === rule.id ? <Spinner size="sm" /> : "Delete"}
                    </Button>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {destructiveActionConfirmation}
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
  transactionSortBy,
  transactionSortDirection,
  onFilterStatusChange,
  onFilterCategoryChange,
  onFilterBankChange,
  onSearchInputChange,
  onSortByChange,
  onSortDirectionChange,
}: {
  categories: AccountCategory[];
  bankAccounts: BankAccount[];
  txFilterStatus: string;
  txFilterCategory: string;
  txFilterBank: string;
  txFilterSearch: string;
  selectionAnnouncement?: string;
  transactionSortBy: TransactionSortField;
  transactionSortDirection: TransactionSortDirection;
  onFilterStatusChange: (value: string) => void;
  onFilterCategoryChange: (value: string) => void;
  onFilterBankChange: (value: string) => void;
  onSearchInputChange: (value: string) => void;
  onSortByChange: (sortBy: TransactionSortField) => void;
  onSortDirectionChange: (sortDirection: TransactionSortDirection) => void;
}) {
  return (
    <div className="mb-4 grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-6">
      <FilterField label="Status">
        <select
          className="w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-800 dark:text-gray-100"
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
          className="w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-800 dark:text-gray-100"
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
          className="w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-800 dark:text-gray-100"
          value={txFilterSearch}
          onChange={(event) => onSearchInputChange(event.target.value)}
        />
      </FilterField>
      <FilterField label="Sort by">
        <select
          aria-label="Sort by"
          className="w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-800 dark:text-gray-100"
          value={transactionSortBy}
          onChange={(event) => onSortByChange(event.target.value as TransactionSortField)}
        >
          <option value="date">Date</option>
          <option value="description">Description</option>
          <option value="amount">Amount</option>
          <option value="confidence">Confidence</option>
        </select>
      </FilterField>
      <FilterField label="Direction">
        <select
          aria-label="Sort direction"
          className="w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-800 dark:text-gray-100"
          value={transactionSortDirection}
          onChange={(event) => onSortDirectionChange(event.target.value as TransactionSortDirection)}
        >
          <option value="desc">Descending</option>
          <option value="asc">Ascending</option>
        </select>
      </FilterField>
    </div>
  );
}

function TransactionTable({
  canWrite,
  transactions,
  transactionTotal,
  filteredTransactionTotal,
  transactionPage,
  transactionPageSize,
  transactionPageCount,
  loadingTransactions,
  categories,
  categorisingId,
  selectedTransactionIds,
  allVisibleTransactionsSelected,
  onSelectVisibleTransactions,
  onToggleTransactionSelection,
  onCategoriseTransaction,
  onPageChange,
  onPageSizeChange,
}: {
  canWrite: boolean;
  transactions: ImportedTransaction[];
  transactionTotal: number;
  filteredTransactionTotal: number;
  transactionPage: number;
  transactionPageSize: number;
  transactionPageCount: number;
  loadingTransactions: boolean;
  categories: AccountCategory[];
  categorisingId: number | null;
  selectedTransactionIds: number[];
  allVisibleTransactionsSelected: boolean;
  onSelectVisibleTransactions: (selected: boolean) => void;
  onToggleTransactionSelection: (transactionId: number, selected: boolean) => void;
  onCategoriseTransaction: (transactionId: number, categoryId: number) => void | Promise<void>;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
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
        <Settings className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
        <p className="text-sm text-[var(--muted-foreground)] italic">
          {transactionTotal === 0
            ? "Import transactions to begin categorisation"
            : "No transactions match the current filters"}
        </p>
      </div>
    );
  }

  return (
    <div id="transaction-register" tabIndex={-1} className="min-w-0 space-y-3 outline-none focus-visible:ring-2 focus-visible:ring-emerald-500">
      {canWrite && (
        <label className="flex min-h-10 items-center gap-2 rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm font-medium text-gray-800 dark:border-neutral-700 dark:bg-neutral-800 dark:text-gray-100">
          <input
            type="checkbox"
            checked={allVisibleTransactionsSelected}
            onChange={(event) => onSelectVisibleTransactions(event.target.checked)}
            aria-label="Select current page"
            className="workbench-checkbox"
          />
          Select all transactions on this page
        </label>
      )}
      <DataGrid
        caption="Transactions to categorise"
        columns={["Select", "Date", "Description", "Amount and entry", "Category", "Confidence"]}
        mobilePresentation="cards"
        rows={transactions.map((transaction) => {
          const entrySide = transaction.amount >= 0 ? "Debit" : "Credit";
          const category = categories.find((item) => item.id === transaction.categoryId);

          return {
            id: transaction.id,
            searchText: `${transaction.date} ${transaction.description} ${transaction.amount} ${entrySide} ${category?.name ?? "Uncategorised"} ${transaction.confidenceScore ?? ""}`,
            cells: [
              canWrite ? (
                <input
                  key="select"
                  type="checkbox"
                  checked={selectedTransactionIds.includes(transaction.id)}
                  onChange={(event) => onToggleTransactionSelection(transaction.id, event.target.checked)}
                  aria-label={`Select ${transaction.description}`}
                  className="workbench-checkbox"
                />
              ) : (
                <span key="select" className="text-xs text-[var(--muted-foreground)]">Read only</span>
              ),
              <time key="date" dateTime={transaction.date} className="text-gray-600 dark:text-gray-400">
                {new Date(transaction.date).toLocaleDateString("en-IE")}
              </time>,
              <span key="description" className="break-words text-gray-900 dark:text-gray-100">
                {transaction.description}
              </span>,
              <div key="amount" className={`font-mono font-medium ${transaction.amount >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                <span className="block">{formatCurrency(transaction.amount)}</span>
                <span className="mt-1 block font-sans text-[11px] font-semibold uppercase tracking-wide">
                  {entrySide} to bank
                </span>
              </div>,
              <div key="category" className="min-w-0">
                <div className="flex min-w-0 items-center gap-2">
                  {canWrite ? (
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
                  ) : (
                    <span className="text-xs text-gray-700 dark:text-gray-300">
                      {category?.name ?? "Uncategorised"}
                    </span>
                  )}
                  {categorisingId === transaction.id && <Spinner size="sm" />}
                </div>
                {transaction.manualOverride && (
                  <p className="mt-1 text-[11px] text-blue-600 dark:text-blue-400">Manual</p>
                )}
              </div>,
              <span key="confidence" className="text-xs text-[var(--muted-foreground)]">
                {transaction.confidenceScore != null ? `${Math.round(transaction.confidenceScore * 100)}%` : "Not scored"}
              </span>,
            ],
          };
        })}
      />
      <div className="flex flex-col gap-3 border-t border-gray-200 bg-gray-50 px-4 py-3 text-xs text-gray-600 dark:border-neutral-700 dark:bg-neutral-800 dark:text-gray-300 md:flex-row md:items-center md:justify-between">
        <p aria-live="polite">
          Showing {(transactionPage - 1) * transactionPageSize + 1}–{Math.min(transactionPage * transactionPageSize, filteredTransactionTotal)}
          {" "}of {filteredTransactionTotal} matching transactions
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2">
            <span>Rows per page</span>
            <select
              aria-label="Rows per page"
              className="rounded-md border border-[var(--control-border)] bg-white px-2 py-1 text-xs text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
              value={transactionPageSize}
              onChange={(event) => onPageSizeChange(Number(event.target.value))}
            >
              <option value="25">25</option>
              <option value="50">50</option>
              <option value="100">100</option>
            </select>
          </label>
          <span>Page {transactionPage} of {transactionPageCount}</span>
          <Button
            variant="outline"
            size="sm"
            onPress={() => onPageChange(transactionPage - 1)}
            isDisabled={transactionPage <= 1 || loadingTransactions}
          >
            Previous
          </Button>
          <Button
            variant="outline"
            size="sm"
            onPress={() => onPageChange(transactionPage + 1)}
            isDisabled={transactionPage >= transactionPageCount || loadingTransactions}
          >
            Next
          </Button>
        </div>
      </div>
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
      className={`w-full min-w-0 max-w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100 ${className ?? ""}`}
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

function FilterField({ label, children }: { label: string; children: ReactElement<{ id?: string }> }) {
  const generatedId = useId();
  const controlId = children.props.id ?? `${generatedId}-control`;

  return (
    <div className="min-w-0">
      <label htmlFor={controlId} className="block text-xs font-medium text-[var(--muted-foreground)] mb-1">{label}</label>
      {cloneElement(children, { id: controlId })}
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
      <p className="text-xs text-[var(--muted-foreground)] mt-1">{label}</p>
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
