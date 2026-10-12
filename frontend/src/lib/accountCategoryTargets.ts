import type { AccountCategory } from "@/lib/api";

// Archived accounts and the bank control account are not posting targets: the server refuses them for
// categorisation, bulk categorisation, transaction rules and opening balances, so pickers omit them.
export function isAssignableCategory(category: AccountCategory): boolean {
  return !category.isArchived && category.systemRole !== "BankControl";
}

// Opening balances go only to balance-sheet accounts; the bank's own opening balance is entered on the
// bank account.
export function openingBalanceTargets(categories: AccountCategory[]): AccountCategory[] {
  return categories.filter(
    (category) => category.type !== "Income" && category.type !== "Expense" && isAssignableCategory(category),
  );
}
