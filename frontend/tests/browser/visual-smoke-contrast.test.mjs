import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import http from "node:http";
import test from "node:test";
import { chromium } from "@playwright/test";
import {
  checkThemeContrast,
  unexpectedVisualSmokeBrowserErrors,
} from "../../scripts/visual-smoke.mjs";

const applicationStyles = readFileSync(new URL("../../src/app/globals.css", import.meta.url), "utf8");

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
    <button style="color: white; background: lab(44.4871% -41.0396 11.0361); border: 2px solid lab(35.3675% -33.1188 8.04002)">Modern colour action</button>
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

test("real Chromium samples a modern CSS colour control boundary", async () => {
  const result = await withPage(documentShell(`
    <p style="color: rgb(17, 24, 39)">Modern colour evidence</p>
    <button style="color: white; background: lab(44.4871% -41.0396 11.0361); border: 2px solid lab(35.3675% -33.1188 8.04002)">Modern colour action</button>
  `), (page) => checkThemeContrast(page, "modern-colour-control"));

  assert.equal(result.status, "passed");
  assert.equal(result.sampledUiComponentCount, 1);
  assert.ok(result.minimumUiComponentContrastRatio >= 3);
});

test("application button states retain semantic contrast and disabled precedence in both themes", async () => {
  for (const theme of ["", "dark"]) {
    const result = await withPage(`<!doctype html>
      <html class="${theme}">
        <head><style>${applicationStyles}</style></head>
        <body style="margin: 0; background: var(--background)">
          <main style="background: var(--surface); padding: 12px">
            <p style="color: var(--foreground)">Button state evidence</p>
            <button class="button button--primary">Primary</button>
            <button class="button button--primary" disabled>Disabled primary</button>
            <button class="button button--secondary" disabled>Disabled secondary</button>
            <button class="button button--outline" aria-disabled="true">Disabled outline</button>
          </main>
        </body>
      </html>`, async (page) => {
        const computed = await page.evaluate(() => {
          const buttons = Array.from(document.querySelectorAll("button"));
          return buttons.map((button) => {
            const style = getComputedStyle(button);
            return {
              backgroundColor: style.backgroundColor,
              borderColor: style.borderTopColor,
              color: style.color,
              opacity: style.opacity,
            };
          });
        });
        const muted = theme === "dark" ? "rgb(32, 40, 32)" : "rgb(238, 242, 237)";
        const mutedForeground = theme === "dark" ? "rgb(170, 181, 170)" : "rgb(102, 112, 103)";
        assert.equal(computed[2].backgroundColor, muted);
        assert.equal(computed[2].color, mutedForeground);
        assert.equal(computed[3].backgroundColor, muted);
        assert.equal(computed[3].color, mutedForeground);
        assert.equal(computed[1].opacity, "1");
        assert.equal(computed[2].opacity, "1");
        assert.equal(computed[3].opacity, "1");
        return checkThemeContrast(page, `semantic-button-states-${theme || "light"}`);
      });

    assert.equal(result.status, "passed");
    assert.ok(result.sampledUiComponentCount >= 4);
  }
});

test("real Chromium composites percentage-alpha foreground colours", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background: black; padding: 8px">
        <p id="percentage-alpha" style="color: white">Transparent guidance</p>
      </div>
    `), async (page) => {
      await page.evaluate(() => {
        const originalGetComputedStyle = window.getComputedStyle.bind(window);
        window.getComputedStyle = (element, pseudoElement) => {
          const style = originalGetComputedStyle(element, pseudoElement);
          if (element.id !== "percentage-alpha" || pseudoElement) return style;
          return new Proxy(style, {
            get(target, property) {
              if (property === "color") return "rgb(100% 100% 100% / 20%)";
              const result = Reflect.get(target, property, target);
              return typeof result === "function" ? result.bind(target) : result;
            },
          });
        };
      });
      return checkThemeContrast(page, "percentage-alpha-foreground");
    }),
    /ratio=1\.[0-9]+ required=4\.50/,
  );
});

test("real Chromium fails closed when partial opacity changes rendered contrast", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <p style="color: black; opacity: 0.5">Partially transparent guidance</p>
    `), (page) => checkThemeContrast(page, "partial-opacity-failure")),
    /partial CSS opacity requires explicit contrast-safe colours/,
  );
});

test("real Chromium rejects low-contrast modern gradient stops", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(90deg in srgb, lab(100% 0 0), oklch(96% 0 0)); padding: 8px">
        <span style="color: white">Modern gradient guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "modern-gradient-failure")),
    /required=4\.55/,
  );
});

test("real Chromium retains the terminal gradient stop in contrast evidence", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(90deg, white, rgb(116, 116, 116)); padding: 8px">
        <span style="color: black">Terminal gradient stop guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "terminal-gradient-stop-failure")),
    /required=4\.55/,
  );
});

test("real Chromium fails closed for implicit modern-colour gradient interpolation", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(lab(29.2787 -56.4096 -105.838), lab(46.4496 -64.1704 42.042)); padding: 8px">
        <span style="color: white">Implicit modern gradient guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "implicit-modern-gradient")),
    /implicit modern-colour gradient interpolation is not machine-verifiable/,
  );
});

test("real Chromium fails closed for translucent gradient stops", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-color: red; background-image: linear-gradient(rgb(128 224 0 / 25%), rgb(0 192 160)); padding: 8px">
        <span style="color: black">Translucent gradient guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "translucent-gradient")),
    /translucent gradient stops require retained human visual review/,
  );
});

test("real Chromium fails closed for multi-stop gradients", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(90deg, rgb(255, 0, 0), rgb(0, 148, 0), rgb(255, 0, 0)); padding: 8px">
        <span style="color: black">Multi-stop gradient guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "multi-stop-gradient")),
    /multi-stop gradients require retained human visual review/,
  );
});

test("real Chromium samples low-contrast gradient interiors", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(90deg, rgb(255, 0, 0), rgb(0, 148, 0)); padding: 8px">
        <span style="color: black">Gradient midpoint guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "gradient-interior-failure")),
    /required=4\.55/,
  );
});

test("real Chromium applies a conservative guard band to near-threshold gradients", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(90deg, rgb(82, 160, 34), rgb(234, 54, 66)); padding: 8px">
        <span style="color: black">Near-threshold gradient guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "gradient-guard-band-failure")),
    /required=4\.55/,
  );
});

test("real Chromium fails closed for non-sRGB gradient interpolation", async () => {
  await assert.rejects(
    () => withPage(documentShell(`
      <div style="background-image: linear-gradient(90deg in hsl longer hue, red, blue); padding: 8px">
        <span style="color: white">Hue interpolation guidance</span>
      </div>
    `), (page) => checkThemeContrast(page, "unsupported-gradient-interpolation")),
    /unresolved .*background: non-sRGB gradient interpolation is not machine-verifiable/,
  );
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
