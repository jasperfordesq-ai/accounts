import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const blockedSeverities = new Set(["high", "critical"]);
const knownSeverities = new Set(["unknown", "low", "medium", "high", "critical"]);
const shaPattern = /^[0-9a-f]{40}$/;
const positiveIntegerPattern = /^[1-9][0-9]*$/;

function trivyVulnerabilities(name, report, failures) {
  if (!report || typeof report !== "object" || Array.isArray(report)) {
    failures.push(`${name} root must be a Trivy JSON object.`);
    return [];
  }
  if (report.SchemaVersion !== 2) {
    failures.push(`${name} must use Trivy SchemaVersion 2.`);
  }
  if (typeof report.ArtifactName !== "string" || report.ArtifactName.trim() === "") {
    failures.push(`${name} must identify a non-empty scanned ArtifactName.`);
  }
  if (report.ArtifactType !== "container_image") {
    failures.push(`${name} ArtifactType must be container_image.`);
  }
  if (!Array.isArray(report.Results) || report.Results.length === 0) {
    failures.push(`${name} must contain a non-empty Trivy Results array.`);
    return [];
  }

  const vulnerabilities = [];
  for (const [resultIndex, result] of report.Results.entries()) {
    if (!result || typeof result !== "object" || Array.isArray(result)) {
      failures.push(`${name} Results[${resultIndex}] must be an object.`);
      continue;
    }
    for (const property of ["Target", "Class", "Type"]) {
      if (typeof result[property] !== "string" || result[property].trim() === "") {
        failures.push(`${name} Results[${resultIndex}].${property} must be a non-empty string.`);
      }
    }
    if (!Object.hasOwn(result, "Vulnerabilities")) {
      // Clean Trivy targets omit the optional Vulnerabilities property.
      continue;
    }
    if (!Array.isArray(result.Vulnerabilities)) {
      failures.push(`${name} Results[${resultIndex}].Vulnerabilities must be an array when present.`);
      continue;
    }
    for (const [findingIndex, finding] of result.Vulnerabilities.entries()) {
      const context = `${name} Results[${resultIndex}].Vulnerabilities[${findingIndex}]`;
      if (!finding || typeof finding !== "object" || Array.isArray(finding)) {
        failures.push(`${context} must be an object.`);
        continue;
      }
      if (typeof finding.VulnerabilityID !== "string" || finding.VulnerabilityID.trim() === "") {
        failures.push(`${context}.VulnerabilityID must be a non-empty string.`);
      }
      const severity = typeof finding.Severity === "string"
        ? finding.Severity.trim().toLowerCase()
        : "";
      if (!knownSeverities.has(severity)) {
        failures.push(`${context}.Severity must be a recognized Trivy severity.`);
        continue;
      }
      vulnerabilities.push({ ...finding, Severity: severity.toUpperCase() });
    }
  }
  return vulnerabilities;
}

function collectNugetVulnerabilities(value, context, failures, findings) {
  if (!value || typeof value !== "object") return;
  if (Array.isArray(value)) {
    value.forEach((item, index) => collectNugetVulnerabilities(item, `${context}[${index}]`, failures, findings));
    return;
  }
  for (const [key, child] of Object.entries(value)) {
    if (key.toLowerCase() !== "vulnerabilities") {
      collectNugetVulnerabilities(child, `${context}.${key}`, failures, findings);
      continue;
    }
    if (!Array.isArray(child)) {
      failures.push(`${context}.${key} must be an array when present.`);
      continue;
    }
    child.forEach((finding, index) => {
      const findingContext = `${context}.${key}[${index}]`;
      if (!finding || typeof finding !== "object" || Array.isArray(finding)) {
        failures.push(`${findingContext} must be an object.`);
        return;
      }
      const severity = typeof finding.severity === "string" ? finding.severity.trim().toLowerCase() : "";
      if (!knownSeverities.has(severity)) {
        failures.push(`${findingContext}.severity must be a recognized NuGet severity.`);
        return;
      }
      findings.push({ ...finding, severity });
    });
  }
}

function nugetVulnerabilities(report, failures) {
  if (!report || typeof report !== "object" || Array.isArray(report)) {
    failures.push("NuGet vulnerability report root must be an object.");
    return [];
  }
  if (report.version !== 1) failures.push("NuGet vulnerability report must use version 1.");
  if (typeof report.parameters !== "string" || !report.parameters.includes("--vulnerable")) {
    failures.push("NuGet vulnerability report must record the --vulnerable parameter.");
  }
  if (!Array.isArray(report.projects) || report.projects.length === 0) {
    failures.push("NuGet vulnerability report must contain a non-empty projects array.");
    return [];
  }
  for (const [index, project] of report.projects.entries()) {
    if (!project || typeof project !== "object" || Array.isArray(project)
      || typeof project.path !== "string" || project.path.trim() === "") {
      failures.push(`NuGet projects[${index}] must identify a non-empty project path.`);
    }
  }
  const findings = [];
  collectNugetVulnerabilities(report.projects, "NuGet.projects", failures, findings);
  return findings;
}

