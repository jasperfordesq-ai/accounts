export type TransactionSelectionScopeChange = "page" | "pageSize" | "sort" | "filter" | "period";

export function selectionAfterTransactionScopeChange(
  selectedIds: readonly number[],
  change: TransactionSelectionScopeChange,
): number[] {
  if (change === "filter" || change === "period") return [];
  return [...new Set(selectedIds)];
}

export function setCurrentPageTransactionSelection(
  selectedIds: readonly number[],
  currentPageIds: readonly number[],
  selected: boolean,
): number[] {
  const next = new Set(selectedIds);
  for (const id of currentPageIds) {
    if (selected) next.add(id);
    else next.delete(id);
  }
  return [...next];
}

export function toggleTransactionSelection(
  selectedIds: readonly number[],
  transactionId: number,
  selected: boolean,
): number[] {
  const next = new Set(selectedIds);
  if (selected) next.add(transactionId);
  else next.delete(transactionId);
  return [...next];
}
