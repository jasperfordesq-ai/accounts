import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { getFrontendReadiness } from "../src/app/health/ready/readiness.ts";

const timestamp = "2026-06-07T00:00:00.000Z";

function jsonResponse(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

{
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "staging",
      API_URL: "http://api:8080",
    },
    fetcher: async () => {
      throw new Error("readiness should fail before contacting the API when the proxy key is missing");
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 503);
  assert.equal(result.body.status, "unready");
  assert.equal(result.body.error, "api_proxy_misconfigured");
  assert.equal(result.body.message, "Frontend API proxy is missing its service API key.");
  assert.equal(result.body.checks.api, "unconfigured");
}

{
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "production",
      API_URL: "http://api:8080",
      ACCOUNTS_API_KEY: "right-key",
      ACCOUNTS_API_KEY_HEADER: "Bad Header",
    },
    fetcher: async () => {
      throw new Error("readiness should fail before contacting the API when the proxy key header is invalid");
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 503);
  assert.equal(result.body.status, "unready");
  assert.equal(result.body.error, "api_proxy_misconfigured");
  assert.equal(result.body.message, "ACCOUNTS_API_KEY_HEADER must be a valid HTTP header name.");
  assert.equal(result.body.checks.api, "unconfigured");
}

{
  const calls = [];
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "development",
      API_URL: "http://api:8080",
    },
    fetcher: async (input, init) => {
      calls.push({ input: String(input), headers: new Headers(init?.headers) });
      const url = new URL(String(input));
      if (url.pathname === "/api/companies") {
        return jsonResponse(401, { error: "Authentication required." });
      }

      return jsonResponse(200, { status: "ready", checks: { database: "reachable" } });
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 200);
  assert.equal(result.body.status, "ready");
  assert.equal("upstream" in result.body, false);
  assert.equal(calls.length, 2);
  assert.equal(calls[0].headers.get("X-Accounts-Api-Key"), null);
}

{
  const calls = [];
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "production",
      API_URL: "http://api:8080",
      ACCOUNTS_API_KEY: "wrong-key",
    },
    fetcher: async (input, init) => {
      calls.push({ input: String(input), headers: new Headers(init?.headers) });
      return jsonResponse(401, { error: "Invalid API access key." });
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 503);
  assert.equal(result.body.status, "unready");
  assert.equal(result.body.error, "api_proxy_misconfigured");
  assert.equal(result.body.checks.proxyAuth, "rejected");
  assert.equal(calls.length, 1);
  assert.equal(calls[0].input, "http://api:8080/api/companies");
  assert.equal(calls[0].headers.get("X-Accounts-Api-Key"), "wrong-key");
  assert.equal("upstream" in result.body, false);
  assert.doesNotMatch(JSON.stringify(result.body), /wrong-key|Invalid API access key/i);
}

{
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "production",
      API_URL: "http://api:8080",
      ACCOUNTS_API_KEY: "right-key",
    },
    fetcher: async (input) => {
      const url = new URL(String(input));
      if (url.pathname === "/api/companies") {
        return jsonResponse(401, { error: "Authentication required." });
      }

      return jsonResponse(503, {
        error: "database_unavailable",
        detail: "Npgsql password=secret failed while connecting to accounts",
      });
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 503);
  assert.equal(result.body.status, "unready");
  assert.equal(result.body.error, "api_unavailable");
  assert.equal(result.body.message, "The Accounts API readiness check failed.");
  assert.equal(result.body.checks.upstreamStatus, 503);
  assert.equal("upstream" in result.body, false);
  assert.doesNotMatch(JSON.stringify(result.body), /password=secret|Npgsql|database_unavailable/i);
}

{
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "production",
      API_URL: "http://api:8080",
      ACCOUNTS_API_KEY: "right-key",
    },
    fetcher: async (input) => {
      const url = new URL(String(input));
      if (url.pathname === "/api/companies") {
        return jsonResponse(401, { error: "Authentication required." });
      }

      throw new Error("Npgsql password=secret failed while checking readiness");
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 503);
  assert.equal(result.body.status, "unready");
  assert.equal(result.body.error, "api_unavailable");
  assert.equal(result.body.message, "The Accounts API readiness check failed.");
  assert.doesNotMatch(JSON.stringify(result.body), /password=secret|Npgsql/i);
}

{
  const calls = [];
  const result = await getFrontendReadiness({
    env: {
      NODE_ENV: "production",
      API_URL: "http://api:8080",
      ACCOUNTS_API_KEY: "right-key",
    },
    fetcher: async (input, init) => {
      calls.push({ input: String(input), headers: new Headers(init?.headers) });
      const url = new URL(String(input));
      if (url.pathname === "/api/companies") {
        return jsonResponse(401, { error: "Authentication required." });
      }

      return jsonResponse(200, { status: "ready", checks: { database: "reachable" } });
    },
    now: () => timestamp,
  });

  assert.equal(result.status, 200);
  assert.equal(result.body.status, "ready");
  assert.equal(result.body.checks.proxyAuth, "accepted");
  assert.equal("upstream" in result.body, false);
  assert.equal(calls.length, 2);
  assert.equal(calls[0].headers.get("X-Accounts-Api-Key"), "right-key");
  assert.equal(calls[1].input, "http://api:8080/health/ready");
}

{
  const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), "accounts-readiness-secret-"));
  const apiKeyFile = path.join(tempDir, "accounts_api_key");
  await fs.writeFile(apiKeyFile, "file-backed-key\n", "utf8");
  const calls = [];
  try {
    const result = await getFrontendReadiness({
      env: {
        NODE_ENV: "production",
        API_URL: "http://api:8080",
        ACCOUNTS_API_KEY_FILE: apiKeyFile,
        ACCOUNTS_API_KEY_HEADER: "  X-Accounts-Service-Key  ",
      },
      fetcher: async (input, init) => {
        calls.push({ input: String(input), headers: new Headers(init?.headers) });
        const url = new URL(String(input));
        if (url.pathname === "/api/companies") {
          return jsonResponse(401, { error: "Authentication required." });
        }

        return jsonResponse(200, { status: "ready", checks: { database: "reachable" } });
      },
      now: () => timestamp,
    });

    assert.equal(result.status, 200);
    assert.equal(calls.length, 2);
    assert.equal(calls[0].headers.get("X-Accounts-Service-Key"), "file-backed-key");
    assert.equal(calls[0].headers.get("X-Accounts-Api-Key"), null);
  } finally {
    await fs.rm(tempDir, { recursive: true, force: true });
  }
}