export function validateSpdxSbom(name, report) {
  const failures = [];
  if (!report || typeof report !== "object" || Array.isArray(report)) {
    return [`${name} root must be an SPDX JSON object.`];
  }
  if (typeof report.spdxVersion !== "string" || !report.spdxVersion.startsWith("SPDX-")) {
    failures.push(`${name} must identify an SPDX version.`);
  }
  if (report.SPDXID !== "SPDXRef-DOCUMENT") failures.push(`${name} must identify the SPDX document root.`);
  if (typeof report.name !== "string" || report.name.trim() === "") failures.push(`${name} must have a non-empty name.`);
  if (!Array.isArray(report.packages) || report.packages.length === 0) {
    failures.push(`${name} must contain a non-empty packages array.`);
  }
  return failures;
}

export function validateSecurityAuditIdentity(identity) {
  const failures = [];
  if (!shaPattern.test(identity.candidateCommitSha ?? "")) failures.push("candidate commit SHA must be 40 lowercase hexadecimal characters.");
  if (!shaPattern.test(identity.workflowCommitSha ?? "")) failures.push("workflow commit SHA must be 40 lowercase hexadecimal characters.");
  if (identity.candidateCommitSha !== identity.workflowCommitSha) failures.push("candidate commit SHA must equal the workflow commit SHA.");
  if (!positiveIntegerPattern.test(String(identity.runId ?? ""))) failures.push("run ID must be a positive integer.");
  if (!positiveIntegerPattern.test(String(identity.runAttempt ?? ""))) failures.push("run attempt must be a positive integer.");
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(identity.repository ?? "")) failures.push("repository must be owner/name.");
  const expectedRunUrl = `https://github.com/${identity.repository}/actions/runs/${identity.runId}`;
  if (identity.runUrl !== expectedRunUrl) failures.push("run URL must match the repository and run ID.");
  if (!new Set(["schedule", "workflow_dispatch"]).has(identity.eventName)) failures.push("event name must be schedule or workflow_dispatch.");
  if (identity.ref !== "refs/heads/main") failures.push("security audit must run from refs/heads/main.");
  if (identity.workflowRef !== `${identity.repository}/.github/workflows/scheduled-security-audit.yml@refs/heads/main`) {
    failures.push("workflow ref must identify scheduled-security-audit.yml on main.");
  }
  return failures;
}

export function evaluateSecurityAudit({ npmAudit, nugetAudit, trivyReports }) {
  const failures = [];
  const npmCounts = npmAudit?.metadata?.vulnerabilities;
  if (!npmCounts || typeof npmCounts !== "object") {
    failures.push("npm audit report is missing vulnerability metadata.");
  } else {
    for (const severity of blockedSeverities) {
      const count = Number(npmCounts[severity] ?? 0);
      if (!Number.isFinite(count) || count < 0) {
        failures.push(`npm audit has an invalid ${severity} count.`);
      } else if (count > 0) {
        failures.push(`npm audit contains ${count} ${severity.toUpperCase()} vulnerability record(s).`);
      }
    }
  }

  if (nugetAudit !== undefined) {
    const blocked = nugetVulnerabilities(nugetAudit, failures)
      .filter((finding) => blockedSeverities.has(finding.severity));
    if (blocked.length > 0) {
      const bySeverity = blocked.reduce((counts, finding) => {
        const severity = finding.severity.toUpperCase();
        counts[severity] = (counts[severity] ?? 0) + 1;
        return counts;
      }, {});
      failures.push(`NuGet contains blocked vulnerabilities: ${Object.entries(bySeverity)
        .map(([severity, count]) => `${severity}=${count}`).join(", ")}.`);
    }
  }

  for (const [name, report] of Object.entries(trivyReports)) {
    const blocked = trivyVulnerabilities(name, report, failures).filter((item) =>
      blockedSeverities.has(item.Severity.toLowerCase()),
    );
    if (blocked.length > 0) {
      const bySeverity = blocked.reduce((counts, item) => {
        const severity = String(item.Severity).toUpperCase();
        counts[severity] = (counts[severity] ?? 0) + 1;
        return counts;
      }, {});
      failures.push(
        `${name} contains blocked vulnerabilities: ${Object.entries(bySeverity)
          .map(([severity, count]) => `${severity}=${count}`)
          .join(", ")}.`,
      );
    }
  }

  return failures;
}

