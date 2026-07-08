import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import { visualSmokeViewports } from "../scripts/visual-smoke-plan.mjs";

describe("visual smoke artifact evidence", () => {
  it("records byte size and sha256 for captured screenshots", async () => {
    const { withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "dashboard-light-desktop.png");
    const screenshotBytes = pngBytes(1440, 1200);

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
      assert.equal(evidence.sha256, `sha256:${createHash("sha256").update(screenshotBytes).digest("hex")}`);
      assert.equal(evidence.imageWidth, 1440);
      assert.equal(evidence.imageHeight, 1200);
      assert.equal(evidence.expectedViewportWidth, 1440);
      assert.equal(evidence.minimumViewportHeight, 1000);
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

  it("rejects screenshots that are not PNG images matching the planned viewport width", async () => {
    const { withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const textPath = path.join(dir, "dashboard-light-desktop.png");
    const wrongWidthPath = path.join(dir, "dashboard-light-desktop-wrong-width.png");

    await writeFile(textPath, "not a real png", "utf8");
    await writeFile(wrongWidthPath, pngBytes(1024, 1200));

    try {
      const artifact = {
        routeName: "dashboard",
        routeKey: "dashboard",
        theme: "light",
        viewportName: "desktop",
        fileName: "dashboard-light-desktop.png",
        artifactPath: textPath,
        expectedText: "Production Readiness",
        openFilingTab: false,
        reviewStatus: "required-review",
        layoutChecks: ["browser-console-errors"],
      };

      await assert.rejects(
        () => withScreenshotEvidence(artifact),
        /visual smoke screenshot is not a PNG with an IHDR header: dashboard-light-desktop\.png/,
      );
      await assert.rejects(
        () => withScreenshotEvidence({
          ...artifact,
          fileName: "dashboard-light-desktop-wrong-width.png",
          artifactPath: wrongWidthPath,
        }),
        /visual smoke screenshot width mismatch: dashboard-light-desktop-wrong-width\.png expected 1440px, found 1024px/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("verifies manifest screenshots against recorded hash and byte size", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");
    const reportPath = path.join(dir, "visual-smoke-evidence-report.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots);

    try {
      const result = await verifyVisualSmokeManifest(manifestPath, {
        checkedAtUtc: "2026-07-08T00:00:00.000Z",
        reportPath,
      });

      assert.equal(result.ok, true);
      assert.equal(result.status, "passed");
      assert.equal(result.manifestPath, manifestPath);
      assert.equal(result.reportPath, reportPath);
      assert.equal(result.evidenceReportFileName, "visual-smoke-evidence-report.json");
      assert.equal(result.routeCount, 7);
      assert.equal(result.screenshotCount, 28);
      assert.equal(result.expectedScreenshotCount, 28);
      assert.deepEqual(result.themes, ["light", "dark"]);
      assert.deepEqual(result.viewports, ["desktop", "mobile"]);
      assert.deepEqual(result.viewportDimensions, visualSmokeViewports);
      assert.equal(result.totalBytes, screenshots.reduce((sum, screenshot) => sum + screenshot.byteSize, 0));
      assert.equal(result.routeCoverage.find((route) => route.routeName === "dashboard")?.screenshotCount, 4);
      assert.equal(result.screenshots.length, 28);
      assert.equal(result.screenshots[0].imageWidth, 1440);
      assert.ok(result.screenshots[0].imageHeight >= 1000);
      assert.equal(JSON.parse(await readFile(reportPath, "utf8")).status, "passed");
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifest screenshots whose recorded hash no longer matches the file", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots.map((screenshot, index) => (
      index === 0
        ? { ...screenshot, sha256: "sha256:0000000000000000000000000000000000000000000000000000000000000000" }
        : screenshot
    )));

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
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    const { expectedVisualSmokeRouteAudits } = await import("../scripts/visual-smoke-plan.mjs");
    await writeManifest(
      manifestPath,
      screenshots,
      expectedVisualSmokeRouteAudits().map((audit) => audit.routeName === "dashboard" ? { ...audit, screenshotCount: 99 } : audit),
    );

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke route audit mismatch: dashboard expected 99 screenshots, found 4/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifests that omit accountant workbench evidence from the review protocol", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots, undefined, ["visual-smoke-evidence-report.json", "screenshot SHA-256 checksums", "screenshot PNG dimensions"]);

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke manifest review protocol must require accountant-workbench-evidence-report\.json/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects duplicate route theme viewport coverage", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    const duplicate = { ...screenshots[1], routeName: screenshots[0].routeName, theme: screenshots[0].theme, viewportName: screenshots[0].viewportName };
    await writeManifest(manifestPath, [screenshots[0], duplicate, ...screenshots.slice(2)]);

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke manifest contains duplicate screenshot coverage: dashboard\/light\/desktop/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

async function completeScreenshots(dir, withScreenshotEvidence) {
  const { expectedVisualSmokeArtifacts } = await import("../scripts/visual-smoke-plan.mjs");
  const screenshots = [];

  for (const artifact of expectedVisualSmokeArtifacts(dir)) {
    const screenshotPath = path.join(dir, artifact.fileName);
    const viewport = visualSmokeViewports.find((item) => item.name === artifact.viewportName);
    await writeFile(screenshotPath, pngBytes(viewport.width, viewport.height + 200));
    screenshots.push(await withScreenshotEvidence({ ...artifact, artifactPath: screenshotPath }));
  }

  return screenshots;
}

async function writeManifest(manifestPath, screenshots, routeAudits, requiredEvidence = ["visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "screenshot SHA-256 checksums", "screenshot PNG dimensions"]) {
  const {
    expectedVisualSmokeRouteAudits,
    visualSmokeLayoutChecks,
    visualSmokeReviewChecks,
  } = await import("../scripts/visual-smoke-plan.mjs");

  await writeFile(
    manifestPath,
    `${JSON.stringify({
      artifactName: "visual-smoke-screenshots",
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: screenshots.length,
      layoutChecks: visualSmokeLayoutChecks,
      reviewChecks: visualSmokeReviewChecks,
      reviewProtocol: { requiredEvidence },
      routeAudits: routeAudits ?? expectedVisualSmokeRouteAudits(),
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

function pngBytes(width, height) {
  const bytes = Buffer.alloc(33);
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]).copy(bytes, 0);
  bytes.writeUInt32BE(13, 8);
  bytes.write("IHDR", 12, "ascii");
  bytes.writeUInt32BE(width, 16);
  bytes.writeUInt32BE(height, 20);
  bytes[24] = 8;
  bytes[25] = 2;
  bytes[26] = 0;
  bytes[27] = 0;
  bytes[28] = 0;
  return bytes;
}
