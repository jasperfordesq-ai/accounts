import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  collectRepoInputs,
  evaluateBuildInputs,
} from "../../scripts/verify-build-inputs.mjs";
import {
  evaluateSecurityAudit,
  validateSecurityAuditIdentity,
  validateSpdxSbom,
} from "../../scripts/verify-security-audit-report.mjs";
import { evaluateContainerHardening } from "../../scripts/container-hardening-policy.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

test("repository build inputs are exact, locked, digest-pinned, and scheduled for audit", () => {
  assert.deepEqual(evaluateBuildInputs(collectRepoInputs(repoRoot)), []);
});

test("base-image digest drift is rejected", () => {
  const inputs = collectRepoInputs(repoRoot);
  inputs.dockerfiles.frontend = inputs.dockerfiles.frontend.replace(
    /node:24-alpine@sha256:[0-9a-f]{64}/,
    "node:24-alpine",
  );
  const failures = evaluateBuildInputs(inputs);
  assert.ok(failures.some((failure) => failure.includes("not digest-pinned")));
  assert.ok(failures.some((failure) => failure.includes("exact FROM reference")));
});

test("Node engine and lockfile drift are rejected", () => {
  const inputs = collectRepoInputs(repoRoot);
  inputs.packageJson = structuredClone(inputs.packageJson);
  inputs.packageLock = structuredClone(inputs.packageLock);
  inputs.packageJson.engines.node = ">=22 <23";
  delete inputs.packageLock.packages[""].engines.npm;
  const failures = evaluateBuildInputs(inputs);
  assert.ok(failures.some((failure) => failure.includes("Node engine")));
  assert.ok(failures.some((failure) => failure.includes("npm engine")));
});

test("an unlocked NuGet package entry is rejected", () => {
  const inputs = collectRepoInputs(repoRoot);
  inputs.nugetLocks = structuredClone(inputs.nugetLocks);
  const apiPackages = inputs.nugetLocks["Accounts.Api/packages.lock.json"].dependencies["net10.0"];
  delete apiPackages.CsvHelper.contentHash;
  assert.ok(
    evaluateBuildInputs(inputs).some((failure) => failure.includes("unlocked package entry")),
  );
});

test("deliberately vulnerable npm and container fixtures fail the security policy", () => {
  const failures = evaluateSecurityAudit({
    npmAudit: {
      metadata: { vulnerabilities: { low: 0, moderate: 0, high: 1, critical: 0 } },
    },
    trivyReports: {
      "fixture-trivy.json": {
        SchemaVersion: 2,
        ArtifactName: "fixture-image@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        ArtifactType: "container_image",
        Results: [
          {
            Target: "clean operating-system target",
            Class: "os-pkgs",
            Type: "alpine",
          },
          {
            Target: "application packages",
            Class: "lang-pkgs",
            Type: "node-pkg",
            Vulnerabilities: [
              { VulnerabilityID: "CVE-2099-0001", Severity: "CRITICAL" },
            ],
          },
        ],
      },
    },
  });
  assert.ok(failures.some((failure) => failure.includes("npm audit contains 1 HIGH")));
  assert.ok(failures.some((failure) => failure.includes("CRITICAL=1")));
});

test("zero-vulnerability evidence passes the security policy", () => {
  assert.deepEqual(
    evaluateSecurityAudit({
      npmAudit: {
        metadata: { vulnerabilities: { low: 0, moderate: 0, high: 0, critical: 0 } },
      },
      trivyReports: {
        "backend-trivy.json": {
          SchemaVersion: 2,
          ArtifactName: "backend-image@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          ArtifactType: "container_image",
          Results: [{ Target: "backend", Class: "lang-pkgs", Type: "dotnet-core" }],
        },
        "frontend-trivy.json": {
          SchemaVersion: 2,
          ArtifactName: "frontend-image@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          ArtifactType: "container_image",
          Results: [{
            Target: "frontend",
            Class: "lang-pkgs",
            Type: "node-pkg",
            Vulnerabilities: [],
          }],
        },
      },
    }),
    [],
  );
});

test("scheduled audit identity is bound to the exact main workflow commit", () => {
  const sha = "a".repeat(40);
  const identity = {
    candidateCommitSha: sha,
    workflowCommitSha: sha,
    runId: "12345",
    runAttempt: "1",
    runUrl: "https://github.com/example/accounts/actions/runs/12345",
    repository: "example/accounts",
    eventName: "workflow_dispatch",
    ref: "refs/heads/main",
    workflowRef: "example/accounts/.github/workflows/scheduled-security-audit.yml@refs/heads/main",
  };
  assert.deepEqual(validateSecurityAuditIdentity(identity), []);
  assert.ok(validateSecurityAuditIdentity({ ...identity, workflowCommitSha: "b".repeat(40) })
    .some((failure) => failure.includes("must equal")));
  assert.ok(validateSecurityAuditIdentity({ ...identity, ref: "refs/heads/feature" })
    .some((failure) => failure.includes("refs/heads/main")));
});

