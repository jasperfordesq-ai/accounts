import assert from "node:assert/strict";
import http from "node:http";
import test from "node:test";
import { chromium } from "@playwright/test";
import {
  checkThemeContrast,
  unexpectedVisualSmokeBrowserErrors,
} from "../../scripts/visual-smoke.mjs";

async function withPage(markup, action) {
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1000, height: 800 } });
    await page.setContent(markup);
    return await action(page);
  } finally {
    await browser.close();
  }
}

const documentShell = (content) => `<!doctype html>
<html>
  <head>
    <style>
      body, main { margin: 0; background: rgb(255, 255, 255); }
      input::placeholder { color: rgb(89, 89, 89); opacity: 1; }
    </style>
  </head>
  <body><main>${content}</main></body>
</html>`;

test("real Chromium contrast collection covers text, controls, placeholders, disabled state, and gradients", async () => {
  const result = await withPage(documentShell(`
    <p style="color: rgb(89, 89, 89); font-size: 16px">Normal accountant guidance</p>
    <p style="color: rgb(119, 119, 119); font-size: 24px">Large status heading</p>
    <button style="color: white; background: rgb(0, 107, 95); border: 2px solid rgb(0, 82, 73)">Save review</button>
    <button disabled style="color: white; background: rgb(75, 85, 99); border: 2px solid rgb(55, 65, 81)">Locked action</button>
    <input aria-label="Search records" placeholder="Search records" style="background: white; border: 2px solid rgb(107, 114, 128); color: rgb(17, 24, 39)" />
    <div style="background-image: linear-gradient(90deg, rgb(255, 255, 255), rgb(229, 231, 235))">
      <span style="color: rgb(17, 24, 39)">Gradient-backed evidence</span>
    </div>
  `), (page) => checkThemeContrast(page, "behavioral-pass"));

  assert.equal(result.status, "passed");
  assert.ok(result.sampledNormalTextCount >= 1);
  assert.ok(result.sampledLargeTextCount >= 1);
  assert.ok(result.sampledInteractiveTextCount >= 2);
  assert.ok(result.sampledPlaceholderCount >= 1);
  assert.ok(result.sampledUiComponentCount >= 2);
  assert.ok(result.sampledGradientTextCount >= 1);
  assert.ok(result.minimumNormalTextContrastRatio >= 4.5);
  assert.ok(result.minimumLargeTextContrastRatio >= 3);
  assert.ok(result.minimumLargeTextContrastRatio < 4.5, "large text should exercise its distinct 3:1 threshold");
  assert.ok(result.minimumUiComponentContrastRatio >= 3);
});

test("real Chromium rejects normal text below 4.5:1", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <p style="color: rgb(119, 119, 119); font-size: 16px">Low contrast normal text</p>
      <button style="color: white; background: rgb(0, 107, 95); border: 2px solid rgb(0, 82, 73)">Action</button>
    `), (page) => checkThemeContrast(page, "normal-text-failure")),
    /required=4\.50/,
  );
});

test("real Chromium rejects indistinguishable interactive boundaries", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <p style="color: rgb(17, 24, 39)">Accessible text remains present</p>
      <button style="color: rgb(17, 24, 39); background: rgb(245, 245, 245); border: 1px solid rgb(238, 238, 238)">Weak boundary</button>
    `), (page) => checkThemeContrast(page, "ui-boundary-failure")),
    /UI component/,
  );
});

test("real Chromium permits only the paired anonymous session 401 and retains other browser errors", async () => {
  const server = http.createServer((request, response) => {
    if (request.url === "/api/auth/me") {
      response.writeHead(401, { "content-type": "application/json" });
      response.end("{}");
      return;
    }
    if (request.url === "/api/unexpected") {
      response.writeHead(403, { "content-type": "application/json" });
      response.end("{}");
      return;
    }
    response.writeHead(200, { "content-type": "text/html" });
    response.end(`<!doctype html><script>
      fetch('/api/auth/me');
      fetch('/api/unexpected');
      setTimeout(() => { throw new Error('synthetic page failure'); }, 10);
    </script>`);
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));

  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    const origin = `http://127.0.0.1:${server.address().port}`;
    const page = await browser.newPage();
    const consoleErrors = [];
    const pageErrors = [];
    const failedResponses = [];
    page.on("console", (message) => {
      if (message.type() === "error") {
        consoleErrors.push({ type: message.type(), text: message.text(), location: message.location() });
      }
    });
    page.on("pageerror", (error) => pageErrors.push(error.message));
    page.on("response", (response) => {
      if (response.status() >= 400) {
        failedResponses.push({
          url: response.url(),
          status: response.status(),
          method: response.request().method(),
        });
      }
    });

    const expectedSessionResponse = page.waitForResponse(
      (response) => response.url() === `${origin}/api/auth/me` && response.status() === 401,
    );
    const expectedUnexpectedResponse = page.waitForResponse(
      (response) => response.url() === `${origin}/api/unexpected` && response.status() === 403,
    );
    const expectedPageError = page.waitForEvent("pageerror", { timeout: 5_000 });
    await page.goto(`${origin}/login`);
    await Promise.all([expectedSessionResponse, expectedUnexpectedResponse, expectedPageError]);
    const errors = unexpectedVisualSmokeBrowserErrors({
      state: { id: "login", authMode: "anonymous" },
      consoleErrors,
      pageErrors,
      failedResponses,
      pageUrl: page.url(),
    });

    assert.equal(consoleErrors.some((message) => message.location.url === `${origin}/api/auth/me`), true);
    assert.equal(errors.some((error) => error.includes("status of 401")), false);
    assert.equal(errors.some((error) => error.includes("status of 403")), true);
    assert.equal(errors.some((error) => error === "pageerror: synthetic page failure"), true);
  } finally {
    await browser?.close();
    await new Promise((resolve) => server.close(resolve));
  }
});
