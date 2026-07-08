import assert from "node:assert/strict";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
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
      assert.equal(result.totalBytes, screenshots.reduce((sum, screenshot) => sum + screenshot.byteSize, 0));
      assert.equal(result.routeCoverage.find((route) => route.routeName === "dashboard")?.screenshotCount, 4);
      assert.equal(result.screenshots.length, 28);
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
    await writeManifest(manifestPath, screenshots, undefined, ["visual-smoke-evidence-report.json", "screenshot SHA-256 checksums"]);

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

async function completeScreenshots(dir, withScreenshotEvidence) {
  const { expectedVisualSmokeArtifacts } = await import("../scripts/visual-smoke-plan.mjs");
  const screenshots = [];

  for (const artifact of expectedVisualSmokeArtifacts(dir)) {
    const screenshotPath = path.join(dir, artifact.fileName);
    await writeFile(screenshotPath, `visual evidence bytes ${artifact.fileName}`, "utf8");
    screenshots.push(await withScreenshotEvidence({ ...artifact, artifactPath: screenshotPath }));
  }

  return screenshots;
}

async function writeManifest(manifestPath, screenshots, routeAudits, requiredEvidence = ["visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "screenshot SHA-256 checksums"]) {
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
