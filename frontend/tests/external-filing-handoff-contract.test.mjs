import assert from "node:assert/strict";
import test from "node:test";

import { externalFilingHandoffWorkspaceSchema } from "../src/lib/externalFilingHandoff.ts";
import { externalFilingHandoffWorkspaceFixture } from "./fixtures/external-filing-handoff.ts";

const clone = () => structuredClone(externalFilingHandoffWorkspaceFixture);

test("external handoff contract accepts immutable support-only CRO and Revenue chains", () => {
  const parsed = externalFilingHandoffWorkspaceSchema.parse(clone());
  assert.equal(parsed.directCroSubmissionSupported, false);
  assert.equal(parsed.directRosSubmissionSupported, false);
  assert.equal(parsed.snapshots[0].document.isCompleteExternalReturn, false);
  assert.equal(parsed.snapshots[0].document.readyForManualHandoff, true);
  assert.equal(parsed.snapshots[1].document.supersedesArtifactSha256, parsed.snapshots[0].artifactSha256);
  assert.equal(parsed.snapshots[2].document.revenueCt1Support.isCompleteCt1Return, false);
  assert.equal(parsed.outcomes[2].snapshotArtifactSha256, parsed.snapshots[0].artifactSha256);
  assert.equal(parsed.outcomes[0].externalReference, null);
  assert.equal(parsed.outcomes[3].supersedingSnapshotArtifactSha256, parsed.snapshots[1].artifactSha256);
  assert.equal(parsed.snapshots[0].document.sources[0].reviewedAtUtc, "2026-07-10T00:00:00Z");
});

test("external handoff contract rejects direct submission and every complete-external-return claim", () => {
  for (const mutate of [
    (workspace) => { workspace.directCroSubmissionSupported = true; },
    (workspace) => { workspace.directRosSubmissionSupported = true; },
    (workspace) => { workspace.snapshots[0].document.directSubmissionSupported = true; },
    (workspace) => { workspace.snapshots[0].document.isCompleteExternalReturn = true; },
    (workspace) => { workspace.snapshots[2].document.revenueCt1Support.isCompleteCt1Return = true; },
    (workspace) => { workspace.snapshots[2].document.revenueCt1Support.outputKind = "complete-ct1"; },
  ]) {
    const workspace = clone();
    mutate(workspace);
    assert.equal(externalFilingHandoffWorkspaceSchema.safeParse(workspace).success, false);
  }
});

test("external handoff contract excludes raw protected identifiers and unmasked presenter IDs", () => {
  const rawOfficerIdentifier = clone();
  rawOfficerIdentifier.snapshots[0].document.croB1.officers[0].ppsn = "1234567A";
  assert.equal(externalFilingHandoffWorkspaceSchema.safeParse(rawOfficerIdentifier).success, false);

  const rawPresenterIdentifier = clone();
  rawPresenterIdentifier.authorities[0].maskedPresenterOrTain = "TAIN-123456";
  assert.equal(externalFilingHandoffWorkspaceSchema.safeParse(rawPresenterIdentifier).success, false);

  for (const rawValue of ["1234567A", "1980-01-01", "31/12/1980", "director@example.ie"]) {
    for (const property of ["identityEvidenceReference", "otherDirectorshipsEvidenceReference"]) {
      const rawReference = clone();
      rawReference.snapshots[0].document.croB1.officers[0][property] = rawValue;
      assert.equal(
        externalFilingHandoffWorkspaceSchema.safeParse(rawReference).success,
        false,
        `${property} accepted raw PII value ${rawValue}`,
      );
    }
  }
});

test("external handoff contract rejects tenant drift and broken predecessor chains", () => {
  for (const mutate of [
    (workspace) => { workspace.authorities[0].tenantId = 99; },
    (workspace) => { workspace.snapshots[0].document.companyId = 99; },
    (workspace) => { workspace.snapshots[1].document.supersedesArtifactSha256 = "0".repeat(64); },
    (workspace) => { workspace.snapshots[1].document.supersedesSnapshotId = workspace.snapshots[2].document.snapshotId; },
    (workspace) => { workspace.snapshots[1].document.version = 3; },
  ]) {
    const workspace = clone();
    mutate(workspace);
    assert.equal(externalFilingHandoffWorkspaceSchema.safeParse(workspace).success, false);
  }
});

test("external handoff contract rejects outcomes bound to a missing or different artifact", () => {
  for (const mutate of [
    (workspace) => { workspace.outcomes[0].snapshotId = "99999999-9999-4999-8999-999999999999"; },
    (workspace) => { workspace.outcomes[0].snapshotArtifactSha256 = "0".repeat(64); },
    (workspace) => { workspace.snapshots[0].artifactSha256 = "1".repeat(64); },
  ]) {
    const workspace = clone();
    mutate(workspace);
    assert.equal(externalFilingHandoffWorkspaceSchema.safeParse(workspace).success, false);
  }
});

test("external handoff contract separates internal readiness from external evidence and binds supersession", () => {
  for (const mutate of [
    (workspace) => {
      workspace.outcomes[0].externalReference = "FABRICATED-EXTERNAL";
      workspace.outcomes[0].externalOccurredAtUtc = "2026-07-09T10:00:00Z";
      workspace.outcomes[0].evidenceReference = "Fabricated external evidence";
      workspace.outcomes[0].evidenceSha256 = "a".repeat(64);
    },
    (workspace) => { workspace.outcomes[1].externalReference = null; },
    (workspace) => { workspace.outcomes[2].evidenceSha256 = null; },
    (workspace) => { workspace.outcomes[3].supersedingSnapshotId = null; },
    (workspace) => { workspace.outcomes[3].supersedingSnapshotArtifactSha256 = "0".repeat(64); },
    (workspace) => { workspace.outcomes[3].supersedingSnapshotId = workspace.snapshots[2].document.snapshotId; },
  ]) {
    const workspace = clone();
    mutate(workspace);
    assert.equal(externalFilingHandoffWorkspaceSchema.safeParse(workspace).success, false);
  }
});
