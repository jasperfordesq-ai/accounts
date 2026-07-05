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

  it("verifies manifest screenshots against recorded hash and byte size", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "dashboard-light-desktop.png");
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    await writeFile(screenshotPath, "visual evidence bytes", "utf8");
    const screenshot = await withScreenshotEvidence(baseScreenshot(screenshotPath));
    await writeManifest(manifestPath, [screenshot], { routeName: "dashboard", screenshotCount: 1 });

    try {
      const result = await verifyVisualSmokeManifest(manifestPath);

      assert.deepEqual(result, {
        ok: true,
        manifestPath,
        screenshotCount: 1,
        totalBytes: screenshot.byteSize,
      });
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifest screenshots whose recorded hash no longer matches the file", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "dashboard-light-desktop.png");
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    await writeFile(screenshotPath, "visual evidence bytes", "utf8");
    const screenshot = await withScreenshotEvidence(baseScreenshot(screenshotPath));
    await writeManifest(manifestPath, [{ ...screenshot, sha256: "sha256:0000000000000000000000000000000000000000000000000000000000000000" }], {
      routeName: "dashboard",
      screenshotCount: 1,
    });

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke screenshot hash mismatch: dashboard-light-desktop\.png/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifest route audits that disagree with captured screenshots", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "dashboard-light-desktop.png");
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    await writeFile(screenshotPath, "visual evidence bytes", "utf8");
    const screenshot = await withScreenshotEvidence(baseScreenshot(screenshotPath));
    await writeManifest(manifestPath, [screenshot], { routeName: "dashboard", screenshotCount: 4 });

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke route audit mismatch: dashboard expected 4 screenshots, found 1/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

function baseScreenshot(screenshotPath) {
  return {
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
  };
}

async function writeManifest(manifestPath, screenshots, routeAudit) {
  await writeFile(
    manifestPath,
    `${JSON.stringify({
      artifactName: "visual-smoke-screenshots",
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: screenshots.length,
      layoutChecks: ["browser-console-errors"],
      reviewChecks: ["accountant-workflow-hierarchy"],
      reviewProtocol: { requiredEvidence: ["screenshot SHA-256 checksums"] },
      routeAudits: [
        {
          routeName: routeAudit.routeName,
          routeKey: routeAudit.routeName,
          label: "Dashboard",
          workflowStages: ["Setup"],
          screenshotCount: routeAudit.screenshotCount,
          reviewStatus: "required-review",
          reviewChecks: ["accountant-workflow-hierarchy"],
        },
      ],
      screenshots,
    }, null, 2)}\n`,
    "utf8",
  );
}

async function mkTempDir() {
  const dir = path.join(os.tmpdir(), `visual-smoke-artifacts-${Date.now()}-${Math.random().toString(16).slice(2)}`);
  await mkdir(dir, { recursive: true });
  return dir;
}
