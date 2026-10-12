import assert from "node:assert/strict";
import { buildApiProxyRequestHeaders, configuredApiKeyHeaderName } from "../src/lib/apiProxyRequest.ts";
import { allowSetCookieForProxyResponse, proxyResponseForUpstream } from "../src/lib/apiProxyResponse.ts";
import { shouldUpgradeInsecureRequests } from "../src/lib/securityHeaders.ts";

const strictTransportSecurity = "max-age=31536000; includeSubDomains; preload";

{
  assert.equal(configuredApiKeyHeaderName(undefined), "X-Accounts-Api-Key");
  assert.equal(configuredApiKeyHeaderName("   "), "X-Accounts-Api-Key");
  assert.equal(configuredApiKeyHeaderName("  X-Accounts-Service-Key  "), "X-Accounts-Service-Key");
  assert.throws(
    () => configuredApiKeyHeaderName("Bad Header"),
    /ACCOUNTS_API_KEY_HEADER must be a valid HTTP header name/,
  );
  assert.throws(
    () => configuredApiKeyHeaderName("Connection"),
    /ACCOUNTS_API_KEY_HEADER must be an end-to-end HTTP header name/,
  );
  assert.throws(
    () => configuredApiKeyHeaderName("Tailscale-User-Login"),
    /ACCOUNTS_API_KEY_HEADER must be an end-to-end HTTP header name/,
  );

  const browserHeaders = new Headers({
    Connection: "X-Dynamic-Hop, X-Second-Hop",
    "Content-Type": "application/json",
    Cookie: "accounts_session=session-token; accounts_csrf=csrf-token",
    "X-CSRF-Token": "csrf-token",
    "If-Match": '"period-v1"',
    "X-Accounts-Api-Key": "client-supplied-default-key",
    "X-Accounts-Service-Key": "client-supplied-custom-key",
    Forwarded: "for=198.51.100.40;proto=http;host=evil.example",
    "X-Forwarded-For": "198.51.100.40",
    "X-Forwarded-Host": "evil.example",
    "X-Forwarded-Port": "80",
    "X-Forwarded-Prefix": "/evil",
    "X-Forwarded-Proto": "http",
    "X-Real-IP": "198.51.100.40",
    "Tailscale-User-Login": "attacker@example.invalid",
    "Tailscale-User-Name": "Attacker Supplied Name",
    "Tailscale-User-Profile-Pic": "https://example.invalid/attacker.png",
    "X-Tailscale-User-Login": "second-attacker@example.invalid",
    "X-Dynamic-Hop": "dynamic-secret",
    "X-Second-Hop": "second-secret",
    Host: "accounts.example.ie",
    "Content-Length": "17",
  });

  const proxyHeaders = buildApiProxyRequestHeaders(browserHeaders, {
    apiKey: "server-side-secret",
    apiKeyHeader: "  X-Accounts-Service-Key  ",
    trustProxyHeaders: false,
    requestProtocol: "https:",
  });

  assert.equal(proxyHeaders.get("Cookie"), "accounts_session=session-token; accounts_csrf=csrf-token");
  assert.equal(proxyHeaders.get("X-CSRF-Token"), "csrf-token");
  assert.equal(proxyHeaders.get("If-Match"), '"period-v1"');
  assert.equal(proxyHeaders.get("X-Accounts-Service-Key"), "server-side-secret");
  assert.equal(proxyHeaders.get("X-Accounts-Api-Key"), null);
  assert.equal(proxyHeaders.get("Forwarded"), null);
  assert.equal(proxyHeaders.get("X-Forwarded-For"), null);
  assert.equal(proxyHeaders.get("X-Forwarded-Host"), null);
  assert.equal(proxyHeaders.get("X-Forwarded-Port"), null);
  assert.equal(proxyHeaders.get("X-Forwarded-Prefix"), null);
  assert.equal(proxyHeaders.get("X-Forwarded-Proto"), null);
  assert.equal(proxyHeaders.get("X-Real-IP"), null);
  assert.equal(proxyHeaders.get("Tailscale-User-Login"), null);
  assert.equal(proxyHeaders.get("Tailscale-User-Name"), null);
  assert.equal(proxyHeaders.get("Tailscale-User-Profile-Pic"), null);
  assert.equal(proxyHeaders.get("X-Tailscale-User-Login"), null);
  assert.equal(proxyHeaders.get("X-Dynamic-Hop"), null);
  assert.equal(proxyHeaders.get("X-Second-Hop"), null);
  assert.equal(proxyHeaders.get("Host"), null);
  assert.equal(proxyHeaders.get("Content-Length"), null);
}

