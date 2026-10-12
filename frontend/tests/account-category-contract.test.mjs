import assert from "node:assert/strict";
import test from "node:test";
import { accountCategorySchema } from "../src/lib/apiContracts.ts";

test("account categories keep explicit presentation attributes from the API", () => {
  const parsed = accountCategorySchema.parse({
    id: 7,
    code: "1400",
    name: "Bank Current Account",
    type: "Asset",
    statementLine: "CashAtBank",
    cashFlowClass: "Cash",
    systemRole: "BankControl",
    isArchived: false,
  });

  assert.equal(parsed.statementLine, "CashAtBank");
  assert.equal(parsed.cashFlowClass, "Cash");
  assert.equal(parsed.systemRole, "BankControl");
  assert.equal(parsed.isArchived, false);
});

test("account categories accept unclassified rows (null or absent attributes)", () => {
  const withNulls = accountCategorySchema.parse({
    id: 8,
    code: "6100",
    name: "Rent",
    type: "Expense",
    statementLine: null,
    cashFlowClass: null,
    systemRole: null,
  });
  assert.equal(withNulls.statementLine, null);
  assert.equal(withNulls.systemRole, null);

  const absent = accountCategorySchema.parse({ id: 9, code: "4000", name: "Sales", type: "Income" });
  assert.deepEqual(absent, { id: 9, code: "4000", name: "Sales", type: "Income" });
});

test("account categories reject an unknown statement line", () => {
  assert.throws(() =>
    accountCategorySchema.parse({ id: 10, code: "1200", name: "Bank", type: "Asset", statementLine: "Bank" }),
  );
});
