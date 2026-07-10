import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const shaPattern = /^[0-9a-f]{64}$/;
const commitPattern = /^[0-9a-f]{40}$/;

function timestamp(value, label, failures) {
  const parsed = Date.parse(value ?? "");
  if (!Number.isFinite(parsed) || !String(value).endsWith("Z")) {
    failures.push(`${label} must be an ISO UTC timestamp.`);
    return Number.NaN;
  }
  return parsed;
}

export function evaluateMonitoringExercise(exercise, options = {}) {
  const failures = [];
  const releaseBlockers = [];
  const synthetic = exercise.evidenceClass === "synthetic-engineering-exercise";
  if (exercise.schemaVersion !== 1) failures.push("schemaVersion must be 1.");
  if (!exercise.exerciseId?.trim()) failures.push("exerciseId is required.");
  if (!commitPattern.test(exercise.releaseCommitSha ?? "")) failures.push("releaseCommitSha is invalid.");
  if (!/^https:\/\/github\.com\/[^/]+\/[^/]+\/actions\/runs\/[1-9][0-9]*$/.test(exercise.githubActionsRunUrl ?? "")) {
    failures.push("githubActionsRunUrl is invalid.");
  }
  if (options.expectedCommitSha && exercise.releaseCommitSha !== options.expectedCommitSha) {
    failures.push("Exercise commit does not match the requested candidate.");
  }
  if (options.expectedRunUrl && exercise.githubActionsRunUrl !== options.expectedRunUrl) {
    failures.push("Exercise run URL does not match the requested candidate.");
  }
  if (exercise.syntheticDataOnly !== true) failures.push("Exercise must explicitly use synthetic data only.");

  const configured = exercise.configuredTargets ?? {};
  if (!(configured.acknowledgementMinutes >= 1 && configured.acknowledgementMinutes <= 60)) {
    failures.push("Configured acknowledgement target is invalid.");
  }
  if (!(configured.escalationMinutes > configured.acknowledgementMinutes && configured.escalationMinutes <= 240)) {
    failures.push("Configured escalation target is invalid.");
  }
  if (!(configured.structuredLogRetentionDays >= 30 && configured.errorEventRetentionDays >= 30)) {
    failures.push("Configured monitoring retention is below 30 days.");
  }

  const timeline = exercise.timeline ?? {};
  const detected = timestamp(timeline.detectedAtUtc, "detectedAtUtc", failures);
  const providerReceived = timestamp(timeline.providerReceivedAtUtc, "providerReceivedAtUtc", failures);
  const alertDelivered = timestamp(timeline.alertDeliveredAtUtc, "alertDeliveredAtUtc", failures);
  const acknowledged = timestamp(timeline.acknowledgedAtUtc, "acknowledgedAtUtc", failures);
  const escalated = timestamp(timeline.escalationTestAtUtc, "escalationTestAtUtc", failures);
  const contained = timestamp(timeline.containedAtUtc, "containedAtUtc", failures);
  const recovered = timestamp(timeline.recoveredAtUtc, "recoveredAtUtc", failures);
  const ordered = [detected, providerReceived, alertDelivered, acknowledged, contained, recovered];
  if (ordered.every(Number.isFinite) && ordered.some((value, index) => index > 0 && value < ordered[index - 1])) {
    failures.push("Primary exercise timeline is not chronological.");
  }
  if (Number.isFinite(escalated) && Number.isFinite(alertDelivered) && escalated < alertDelivered) {
    failures.push("Escalation test precedes alert delivery.");
  }

  const alertLatencyMinutes = (alertDelivered - detected) / 60_000;
  const acknowledgementMinutes = (acknowledged - alertDelivered) / 60_000;
  const escalationMinutes = (escalated - alertDelivered) / 60_000;
  if (Number.isFinite(acknowledgementMinutes)
      && acknowledgementMinutes > configured.acknowledgementMinutes) {
    failures.push("Measured acknowledgement exceeded the configured target.");
  }
  if (Number.isFinite(escalationMinutes) && escalationMinutes > configured.escalationMinutes) {
    failures.push("Measured escalation exceeded the configured target.");
  }

  const provider = exercise.providerEvidence ?? {};
  for (const field of ["provider", "eventId", "correlationId", "eventReference"]) {
    if (!provider[field]?.trim()) failures.push(`providerEvidence.${field} is required.`);
  }
  if (provider.structuredLogMatched !== true) failures.push("Provider correlation must match the structured log.");

  const redaction = exercise.redactionReview ?? {};
  for (const field of [
    "requestBodyAbsent",
    "queryStringAbsent",
    "userIdentityAbsent",
    "clientDataAbsent",
    "secretDataAbsent",
  ]) {
    if (redaction[field] !== true) failures.push(`Redaction review failed: ${field}.`);
  }

  const response = exercise.responseExercise ?? {};
  for (const field of [
    "notificationRouteTested",
    "unacknowledgedEscalationTested",
    "containmentWalkthroughCompleted",
    "recoveryWalkthroughCompleted",
    "auditChainVerified",
  ]) {
    if (response[field] !== true) failures.push(`Response exercise is incomplete: ${field}.`);
  }
  if (!response.operator?.trim() || !response.incidentCommander?.trim()) {
    failures.push("Named exercise operator and incident commander are required.");
  }
  if (!Array.isArray(exercise.evidence) || exercise.evidence.length === 0) {
    failures.push("At least one hashed exercise evidence reference is required.");
  } else {
    for (const item of exercise.evidence) {
      if (!item.reference?.trim() || !shaPattern.test(item.sha256 ?? "")) {
        failures.push("Every exercise evidence item requires a reference and lowercase SHA-256.");
      }
    }
  }

  if (synthetic) {
    if (!options.allowSynthetic) failures.push("Synthetic engineering exercise requires explicit allowance.");
    releaseBlockers.push("Synthetic engineering exercise is not real provider/operator confirmation.");
    if (exercise.acceptedForProduction !== false) failures.push("Synthetic exercise cannot be accepted for production.");
  } else {
    if (exercise.evidenceClass !== "retained-provider-exercise") {
      failures.push("Unknown monitoring exercise evidence class.");
    }
    if (exercise.acceptedForProduction !== true) {
      releaseBlockers.push("Named incident commander has not accepted the retained provider exercise.");
    }
  }

  return {
    failures,
    releaseBlockers,
    metrics: {
      alertLatencyMinutes,
      acknowledgementMinutes,
      escalationMinutes,
    },
  };
}

