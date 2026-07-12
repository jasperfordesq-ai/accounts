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
