import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { PeriodCategoriseWorkspace } from "@/components/period/PeriodCategoriseWorkspace";
import type { ImportedTransaction } from "@/lib/api";

describe("PeriodCategoriseWorkspace pagination", () => {
  it("renders semantic labelled mobile rows with accessible transaction evidence and entry sides", () => {
    const { container } = render(
      <PeriodCategoriseWorkspace
        {...workspaceProps({
          transactions: [
            {
              id: 1,
              date: "2025-01-03",
              description: "Customer receipt with a long reference that must wrap",
              amount: 1_250,
              categoryId: 1,
              confidenceScore: 0.8,
              manualOverride: false,
            },
            {
              id: 2,
              date: "2025-01-04",
              description: "Supplier payment",
              amount: -240,
              manualOverride: false,
            },
          ],
          filteredTransactionTotal: 2,
          transactionTotal: 2,
          transactionPageCount: 1,
          visibleTransactionIds: [1, 2],
        })}
      />,
    );

    expect(screen.getByRole("table", { name: "Transactions to categorise" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Transactions to categorise scrollable table" }))
      .toHaveAttribute("data-responsive", "card");
    expect(container.querySelector('td[data-label="Date"]')).toHaveTextContent("3/1/2025");
    expect(container.querySelector('td[data-label="Description"]')).toHaveTextContent("Customer receipt with a long reference that must wrap");
    expect(container.querySelector('td[data-label="Amount and entry"]')).toHaveTextContent("Debit to bank");
    expect(screen.getByText("Credit to bank")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Categorise Customer receipt with a long reference that must wrap" })).toBeInTheDocument();
    expect(container.querySelector('td[data-label="Confidence"]')).toHaveTextContent("80%");
  });

  it("makes every page reachable when more than 50 transactions match", async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    const onPageSizeChange = vi.fn();
    const onSortByChange = vi.fn();
    const onSortDirectionChange = vi.fn();

    render(
      <PeriodCategoriseWorkspace
        {...workspaceProps({
          transactions: transactions(1, 50),
          transactionTotal: 75,
          filteredTransactionTotal: 75,
          categorisedCount: 40,
          uncategorisedCount: 35,
          transactionPage: 1,
          transactionPageCount: 2,
          onPageChange,
          onPageSizeChange,
          onSortByChange,
          onSortDirectionChange,
        })}
      />,
    );

    expect(screen.getByText(/Showing 1–50 of 75 matching transactions/)).toBeInTheDocument();
    expect(screen.getByText("Page 1 of 2")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Next" }));
    expect(onPageChange).toHaveBeenCalledWith(2);

    await user.selectOptions(screen.getByRole("combobox", { name: "Rows per page" }), "100");
    expect(onPageSizeChange).toHaveBeenCalledWith(100);

    await user.selectOptions(screen.getByRole("combobox", { name: "Sort by" }), "amount");
    expect(onSortByChange).toHaveBeenCalledWith("amount");

    await user.selectOptions(screen.getByRole("combobox", { name: "Sort direction" }), "asc");
    expect(onSortDirectionChange).toHaveBeenCalledWith("asc");
  });

  it("renders the final boundary page of a 125-row result and states page-only selection scope", async () => {
    const user = userEvent.setup();
    const onSelectVisibleTransactions = vi.fn();
    const currentPageIds = transactionIds(101, 125);

    render(
      <PeriodCategoriseWorkspace
        {...workspaceProps({
          transactions: transactions(101, 125),
          transactionTotal: 125,
          filteredTransactionTotal: 125,
          categorisedCount: 80,
          uncategorisedCount: 45,
          transactionPage: 3,
          transactionPageCount: 3,
          selectedTransactionIds: [...transactionIds(1, 50), ...currentPageIds],
          visibleTransactionIds: currentPageIds,
          allVisibleTransactionsSelected: true,
          onSelectVisibleTransactions,
        })}
      />,
    );

    expect(screen.getByText(/Showing 101–125 of 125 matching transactions/)).toBeInTheDocument();
    expect(screen.getByText("Page 3 of 3")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();

    const pageSelection = screen.getByRole("checkbox", { name: "Select current page" });
    expect(pageSelection).toBeChecked();
    expect(screen.getByText(/75 selected across pages; 25 of 25 on this page/)).toHaveTextContent(
      "Select current page never selects every matching result. Changing a filter clears the selection.",
    );

    await user.click(pageSelection);
    expect(onSelectVisibleTransactions).toHaveBeenCalledWith(false);
  });

  it("announces when a filter clears selection without implying pagination clears it", () => {
    render(
      <PeriodCategoriseWorkspace
        {...workspaceProps({
          selectedTransactionIds: [1, 2],
          selectionAnnouncement: "Selection cleared because the category filter changed.",
        })}
      />,
    );

    expect(screen.getByText("Selection cleared because the category filter changed.")).toHaveAttribute(
      "aria-live",
      "polite",
    );
    expect(screen.getByText(/Select current page never selects every matching result/)).toHaveTextContent(
      "Changing a filter clears the selection.",
    );
  });

  it("selects a transaction through its keyboard-accessible checkbox", async () => {
    const user = userEvent.setup();
    const onToggleTransactionSelection = vi.fn();
    render(
      <PeriodCategoriseWorkspace
        {...workspaceProps({
          transactions: [{
            id: 17,
            date: "2025-01-05",
            description: "Keyboard bank receipt",
            amount: 500,
            manualOverride: false,
          }],
          transactionTotal: 1,
          filteredTransactionTotal: 1,
          visibleTransactionIds: [17],
          onToggleTransactionSelection,
        })}
      />,
    );

    const checkbox = screen.getByRole("checkbox", { name: "Select Keyboard bank receipt" });
    checkbox.focus();
    await user.keyboard(" ");

    expect(onToggleTransactionSelection).toHaveBeenCalledWith(17, true);
  });
});

function workspaceProps(
  overrides: Partial<ComponentProps<typeof PeriodCategoriseWorkspace>> = {},
): ComponentProps<typeof PeriodCategoriseWorkspace> {
  const currentTransactions = transactions(1, 50);
  return {
    transactions: currentTransactions,
    transactionTotal: 125,
    filteredTransactionTotal: 125,
    categorisedCount: 80,
    uncategorisedCount: 45,
    transactionPage: 1,
    transactionPageSize: 50,
    transactionPageCount: 3,
    transactionSortBy: "date",
    transactionSortDirection: "desc",
    loadingTransactions: false,
    categorisingId: null,
    categories: [{ id: 1, code: "4000", name: "Sales", type: "Income" }],
    bankAccounts: [],
    transactionRules: [],
    selectedTransactionIds: [],
    visibleTransactionIds: currentTransactions.map((transaction) => transaction.id),
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

function transactions(first: number, last: number): ImportedTransaction[] {
  return transactionIds(first, last).map((id) => ({
    id,
    date: "2025-01-01",
    description: `Transaction ${id}`,
    amount: id,
    manualOverride: false,
  }));
}

function transactionIds(first: number, last: number): number[] {
  return Array.from({ length: last - first + 1 }, (_, index) => first + index);
}
