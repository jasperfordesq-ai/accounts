import assert from "node:assert/strict";
import { mkdir, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";

describe("visual smoke artifact evidence", () => {
  it("records byte size and sha256 for captured screenshots", async () => {
    const { withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "dashboard-light-desktop.png");
    const screenshotBytes = Buffer.from("not a real png, but deterministic evidence bytes", "utf8");

    await writeFile(screenshotPath, screenshotBytes);

    try {
      const evidence = await withScreenshotEvidence({
        routeName: "dashboard",
        routeKey: "dashboard",
        theme: "light",
        viewportName: "desktop",
        fileName: "dashboard-light-desktop.png",
        artifactPath: screenshotPath,
        expectedText: "Production Readiness",
        openFilingTab: false,
        reviewStatus: "required-review",
        layoutChecks: ["browser-console-errors"],
      });

      assert.equal(evidence.byteSize, screenshotBytes.length);
      assert.equal(
        evidence.sha256,
        "sha256:7285fca36498abf5103a4dcf4dd97ee26d341f8c89c6eca86ae99179cb64e471",
      );
      assert.equal(evidence.fileName, "dashboard-light-desktop.png");
      assert.equal(evidence.artifactPath, screenshotPath);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects empty captured screenshots", async () => {
    const { withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "empty.png");

    await writeFile(screenshotPath, "");

    try {
      await assert.rejects(
        () => withScreenshotEvidence({
          routeName: "dashboard",
          routeKey: "dashboard",
          theme: "light",
          viewportName: "desktop",
          fileName: "empty.png",
          artifactPath: screenshotPath,
          expectedText: "Production Readiness",
          openFilingTab: false,
          reviewStatus: "required-review",
          layoutChecks: ["browser-console-errors"],
        }),
        /visual smoke screenshot is empty: empty\.png/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

async function mkTempDir() {
  const dir = path.join(os.tmpdir(), `visual-smoke-artifacts-${Date.now()}-${Math.random().toString(16).slice(2)}`);
  await mkdir(dir, { recursive: true });
  return dir;
}
