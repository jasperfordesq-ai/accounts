import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const DEFAULTS = Object.freeze({
  requests: 120,
  concurrency: 12,
  p95Milliseconds: 1_000,
  maximumErrorRatePercent: 0,
  minimumThroughputPerSecond: 10,
  timeoutMilliseconds: 5_000,
});

export function percentile95(values) {
  const sorted = [...values].sort((left, right) => left - right);
  if (sorted.length === 0) return 0;
  return sorted[Math.max(0, Math.ceil(sorted.length * 0.95) - 1)];
}

export function summarizeCapacitySamples(samples, elapsedMilliseconds, thresholds = DEFAULTS) {
  const count = samples.length;
  const failures = samples.filter((sample) => !sample.ok);
  const errorRatePercent = count === 0 ? 100 : failures.length * 100 / count;
  const throughputPerSecond = elapsedMilliseconds <= 0 ? 0 : count / (elapsedMilliseconds / 1_000);
  const p95Milliseconds = percentile95(samples.map((sample) => sample.durationMilliseconds));
  const endpointSeries = [...new Set(samples.map((sample) => sample.endpoint))]
    .sort()
    .map((endpoint) => {
      const endpointSamples = samples.filter((sample) => sample.endpoint === endpoint);
      return {
        endpoint,
        count: endpointSamples.length,
        failedCount: endpointSamples.filter((sample) => !sample.ok).length,
        p95Milliseconds: round(percentile95(endpointSamples.map((sample) => sample.durationMilliseconds))),
      };
    });
  const thresholdFailures = [];
  if (count !== thresholds.requests) {
    thresholdFailures.push(`completed ${count} requests; expected ${thresholds.requests}`);
  }
  if (errorRatePercent > thresholds.maximumErrorRatePercent) {
    thresholdFailures.push(`error rate ${round(errorRatePercent)}% exceeded ${thresholds.maximumErrorRatePercent}%`);
  }
  if (p95Milliseconds > thresholds.p95Milliseconds) {
    thresholdFailures.push(`p95 ${round(p95Milliseconds)}ms exceeded ${thresholds.p95Milliseconds}ms`);
  }
  if (throughputPerSecond < thresholds.minimumThroughputPerSecond) {
    thresholdFailures.push(`throughput ${round(throughputPerSecond)}/s was below ${thresholds.minimumThroughputPerSecond}/s`);
  }
  for (const endpoint of ["/health", "/health/ready"]) {
    if (!endpointSeries.some((series) => series.endpoint === endpoint && series.count > 0)) {
      thresholdFailures.push(`required endpoint ${endpoint} was not exercised`);
    }
  }

  return {
    status: thresholdFailures.length === 0 ? "passed" : "failed",
    requestCount: count,
    failedCount: failures.length,
    errorRatePercent: round(errorRatePercent),
    elapsedMilliseconds: round(elapsedMilliseconds),
    throughputPerSecond: round(throughputPerSecond),
    p95Milliseconds: round(p95Milliseconds),
    endpointSeries,
    failureCodes: Object.entries(Object.groupBy(failures, (sample) => sample.failureCode ?? "unknown"))
      .map(([code, rows]) => ({ code, count: rows.length }))
      .sort((left, right) => left.code.localeCompare(right.code)),
    thresholdFailures,
  };
}

export async function runCapacityProfile(options) {
  const baseUrl = validatedBaseUrl(options.baseUrl);
  const commitSha = String(options.commitSha ?? "").trim();
  const githubActionsRunUrl = String(options.githubActionsRunUrl ?? "").trim();
  if ((commitSha.length > 0 || githubActionsRunUrl.length > 0) &&
      (!/^[0-9a-f]{40}$/.test(commitSha) || !/^https:\/\/github\.com\/[^/\s]+\/[^/\s]+\/actions\/runs\/[0-9]+$/.test(githubActionsRunUrl))) {
    throw new Error("release candidate identity requires a full lowercase commit SHA and exact GitHub Actions run URL.");
  }
  const thresholds = {
    requests: positiveInteger(options.requests, "requests"),
    concurrency: positiveInteger(options.concurrency, "concurrency"),
    p95Milliseconds: positiveNumber(options.p95Milliseconds, "p95-ms"),
    maximumErrorRatePercent: nonNegativeNumber(options.maximumErrorRatePercent, "max-error-rate-percent"),
    minimumThroughputPerSecond: positiveNumber(options.minimumThroughputPerSecond, "min-throughput-rps"),
    timeoutMilliseconds: positiveInteger(options.timeoutMilliseconds, "timeout-ms"),
  };
  if (thresholds.concurrency > thresholds.requests) thresholds.concurrency = thresholds.requests;

  for (const endpoint of ["/health", "/health/ready"]) {
    const warmup = await timedRequest(baseUrl, endpoint, thresholds.timeoutMilliseconds);
    if (!warmup.ok) throw new Error(`Capacity warm-up failed for ${endpoint} (${warmup.failureCode}).`);
  }

  const endpoints = ["/health", "/health/ready"];
  const samples = [];
  let nextIndex = 0;
  const started = performance.now();
  await Promise.all(Array.from({ length: thresholds.concurrency }, async () => {
    while (true) {
      const index = nextIndex++;
      if (index >= thresholds.requests) return;
      samples.push(await timedRequest(baseUrl, endpoints[index % endpoints.length], thresholds.timeoutMilliseconds));
    }
  }));
  const elapsedMilliseconds = performance.now() - started;
  const summary = summarizeCapacitySamples(samples, elapsedMilliseconds, thresholds);
  const report = {
    schemaVersion: "accounts-capacity-profile-v1",
    generatedAtUtc: new Date().toISOString(),
    profile: "bounded-production-stack-health-v1",
    releaseCandidate: {
      commitSha,
      githubActionsRunUrl,
      identityProvided: commitSha.length > 0 && githubActionsRunUrl.length > 0,
    },
    targetOrigin: baseUrl.origin,
    thresholds: {
      requests: thresholds.requests,
      concurrency: thresholds.concurrency,
      p95Milliseconds: thresholds.p95Milliseconds,
      maximumErrorRatePercent: thresholds.maximumErrorRatePercent,
      minimumThroughputPerSecond: thresholds.minimumThroughputPerSecond,
      timeoutMilliseconds: thresholds.timeoutMilliseconds,
    },
    ...summary,
    privacy: {
      requestBodiesSent: false,
      responseBodiesRetained: false,
      authenticationUsed: false,
      clientOrTenantIdentifiersRetained: false,
    },
    scopeBoundary: "This bounded CI profile proves ingress/API/database readiness under concurrent health load; it does not replace production-scale financial-write, document-generation, host-failover, or named recovery drills.",
  };

  const reportPath = resolve(options.reportPath);
  await mkdir(dirname(reportPath), { recursive: true });
  await writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  if (summary.status !== "passed") {
    throw new Error(`Capacity profile failed: ${summary.thresholdFailures.join("; ")}. Evidence: ${reportPath}`);
  }
  return { report, reportPath };
}

