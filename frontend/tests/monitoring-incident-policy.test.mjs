import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { evaluateMonitoringExercise } from "../../scripts/verify-monitoring-incident-exercise.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const fixture = JSON.parse(fs.readFileSync(
  path.join(repoRoot, "backend/Accounts.Tests/Fixtures/monitoring-incident-exercise.synthetic.json"),
  "utf8",
));

test("synthetic incident exercise proves engineering controls but remains release-blocked", () => {
  const result = evaluateMonitoringExercise(fixture, {
    allowSynthetic: true,
    expectedCommitSha: fixture.releaseCommitSha,
    expectedRunUrl: fixture.githubActionsRunUrl,
  });
  assert.deepEqual(result.failures, []);
  assert.deepEqual(result.releaseBlockers, [
    "Synthetic engineering exercise is not real provider/operator confirmation.",
  ]);
  assert.equal(result.metrics.alertLatencyMinutes, 1);
  assert.equal(result.metrics.acknowledgementMinutes, 3);
  assert.equal(result.metrics.escalationMinutes, 28);
});

test("synthetic exercise cannot masquerade as real provider evidence", () => {
  const result = evaluateMonitoringExercise(fixture);
  assert.ok(result.failures.includes("Synthetic engineering exercise requires explicit allowance."));
  assert.ok(result.releaseBlockers.length > 0);
});

test("late alerts, failed redaction, incomplete recovery, and candidate drift fail closed", () => {
  const changed = structuredClone(fixture);
  changed.releaseCommitSha = "d".repeat(40);
  changed.timeline.acknowledgedAtUtc = "2026-07-10T09:20:00Z";
  changed.redactionReview.clientDataAbsent = false;
  changed.responseExercise.recoveryWalkthroughCompleted = false;
  const result = evaluateMonitoringExercise(changed, {
    allowSynthetic: true,
    expectedCommitSha: fixture.releaseCommitSha,
  });
  assert.ok(result.failures.some((failure) => failure.includes("candidate")));
  assert.ok(result.failures.some((failure) => failure.includes("acknowledgement exceeded")));
  assert.ok(result.failures.some((failure) => failure.includes("clientDataAbsent")));
  assert.ok(result.failures.some((failure) => failure.includes("recoveryWalkthroughCompleted")));
});

test("client monitoring evidence requires provider, log-correlation, and privacy proof", () => {
  const incidentRunbook = fs.readFileSync(
    path.join(repoRoot, "Docs/operations/monitoring-incident-response.md"),
    "utf8",
  );
  const providerTemplate = fs.readFileSync(
    path.join(repoRoot, "Docs/release-evidence/monitoring-provider-confirmation-template.md"),
    "utf8",
  );
  const structuredVerifier = fs.readFileSync(
    path.join(repoRoot, "scripts/verify-structured-logs.ps1"),
    "utf8",
  );

  assert.match(incidentRunbook, /fixed event codes/i);
  assert.match(incidentRunbook, /exception messages, stack traces, response bodies, request bodies/i);
  assert.match(providerTemplate, /Client event id:/);
  assert.match(providerTemplate, /Matched client monitoring line: yes \/ no/);
  assert.match(providerTemplate, /Synthetic sensitive markers absent: yes \/ no/);
  assert.match(providerTemplate, /Client provider event URL or reference:/);
  assert.match(structuredVerifier, /matchedClientMonitoringLine/);
  assert.match(structuredVerifier, /syntheticSensitiveMarkersAbsent/);
});
