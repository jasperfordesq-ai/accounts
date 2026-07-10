import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const fixturePath = resolve(
  process.argv[2] ?? "backend/Accounts.Tests/Fixtures/privacy-incident-exercise.synthetic.json",
);
const outputPath = resolve(process.argv[3] ?? "privacy-incident-exercise-report.json");
const raw = await readFile(fixturePath);
const fixture = JSON.parse(raw.toString("utf8"));
const failures = [];
const sha256 = (value) => /^[a-f0-9]{64}$/.test(value ?? "");
const utc = (value) =>
  typeof value === "string" && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/.test(value);

if (fixture.schemaVersion !== "privacy-incident-exercise-v1") failures.push("schemaVersion is invalid");
if (fixture.status !== "engineering-passed-release-blocked") failures.push("synthetic status must remain release-blocked");
if (fixture.exerciseKind !== "synthetic-tabletop") failures.push("exerciseKind must identify a synthetic tabletop");
if (!/^[a-f0-9]{40}$/.test(fixture.releaseCandidate ?? "")) failures.push("releaseCandidate must be a 40-character commit identity");
if (!sha256(fixture.scenarioSha256)) failures.push("scenarioSha256 is invalid");
if (!sha256(fixture.evidenceManifestSha256)) failures.push("evidenceManifestSha256 is invalid");

const milestoneNames = [
  "detectedAtUtc",
  "notificationRoutedAtUtc",
  "containedAtUtc",
  "evidencePreservedAtUtc",
  "recoveryVerifiedAtUtc",
  "reviewedAtUtc",
];
const milestoneValues = milestoneNames.map((name) => fixture[name]);
for (const [index, value] of milestoneValues.entries()) {
  if (!utc(value)) failures.push(`${milestoneNames[index]} must be a whole-second UTC timestamp`);
}
const epochValues = milestoneValues.map((value) => Date.parse(value));
if (epochValues.some(Number.isNaN) || epochValues.some((value, index) => index > 0 && value < epochValues[index - 1])) {
  failures.push("incident milestones must be chronological");
}

for (const flag of [
  "usedSyntheticDataOnly",
  "tenantIsolationVerified",
  "auditIntegrityVerified",
  "financialIntegrityVerified",
]) {
  if (fixture[flag] !== true) failures.push(`${flag} must be true`);
}
if (fixture.providerOrRegulatorContacted !== false) failures.push("a synthetic exercise must not claim provider or regulator contact");
if (fixture.containsClientData !== false) failures.push("a repository exercise must contain no client data");
if (fixture.reviewDecision !== "accepted") failures.push("reviewDecision must be accepted");
if (!Array.isArray(fixture.releaseBlockers) || fixture.releaseBlockers.length < 3) failures.push("releaseBlockers must preserve all genuine external decisions");

const serialized = raw.toString("utf8");
const forbiddenPatterns = [
  /password\s*[:=]\s*[^"\s]+/i,
  /secret\s*[:=]\s*[^"\s]+/i,
  /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/i,
  /\b(?:\d[ -]*?){13,19}\b/,
];
if (forbiddenPatterns.some((pattern) => pattern.test(serialized))) failures.push("fixture contains a forbidden PII/secret marker");

const report = {
  schemaVersion: "privacy-incident-exercise-verification-v1",
  passed: failures.length === 0,
  fixturePath: fixturePath.replaceAll("\\", "/"),
  fixtureSha256: createHash("sha256").update(raw).digest("hex"),
  releaseCandidate: fixture.releaseCandidate ?? null,
  environment: fixture.environment ?? null,
  status: fixture.status ?? null,
  measuredMinutes: {
    notificationRouting: Number.isNaN(epochValues[1] - epochValues[0]) ? null : (epochValues[1] - epochValues[0]) / 60000,
    containment: Number.isNaN(epochValues[2] - epochValues[0]) ? null : (epochValues[2] - epochValues[0]) / 60000,
    evidencePreservation: Number.isNaN(epochValues[3] - epochValues[0]) ? null : (epochValues[3] - epochValues[0]) / 60000,
    recovery: Number.isNaN(epochValues[4] - epochValues[0]) ? null : (epochValues[4] - epochValues[0]) / 60000,
    review: Number.isNaN(epochValues[5] - epochValues[0]) ? null : (epochValues[5] - epochValues[0]) / 60000,
  },
  recoveryChecks: {
    tenantIsolation: fixture.tenantIsolationVerified === true,
    auditIntegrity: fixture.auditIntegrityVerified === true,
    financialIntegrity: fixture.financialIntegrityVerified === true,
  },
  syntheticControls: {
    usedSyntheticDataOnly: fixture.usedSyntheticDataOnly === true,
    providerOrRegulatorContacted: fixture.providerOrRegulatorContacted === true,
    containsClientData: fixture.containsClientData === true,
  },
  releaseBlockers: fixture.releaseBlockers ?? [],
  failures,
};

await writeFile(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
if (!report.passed) {
  process.stderr.write(`${failures.join("\n")}\n`);
  process.exitCode = 1;
}