async function timedRequest(baseUrl, endpoint, timeoutMilliseconds) {
  const started = performance.now();
  try {
    const response = await fetch(new URL(endpoint, baseUrl), {
      redirect: "error",
      signal: AbortSignal.timeout(timeoutMilliseconds),
      headers: { accept: "application/json", "user-agent": "accounts-capacity-profile-v1" },
    });
    await response.arrayBuffer();
    return {
      endpoint,
      ok: response.status === 200,
      statusCode: response.status,
      durationMilliseconds: performance.now() - started,
      failureCode: response.status === 200 ? null : "unexpected-status",
    };
  } catch (error) {
    return {
      endpoint,
      ok: false,
      statusCode: null,
      durationMilliseconds: performance.now() - started,
      failureCode: error?.name === "TimeoutError" ? "timeout" : "request-failed",
    };
  }
}

function validatedBaseUrl(value) {
  const url = new URL(value);
  const local = ["127.0.0.1", "localhost", "::1"].includes(url.hostname);
  if (url.protocol !== "https:" && !(url.protocol === "http:" && local)) {
    throw new Error("base-url must use HTTPS, except for an explicit loopback target.");
  }
  url.pathname = "/";
  url.search = "";
  url.hash = "";
  return url;
}

function positiveInteger(value, name) {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) throw new Error(`${name} must be a positive integer.`);
  return parsed;
}

function positiveNumber(value, name) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed <= 0) throw new Error(`${name} must be positive.`);
  return parsed;
}

function nonNegativeNumber(value, name) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0) throw new Error(`${name} must be non-negative.`);
  return parsed;
}

function round(value) {
  return Math.round(value * 1_000) / 1_000;
}

function cliOptions(argv) {
  const values = new Map(argv.map((argument) => {
    const match = /^--([^=]+)=(.*)$/.exec(argument);
    if (!match) throw new Error(`Unsupported argument '${argument}'; use --name=value.`);
    return [match[1], match[2]];
  }));
  return {
    baseUrl: values.get("base-url") ?? process.env.BASE_URL,
    reportPath: values.get("report-path") ?? "capacity-profile-report.json",
    requests: values.get("requests") ?? DEFAULTS.requests,
    concurrency: values.get("concurrency") ?? DEFAULTS.concurrency,
    p95Milliseconds: values.get("p95-ms") ?? DEFAULTS.p95Milliseconds,
    maximumErrorRatePercent: values.get("max-error-rate-percent") ?? DEFAULTS.maximumErrorRatePercent,
    minimumThroughputPerSecond: values.get("min-throughput-rps") ?? DEFAULTS.minimumThroughputPerSecond,
    timeoutMilliseconds: values.get("timeout-ms") ?? DEFAULTS.timeoutMilliseconds,
    commitSha: values.get("commit-sha") ?? process.env.GITHUB_SHA ?? "",
    githubActionsRunUrl: values.get("github-actions-run-url") ?? (
      process.env.GITHUB_REPOSITORY && process.env.GITHUB_RUN_ID
        ? `https://github.com/${process.env.GITHUB_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}`
        : ""
    ),
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const options = cliOptions(process.argv.slice(2));
  if (!options.baseUrl) throw new Error("--base-url or BASE_URL is required.");
  const result = await runCapacityProfile(options);
  console.log(`Capacity profile passed: ${result.report.requestCount} requests, p95 ${result.report.p95Milliseconds}ms, ${result.report.throughputPerSecond}/s.`);
  console.log(`Capacity evidence written: ${result.reportPath}`);
}
