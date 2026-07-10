import { test } from "node:test";
import assert from "node:assert/strict";
import {
  validateStep,
  companyLegalSchema,
  companyAddressSchema,
  companyOnboardingSetupSchema,
  officerSchema,
} from "../src/lib/validation.ts";

// Critical UI flow: the 4-step company onboarding wizard relies on these schemas to stop bad
// data reaching the API. These tests are the regression net for that flow (G5 / BL-19).

test("validateStep(0) accepts a well-formed legal step", () => {
  const errors = validateStep(0, {
    legalName: "Connacht Digital Solutions Limited",
    companyType: "Private",
    croNumber: "123456",
    taxReference: "1234567T",
    incorporationDate: "2024-01-01",
  });
  assert.deepEqual(errors, {});
});

test("validateStep(0) requires a legal name", () => {
  const errors = validateStep(0, { legalName: "", companyType: "Private" });
  assert.ok(errors.legalName, "expected a legalName error");
});

test("validateStep(0) rejects a non-numeric CRO number", () => {
  const errors = validateStep(0, {
    legalName: "Valid Name Ltd",
    companyType: "Private",
    croNumber: "ABC123",
  });
  assert.ok(errors.croNumber, "expected a croNumber error");
});

test("validateStep(0) rejects a future incorporation date", () => {
  const errors = validateStep(0, {
    legalName: "Valid Name Ltd",
    companyType: "Private",
    incorporationDate: "2999-01-01",
  });
  assert.ok(errors.incorporationDate, "expected an incorporationDate error");
});

test("validateStep(0) requires an explicit incorporation date", () => {
  const errors = validateStep(0, {
    legalName: "Valid Name Ltd",
    companyType: "Private",
    incorporationDate: "",
  });
  assert.equal(errors.incorporationDate, "Incorporation date is required");
});

test("validateStep(2) rejects a malformed Eircode but accepts a valid one", () => {
  const bad = validateStep(2, { eircode: "!!notaneircode!!" });
  assert.ok(bad.eircode, "expected an eircode error");

  const good = validateStep(2, { eircode: "D02 AF30" });
  assert.equal(good.eircode, undefined);
});

test("officerSchema rejects an unknown role", () => {
  assert.throws(() => officerSchema.parse({ name: "A Director", role: "Janitor" }));
  assert.doesNotThrow(() => officerSchema.parse({ name: "A Director", role: "Director" }));
});

test("companyLegalSchema caps the legal name length", () => {
  assert.throws(() =>
    companyLegalSchema.parse({ legalName: "x".repeat(201), companyType: "Private" }),
  );
});

test("companyAddressSchema accepts an empty address (all fields optional)", () => {
  assert.doesNotThrow(() => companyAddressSchema.parse({}));
});

test("companyOnboardingSetupSchema requires a valid first period and opening bank", () => {
  assert.throws(() => companyOnboardingSetupSchema.parse({
    incorporationDate: "2025-01-01",
    annualReturnDate: "",
    annualReturnDateEvidenceReference: "",
    firstPeriodEnd: "",
    bankAccountName: "",
    openingBalance: "not-money",
  }));
  assert.doesNotThrow(() => companyOnboardingSetupSchema.parse({
    incorporationDate: "2025-01-01",
    annualReturnDate: "2025-07-01",
    annualReturnDateEvidenceReference: "CRO-CORE-ARD-2025",
    firstPeriodEnd: "2025-12-31",
    bankAccountName: "Main Current Account",
    openingBalance: "100.00",
  }));
  assert.throws(() => companyOnboardingSetupSchema.parse({
    incorporationDate: "2025-01-01",
    annualReturnDate: "2024-12-31",
    annualReturnDateEvidenceReference: "CRO-CORE-ARD-INVALID",
    firstPeriodEnd: "2025-12-31",
    bankAccountName: "Main Current Account",
    openingBalance: "100.00",
  }), /Annual Return Date cannot be before incorporation/);
});