{
  const publicProduction = {
    NODE_ENV: "production",
    DEPLOYMENT_MODE: "PublicProduction",
    PRIVATE_LOCAL_ORIGIN: "http://localhost:3500",
  };
  assert.equal(shouldUpgradeInsecureRequests("http://localhost:3500/login", publicProduction), true);

  const privateServer = { ...publicProduction, DEPLOYMENT_MODE: "PrivateServer" };
  assert.equal(shouldUpgradeInsecureRequests("http://localhost:3500/login", privateServer), false);
  assert.equal(shouldUpgradeInsecureRequests(
    "http://localhost:3500/login",
    privateServer,
    "device.tailnet.ts.net",
  ), true);
  assert.equal(shouldUpgradeInsecureRequests(
    "http://127.0.0.1:3000/login",
    privateServer,
    "localhost:3500",
  ), false);
  assert.equal(shouldUpgradeInsecureRequests("http://localhost:3501/login", privateServer), true);
  assert.equal(shouldUpgradeInsecureRequests("http://127.0.0.1:3500/login", privateServer), true);
  assert.equal(shouldUpgradeInsecureRequests("https://device.tailnet.ts.net/login", privateServer), true);
  assert.equal(shouldUpgradeInsecureRequests("http://localhost.evil:3500/login", privateServer), true);
  assert.equal(shouldUpgradeInsecureRequests("http://localhost:3500/login", {
    ...privateServer,
    PRIVATE_LOCAL_ORIGIN: "http://localhost.evil:3500",
  }), true);

  assert.equal(shouldUpgradeInsecureRequests("http://localhost:3000/login", {
    NODE_ENV: "development",
  }), false);
}

{
  const trustedHeaders = buildApiProxyRequestHeaders(new Headers({
    "X-Real-IP": "203.0.113.24",
    Host: "accounts.example.ie",
  }), {
    apiKey: "server-side-secret",
    apiKeyHeader: undefined,
    trustProxyHeaders: true,
    requestProtocol: "https:",
  });

  assert.equal(trustedHeaders.get("X-Accounts-Api-Key"), "server-side-secret");
  assert.equal(trustedHeaders.get("X-Forwarded-For"), "203.0.113.24");
  assert.equal(trustedHeaders.get("X-Forwarded-Host"), "accounts.example.ie");
  assert.equal(trustedHeaders.get("X-Forwarded-Proto"), "https");
  assert.equal(trustedHeaders.get("X-Real-IP"), null);
  assert.equal(trustedHeaders.get("Forwarded"), null);
}

{
  const upstream = new Response(
    JSON.stringify({
      error: "database_unavailable",
      detail: "Npgsql password=secret failed while loading companies",
    }),
    {
      status: 500,
      headers: {
        "Content-Type": "application/json",
        "X-Internal-Trace": "trace-secret",
      },
    },
  );

  const response = proxyResponseForUpstream(upstream);
  const body = await response.json();

  assert.equal(response.status, 502);
  assert.equal(body.error, "upstream_unavailable");
  assert.equal(body.message, "The Accounts API is unavailable.");
  assert.equal(response.headers.get("X-Internal-Trace"), null);
  assert.doesNotMatch(JSON.stringify(body), /password=secret|Npgsql|database_unavailable|trace-secret/i);
}

