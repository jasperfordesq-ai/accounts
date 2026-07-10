import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { delimiter, join, resolve } from "node:path";
import { spawn } from "node:child_process";
import test from "node:test";

const repositoryRoot = resolve(import.meta.dirname, "../..");
const scriptPath = join(repositoryRoot, "scripts", "test-production-failover.ps1");
const commitSha = "b".repeat(40);
const runUrl = "https://github.com/example/accounts/actions/runs/456";

test("production failover drill proves API/database detection and bounded recovery", async () => {
  const harness = await createHarness();
  try {
    const result = await harness.run();
    assert.equal(result.code, 0, result.output);

    const report = JSON.parse(await readFile(harness.reportPath, "utf8"));
    assert.equal(report.status, "passed");
    assert.equal(report.schemaVersion, "accounts-production-failover-v1");
    assert.deepEqual(report.releaseCandidate, { commitSha, githubActionsRunUrl: runUrl });
    assert.deepEqual(report.observations.map(({ phase, expectedHealthy, passed }) => ({ phase, expectedHealthy, passed })), [
      { phase: "initial-ready", expectedHealthy: true, passed: true },
      { phase: "api-host-failure-detected", expectedHealthy: false, passed: true },
      { phase: "api-host-recovered", expectedHealthy: true, passed: true },
      { phase: "database-failure-detected", expectedHealthy: false, passed: true },
      { phase: "database-recovered", expectedHealthy: true, passed: true },
    ]);
    assert.deepEqual(report.privacy, {
      responseBodiesRetained: false,
      authenticationRetained: false,
      tenantOrClientIdentifiersRetained: false,
    });
  } finally {
    await harness.dispose();
  }
});

test("production failover drill fails closed when an interruption is not detected", async () => {
  const harness = await createHarness({ ignoreStop: "api" });
  try {
    const result = await harness.run();
    assert.notEqual(result.code, 0);

    const report = JSON.parse(await readFile(harness.reportPath, "utf8"));
    assert.equal(report.status, "failed");
    const failedPhase = report.observations.find(({ phase }) => phase === "api-host-failure-detected");
    assert.deepEqual(
      { expectedHealthy: failedPhase.expectedHealthy, passed: failedPhase.passed },
      { expectedHealthy: false, passed: false },
    );
    assert.ok(report.failures.some((failure) => failure.includes("api-host-failure-detected")));
    assert.deepEqual(JSON.parse(await readFile(harness.statePath, "utf8")), { api: true, db: true });
  } finally {
    await harness.dispose();
  }
});

async function createHarness({ ignoreStop = "" } = {}) {
  const directory = await mkdtemp(join(tmpdir(), "accounts-failover-test-"));
  const statePath = join(directory, "state.json");
  const reportPath = join(directory, "production-failover-report.json");
  const composePath = join(directory, "compose.production.yml");
  const helperPath = join(directory, "fake-docker.mjs");
  await writeFile(statePath, '{"api":true,"db":true}\n', "utf8");
  await writeFile(composePath, "services: {}\n", "utf8");
  await writeFile(helperPath, fakeDockerSource, "utf8");

  if (process.platform === "win32") {
    await writeFile(join(directory, "docker.cmd"), '@node "%~dp0fake-docker.mjs" %*\r\n', "utf8");
  } else {
    const wrapperPath = join(directory, "docker");
    await writeFile(wrapperPath, '#!/bin/sh\nexec node "$(dirname "$0")/fake-docker.mjs" "$@"\n', { mode: 0o755 });
  }

  const server = createServer(async (_request, response) => {
    try {
      const state = JSON.parse(await readFile(statePath, "utf8"));
      const healthy = state.api && state.db;
      response.writeHead(healthy ? 200 : 503, { "content-type": "application/json" });
      response.end(healthy ? '{"status":"ready"}' : '{"status":"unavailable"}');
    } catch {
      response.writeHead(503).end();
    }
  });
  await new Promise((resolvePromise) => server.listen(0, "127.0.0.1", resolvePromise));
  const address = server.address();

  return {
    statePath,
    reportPath,
    async run() {
      const executable = process.platform === "win32" ? "powershell.exe" : "pwsh";
      const args = [
        "-NoProfile", "-NonInteractive",
        ...(process.platform === "win32" ? ["-ExecutionPolicy", "Bypass"] : []),
        "-File", scriptPath,
        "-BaseUrl", `http://127.0.0.1:${address.port}`,
        "-ComposeFile", composePath,
        "-EvidencePath", reportPath,
        "-FailureDetectionSeconds", "1",
        "-ApiRecoverySeconds", "5",
        "-DatabaseRecoverySeconds", "5",
        "-CommitSha", commitSha,
        "-GitHubActionsRunUrl", runUrl,
        "-ConfirmEphemeralCandidateStack",
        "-ExpectedComposeProject", "accounts-test",
      ];
      return spawnAndCollect(executable, args, {
        ...process.env,
        PATH: `${directory}${delimiter}${process.env.PATH ?? ""}`,
        FAILOVER_STATE_FILE: statePath,
        FAILOVER_IGNORE_STOP: ignoreStop,
        FAILOVER_PROJECT: "accounts-test",
      });
    },
    async dispose() {
      await new Promise((resolvePromise, rejectPromise) => server.close((error) => error ? rejectPromise(error) : resolvePromise()));
      await rm(directory, { recursive: true, force: true });
    },
  };
}

function spawnAndCollect(command, args, env) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, args, { cwd: repositoryRoot, env, windowsHide: true });
    let output = "";
    child.stdout.on("data", (chunk) => { output += chunk; });
    child.stderr.on("data", (chunk) => { output += chunk; });
    child.on("error", rejectPromise);
    child.on("close", (code) => resolvePromise({ code, output }));
  });
}

const fakeDockerSource = `
import { readFile, writeFile } from "node:fs/promises";

const statePath = process.env.FAILOVER_STATE_FILE;
const args = process.argv.slice(2);
if (args.includes("ps") && args.includes("json")) {
  console.log(JSON.stringify([
    { Project: process.env.FAILOVER_PROJECT, Service: "api", State: "running" },
    { Project: process.env.FAILOVER_PROJECT, Service: "db", State: "running" },
  ]));
  process.exit(0);
}
const action = args.find((argument) => argument === "stop" || argument === "start");
const service = process.argv.at(-1);
if (!statePath || !action || !["api", "db"].includes(service)) process.exit(2);
const state = JSON.parse(await readFile(statePath, "utf8"));
if (!(action === "stop" && process.env.FAILOVER_IGNORE_STOP === service)) state[service] = action === "start";
await writeFile(statePath, JSON.stringify(state), "utf8");
`;
