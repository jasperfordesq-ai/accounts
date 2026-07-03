// BL-05 / BL-25: verifies the year-end API client functions (loans, director loans, share
// capital, and the new update/PUT helpers) issue the correct HTTP method and URL. Mocks the
// global fetch so no server is needed.
import assert from "node:assert/strict";
import {
  // loans
  getLoans,
  createLoan,
  updateLoan,
  deleteLoan,
  // loan snapshots
  getLoanSnapshots,
  createLoanSnapshot,
  updateLoanSnapshot,
  deleteLoanSnapshot,
  // director loans
  getDirectorLoans,
  createDirectorLoan,
  updateDirectorLoan,
  deleteDirectorLoan,
  // share capital
  getShareCapital,
  createShareCapital,
  updateShareCapital,
  deleteShareCapital,
  // year-end row updates (BL-25)
  updateDebtor,
  updateCreditor,
  updateFixedAsset,
  updateInventory,
  updateDividend,
  getProductionReadinessReport,
} from "../src/lib/api.ts";

const calls = [];
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), method: (init?.method ?? "GET").toUpperCase() });
  return new Response(JSON.stringify({ ok: true }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
};

async function expect(fn, method, url) {
  calls.length = 0;
  await fn();
  assert.equal(calls.length, 1, `expected exactly one fetch for ${method} ${url}`);
  assert.equal(calls[0].method, method, `method for ${url}`);
  assert.equal(calls[0].url, url, `url for ${method}`);
}

const loan = {
  lender: "Bank",
  originalAmount: 1000,
  balance: 1000,
  interestRate: 5,
  isDirectorLoan: false,
  dueWithinYear: 100,
  dueAfterYear: 900,
};
const snapshot = {
  loanId: 7,
  openingBalance: 0,
  drawdowns: 1000,
  repayments: 0,
  closingBalance: 1000,
  dueWithinYear: 100,
  dueAfterYear: 900,
};
const dirLoan = {
  directorId: 3,
  openingBalance: 0,
  advances: 500,
  repayments: 0,
  closingBalance: 500,
  interestRate: 5,
  interestCharged: 0,
  isDocumented: true,
  maxBalanceDuringYear: 500,
};
const share = {
  shareClass: "Ordinary",
  nominalValue: 1,
  numberIssued: 100,
  totalValue: 100,
  isFullyPaid: true,
};

// Loans (company-scoped)
await expect(() => getLoans(1), "GET", "/api/companies/1/loans");
await expect(() => createLoan(1, loan), "POST", "/api/companies/1/loans");
await expect(() => updateLoan(1, 9, loan), "PUT", "/api/companies/1/loans/9");
await expect(() => deleteLoan(1, 9), "DELETE", "/api/companies/1/loans/9");

// Loan snapshots (per period)
await expect(() => getLoanSnapshots(1, 2), "GET", "/api/companies/1/periods/2/loan-balance-snapshots");
await expect(() => createLoanSnapshot(1, 2, snapshot), "POST", "/api/companies/1/periods/2/loan-balance-snapshots");
await expect(() => updateLoanSnapshot(1, 2, 9, snapshot), "PUT", "/api/companies/1/periods/2/loan-balance-snapshots/9");
await expect(() => deleteLoanSnapshot(1, 2, 9), "DELETE", "/api/companies/1/periods/2/loan-balance-snapshots/9");

// Director loans (per period)
await expect(() => getDirectorLoans(1, 2), "GET", "/api/companies/1/periods/2/director-loans");
await expect(() => createDirectorLoan(1, 2, dirLoan), "POST", "/api/companies/1/periods/2/director-loans");
await expect(() => updateDirectorLoan(1, 2, 9, dirLoan), "PUT", "/api/companies/1/periods/2/director-loans/9");
await expect(() => deleteDirectorLoan(1, 2, 9), "DELETE", "/api/companies/1/periods/2/director-loans/9");

// Share capital (company-scoped)
await expect(() => getShareCapital(1), "GET", "/api/companies/1/share-capital");
await expect(() => createShareCapital(1, share), "POST", "/api/companies/1/share-capital");
await expect(() => updateShareCapital(1, 9, share), "PUT", "/api/companies/1/share-capital/9");
await expect(() => deleteShareCapital(1, 9), "DELETE", "/api/companies/1/share-capital/9");

// Year-end row updates (BL-25)
await expect(() => updateDebtor(1, 2, 9, { name: "x", amount: 1, type: "Trade" }), "PUT", "/api/companies/1/periods/2/debtors/9");
await expect(() => updateCreditor(1, 2, 9, { name: "x", amount: 1, type: "Trade", dueWithinYear: true }), "PUT", "/api/companies/1/periods/2/creditors/9");
await expect(() => updateFixedAsset(1, 9, { name: "x", category: "Equipment", cost: 1, acquisitionDate: "2025-01-01", usefulLifeYears: 4, depreciationMethod: "StraightLine" }), "PUT", "/api/companies/1/fixed-assets/9");
await expect(() => updateInventory(1, 2, 9, { description: "x", value: 1, valuationMethod: "FIFO" }), "PUT", "/api/companies/1/periods/2/inventory/9");
await expect(() => updateDividend(1, 2, 9, { amount: 1 }), "PUT", "/api/companies/1/periods/2/dividends/9");

// System assurance report
await expect(() => getProductionReadinessReport(), "GET", "/api/system/production-readiness");

console.log("verify-api-client: all checked client routes OK");