{
  const upstream = new Response(JSON.stringify({ error: "Category is not available for this company." }), {
    status: 400,
    headers: {
      "Content-Type": "application/json",
      ETag: '"period-v2"',
      Location: "http://api:8080/internal",
      "Set-Cookie": "accounts_session=secret",
      "X-Internal-Trace": "trace-secret",
    },
  });

  const response = proxyResponseForUpstream(upstream);
  const body = await response.json();

  assert.equal(response.status, 400);
  assert.equal(response.headers.get("Content-Type"), "application/json");
  assert.equal(response.headers.get("ETag"), '"period-v2"');
  assert.equal(response.headers.get("Location"), null);
  assert.equal(response.headers.get("Set-Cookie"), null);
  assert.equal(response.headers.get("X-Internal-Trace"), null);
  assert.equal(body.error, "Category is not available for this company.");
}

{
  // MCP connector headers: back-off, idempotent replay evidence and tracing reach the client;
  // Authorization never comes back.
  const upstream = new Response(JSON.stringify({ code: "CONNECTOR_WRITE_LIMIT" }), {
    status: 429,
    headers: {
      "Content-Type": "application/json",
      "Retry-After": "12",
      "Idempotency-Replayed": "true",
      "Idempotency-Record-Id": "42",
      "X-Correlation-ID": "mcp:abc",
      Authorization: "Bearer fbc1.secret",
    },
  });

  const response = proxyResponseForUpstream(upstream);

  assert.equal(response.status, 429);
  assert.equal(response.headers.get("Retry-After"), "12");
  assert.equal(response.headers.get("Idempotency-Replayed"), "true");
  assert.equal(response.headers.get("Idempotency-Record-Id"), "42");
  assert.equal(response.headers.get("X-Correlation-ID"), "mcp:abc");
  assert.equal(response.headers.get("Authorization"), null);
}

{
  const upstream = new Response("Redirecting to http://api:8080/internal", {
    status: 302,
    headers: {
      Location: "http://api:8080/internal",
      "X-Internal-Trace": "trace-secret",
    },
  });

  const response = proxyResponseForUpstream(upstream);
  const body = await response.json();

  assert.equal(response.status, 502);
  assert.equal(body.error, "upstream_unavailable");
  assert.equal(body.message, "The Accounts API returned an invalid proxy response.");
  assert.equal(response.headers.get("Location"), null);
  assert.equal(response.headers.get("X-Internal-Trace"), null);
  assert.doesNotMatch(JSON.stringify(body), /api:8080|internal|trace-secret/i);
}

{
  const upstreamHeaders = new Headers({
    "Content-Type": "application/json",
    "Set-Cookie": "accounts_session=session-token; HttpOnly; Secure; SameSite=Lax",
    "X-Internal-Trace": "trace-secret",
  });
  upstreamHeaders.append("Set-Cookie", "accounts_csrf=csrf-token; Secure; SameSite=Lax");

  const upstream = new Response(JSON.stringify({ userId: 1 }), {
    status: 200,
    headers: upstreamHeaders,
  });

  const response = proxyResponseForUpstream(upstream, upstreamHeaders, {
    allowSetCookie: allowSetCookieForProxyResponse("POST", ["auth", "mfa", "challenge"], upstream.status),
  });
  const setCookies = response.headers.getSetCookie();
  const body = await response.json();

  assert.equal(response.status, 200);
  assert.equal(body.userId, 1);
  assert.deepEqual(setCookies, [
    "accounts_session=session-token; HttpOnly; Secure; SameSite=Lax",
    "accounts_csrf=csrf-token; Secure; SameSite=Lax",
  ]);
  assert.equal(response.headers.get("X-Internal-Trace"), null);
}

