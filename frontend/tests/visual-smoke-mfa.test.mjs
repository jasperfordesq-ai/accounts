import assert from "node:assert/strict";
import { chmod, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import {
  assertRequiredMfaCompleted,
  consumeEphemeralMfaHandoff,
  nextFreshTotpCode,
  safeLoginFailureDiagnostic,
  totpCodeForCounter,
  visualFixtureIdempotencyKey,
} from "../scripts/visual-smoke.mjs";

const RFC_SECRET = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

test("visual smoke TOTP uses the RFC 6238 SHA-1 counter calculation", () => {
  assert.equal(totpCodeForCounter(RFC_SECRET, 1), "287082");
});

test("visual smoke waits for a counter newer than the smoke enrollment counter", async () => {
  let nowMs = 59_900;
  const waits = [];
  const credential = await nextFreshTotpCode(
    { secret: RFC_SECRET, lastUsedCounter: 1 },
    {
      now: () => nowMs,
      wait: async (milliseconds) => {
        waits.push(milliseconds);
        nowMs += milliseconds;
      },
    },
  );

  assert.deepEqual(waits, [350]);
  assert.equal(credential.counter, 2);
  assert.equal(credential.code, totpCodeForCounter(RFC_SECRET, 2));
});

test("visual smoke login diagnostics redact enrollment seeds and credentials", () => {
  const email = "owner@example.ie";
  const password = "Correct Horse Battery Staple 1!";
  const diagnostic = safeLoginFailureDiagnostic(
    new Error(`Enrollment failed for ${email} using ${password}; seed ${RFC_SECRET}`),
    "mfa-enrollment",
    [email, password, RFC_SECRET],
  );

  assert.equal(diagnostic.includes(email), false);
  assert.equal(diagnostic.includes(password), false);
  assert.equal(diagnostic.includes(RFC_SECRET), false);
  assert.match(diagnostic, /\[redacted\]/);
  assert.match(diagnostic, /Login UI state: mfa-enrollment/);
});

test("visual smoke fails when a privileged login reaches the dashboard without fresh MFA", () => {
  assert.throws(
    () => assertRequiredMfaCompleted({ secret: RFC_SECRET }, false),
    /bypassed the required fresh MFA challenge/,
  );
  assert.doesNotThrow(() => assertRequiredMfaCompleted({ secret: RFC_SECRET }, true));
  assert.doesNotThrow(() => assertRequiredMfaCompleted({ secret: null }, false));
});

test("visual smoke diagnostics never read whole form text", async () => {
  const source = await readFile(new URL("../scripts/visual-smoke.mjs", import.meta.url), "utf8");
  assert.doesNotMatch(source, /locator\(["']form["']\)\.innerText/);
  assert.doesNotMatch(source, /page\.getByRole\(["']alert["']\)/);
  assert.doesNotMatch(source, /document\.querySelector\(["']\[role=[^)]*alert/);
  assert.match(source, /form \[role=[^\]]*alert/);
});

test("visual smoke consumes and deletes a mode-0600 runner MFA handoff", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "accounts-visual-mfa-"));
  const handoffPath = path.join(directory, "totp-handoff.json");
  try {
    await writeFile(handoffPath, JSON.stringify({
      schemaVersion: "accounts-visual-mfa-handoff-v1",
      secret: RFC_SECRET,
      lastAcceptedCounter: 42,
    }), { encoding: "utf8", mode: 0o600 });

    assert.deepEqual(await consumeEphemeralMfaHandoff(handoffPath, { temporaryRoot: directory }), {
      secret: RFC_SECRET,
      lastUsedCounter: 42,
    });
    await assert.rejects(() => readFile(handoffPath), (error) => error?.code === "ENOENT");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("visual smoke fails closed when an explicit MFA handoff is missing", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "accounts-visual-mfa-missing-"));
  try {
    await assert.rejects(
      () => consumeEphemeralMfaHandoff(path.join(directory, "missing.json"), { temporaryRoot: directory }),
      /required visual smoke MFA handoff file is missing/,
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("visual smoke deletes malformed MFA handoffs while failing closed", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "accounts-visual-mfa-invalid-"));
  const handoffPath = path.join(directory, "totp-handoff.json");
  try {
    await writeFile(handoffPath, JSON.stringify({
      schemaVersion: "accounts-visual-mfa-handoff-v1",
      secret: "not-base32",
      lastAcceptedCounter: 42,
    }), { encoding: "utf8", mode: 0o600 });

    await assert.rejects(
      () => consumeEphemeralMfaHandoff(handoffPath, { temporaryRoot: directory }),
      /invalid Base32 secret/,
    );
    await assert.rejects(() => readFile(handoffPath), (error) => error?.code === "ENOENT");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("visual smoke rejects and deletes a group-readable MFA handoff", {
  skip: process.platform === "win32",
}, async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "accounts-visual-mfa-mode-"));
  const handoffPath = path.join(directory, "totp-handoff.json");
  try {
    await writeFile(handoffPath, JSON.stringify({
      schemaVersion: "accounts-visual-mfa-handoff-v1",
      secret: RFC_SECRET,
      lastAcceptedCounter: 42,
    }), "utf8");
    await chmod(handoffPath, 0o640);

    await assert.rejects(
      () => consumeEphemeralMfaHandoff(handoffPath, { temporaryRoot: directory }),
      /mode 0600/,
    );
    await assert.rejects(() => readFile(handoffPath), (error) => error?.code === "ENOENT");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("visual smoke rejects an outside-root handoff without deleting it", async () => {
  const allowedDirectory = await mkdtemp(path.join(os.tmpdir(), "accounts-visual-mfa-allowed-"));
  const outsideDirectory = await mkdtemp(path.join(os.tmpdir(), "accounts-visual-mfa-outside-"));
  const handoffPath = path.join(outsideDirectory, "do-not-delete.json");
  try {
    const payload = JSON.stringify({
      schemaVersion: "accounts-visual-mfa-handoff-v1",
      secret: RFC_SECRET,
      lastAcceptedCounter: 42,
    });
    await writeFile(handoffPath, payload, { encoding: "utf8", mode: 0o600 });

    await assert.rejects(
      () => consumeEphemeralMfaHandoff(handoffPath, { temporaryRoot: allowedDirectory }),
      /must remain inside runner-temporary storage/,
    );
    assert.equal(await readFile(handoffPath, "utf8"), payload);
  } finally {
    await rm(allowedDirectory, { recursive: true, force: true });
    await rm(outsideDirectory, { recursive: true, force: true });
  }
});

test("smoke writes the disposable MFA handoff only after authenticated checks and logout pass", async () => {
  const source = await readFile(new URL("../../scripts/smoke-production.ps1", import.meta.url), "utf8");
  const writeIndex = source.lastIndexOf("Write-EphemeralMfaHandoff `");
  const logoutCheckIndex = source.indexOf('throw "Expected /api/auth/me to be unauthorized after logout');
  assert.ok(logoutCheckIndex >= 0, "smoke must retain its post-logout session assertion");
  assert.ok(writeIndex > logoutCheckIndex, "MFA handoff must not exist until every smoke assertion has passed");
  assert.match(source, /mfaVerified[^\r\n]*true/);
  assert.match(source, /mfaMethod[^\r\n]*totp/);
});

test("visual smoke company setup retains idempotent mutations and the evidence-backed ARD contract", async () => {
  const source = await readFile(new URL("../scripts/visual-smoke.mjs", import.meta.url), "utf8");
  const companyKey = visualFixtureIdempotencyKey("company", "11111111-1111-4111-8111-111111111111");
  const periodKey = visualFixtureIdempotencyKey("period", "22222222-2222-4222-8222-222222222222");
  assert.match(companyKey, /^[A-Za-z0-9._:-]{8,128}$/);
  assert.match(periodKey, /^[A-Za-z0-9._:-]{8,128}$/);
  assert.notEqual(companyKey, periodKey);
  assert.ok(source.includes('headers.set("Idempotency-Key", requestIdempotencyKey)'));
  assert.ok(source.includes('idempotencyKey: visualFixtureIdempotencyKey("company")'));
  assert.ok(source.includes('idempotencyKey: visualFixtureIdempotencyKey("period")'));
  assert.match(source, /annualReturnDate: "2025-09-30"/);
  assert.match(source, /annualReturnDateEffectiveFrom: "2025-09-30"/);
  assert.match(source, /annualReturnDateSource: "CroRecord"/);
  assert.match(source, /annualReturnDateEvidenceReference: "CI-VISUAL-SMOKE-CRO-ARD-FIXTURE"/);
  assert.doesNotMatch(source, /ardMonth:/);
});
