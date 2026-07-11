import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import { deflateSync } from "node:zlib";
import {
  MIN_VISUAL_SMOKE_CONTRAST_RATIO,
  visualSmokeContrastCheck,
  visualSmokeLayoutChecks,
  visualSmokeViewports,
} from "../scripts/visual-smoke-plan.mjs";

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
      assert.ok(evidence.pngChunkCount >= 3);
      assert.ok(evidence.pngIdatByteSize > 0);
      assert.ok(evidence.pixelSampleCount > 0);
      assert.ok(evidence.sampledDistinctColorCount >= 4);
      assert.ok(evidence.luminanceRange >= 10);
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

  it("rejects structurally valid screenshots that are visually blank", async () => {
    const { withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const screenshotPath = path.join(dir, "blank.png");

    await writeFile(screenshotPath, pngBytes(1440, 1200, { blank: true }));

    try {
      await assert.rejects(
        () => withScreenshotEvidence({
          routeName: "dashboard",
          routeKey: "dashboard",
          theme: "light",
          viewportName: "desktop",
          fileName: "blank.png",
          artifactPath: screenshotPath,
          expectedText: "Production Readiness",
          openFilingTab: false,
          reviewStatus: "required-review",
          layoutChecks: ["browser-console-errors"],
        }),
        /visual smoke screenshot appears visually blank or low-information: blank\.png/,
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
      assert.equal(result.inventoryStateCount, 32);
      assert.equal(result.routeCount, 32);
      assert.equal(result.accountantWorkbenchRouteCount, 7);
      assert.equal(result.screenshotCount, 192);
      assert.equal(result.expectedScreenshotCount, 192);
      assert.deepEqual(result.themes, ["light", "dark"]);
      assert.deepEqual(result.viewports, ["mobile", "tablet", "desktop"]);
      assert.deepEqual(result.viewportDimensions, visualSmokeViewports);
      assert.equal(result.totalBytes, screenshots.reduce((sum, screenshot) => sum + screenshot.byteSize, 0));
      assert.equal(result.layoutChecksPassed, true);
      assert.equal(result.layoutCheckResultCount, screenshots.length * visualSmokeLayoutChecks.length);
      assert.equal(result.themeContrastChecksPassed, true);
      assert.equal(result.contrastCheckResultCount, screenshots.length);
      assert.equal(result.minimumContrastRatio, MIN_VISUAL_SMOKE_CONTRAST_RATIO);
      assert.equal(result.routeCoverage.find((route) => route.routeName === "dashboard")?.screenshotCount, 6);
      assert.equal(result.screenshots.length, 192);
      assert.equal(result.screenshots[0].imageWidth, 390);
      assert.ok(result.screenshots[0].imageHeight >= 844);
      assert.equal(result.screenshots[0].expectedText, "Sign in");
      assert.equal(result.screenshots[0].canonicalUrl, "/login");
      assert.match(result.screenshots[0].semanticContentSha256, /^sha256:[a-f0-9]{64}$/);
      assert.ok(result.screenshots[0].sampledDistinctColorCount >= 4);
      assert.ok(result.screenshots[0].luminanceRange >= 10);
      assert.deepEqual(
        result.screenshots[0].layoutCheckResults.map((item) => `${item.check}:${item.status}`),
        visualSmokeLayoutChecks.map((check) => `${check}:passed`),
      );
      assert.equal(result.screenshots[0].themeContrastResult.check, visualSmokeContrastCheck);
      assert.equal(result.screenshots[0].themeContrastResult.minimumContrastRatio, MIN_VISUAL_SMOKE_CONTRAST_RATIO);
      assert.equal(JSON.parse(await readFile(reportPath, "utf8")).status, "passed");
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifest screenshots without passed theme contrast results", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots.map((screenshot, index) => (
      index === 0
        ? { ...screenshot, themeContrastResult: { ...screenshot.themeContrastResult, minimumContrastRatio: 1.5 } }
        : screenshot
    )));

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke screenshot login-light-mobile\.png themeContrastResult\.minimumContrastRatio must be at least 3/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects evidence that applies only the UI floor to normal text", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots.map((screenshot, index) => (
      index === 0
        ? {
            ...screenshot,
            themeContrastResult: {
              ...screenshot.themeContrastResult,
              minimumNormalTextContrastRatio: 4.49,
            },
          }
        : screenshot
    )));

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /themeContrastResult\.minimumNormalTextContrastRatio must be at least 4\.5/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("allows no UI boundary only for canonical text-only states and rejects it elsewhere", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");
    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    const textOnlyScreenshots = screenshots.map((screenshot) => screenshot.stateId === "state-loading"
      ? {
          ...screenshot,
          themeContrastResult: {
            ...screenshot.themeContrastResult,
            sampledUiComponentCount: 0,
            minimumUiComponentContrastRatio: 0,
          },
        }
      : screenshot);

    try {
      await writeManifest(manifestPath, textOnlyScreenshots);
      const result = await verifyVisualSmokeManifest(manifestPath);
      assert.equal(result.status, "passed");

      const invalid = textOnlyScreenshots.map((screenshot) => screenshot.stateId === "login"
        ? {
            ...screenshot,
            themeContrastResult: {
              ...screenshot.themeContrastResult,
              sampledUiComponentCount: 0,
              minimumUiComponentContrastRatio: 0,
            },
          }
        : screenshot);
      await writeManifest(manifestPath, invalid);
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /login-light-mobile\.png themeContrastResult\.sampledUiComponentCount must prove at least one UI component sample/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifest screenshots without passed layout check results", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots.map((screenshot, index) => (
      index === 0
        ? {
            ...screenshot,
            layoutCheckResults: screenshot.layoutCheckResults.filter((result) => result.check !== "visible-text-overlap"),
          }
        : screenshot
    )));

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke screenshot login-light-mobile\.png is missing passed layout check result visible-text-overlap/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects manifest screenshots whose expected text drifts from the visual smoke plan", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");

    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    await writeManifest(manifestPath, screenshots.map((screenshot, index) => (
      index === 0
        ? { ...screenshot, expectedText: "Wrong route heading" }
        : screenshot
    )));

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke screenshot expected text mismatch for login\/light\/mobile: expected Sign in, found Wrong route heading/,
      );
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
        /visual smoke screenshot hash mismatch: login-light-mobile\.png/,
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
        /visual smoke route audit mismatch: dashboard expected 99 screenshots, found 6/,
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
    await writeManifest(
      manifestPath,
      screenshots,
      undefined,
      [
        "visual-smoke-evidence-report.json",
        "screenshot SHA-256 checksums",
        "screenshot PNG dimensions",
        "screenshot nonblank pixel diversity evidence",
        "per-screenshot automated theme contrast smoke evidence",
      ],
    );

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
    const duplicate = {
      ...screenshots[1],
      stateId: screenshots[0].stateId,
      routeName: screenshots[0].routeName,
      theme: screenshots[0].theme,
      viewportName: screenshots[0].viewportName,
    };
    await writeManifest(manifestPath, [screenshots[0], duplicate, ...screenshots.slice(2)]);

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /visual smoke manifest contains duplicate screenshot coverage: login\/light\/mobile/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects semantically identical intended states in the same theme and viewport", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");
    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    const login = screenshots.find((item) => item.stateId === "login" && item.theme === "light" && item.viewportName === "mobile");
    const password = screenshots.find((item) => item.stateId === "password-change" && item.theme === "light" && item.viewportName === "mobile");
    password.semanticContentSha256 = login.semanticContentSha256;
    await writeManifest(manifestPath, screenshots);

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /semantically identical semantic content captures for light\/mobile: login, password-change/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects canonical URL or observed tab drift", async () => {
    const { verifyVisualSmokeManifest, withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
    const dir = await mkTempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");
    const screenshots = await completeScreenshots(dir, withScreenshotEvidence);
    const filing = screenshots.find((item) => item.stateId === "filing-review" && item.theme === "light" && item.viewportName === "mobile");
    filing.canonicalUrl = filing.canonicalUrl.replace("?tab=filing", "");
    filing.observedUrl = filing.canonicalUrl;
    filing.observedTabState = { kind: "period-tab", id: "import", label: "Import" };
    await writeManifest(manifestPath, screenshots);

    try {
      await assert.rejects(
        () => verifyVisualSmokeManifest(manifestPath),
        /canonical URL mismatch: filing-review-light-mobile\.png|observed tab state mismatch: filing-review-light-mobile\.png/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

async function completeScreenshots(dir, withScreenshotEvidence) {
  const { expectedVisualSmokeArtifacts } = await import("../scripts/visual-smoke-plan.mjs");
  const screenshots = [];
  const basePngs = new Map();

  for (const [index, artifact] of expectedVisualSmokeArtifacts(dir).entries()) {
    const screenshotPath = path.join(dir, artifact.fileName);
    const viewport = visualSmokeViewports.find((item) => item.name === artifact.viewportName);
    const baseKey = `${viewport.width}x${viewport.height}`;
    if (!basePngs.has(baseKey)) basePngs.set(baseKey, pngBytes(viewport.width, viewport.height));
    await writeFile(screenshotPath, pngWithIdentity(basePngs.get(baseKey), `${artifact.stateId}-${artifact.theme}-${artifact.viewportName}-${index}`));
    const concreteUrl = artifact.canonicalUrlTemplate
      .replace("{companyId}", "41")
      .replace("{periodId}", "52");
    const semanticContentSha256 = `sha256:${createHash("sha256").update(`${artifact.stateId}-semantic-content`).digest("hex")}`;
    screenshots.push(await withScreenshotEvidence({
      ...artifact,
      artifactPath: screenshotPath,
      canonicalUrl: concreteUrl,
      observedUrl: concreteUrl,
      observedTabState: artifact.canonicalTabState.kind.endsWith("-tab") ? artifact.canonicalTabState : null,
      semanticContentSha256,
      semanticContentByteSize: 128 + index,
    }));
  }

  return screenshots;
}

async function writeManifest(manifestPath, screenshots, routeAudits, requiredEvidence = [
  "canonical state inventory and exact URL/tab evidence",
  "semantic content SHA-256 distinctness evidence",
  "visual-smoke-evidence-report.json",
  "accountant-workbench-evidence-report.json",
  "screenshot SHA-256 checksums",
  "screenshot PNG dimensions",
  "screenshot nonblank pixel diversity evidence",
  "per-screenshot automated theme contrast smoke evidence",
]) {
  const {
    expectedVisualSmokeManifest,
  } = await import("../scripts/visual-smoke-plan.mjs");

  const expectedManifest = expectedVisualSmokeManifest(path.dirname(manifestPath));

  await writeFile(
    manifestPath,
    `${JSON.stringify({
      ...expectedManifest,
      expectedScreenshotCount: screenshots.length,
      reviewProtocol: { ...expectedManifest.reviewProtocol, requiredEvidence },
      routeAudits: routeAudits ?? expectedManifest.routeAudits,
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

function pngWithIdentity(basePng, identity) {
  const data = Buffer.from(identity, "utf8");
  const chunk = Buffer.alloc(12 + data.length);
  chunk.writeUInt32BE(data.length, 0);
  chunk.write("tEXt", 4, "ascii");
  data.copy(chunk, 8);
  // Pixel evidence ignores ancillary CRC values; identity still changes the retained file hash.
  chunk.writeUInt32BE(0, 8 + data.length);
  return Buffer.concat([basePng.subarray(0, -12), chunk, basePng.subarray(-12)]);
}

function pngBytes(width, height, options = {}) {
  const bytesPerPixel = 3;
  const stride = width * bytesPerPixel;
  const raw = Buffer.alloc(height * (stride + 1));
  for (let y = 0; y < height; y += 1) {
    const rowStart = y * (stride + 1);
    raw[rowStart] = 0;
    for (let x = 0; x < width; x += 1) {
      const offset = rowStart + 1 + x * bytesPerPixel;
      if (options.blank) {
        raw[offset] = 255;
        raw[offset + 1] = 255;
        raw[offset + 2] = 255;
      } else {
        raw[offset] = (x * 17 + y * 3) & 0xff;
        raw[offset + 1] = (x * 5 + y * 11) & 0xff;
        raw[offset + 2] = (x * 13 + y * 7) & 0xff;
      }
    }
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 2;
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", deflateSync(raw)),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

function pngChunk(type, data) {
  const header = Buffer.alloc(8);
  header.writeUInt32BE(data.length, 0);
  header.write(type, 4, "ascii");
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([Buffer.from(type, "ascii"), data])), 0);
  return Buffer.concat([header, data, crc]);
}

function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}
