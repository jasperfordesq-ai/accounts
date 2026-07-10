import assert from "node:assert/strict";
import test from "node:test";
import { getTransactions } from "../src/lib/api.ts";
import {
  selectionAfterTransactionScopeChange,
  setCurrentPageTransactionSelection,
  toggleTransactionSelection,
} from "../src/lib/transactionSelection.ts";

test("transaction selection remains ID-backed across three pages of 125 rows", () => {
  const firstPage = transactionIds(1, 50);
  const secondPage = transactionIds(51, 100);
  const thirdPage = transactionIds(101, 125);

  let selected = setCurrentPageTransactionSelection([], firstPage, true);
  selected = selectionAfterTransactionScopeChange(selected, "page");
  selected = setCurrentPageTransactionSelection(selected, secondPage, true);

  assert.equal(selected.length, 100);
  assert.deepEqual(selected.slice(0, 3), [1, 2, 3]);
  assert.deepEqual(selected.slice(-3), [98, 99, 100]);

  selected = setCurrentPageTransactionSelection(selected, thirdPage, true);
  assert.equal(selected.length, 125);

  selected = setCurrentPageTransactionSelection(selected, secondPage, false);
  assert.equal(selected.length, 75);
  assert.equal(selected.includes(50), true);
  assert.equal(selected.includes(51), false);
  assert.equal(selected.includes(100), false);
  assert.equal(selected.includes(101), true);
});

test("page-size and sort changes retain explicit IDs while filter and period changes clear them", () => {
  const selected = transactionIds(1, 60);

  assert.deepEqual(selectionAfterTransactionScopeChange(selected, "pageSize"), selected);
  assert.deepEqual(selectionAfterTransactionScopeChange(selected, "sort"), selected);
  assert.deepEqual(selectionAfterTransactionScopeChange(selected, "filter"), []);
  assert.deepEqual(selectionAfterTransactionScopeChange(selected, "period"), []);

  assert.deepEqual(toggleTransactionSelection([1, 2], 2, true), [1, 2]);
  assert.deepEqual(toggleTransactionSelection([1, 2], 2, false), [1]);
});

test("getTransactions sends page, page size, filters and stable sort to the server", async () => {
  const originalFetch = globalThis.fetch;
  let requestedUrl = "";
  globalThis.fetch = async (input) => {
    requestedUrl = String(input);
    return new Response(JSON.stringify({
      total: 125,
      items: [],
      page: 3,
      pageSize: 25,
      totalPages: 5,
      hasPreviousPage: true,
      hasNextPage: true,
      sortBy: "amount",
      sortDirection: "asc",
      aggregates: { total: 140, categorised: 80, uncategorised: 60 },
    }), { status: 200, headers: { "Content-Type": "application/json" } });
  };

  try {
    await getTransactions(7, 9, 3, 25, {
      uncategorised: true,
      categoryId: 42,
      bankAccountId: 8,
      search: "insurance premium",
      sortBy: "amount",
      sortDirection: "asc",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }

  const query = new URL(requestedUrl, "http://accounts.test");
  assert.equal(query.pathname, "/api/companies/7/periods/9/transactions");
  assert.deepEqual(Object.fromEntries(query.searchParams), {
    page: "3",
    pageSize: "25",
    uncategorised: "true",
    categoryId: "42",
    bankAccountId: "8",
    search: "insurance premium",
    sortBy: "amount",
    sortDirection: "asc",
  });
});

function transactionIds(first, last) {
  return Array.from({ length: last - first + 1 }, (_, index) => first + index);
}
