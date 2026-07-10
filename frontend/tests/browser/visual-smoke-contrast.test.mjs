import assert from "node:assert/strict";
import test from "node:test";
import { chromium } from "@playwright/test";
import { checkThemeContrast } from "../../scripts/visual-smoke.mjs";

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
