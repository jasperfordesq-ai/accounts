import { test } from "node:test";
import assert from "node:assert/strict";
import {
  formatCompanyType,
  formatDateIE,
  formatPeriodRange,
} from "../src/lib/format.ts";

// These format helpers render company type, dates and period ranges across every page; a wrong
// label or a thrown error on missing data is a visible defect (G5 / BL-19).

test("formatCompanyType maps known codes to Irish abbreviations", () => {
  assert.equal(formatCompanyType("Private"), "LTD");
  assert.equal(formatCompanyType("DesignatedActivityCompany"), "DAC");
  assert.equal(formatCompanyType("CompanyLimitedByGuarantee"), "CLG");
  assert.equal(formatCompanyType("PublicLimitedCompany"), "PLC");
});

test("formatCompanyType falls back to a humanised label and a default", () => {
  assert.equal(formatCompanyType(undefined), "Company");
  assert.equal(formatCompanyType("SomeOtherType"), "Some Other Type");
});

test("formatDateIE returns a placeholder for missing/invalid input", () => {
  assert.equal(formatDateIE(undefined), "-");
  assert.equal(formatDateIE("not-a-date"), "not-a-date");
});

test("formatDateIE renders a real date in Irish format", () => {
  const formatted = formatDateIE("2025-12-31");
  assert.match(formatted, /2025/);
  assert.match(formatted, /Dec/i);
  assert.match(formatted, /31/);
});

test("formatPeriodRange handles missing bounds and a full range", () => {
  assert.equal(formatPeriodRange(undefined, undefined), "Period not set");
  assert.equal(formatPeriodRange("2025-01-01", undefined), "Period not set");
  const range = formatPeriodRange("2025-01-01", "2025-12-31");
  assert.match(range, /2025/);
  assert.match(range, /to/);
});