test("NuGet high vulnerabilities and malformed SPDX evidence fail closed", () => {
  const failures = evaluateSecurityAudit({
    npmAudit: { metadata: { vulnerabilities: { high: 0, critical: 0 } } },
    nugetAudit: {
      version: 1,
      parameters: "--vulnerable --include-transitive",
      projects: [{
        path: "/repo/backend/Accounts.Api/Accounts.Api.csproj",
        frameworks: [{
          framework: "net10.0",
          transitivePackages: [{
            id: "Fixture.Package",
            vulnerabilities: [{ severity: "High", advisoryurl: "https://example.invalid/advisory" }],
          }],
        }],
      }],
    },
    trivyReports: {},
  });
  assert.ok(failures.some((failure) => failure.includes("NuGet contains blocked vulnerabilities: HIGH=1")));
  assert.ok(validateSpdxSbom("fixture-sbom.json", { spdxVersion: "SPDX-2.3" })
    .some((failure) => failure.includes("SPDX document root")));
  assert.deepEqual(validateSpdxSbom("fixture-sbom.json", {
    spdxVersion: "SPDX-2.3",
    SPDXID: "SPDXRef-DOCUMENT",
    name: "fixture",
    packages: [{ SPDXID: "SPDXRef-Package" }],
  }), []);
});

test("malformed or truncated Trivy evidence fails closed", () => {
  const report = (overrides = {}) => ({
    SchemaVersion: 2,
    ArtifactName: "image@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    ArtifactType: "container_image",
    Results: [{ Target: "image", Class: "os-pkgs", Type: "alpine" }],
    ...overrides,
  });
  const failures = evaluateSecurityAudit({
    npmAudit: {
      metadata: { vulnerabilities: { low: 0, moderate: 0, high: 0, critical: 0 } },
    },
    trivyReports: {
      "empty-results.json": report({ Results: [] }),
      "null-vulnerabilities.json": report({
        Results: [{ Target: "image", Class: "os-pkgs", Type: "alpine", Vulnerabilities: null }],
      }),
      "unknown-severity.json": report({
        Results: [{
          Target: "image",
          Class: "os-pkgs",
          Type: "alpine",
          Vulnerabilities: [{ VulnerabilityID: "CVE-2099-0002", Severity: "UNBOUNDED" }],
        }],
      }),
      "wrong-type.json": report({ ArtifactType: "filesystem" }),
    },
  });

  assert.ok(failures.some((failure) => failure.includes("non-empty Trivy Results array")));
  assert.ok(failures.some((failure) => failure.includes("Vulnerabilities must be an array")));
  assert.ok(failures.some((failure) => failure.includes("recognized Trivy severity")));
  assert.ok(failures.some((failure) => failure.includes("ArtifactType must be container_image")));
});

test("the normalized production topology satisfies the container hardening policy", () => {
  assert.deepEqual(evaluateContainerHardening(hardenedComposeFixture()), []);
});

test("missing isolation, capabilities, read-only, and resource controls are rejected", () => {
  const fixture = hardenedComposeFixture();
  fixture.services.api.read_only = false;
  fixture.services.api.cap_drop = [];
  fixture.services.api.pids_limit = 0;
  fixture.services.frontend.networks.api_db = {};
  const failures = evaluateContainerHardening(fixture);
  assert.ok(failures.some((failure) => failure.includes("api root filesystem")));
  assert.ok(failures.some((failure) => failure.includes("api does not drop")));
  assert.ok(failures.some((failure) => failure.includes("api has no positive PID")));
  assert.ok(failures.some((failure) => failure.includes("frontend has unintended network")));
});

function hardenedComposeFixture() {
  const service = (networks) => ({
    read_only: true,
    security_opt: ["no-new-privileges:true"],
    cap_drop: ["ALL"],
    pids_limit: 256,
    mem_limit: 536_870_912,
    cpus: 1,
    tmpfs: ["/tmp:rw,noexec,nosuid,nodev,size=64m"],
    networks: Object.fromEntries(networks.map((name) => [name, {}])),
  });
  return {
    services: {
      db: service(["api_db"]),
      migrate: service(["api_db"]),
      api: service(["api_db", "frontend_api", "api_egress"]),
      frontend: {
        ...service(["frontend_api"]),
        ports: [{ host_ip: "127.0.0.1", target: 3000, published: "3000" }],
      },
    },
    networks: {
      frontend_api: { internal: true },
      api_db: { internal: true },
      api_egress: { internal: false },
    },
  };
}
