import { render, screen, within } from "@testing-library/react";
import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { PeriodCategoriseWorkspace } from "@/components/period/PeriodCategoriseWorkspace";
import type { AccountCategory } from "@/lib/api";
import { isAssignableCategory, openingBalanceTargets } from "@/lib/accountCategoryTargets";

// Archived accounts and the bank control account are refused as posting targets by the server, so
// the categorisation, bulk, rule and opening-balance pickers do not offer them. The transaction filter
// still lists every account returned (an archived account with a balance stays findable).
const categories: AccountCategory[] = [
  { id: 1, code: "1200", name: "Bank current account", type: "Asset", statementLine: "CashAtBank", systemRole: "BankControl", isArchived: false },
  { id: 2, code: "2240", name: "Other creditors", type: "Liability", statementLine: "OtherCreditorsWithinYear", systemRole: null, isArchived: false },
  { id: 3, code: "4000", name: "Donations", type: "Income", statementLine: "Turnover", systemRole: "TradeDebtorIncome", isArchived: false },
  { id: 4, code: "6000", name: "Software", type: "Expense", statementLine: "AdministrativeExpenses", systemRole: null, isArchived: false },
  { id: 5, code: "G-6800", name: "Legacy expenses (legacy)", type: "Expense", statementLine: "AdministrativeExpenses", systemRole: null, isArchived: true },
  { id: 6, code: "G-2500", name: "Legacy loan (legacy)", type: "Liability", statementLine: "OtherCreditorsWithinYear", systemRole: null, isArchived: true },
];

function optionValues(select: HTMLElement): string[] {
  return within(select).getAllByRole("option").map((option) => (option as HTMLOptionElement).value).filter(Boolean);
}

describe("category pickers", () => {
  it("offer neither archived accounts nor the bank control, while the filter lists every account", () => {
    render(
      <PeriodCategoriseWorkspace
        {...workspaceProps({
          transactions: [
            { id: 10, date: "2025-02-01", description: "Receipt", amount: 100, categoryId: 3, manualOverride: true },
            { id: 11, date: "2025-02-02", description: "Old expense", amount: -45, categoryId: 5, manualOverride: true },
          ],
          visibleTransactionIds: [10, 11],
        })}
      />,
    );

    expect(optionValues(screen.getByRole("combobox", { name: "Bulk category" }))).toEqual(["2", "3", "4"]);
    expect(optionValues(screen.getByRole("combobox", { name: "Rule category" }))).toEqual(["2", "3", "4"]);
    expect(optionValues(screen.getByRole("combobox", { name: "Categorise Receipt" }))).toEqual(["2", "3", "4"]);
    // A row already on an archived account still shows that account as its current value.
    const archivedRow = screen.getByRole("combobox", { name: "Categorise Old expense" });
    expect(optionValues(archivedRow)).toEqual(["2", "3", "4", "5"]);
    expect(archivedRow).toHaveValue("5");
    expect(optionValues(screen.getByRole("combobox", { name: "Filter category" }))).toEqual(["1", "2", "3", "4", "5", "6"]);
  });

  it("limit opening balances to active balance-sheet accounts other than the bank control", () => {
    expect(openingBalanceTargets(categories).map((category) => category.id)).toEqual([2]);
    expect(categories.filter(isAssignableCategory).map((category) => category.id)).toEqual([2, 3, 4]);
    // A row from an older payload (no attributes) remains assignable.
    expect(isAssignableCategory({ id: 7, code: "7700", name: "Sundry", type: "Expense" })).toBe(true);
  });
});

function workspaceProps(
  overrides: Partial<ComponentProps<typeof PeriodCategoriseWorkspace>> = {},
): ComponentProps<typeof PeriodCategoriseWorkspace> {
  return {
    transactions: [],
    transactionTotal: 2,
    filteredTransactionTotal: 2,
    categorisedCount: 2,
    uncategorisedCount: 0,
    transactionPage: 1,
    transactionPageSize: 50,
    transactionPageCount: 1,
    transactionSortBy: "date",
    transactionSortDirection: "desc",
    loadingTransactions: false,
    categorisingId: null,
    categories,
    bankAccounts: [],
    transactionRules: [],
    selectedTransactionIds: [],
    visibleTransactionIds: [],
    allVisibleTransactionsSelected: false,
    bulkCategoryId: "",
    bulkCategorising: false,
    ruleForm: { pattern: "", categoryId: "", priority: "100" },
    savingRule: false,
    deletingRuleId: null,
    txFilterStatus: "",
    txFilterCategory: "",
    txFilterBank: "",
    txFilterSearch: "",
    onRefresh: vi.fn(),
    onRuleFormChange: vi.fn(),
    onCreateRule: vi.fn(),
    onDeleteRule: vi.fn(),
    onBulkCategoryChange: vi.fn(),
    onBulkCategorise: vi.fn(),
    onFilterStatusChange: vi.fn(),
    onFilterCategoryChange: vi.fn(),
    onFilterBankChange: vi.fn(),
    onSearchInputChange: vi.fn(),
    onPageChange: vi.fn(),
    onPageSizeChange: vi.fn(),
    onSortByChange: vi.fn(),
    onSortDirectionChange: vi.fn(),
    onSelectVisibleTransactions: vi.fn(),
    onToggleTransactionSelection: vi.fn(),
    onCategoriseTransaction: vi.fn(),
    ...overrides,
  };
}
