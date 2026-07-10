import assert from "node:assert/strict";
import test from "node:test";

import {
  createIdempotencyKey,
  fetchDocumentBlob,
  updateCroFilingStatus,
  uploadBankCsv,
} from "../src/lib/api.ts";

function ok(body = {}) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

test("automatic unsafe retry reuses one key while a fresh user command gets a new key", async (context) => {
  const originalFetch = globalThis.fetch;
  const originalDocument = globalThis.document;
  const requests = [];
  globalThis.document = { cookie: "accounts_csrf=csrf-idempotency-test" };
  globalThis.fetch = async (input, init = {}) => {
    const headers = new Headers(init.headers);
    requests.push({
      path: String(input),
      method: init.method,
      key: headers.get("Idempotency-Key"),
    });
    if (requests.length === 1) {
      return new Response(JSON.stringify({ error: "transient" }), {
        status: 503,
        statusText: "Service Unavailable",
        headers: { "Content-Type": "application/json" },
      });
    }
    return ok({ status: "Prepared" });
  };
  context.after(() => {
    globalThis.fetch = originalFetch;
    globalThis.document = originalDocument;
  });

  await updateCroFilingStatus(42, 7, { status: "Prepared" });
  await updateCroFilingStatus(42, 7, { status: "Prepared" });

  assert.equal(requests.length, 3);
  assert.equal(requests[0].key, requests[1].key);
  assert.notEqual(requests[1].key, requests[2].key);
  for (const request of requests) {
    assert.match(request.key, /^[A-Za-z0-9._:-]{8,128}$/);
    assert.equal(request.method, "PUT");
  }
});

test("an explicitly retained user-command key survives repeated invocations", async (context) => {
  const originalFetch = globalThis.fetch;
  const requests = [];
  globalThis.fetch = async (_input, init = {}) => {
    requests.push(new Headers(init.headers).get("Idempotency-Key"));
    return ok({ status: "Prepared" });
  };
  context.after(() => {
    globalThis.fetch = originalFetch;
  });

  const retainedKey = createIdempotencyKey("filing-cro-status");
  await updateCroFilingStatus(42, 7, { status: "Prepared" }, retainedKey);
  await updateCroFilingStatus(42, 7, { status: "Prepared" }, retainedKey);

  assert.deepEqual(requests, [retainedKey, retainedKey]);
});

test("POST document blob retry retains its key and exact successful bytes", async (context) => {
  const originalFetch = globalThis.fetch;
  const originalDocument = globalThis.document;
  const requests = [];
  globalThis.document = { cookie: "accounts_csrf=csrf-document-test" };
  globalThis.fetch = async (_input, init = {}) => {
    requests.push(new Headers(init.headers).get("Idempotency-Key"));
    if (requests.length === 1) throw new TypeError("fetch failed after server completion");
    return new Response(new Uint8Array([37, 80, 68, 70, 45, 49]), {
      status: 200,
      headers: { "Content-Type": "application/pdf" },
    });
  };
  context.after(() => {
    globalThis.fetch = originalFetch;
    globalThis.document = originalDocument;
  });

  const blob = await fetchDocumentBlob("/api/companies/1/periods/2/documents/cro-filing-pack", "POST");

  assert.deepEqual([...new Uint8Array(await blob.arrayBuffer())], [37, 80, 68, 70, 45, 49]);
  assert.equal(requests.length, 2);
  assert.equal(requests[0], requests[1]);
  assert.match(requests[0], /^[A-Za-z0-9._:-]{8,128}$/);
});

test("bank upload retries a 5xx with the same key and returns the retained result", async (context) => {
  const originalFetch = globalThis.fetch;
  const originalDocument = globalThis.document;
  const requests = [];
  globalThis.document = { cookie: "accounts_csrf=csrf-import-test" };
  globalThis.fetch = async (_input, init = {}) => {
    requests.push(new Headers(init.headers).get("Idempotency-Key"));
    if (requests.length === 1) {
      return new Response(JSON.stringify({ error: "response lost" }), {
        status: 503,
        headers: { "Content-Type": "application/json" },
      });
    }
    return ok({
      totalRows: 1,
      importedRows: 1,
      duplicateCandidates: 0,
      autoCategorised: 0,
      warnings: [],
      importBatchId: 17,
      sourceFilename: "bank.csv",
      sourceFileSha256: "a".repeat(64),
      sourceFileBytes: 64,
    });
  };
  context.after(() => {
    globalThis.fetch = originalFetch;
    globalThis.document = originalDocument;
  });

  const result = await uploadBankCsv(
    1,
    2,
    3,
    new File(["Date,Description,Amount\n01/01/2025,Receipt,100\n"], "bank.csv", { type: "text/csv" }),
  );

  assert.equal(result.importBatchId, 17);
  assert.equal(requests.length, 2);
  assert.equal(requests[0], requests[1]);
  assert.match(requests[0], /^[A-Za-z0-9._:-]{8,128}$/);
});
