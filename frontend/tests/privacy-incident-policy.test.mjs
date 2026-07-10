import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const verifier = path.join(repoRoot, "scripts/verify-privacy-incident-exercise.mjs");
const fixturePath = path.join(
  repoRoot,
  "backend/Accounts.Tests/Fixtures/privacy-incident-exercise.synthetic.json",
);

test("synthetic privacy exercise proves engineering controls while retaining real-world blockers", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "accounts-privacy-exercise-"));
  const reportPath = path.join(temp, "report.json");
  execFileSync(process.execPath, [verifier, fixturePath, reportPath], { cwd: repoRoot });
  const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));

  assert.equal(report.passed, true);
  assert.equal(report.status, "engineering-passed-release-blocked");
  assert.deepEqual(report.measuredMinutes, {
    notificationRouting: 2,
    containment: 5,
    evidencePreservation: 7,
    recovery: 20,
    review: 30,
  });
  assert.equal(report.recoveryChecks.tenantIsolation, true);
  assert.equal(report.recoveryChecks.auditIntegrity, true);
  assert.equal(report.recoveryChecks.financialIntegrity, true);
  assert.equal(report.syntheticControls.providerOrRegulatorContacted, false);
  assert.equal(report.syntheticControls.containsClientData, false);
  assert.equal(report.releaseBlockers.length, 3);
});

test("PII, false recovery, chronological drift, and synthetic external claims fail closed", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "accounts-privacy-exercise-negative-"));
  const changedPath = path.join(temp, "unsafe.json");
  const reportPath = path.join(temp, "report.json");
  const changed = JSON.parse(fs.readFileSync(fixturePath, "utf8"));
  changed.reviewedAtUtc = "2026-07-10T11:30:00Z";
  changed.auditIntegrityVerified = false;
  changed.providerOrRegulatorContacted = true;
  changed.containsClientData = true;
  changed.accidentalContact = "client@example.ie";
  fs.writeFileSync(changedPath, `${JSON.stringify(changed, null, 2)}\n`, "utf8");

  const run = spawnSync(process.execPath, [verifier, changedPath, reportPath], {
    cwd: repoRoot,
    encoding: "utf8",
  });
  const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));

  assert.notEqual(run.status, 0);
  assert.equal(report.passed, false);
  assert.ok(report.failures.some((failure) => failure.includes("chronological")));
  assert.ok(report.failures.some((failure) => failure.includes("auditIntegrityVerified")));
  assert.ok(report.failures.some((failure) => failure.includes("provider or regulator")));
  assert.ok(report.failures.some((failure) => failure.includes("client data")));
  assert.ok(report.failures.some((failure) => failure.includes("PII/secret")));
});
