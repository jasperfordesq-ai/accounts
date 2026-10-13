import assert from "node:assert/strict";
import test from "node:test";
import {
  connectorActivityPageSchema,
  connectorApprovalViewSchema,
  connectorSessionSummarySchema,
} from "../src/lib/apiContracts.ts";

// The API serialises with WhenWritingNull, so null properties are omitted from the JSON.
test("a pending approval without its null fields parses, with them normalised to null", () => {
  const parsed = connectorApprovalViewSchema.parse({
    approvalId: "0f8fad5b-d9cb-469f-a165-70867728950e",
    summary: "Delete officers, company Example Limited",
    method: "DELETE",
    routePattern: "/api/companies/{companyId}/officers/{id}",
    createdAtUtc: "2026-10-13T09:00:00Z",
    expiresAtUtc: "2026-10-13T09:05:00Z",
  });
  assert.equal(parsed.resourceId, null);
  assert.equal(parsed.approvedAtUtc, null);
  assert.equal(parsed.consumedAtUtc, null);
});

test("an unused agent and an activity row without optional fields parse", () => {
  const session = connectorSessionSummarySchema.parse({
    familyId: "0f8fad5b-d9cb-469f-a165-70867728950e",
    userId: 1,
    accessLevel: "Write",
    dataScope: "Withheld",
    connectedAtUtc: "2026-10-13T09:00:00Z",
    expiresAtUtc: "2026-11-12T09:00:00Z",
  });
  assert.equal(session.deviceLabel, null);
  assert.equal(session.lastUsedAtUtc, null);

  const page = connectorActivityPageSchema.parse({
    data: [{
      id: 1,
      sessionFamilyId: "0f8fad5b-d9cb-469f-a165-70867728950e",
      accessLevel: "Write",
      dataScope: "Withheld",
      method: "POST",
      routePattern: "/api/companies/{companyId}/officers",
      statusCode: 201,
      occurredAtUtc: "2026-10-13T09:00:00Z",
    }],
    page: 1,
    pageSize: 25,
    hasMore: false,
  });
  assert.equal(page.data[0].reason, null);
  assert.equal(page.data[0].companyId, null);
});

test("explicit nulls are still accepted", () => {
  const parsed = connectorApprovalViewSchema.parse({
    approvalId: "0f8fad5b-d9cb-469f-a165-70867728950e",
    summary: "x",
    method: "DELETE",
    routePattern: "/r",
    resourceId: null,
    createdAtUtc: "2026-10-13T09:00:00Z",
    expiresAtUtc: "2026-10-13T09:05:00Z",
    approvedAtUtc: null,
    consumedAtUtc: null,
  });
  assert.equal(parsed.approvedAtUtc, null);
});
