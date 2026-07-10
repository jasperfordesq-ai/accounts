import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { percentile95, runCapacityProfile, summarizeCapacitySamples } from "../../scripts/run-capacity-profile.mjs";

test("capacity profile calculates deterministic p95 and passes bounded thresholds", () => {
  assert.equal(percentile95([10, 20, 30, 40, 50]), 50);
  const samples = Array.from({ length: 20 }, (_, index) => ({
    endpoint: index % 2 === 0 ? "/health" : "/health/ready",
    ok: true,
    durationMilliseconds: 20 + index,
    failureCode: null,
  }));

  const summary = summarizeCapacitySamples(samples, 500, {
    requests: 20,
    p95Milliseconds: 100,
    maximumErrorRatePercent: 0,
    minimumThroughputPerSecond: 10,
  });

  assert.equal(summary.status, "passed");
  assert.equal(summary.requestCount, 20);
  assert.equal(summary.errorRatePercent, 0);
  assert.equal(summary.throughputPerSecond, 40);
  assert.equal(summary.endpointSeries.length, 2);
});

test("capacity profile fails closed on count, errors, latency, throughput and route coverage", () => {
  const summary = summarizeCapacitySamples([
    { endpoint: "/health", ok: false, durationMilliseconds: 2_000, failureCode: "timeout" },
  ], 2_000, {
    requests: 2,
    p95Milliseconds: 1_000,
    maximumErrorRatePercent: 0,
    minimumThroughputPerSecond: 1,
  });

  assert.equal(summary.status, "failed");
  assert.equal(summary.failureCodes[0].code, "timeout");
  assert.ok(summary.thresholdFailures.some((failure) => failure.includes("expected 2")));
  assert.ok(summary.thresholdFailures.some((failure) => failure.includes("error rate")));
  assert.ok(summary.thresholdFailures.some((failure) => failure.includes("p95")));
  assert.ok(summary.thresholdFailures.some((failure) => failure.includes("throughput")));
  assert.ok(summary.thresholdFailures.some((failure) => failure.includes("/health/ready")));
});

test("capacity profile retains exact release identity and privacy-safe measurements", async () => {
  const server = createServer((request, response) => {
    if (request.url === "/health" || request.url === "/health/ready") {
      response.writeHead(200, { "content-type": "application/json" });
      response.end('{"status":"ok"}');
      return;
    }
    response.writeHead(404).end();
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  const directory = await mkdtemp(join(tmpdir(), "accounts-capacity-test-"));
  const reportPath = join(directory, "capacity-profile-report.json");
  const commitSha = "a".repeat(40);
  const githubActionsRunUrl = "https://github.com/example/accounts/actions/runs/123";

  try {
    const { report } = await runCapacityProfile({
      baseUrl: `http://127.0.0.1:${address.port}`,
      reportPath,
      requests: 4,
      concurrency: 2,
      p95Milliseconds: 1_000,
      maximumErrorRatePercent: 0,
      minimumThroughputPerSecond: 0.1,
      timeoutMilliseconds: 1_000,
      commitSha,
      githubActionsRunUrl,
    });

    assert.equal(report.status, "passed");
    assert.deepEqual(report.releaseCandidate, { commitSha, githubActionsRunUrl, identityProvided: true });
    assert.deepEqual(report.privacy, {
      requestBodiesSent: false,
      responseBodiesRetained: false,
      authenticationUsed: false,
      clientOrTenantIdentifiersRetained: false,
    });
    assert.deepEqual(JSON.parse(await readFile(reportPath, "utf8")), report);
  } finally {
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
    await rm(directory, { recursive: true, force: true });
  }
});

test("capacity profile rejects partial or malformed release identity before load", async () => {
  await assert.rejects(
    runCapacityProfile({
      baseUrl: "http://127.0.0.1:1",
      reportPath: "ignored.json",
      requests: 1,
      concurrency: 1,
      p95Milliseconds: 1_000,
      maximumErrorRatePercent: 0,
      minimumThroughputPerSecond: 1,
      timeoutMilliseconds: 100,
      commitSha: "not-a-commit",
      githubActionsRunUrl: "",
    }),
    /release candidate identity requires/,
  );
});