function parseArgs(argv) {
  const result = { trivy: [], sbom: [] };
  for (let index = 0; index < argv.length; index += 1) {
    const value = argv[index];
    if (value === "--npm") result.npm = argv[++index];
    else if (value === "--npm-exit-code") result.npmExitCode = argv[++index];
    else if (value === "--nuget") result.nuget = argv[++index];
    else if (value === "--trivy") result.trivy.push(argv[++index]);
    else if (value === "--sbom") result.sbom.push(argv[++index]);
    else if (value === "--report") result.report = argv[++index];
    else if (value === "--candidate-sha") result.candidateCommitSha = argv[++index];
    else if (value === "--workflow-sha") result.workflowCommitSha = argv[++index];
    else if (value === "--run-id") result.runId = argv[++index];
    else if (value === "--run-attempt") result.runAttempt = argv[++index];
    else if (value === "--run-url") result.runUrl = argv[++index];
    else if (value === "--repository") result.repository = argv[++index];
    else if (value === "--event-name") result.eventName = argv[++index];
    else if (value === "--ref") result.ref = argv[++index];
    else if (value === "--workflow-ref") result.workflowRef = argv[++index];
    else throw new Error(`Unknown argument: ${value}`);
  }
  const required = ["npm", "npmExitCode", "nuget", "report", "candidateCommitSha", "workflowCommitSha", "runId", "runAttempt", "runUrl", "repository", "eventName", "ref", "workflowRef"];
  if (required.some((key) => !result[key]) || result.trivy.length !== 2 || result.sbom.length !== 2) {
    throw new Error("Security audit verification requires npm, npm exit code, NuGet, two Trivy reports, two SPDX SBOMs, exact workflow identity and a report path.");
  }
  return result;
}

function readEvidence(filePath) {
  const bytes = fs.readFileSync(filePath);
  return {
    path: path.basename(filePath),
    sha256: crypto.createHash("sha256").update(bytes).digest("hex"),
    byteSize: bytes.length,
    json: JSON.parse(bytes.toString("utf8")),
  };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const npm = readEvidence(args.npm);
  const npmExitCode = fs.readFileSync(args.npmExitCode, "utf8").trim();
  const npmExitCodeEvidence = readEvidence(args.npmExitCode);
  const nuget = readEvidence(args.nuget);
  const trivy = Object.fromEntries(args.trivy.map((filePath) => {
    const evidence = readEvidence(filePath);
    return [evidence.path, evidence];
  }));
  const sbom = Object.fromEntries(args.sbom.map((filePath) => {
    const evidence = readEvidence(filePath);
    return [evidence.path, evidence];
  }));
  const identity = {
    candidateCommitSha: args.candidateCommitSha,
    workflowCommitSha: args.workflowCommitSha,
    runId: args.runId,
    runAttempt: args.runAttempt,
    runUrl: args.runUrl,
    repository: args.repository,
    eventName: args.eventName,
    ref: args.ref,
    workflowRef: args.workflowRef,
  };
  const failures = validateSecurityAuditIdentity(identity);
  if (npmExitCode !== "0") failures.push("npm audit exit code must be 0.");
  failures.push(...evaluateSecurityAudit({
    npmAudit: npm.json,
    nugetAudit: nuget.json,
    trivyReports: Object.fromEntries(
      Object.entries(trivy).map(([name, evidence]) => [name, evidence.json]),
    ),
  }));
  for (const [name, evidence] of Object.entries(sbom)) {
    failures.push(...validateSpdxSbom(name, evidence.json));
  }
  const manifestEntry = (evidence) => ({ path: evidence.path, sha256: evidence.sha256, byteSize: evidence.byteSize });
  const report = {
    generatedAtUtc: new Date().toISOString(),
    status: failures.length === 0 ? "passed" : "failed",
    identity,
    policy: { blockedSeverities: [...blockedSeverities].map((item) => item.toUpperCase()) },
    inputs: {
      npm: manifestEntry(npm),
      npmExitCode: manifestEntry(npmExitCodeEvidence),
      nuget: manifestEntry(nuget),
      trivy: Object.fromEntries(
        Object.entries(trivy).map(([name, evidence]) => [name, manifestEntry(evidence)]),
      ),
      sbom: Object.fromEntries(
        Object.entries(sbom).map(([name, evidence]) => [name, manifestEntry(evidence)]),
      ),
    },
    failures,
  };
  fs.mkdirSync(path.dirname(path.resolve(args.report)), { recursive: true });
  fs.writeFileSync(args.report, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  if (failures.length > 0) {
    for (const failure of failures) console.error(`- ${failure}`);
    process.exitCode = 1;
    return;
  }
  console.log("Scheduled dependency and container vulnerability policy passed.");
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main();
}
