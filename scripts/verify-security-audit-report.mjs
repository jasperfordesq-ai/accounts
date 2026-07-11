import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const blockedSeverities = new Set(["high", "critical"]);
const knownSeverities = new Set(["unknown", "low", "medium", "high", "critical"]);

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

export function evaluateSecurityAudit({ npmAudit, trivyReports }) {
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
  const result = { trivy: [] };
  for (let index = 0; index < argv.length; index += 1) {
    const value = argv[index];
    if (value === "--npm") result.npm = argv[++index];
    else if (value === "--trivy") result.trivy.push(argv[++index]);
    else if (value === "--report") result.report = argv[++index];
    else throw new Error(`Unknown argument: ${value}`);
  }
  if (!result.npm || result.trivy.length === 0 || !result.report) {
    throw new Error("Usage: --npm <npm-audit.json> --trivy <trivy.json> [--trivy ...] --report <report.json>");
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
  const trivy = Object.fromEntries(args.trivy.map((filePath) => {
    const evidence = readEvidence(filePath);
    return [evidence.path, evidence];
  }));
  const failures = evaluateSecurityAudit({
    npmAudit: npm.json,
    trivyReports: Object.fromEntries(
      Object.entries(trivy).map(([name, evidence]) => [name, evidence.json]),
    ),
  });
  const report = {
    generatedAtUtc: new Date().toISOString(),
    status: failures.length === 0 ? "passed" : "failed",
    policy: { blockedSeverities: [...blockedSeverities].map((item) => item.toUpperCase()) },
    inputs: {
      npm: { path: npm.path, sha256: npm.sha256, byteSize: npm.byteSize },
      trivy: Object.fromEntries(
        Object.entries(trivy).map(([name, evidence]) => [name, {
          path: evidence.path,
          sha256: evidence.sha256,
          byteSize: evidence.byteSize,
        }]),
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