{
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "login"], 200), true);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "logout"], 204), true);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "password"], 200), true);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "mfa", "challenge"], 200), true);

  assert.equal(allowSetCookieForProxyResponse("GET", ["auth", "me"], 200), false);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "login"], 401), false);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "mfa", "challenge"], 401), false);
  assert.equal(allowSetCookieForProxyResponse("GET", ["auth", "mfa", "challenge"], 200), false);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "mfa", "enroll"], 200), false);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "mfa", "challenge", "extra"], 200), false);
  assert.equal(allowSetCookieForProxyResponse("GET", ["auth", "login"], 200), false);
  assert.equal(allowSetCookieForProxyResponse("POST", ["auth", "unknown"], 200), false);
  assert.equal(allowSetCookieForProxyResponse("POST", ["companies"], 200), false);
}

{
  const previousEnableHsts = process.env.ENABLE_HSTS;
  try {
    delete process.env.ENABLE_HSTS;
    const defaultResponse = proxyResponseForUpstream(new Response("{}", {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }));
    assert.equal(defaultResponse.headers.get("Strict-Transport-Security"), null);

    process.env.ENABLE_HSTS = "true";
    const successfulResponse = proxyResponseForUpstream(new Response("{}", {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }));
    assert.equal(successfulResponse.headers.get("Strict-Transport-Security"), strictTransportSecurity);

    const sanitizedErrorResponse = proxyResponseForUpstream(new Response("upstream boom", {
      status: 500,
      headers: { "Content-Type": "text/plain", "X-Internal-Trace": "secret-trace" },
    }));
    assert.equal(sanitizedErrorResponse.status, 502);
    assert.equal(sanitizedErrorResponse.headers.get("Strict-Transport-Security"), strictTransportSecurity);
    assert.equal(sanitizedErrorResponse.headers.get("X-Internal-Trace"), null);
  } finally {
    if (previousEnableHsts === undefined) {
      delete process.env.ENABLE_HSTS;
    } else {
      process.env.ENABLE_HSTS = previousEnableHsts;
    }
  }
}

