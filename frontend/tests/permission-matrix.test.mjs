import assert from "node:assert/strict";
import test from "node:test";

import {
  authenticatedRoutePolicies,
  canAccessRoute,
  canPerformAction,
  permissionActionCatalog,
  permissionsForRole,
} from "../src/lib/permissions.ts";

const roles = ["Owner", "Accountant", "Reviewer", "Client"];

test("canonical role matrix matches the backend write model", () => {
  assert.deepEqual(permissionsForRole("Owner"), {
    canRead: true,
    canCreateCompany: true,
    canDeleteCompany: true,
    canManageUsers: true,
    canWriteWorkingPapers: true,
    canReadInternalWorkingPapers: true,
    canReview: true,
    canApprove: true,
    canReviewReleaseEvidence: true,
  });
  assert.deepEqual(permissionsForRole("Accountant"), {
    canRead: true,
    canCreateCompany: false,
    canDeleteCompany: false,
    canManageUsers: false,
    canWriteWorkingPapers: true,
    canReadInternalWorkingPapers: true,
    canReview: false,
    canApprove: false,
    canReviewReleaseEvidence: false,
  });
  assert.deepEqual(permissionsForRole("Reviewer"), {
    canRead: true,
    canCreateCompany: false,
    canDeleteCompany: false,
    canManageUsers: false,
    canWriteWorkingPapers: false,
    canReadInternalWorkingPapers: true,
    canReview: true,
    canApprove: true,
    canReviewReleaseEvidence: true,
  });
  assert.deepEqual(permissionsForRole("Client"), {
    canRead: true,
    canCreateCompany: false,
    canDeleteCompany: false,
    canManageUsers: false,
    canWriteWorkingPapers: false,
    canReadInternalWorkingPapers: false,
    canReview: false,
    canApprove: false,
    canReviewReleaseEvidence: false,
  });
});

test("role matching is case-insensitive and unknown roles fail closed for writes", () => {
  assert.equal(permissionsForRole(" reviewer ").canReview, true);
  const unknown = permissionsForRole("UnexpectedRole");
  assert.equal(unknown.canRead, true);
  assert.equal(Object.entries(unknown).filter(([key]) => key !== "canRead").every(([, value]) => value === false), true);
});

test("every authenticated user-facing route has an explicit deep-link policy", () => {
  assert.deepEqual(
    authenticatedRoutePolicies.map((policy) => policy.id).sort(),
    [
      "change-password",
      "charity",
      "classification",
      "company-detail",
      "dashboard",
      "new-company",
      "notes",
      "period-workspace",
      "production-readiness",
      "statements",
      "user-administration",
      "workbench-preview",
      "working-papers",
      "year-end",
    ],
  );

  for (const policy of authenticatedRoutePolicies) {
    const pathname = policy.pathTemplate
      .replace(":companyId", "7")
      .replace(":periodId", "3");
    for (const role of roles) {
      const expected = policy.requiredPermission === "canCreateCompany"
        ? role === "Owner"
        : policy.requiredPermission === "canManageUsers"
          ? role === "Owner"
        : policy.requiredPermission === "canReviewReleaseEvidence"
          ? role === "Owner" || role === "Reviewer"
          : policy.requiredPermission === "canReadInternalWorkingPapers"
            ? role !== "Client"
          : true;
      assert.equal(canAccessRoute(role, pathname), expected, `${role} ${pathname}`);
    }
  }
});

test("every audited UI action resolves through the canonical role capability matrix", () => {
  assert.equal(permissionActionCatalog.length >= 65, true);
  assert.equal(new Set(permissionActionCatalog.map((action) => action.id)).size, permissionActionCatalog.length);

  for (const action of permissionActionCatalog) {
    if (action.method !== "LOCAL") assert.match(action.path, /^\/api\//);
    for (const role of roles) {
      assert.equal(
        canPerformAction(role, action.id),
        permissionsForRole(role)[action.requiredPermission],
        `${role} ${action.id}`,
      );
    }
  }

  assert.equal(canPerformAction("Owner", "missing-action"), false);
});
