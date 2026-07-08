import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import {
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeScreenshotCount,
  visualSmokeLayoutChecks,
  visualSmokeReviewChecks,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";

const VISUAL_SMOKE_EVIDENCE_REPORT_FILE = "visual-smoke-evidence-report.json";

export async function withScreenshotEvidence(artifact) {
  const bytes = await readFile(artifact.artifactPath);
  if (bytes.length === 0) {
    throw new Error(`visual smoke screenshot is empty: ${artifact.fileName}`);
  }

  return {
    ...artifact,
    byteSize: bytes.length,
    sha256: `sha256:${createHash("sha256").update(bytes).digest("hex")}`,
  };
}

export async function verifyVisualSmokeManifest(manifestPath, options = {}) {
  const resolvedManifestPath = path.resolve(manifestPath);
  const checkedAtUtc = options.checkedAtUtc ?? new Date().toISOString();
  const manifest = JSON.parse(await readFile(resolvedManifestPath, "utf8"));
  const screenshots = Array.isArray(manifest.screenshots) ? manifest.screenshots : [];
  const failures = [];

  if (manifest.expectedScreenshotCount !== screenshots.length) {
    failures.push(`visual smoke manifest screenshot count mismatch: expected ${manifest.expectedScreenshotCount}, found ${screenshots.length}`);
  }

  if (manifest.expectedScreenshotCount !== expectedVisualSmokeScreenshotCount()) {
    failures.push(
      `visual smoke manifest expected screenshot count must be ${expectedVisualSmokeScreenshotCount()}, found ${manifest.expectedScreenshotCount}`,
    );
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("screenshot SHA-256 checksums")) {
    failures.push("visual smoke manifest review protocol must require screenshot SHA-256 checksums");
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes(VISUAL_SMOKE_EVIDENCE_REPORT_FILE)) {
    failures.push(`visual smoke manifest review protocol must require ${VISUAL_SMOKE_EVIDENCE_REPORT_FILE}`);
  }

  let totalBytes = 0;
  const screenshotSummaries = [];
  const duplicateKeys = new Set();
  const seenKeys = new Set();
  for (const screenshot of screenshots) {
    const evidence = await withScreenshotEvidence({
      ...screenshot,
      artifactPath: resolveArtifactPath(screenshot.artifactPath),
    });
    const key = screenshotKey(screenshot);

    if (seenKeys.has(key)) {
      duplicateKeys.add(key);
    }
    seenKeys.add(key);

    if (screenshot.byteSize !== evidence.byteSize) {
      failures.push(`visual smoke screenshot byte size mismatch: ${screenshot.fileName}`);
    }

    if (screenshot.sha256 !== evidence.sha256) {
      failures.push(`visual smoke screenshot hash mismatch: ${screenshot.fileName}`);
    }

    totalBytes += evidence.byteSize;
    screenshotSummaries.push({
      routeName: screenshot.routeName,
      routeKey: screenshot.routeKey,
      theme: screenshot.theme,
      viewportName: screenshot.viewportName,
      fileName: screenshot.fileName,
      byteSize: evidence.byteSize,
      sha256: evidence.sha256,
      reviewStatus: screenshot.reviewStatus,
    });
  }

  for (const key of duplicateKeys) {
    failures.push(`visual smoke manifest contains duplicate screenshot coverage: ${key}`);
  }

  for (const expected of expectedVisualSmokeArtifacts()) {
    const actual = screenshots.find((screenshot) => screenshotKey(screenshot) === screenshotKey(expected));
    if (!actual) {
      failures.push(`visual smoke manifest is missing screenshot coverage: ${screenshotKey(expected)}`);
      continue;
    }

    if (actual.routeKey !== expected.routeKey) {
      failures.push(`visual smoke screenshot route key mismatch: ${actual.fileName}`);
    }

    if (actual.fileName !== expected.fileName) {
      failures.push(`visual smoke screenshot file name mismatch for ${screenshotKey(expected)}: expected ${expected.fileName}, found ${actual.fileName}`);
    }

    if (actual.reviewStatus !== "required-review") {
      failures.push(`visual smoke screenshot must remain pending human review: ${actual.fileName}`);
    }

    for (const layoutCheck of visualSmokeLayoutChecks) {
      if (!actual.layoutChecks?.includes(layoutCheck)) {
        failures.push(`visual smoke screenshot ${actual.fileName} is missing layout check ${layoutCheck}`);
      }
    }
  }

  for (const audit of manifest.routeAudits ?? []) {
    const actualCount = screenshots.filter((screenshot) => screenshot.routeName === audit.routeName).length;
    if (audit.screenshotCount !== actualCount) {
      failures.push(`visual smoke route audit mismatch: ${audit.routeName} expected ${audit.screenshotCount} screenshots, found ${actualCount}`);
    }
  }

  for (const expectedAudit of expectedVisualSmokeRouteAudits()) {
    const actualAudit = (manifest.routeAudits ?? []).find((audit) => audit.routeName === expectedAudit.routeName);
    if (!actualAudit) {
      failures.push(`visual smoke manifest is missing route audit: ${expectedAudit.routeName}`);
      continue;
    }

    if (actualAudit.screenshotCount !== expectedAudit.screenshotCount) {
      failures.push(
        `visual smoke route audit expected ${expectedAudit.screenshotCount} screenshots for ${expectedAudit.routeName}, found ${actualAudit.screenshotCount}`,
      );
    }

    if (actualAudit.reviewStatus !== "required-review") {
      failures.push(`visual smoke route audit must remain pending human review: ${expectedAudit.routeName}`);
    }

    for (const reviewCheck of visualSmokeReviewChecks) {
      if (!actualAudit.reviewChecks?.includes(reviewCheck)) {
        failures.push(`visual smoke route audit ${expectedAudit.routeName} is missing review check ${reviewCheck}`);
      }
    }
  }

  if (failures.length > 0) {
    throw new Error(failures.join("\n"));
  }

  const report = {
    ok: true,
    status: "passed",
    checkedAtUtc,
    manifestPath: resolvedManifestPath,
    evidenceReportFileName: VISUAL_SMOKE_EVIDENCE_REPORT_FILE,
    routeCount: expectedVisualSmokeRouteAudits().length,
    screenshotCount: screenshots.length,
    expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
    themes: visualSmokeThemes,
    viewports: visualSmokeViewports.map((viewport) => viewport.name),
    totalBytes,
    routeCoverage: expectedVisualSmokeRouteAudits().map((audit) => ({
      routeName: audit.routeName,
      routeKey: audit.routeKey,
      screenshotCount: screenshots.filter((screenshot) => screenshot.routeName === audit.routeName).length,
      requiredReviewChecks: audit.reviewChecks,
      reviewStatus: audit.reviewStatus,
    })),
    screenshots: screenshotSummaries,
  };

  if (options.reportPath) {
    const resolvedReportPath = path.resolve(options.reportPath);
    await mkdir(path.dirname(resolvedReportPath), { recursive: true });
    await writeFile(resolvedReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
    report.reportPath = resolvedReportPath;
  }

  return report;
}

function resolveArtifactPath(artifactPath) {
  return path.isAbsolute(artifactPath) ? artifactPath : path.resolve(artifactPath);
}

function screenshotKey(screenshot) {
  return `${screenshot.routeName}/${screenshot.theme}/${screenshot.viewportName}`;
}