function parseArgs(argv) {
  const args = { allowSynthetic: false };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--exercise") args.exercise = argv[++index];
    else if (arg === "--report") args.report = argv[++index];
    else if (arg === "--commit") args.expectedCommitSha = argv[++index];
    else if (arg === "--run-url") args.expectedRunUrl = argv[++index];
    else if (arg === "--allow-synthetic") args.allowSynthetic = true;
    else throw new Error(`Unknown argument: ${arg}`);
  }
  if (!args.exercise || !args.report) throw new Error("--exercise and --report are required.");
  return args;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const exercise = JSON.parse(fs.readFileSync(args.exercise, "utf8"));
  const result = evaluateMonitoringExercise(exercise, args);
  const report = {
    generatedAtUtc: new Date().toISOString(),
    status: result.failures.length > 0
      ? "failed"
      : result.releaseBlockers.length > 0
        ? "engineering-passed-release-blocked"
        : "passed",
    exerciseId: exercise.exerciseId,
    releaseCommitSha: exercise.releaseCommitSha,
    githubActionsRunUrl: exercise.githubActionsRunUrl,
    evidenceClass: exercise.evidenceClass,
    metrics: result.metrics,
    failures: result.failures,
    releaseBlockers: result.releaseBlockers,
  };
  fs.mkdirSync(path.dirname(path.resolve(args.report)), { recursive: true });
  fs.writeFileSync(args.report, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  if (result.failures.length > 0) {
    result.failures.forEach((failure) => console.error(`- ${failure}`));
    process.exitCode = 1;
    return;
  }
  console.log(report.status);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) main();