// --- Remote MCP endpoint (/mcp) and OAuth discovery (/.well-known/*) route handlers ---
{
  const {
    buildMcpUpstreamHeaders,
    mcpResponseHeaders,
    mcpUpstreamUrl,
    proxyMcpRequest,
  } = await import("../src/lib/mcpProxy.ts");
  const { OAUTH_METADATA_API_PATHS, proxyOAuthMetadata } = await import("../src/lib/oauthMetadataProxy.ts");
  const { boundedRequestBody, contentLengthExceedsLimit, maxProxyBodyBytes, PayloadTooLargeError } = await import("../src/lib/apiProxyBody.ts");

  const realFetch = globalThis.fetch;
  const realConsoleError = console.error;
  const withFetch = async (handler, run) => {
    const calls = [];
    globalThis.fetch = async (input, init = {}) => {
      calls.push({ url: String(input), init, headers: new Headers(init.headers) });
      return handler(calls.at(-1));
    };
    console.error = () => {};
    try {
      await run(calls);
    } finally {
      globalThis.fetch = realFetch;
      console.error = realConsoleError;
    }
  };

  // Shared body bounding.
  assert.equal(maxProxyBodyBytes({}), 5 * 1024 * 1024);
  assert.equal(maxProxyBodyBytes({ API_PROXY_MAX_BODY_BYTES: "10" }), 10);
  assert.equal(maxProxyBodyBytes({ API_PROXY_MAX_BODY_BYTES: "nonsense" }), 5 * 1024 * 1024);
  assert.equal(contentLengthExceedsLimit(new Headers({ "Content-Length": "11" }), 10), true);
  assert.equal(contentLengthExceedsLimit(new Headers({ "Content-Length": "10" }), 10), false);
  const bounded = boundedRequestBody(new Response("x".repeat(20)).body, 10);
  await assert.rejects(new Response(bounded).text(), (error) => error instanceof PayloadTooLargeError);

  // Request headers: an explicit MCP allowlist; no cookies, API key, Origin or forwarding headers.
  const inbound = new Headers({
    Authorization: "Bearer fbc1.agent-token",
    Accept: "application/json, text/event-stream",
    "Content-Type": "application/json",
    "Mcp-Session-Id": "session-1",
    "Mcp-Protocol-Version": "2025-06-18",
    "Last-Event-ID": "event-7",
    Cookie: "accounts_session=browser-session; accounts_csrf=csrf",
    "X-CSRF-Token": "csrf",
    "X-Accounts-Api-Key": "client-supplied-key",
    Origin: "https://evil.example",
    Referer: "https://evil.example/page",
    "Sec-Fetch-Site": "cross-site",
    "X-Forwarded-For": "198.51.100.1",
    "Tailscale-User-Login": "attacker@example.invalid",
    Host: "fb.example",
  });
  const upstreamHeaders = buildMcpUpstreamHeaders(inbound);
  assert.deepEqual([...upstreamHeaders.keys()].sort(), [
    "accept",
    "authorization",
    "content-type",
    "last-event-id",
    "mcp-protocol-version",
    "mcp-session-id",
  ]);
  assert.equal(upstreamHeaders.get("Authorization"), "Bearer fbc1.agent-token");

  // Response headers: only the MCP pass-back allowlist.
  const passedBack = mcpResponseHeaders(new Headers({
    "Content-Type": "text/event-stream",
    "Mcp-Session-Id": "session-1",
    "Mcp-Protocol-Version": "2025-06-18",
    "WWW-Authenticate": "Bearer resource_metadata=\"https://fb.example/.well-known/oauth-protected-resource/mcp\"",
    "Cache-Control": "no-cache",
    "Retry-After": "3",
    "Set-Cookie": "stolen=1",
    "X-Internal-Trace": "secret",
    Location: "http://mcp:3100/internal",
  }));
  assert.deepEqual([...passedBack.keys()].sort(), [
    "cache-control",
    "content-type",
    "mcp-protocol-version",
    "mcp-session-id",
    "retry-after",
    "www-authenticate",
  ]);

  const mcpEnv = { MCP_URL: "http://mcp:3100/mcp" };

  // MCP_URL unset (or invalid) answers 404 JSON and never calls anything.
  assert.equal(mcpUpstreamUrl({}), null);
  assert.equal(mcpUpstreamUrl({ MCP_URL: "ftp://mcp/mcp" }), null);
  await withFetch(() => { throw new Error("must not be called"); }, async (calls) => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", { method: "POST", body: "{}" }), {});
    assert.equal(response.status, 404);
    assert.equal((await response.json()).error, "not_found");
    assert.equal(calls.length, 0);
  });

  // POST streams the body to MCP_URL with allowlisted headers and passes the response back untouched.
  await withFetch(
    () => new Response("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", {
      status: 200,
      headers: { "Content-Type": "application/json", "Mcp-Session-Id": "session-2", "Set-Cookie": "x=1" },
    }),
    async (calls) => {
      const response = await proxyMcpRequest(new Request("https://fb.example/mcp?ignored=1", {
        method: "POST",
        headers: inbound,
        body: "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}",
      }), mcpEnv);
      assert.equal(calls.length, 1);
      assert.equal(calls[0].url, "http://mcp:3100/mcp");
      assert.equal(calls[0].init.method, "POST");
      assert.equal(calls[0].init.redirect, "manual");
      assert.equal(calls[0].headers.get("cookie"), null);
      assert.equal(calls[0].headers.get("x-accounts-api-key"), null);
      assert.equal(calls[0].headers.get("origin"), null);
      assert.equal(await new Response(calls[0].init.body).text(), "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}");
      assert.equal(response.status, 200);
      assert.equal(response.headers.get("Mcp-Session-Id"), "session-2");
      assert.equal(response.headers.get("Set-Cookie"), null);
      assert.equal(await response.text(), "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}");
    },
  );

  // 401 keeps WWW-Authenticate so clients can discover the authorization server.
  await withFetch(
    () => new Response("{\"error\":\"invalid_token\"}", {
      status: 401,
      headers: { "Content-Type": "application/json", "WWW-Authenticate": "Bearer error=\"invalid_token\"" },
    }),
    async () => {
      const response = await proxyMcpRequest(new Request("https://fb.example/mcp", { method: "GET" }), mcpEnv);
      assert.equal(response.status, 401);
      assert.equal(response.headers.get("WWW-Authenticate"), "Bearer error=\"invalid_token\"");
    },
  );

  // DELETE without a body is forwarded without one.
  await withFetch(() => new Response(null, { status: 204 }), async (calls) => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", {
      method: "DELETE",
      headers: { "Mcp-Session-Id": "session-3" },
    }), mcpEnv);
    assert.equal(response.status, 204);
    assert.equal(calls[0].init.method, "DELETE");
    assert.equal(calls[0].init.body, undefined);
    assert.equal(calls[0].headers.get("mcp-session-id"), "session-3");
  });

  // An SSE body keeps streaming after the connect timeout has elapsed: the timer covers headers only.
  await withFetch(() => {
    const encoder = new TextEncoder();
    const body = new ReadableStream({
      async start(controller) {
        controller.enqueue(encoder.encode("event: message\ndata: 1\n\n"));
        await new Promise((resolve) => setTimeout(resolve, 60));
        controller.enqueue(encoder.encode("event: message\ndata: 2\n\n"));
        controller.close();
      },
    });
    return new Response(body, { status: 200, headers: { "Content-Type": "text/event-stream" } });
  }, async () => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", { method: "GET" }), {
      ...mcpEnv,
      API_PROXY_MCP_TIMEOUT_MS: "20",
    });
    assert.equal(response.headers.get("Content-Type"), "text/event-stream");
    assert.equal(await response.text(), "event: message\ndata: 1\n\nevent: message\ndata: 2\n\n");
  });

  // No response headers before the connect timeout: 504.
  await withFetch(({ init }) => new Promise((_, reject) => {
    init.signal.addEventListener("abort", () => reject(init.signal.reason));
  }), async () => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", { method: "GET" }), {
      ...mcpEnv,
      API_PROXY_MCP_TIMEOUT_MS: "10",
    });
    assert.equal(response.status, 504);
  });

  // Unreachable service, upstream redirect and oversize body.
  await withFetch(() => { throw new TypeError("fetch failed"); }, async () => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", { method: "GET" }), mcpEnv);
    assert.equal(response.status, 502);
  });
  await withFetch(() => new Response(null, { status: 302, headers: { Location: "http://mcp:3100/internal" } }), async () => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", { method: "GET" }), mcpEnv);
    assert.equal(response.status, 502);
    assert.equal(response.headers.get("Location"), null);
  });
  await withFetch(() => { throw new Error("must not be called"); }, async (calls) => {
    const response = await proxyMcpRequest(new Request("https://fb.example/mcp", {
      method: "POST",
      headers: { "Content-Length": "100" },
      body: "x".repeat(100),
    }), { ...mcpEnv, API_PROXY_MCP_MAX_BODY_BYTES: "10" });
    assert.equal(response.status, 413);
    assert.equal(calls.length, 0);
  });

  // OAuth discovery: server-side GET to the API with the service key, no caller credentials, cached publicly.
  const metadataEnv = { API_URL: "http://api:8080", ACCOUNTS_API_KEY: "server-side-secret", NODE_ENV: "production" };
  await withFetch(
    () => new Response("{\"issuer\":\"https://fb.example\"}", {
      status: 200,
      headers: { "Content-Type": "application/json; charset=utf-8", "X-Internal": "1" },
    }),
    async (calls) => {
      const response = await proxyOAuthMetadata(new Request("https://fb.example/.well-known/oauth-authorization-server", {
        headers: { Cookie: "accounts_session=s", Authorization: "Bearer fbc1.x", "X-Accounts-Api-Key": "client" },
      }), OAUTH_METADATA_API_PATHS.authorizationServer, metadataEnv);
      assert.equal(calls[0].url, "http://api:8080/api/oauth/metadata/authorization-server");
      assert.equal(calls[0].init.method, "GET");
      assert.equal(calls[0].headers.get("X-Accounts-Api-Key"), "server-side-secret");
      assert.equal(calls[0].headers.get("Cookie"), null);
      assert.equal(calls[0].headers.get("Authorization"), null);
      assert.equal(response.status, 200);
      assert.equal(response.headers.get("Cache-Control"), "public, max-age=300");
      assert.equal(response.headers.get("Content-Type"), "application/json; charset=utf-8");
      assert.equal(response.headers.get("X-Internal"), null);
      assert.deepEqual(await response.json(), { issuer: "https://fb.example" });
    },
  );
  await withFetch(() => new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }), async (calls) => {
    await proxyOAuthMetadata(
      new Request("https://fb.example/.well-known/oauth-protected-resource/mcp"),
      OAUTH_METADATA_API_PATHS.protectedResource,
      metadataEnv,
    );
    assert.equal(calls[0].url, "http://api:8080/api/oauth/metadata/protected-resource");
  });
  await withFetch(() => new Response(null, { status: 404 }), async () => {
    const response = await proxyOAuthMetadata(
      new Request("https://fb.example/.well-known/oauth-protected-resource"),
      OAUTH_METADATA_API_PATHS.protectedResource,
      metadataEnv,
    );
    assert.equal(response.status, 404);
    assert.equal(response.headers.get("Cache-Control"), "no-store");
  });
  await withFetch(() => new Response("Npgsql failure", { status: 500 }), async () => {
    const response = await proxyOAuthMetadata(
      new Request("https://fb.example/.well-known/oauth-authorization-server"),
      OAUTH_METADATA_API_PATHS.authorizationServer,
      metadataEnv,
    );
    assert.equal(response.status, 502);
    assert.doesNotMatch(await response.text(), /Npgsql/);
  });
  await withFetch(() => { throw new Error("must not be called"); }, async (calls) => {
    const response = await proxyOAuthMetadata(
      new Request("https://fb.example/.well-known/oauth-authorization-server"),
      OAUTH_METADATA_API_PATHS.authorizationServer,
      { NODE_ENV: "production", API_URL: "http://api:8080" },
    );
    assert.equal(response.status, 500);
    assert.equal(calls.length, 0);
  });
}

// The CSP proxy matcher keeps nonces on every page (including the OAuth consent page) but skips the
// JSON / event-stream route handlers.
{
  const { readFileSync } = await import("node:fs");
  const source = readFileSync(new URL("../src/proxy.ts", import.meta.url), "utf8");
  const pattern = /source: "\/\(\(\?!(.*?)\)\.\*\)"/.exec(source);
  assert.ok(pattern, "proxy matcher source must remain a negative-lookahead pattern");
  const matcher = new RegExp(`^/(?!${pattern[1].replaceAll("\\\\", "\\")}).*$`);
  for (const path of ["/", "/login", "/oauth/authorize", "/settings/approvals", "/settings/agents", "/mcpx", "/apiary"]) {
    assert.ok(matcher.test(path), `${path} must receive the CSP nonce`);
  }
  for (const path of ["/api/auth/me", "/mcp", "/.well-known/oauth-authorization-server", "/.well-known/oauth-protected-resource/mcp", "/_next/static/x.js"]) {
    assert.ok(!matcher.test(path), `${path} must skip the CSP proxy`);
  }
}

console.log("API proxy, MCP proxy and OAuth discovery checks passed.");
