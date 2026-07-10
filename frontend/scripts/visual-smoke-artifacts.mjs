import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { inflateSync } from "node:zlib";
import {
  accountantWorkbenchRoutes,
  canonicalStateUrlMatches,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeScreenshotCount,
  MIN_LARGE_TEXT_CONTRAST_RATIO,
  MIN_NORMAL_TEXT_CONTRAST_RATIO,
  MIN_UI_COMPONENT_CONTRAST_RATIO,
  MIN_VISUAL_SMOKE_CONTRAST_RATIO,
  REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES,
  REQUIRED_VISUAL_SMOKE_UI_STATES,
  VISUAL_SMOKE_INVENTORY_VERSION,
  visualSmokeContrastCheck,
  visualSmokeLayoutChecks,
  visualSmokeReviewChecks,
  visualSmokeStateInventory,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";

const VISUAL_SMOKE_EVIDENCE_REPORT_FILE = "visual-smoke-evidence-report.json";
const MIN_VISUAL_SMOKE_DISTINCT_COLORS = 4;
const MIN_VISUAL_SMOKE_LUMINANCE_RANGE = 10;
const pngPixelEvidenceCache = new Map();

export async function withScreenshotEvidence(artifact) {
  const bytes = await readFile(artifact.artifactPath);
  if (bytes.length === 0) {
    throw new Error(`visual smoke screenshot is empty: ${artifact.fileName}`);
  }

  const png = readPngEvidence(bytes, artifact.fileName);
  const viewport = visualSmokeViewports.find((item) => item.name === artifact.viewportName);
  if (!viewport) {
    throw new Error(`visual smoke screenshot uses unknown viewport: ${artifact.fileName}`);
  }

  if (png.width !== viewport.width) {
    throw new Error(
      `visual smoke screenshot width mismatch: ${artifact.fileName} expected ${viewport.width}px, found ${png.width}px`,
    );
  }

  if (png.height < viewport.height) {
    throw new Error(
      `visual smoke screenshot height is smaller than viewport: ${artifact.fileName} expected at least ${viewport.height}px, found ${png.height}px`,
    );
  }

  return {
    ...artifact,
    byteSize: bytes.length,
    imageWidth: png.width,
    imageHeight: png.height,
    expectedViewportWidth: viewport.width,
    minimumViewportHeight: viewport.height,
    pngChunkCount: png.chunkCount,
    pngIdatByteSize: png.idatByteSize,
    pixelSampleCount: png.pixelSampleCount,
    sampledDistinctColorCount: png.sampledDistinctColorCount,
    luminanceRange: png.luminanceRange,
    sha256: `sha256:${createHash("sha256").update(bytes).digest("hex")}`,
  };
}

export async function verifyVisualSmokeManifest(manifestPath, options = {}) {
  const resolvedManifestPath = path.resolve(manifestPath);
  const checkedAtUtc = options.checkedAtUtc ?? new Date().toISOString();
  const manifest = JSON.parse(await readFile(resolvedManifestPath, "utf8"));
  const screenshots = Array.isArray(manifest.screenshots) ? manifest.screenshots : [];
  const failures = [];

  if (manifest.inventoryVersion !== VISUAL_SMOKE_INVENTORY_VERSION) {
    failures.push(`visual smoke manifest inventoryVersion must be ${VISUAL_SMOKE_INVENTORY_VERSION}`);
  }

  if (manifest.inventoryStateCount !== visualSmokeStateInventory.length) {
    failures.push(
      `visual smoke manifest inventoryStateCount must be ${visualSmokeStateInventory.length}, found ${manifest.inventoryStateCount}`,
    );
  }

  if (!sameJson(manifest.requiredMaterialRoutes, REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES)) {
    failures.push("visual smoke manifest requiredMaterialRoutes must match the canonical material-route inventory");
  }

  if (!sameJson(manifest.requiredUiStates, REQUIRED_VISUAL_SMOKE_UI_STATES)) {
    failures.push("visual smoke manifest requiredUiStates must match the canonical material-state inventory");
  }

  if (!sameJson(manifest.themes, visualSmokeThemes)) {
    failures.push("visual smoke manifest themes must be exactly light and dark");
  }

  if (!sameJson(manifest.viewportDimensions, visualSmokeViewports)) {
    failures.push("visual smoke manifest viewportDimensions must be exactly 390x844, 768x1024 and 1440x1000");
  }

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

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("screenshot PNG dimensions")) {
    failures.push("visual smoke manifest review protocol must require screenshot PNG dimensions");
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("screenshot nonblank pixel diversity evidence")) {
    failures.push("visual smoke manifest review protocol must require screenshot nonblank pixel diversity evidence");
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("per-screenshot automated theme contrast smoke evidence")) {
    failures.push("visual smoke manifest review protocol must require per-screenshot automated theme contrast smoke evidence");
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("canonical state inventory and exact URL/tab evidence")) {
    failures.push("visual smoke manifest review protocol must require canonical state inventory and exact URL/tab evidence");
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("semantic content SHA-256 distinctness evidence")) {
    failures.push("visual smoke manifest review protocol must require semantic content SHA-256 distinctness evidence");
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes(VISUAL_SMOKE_EVIDENCE_REPORT_FILE)) {
    failures.push(`visual smoke manifest review protocol must require ${VISUAL_SMOKE_EVIDENCE_REPORT_FILE}`);
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("accountant-workbench-evidence-report.json")) {
    failures.push("visual smoke manifest review protocol must require accountant-workbench-evidence-report.json");
  }

  let totalBytes = 0;
  const screenshotSummaries = [];
  const duplicateKeys = new Set();
  const seenKeys = new Set();
  const semanticContentGroups = new Map();
  const screenshotHashGroups = new Map();
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

    const semanticHash = String(screenshot.semanticContentSha256 ?? "");
    if (!/^sha256:[a-f0-9]{64}$/.test(semanticHash)) {
      failures.push(`visual smoke screenshot ${screenshot.fileName} must record a canonical semanticContentSha256`);
    }
    if (Number(screenshot.semanticContentByteSize) <= 0) {
      failures.push(`visual smoke screenshot ${screenshot.fileName} semanticContentByteSize must be greater than zero`);
    }

    addCaptureIdentity(
      semanticContentGroups,
      `${screenshot.theme}/${screenshot.viewportName}/${semanticHash}`,
      screenshot,
    );
    addCaptureIdentity(
      screenshotHashGroups,
      `${screenshot.theme}/${screenshot.viewportName}/${evidence.sha256}`,
      screenshot,
    );

    if (screenshot.byteSize !== evidence.byteSize) {
      failures.push(`visual smoke screenshot byte size mismatch: ${screenshot.fileName}`);
    }

    if (screenshot.sha256 !== evidence.sha256) {
      failures.push(`visual smoke screenshot hash mismatch: ${screenshot.fileName}`);
    }

    if (screenshot.imageWidth !== undefined && screenshot.imageWidth !== evidence.imageWidth) {
      failures.push(`visual smoke screenshot recorded width mismatch: ${screenshot.fileName}`);
    }

    if (screenshot.imageHeight !== undefined && screenshot.imageHeight !== evidence.imageHeight) {
      failures.push(`visual smoke screenshot recorded height mismatch: ${screenshot.fileName}`);
    }

    totalBytes += evidence.byteSize;
    screenshotSummaries.push({
      stateId: screenshot.stateId,
      routeName: screenshot.routeName,
      routeKey: screenshot.routeKey,
      materialRoute: screenshot.materialRoute ?? null,
      uiState: screenshot.uiState,
      authMode: screenshot.authMode,
      theme: screenshot.theme,
      viewportName: screenshot.viewportName,
      fileName: screenshot.fileName,
      expectedText: screenshot.expectedText,
      expectedStateText: screenshot.expectedStateText,
      canonicalUrlTemplate: screenshot.canonicalUrlTemplate,
      canonicalUrl: screenshot.canonicalUrl,
      observedUrl: screenshot.observedUrl,
      canonicalQuery: screenshot.canonicalQuery,
      canonicalTabState: screenshot.canonicalTabState,
      observedTabState: screenshot.observedTabState ?? null,
      semanticContentSha256: screenshot.semanticContentSha256,
      semanticContentByteSize: screenshot.semanticContentByteSize,
      byteSize: evidence.byteSize,
      imageWidth: evidence.imageWidth,
      imageHeight: evidence.imageHeight,
      expectedViewportWidth: evidence.expectedViewportWidth,
      minimumViewportHeight: evidence.minimumViewportHeight,
      pngChunkCount: evidence.pngChunkCount,
      pngIdatByteSize: evidence.pngIdatByteSize,
      pixelSampleCount: evidence.pixelSampleCount,
      sampledDistinctColorCount: evidence.sampledDistinctColorCount,
      luminanceRange: evidence.luminanceRange,
      sha256: evidence.sha256,
      reviewStatus: screenshot.reviewStatus,
      layoutCheckResults: Array.isArray(screenshot.layoutCheckResults) ? screenshot.layoutCheckResults : [],
      themeContrastResult: screenshot.themeContrastResult ?? null,
    });
  }

  for (const key of duplicateKeys) {
    failures.push(`visual smoke manifest contains duplicate screenshot coverage: ${key}`);
  }

  reportSemanticallyIdenticalCaptures(semanticContentGroups, failures, "semantic content");
  reportSemanticallyIdenticalCaptures(screenshotHashGroups, failures, "PNG image");

  for (const expected of expectedVisualSmokeArtifacts()) {
    const actual = screenshots.find((screenshot) => screenshotKey(screenshot) === screenshotKey(expected));
    if (!actual) {
      failures.push(`visual smoke manifest is missing screenshot coverage: ${screenshotKey(expected)}`);
      continue;
    }

    if (actual.routeKey !== expected.routeKey) {
      failures.push(`visual smoke screenshot route key mismatch: ${actual.fileName}`);
    }

    if (actual.stateId !== expected.stateId) {
      failures.push(`visual smoke screenshot state id mismatch: ${actual.fileName}`);
    }

    if ((actual.materialRoute ?? null) !== expected.materialRoute) {
      failures.push(`visual smoke screenshot material route mismatch: ${actual.fileName}`);
    }

    if (actual.uiState !== expected.uiState) {
      failures.push(`visual smoke screenshot UI state mismatch: ${actual.fileName}`);
    }

    if (actual.authMode !== expected.authMode) {
      failures.push(`visual smoke screenshot auth mode mismatch: ${actual.fileName}`);
    }

    if (actual.fileName !== expected.fileName) {
      failures.push(`visual smoke screenshot file name mismatch for ${screenshotKey(expected)}: expected ${expected.fileName}, found ${actual.fileName}`);
    }

    if (actual.expectedText !== expected.expectedText) {
      failures.push(
        `visual smoke screenshot expected text mismatch for ${screenshotKey(expected)}: expected ${expected.expectedText}, found ${actual.expectedText ?? "(missing)"}`,
      );
    }


    if (actual.expectedStateText !== expected.expectedStateText) {
      failures.push(
        `visual smoke screenshot expected state text mismatch for ${screenshotKey(expected)}: expected ${expected.expectedStateText}, found ${actual.expectedStateText ?? "(missing)"}`,
      );
    }

    if (actual.canonicalUrlTemplate !== expected.canonicalUrlTemplate) {
      failures.push(`visual smoke screenshot canonical URL template mismatch: ${actual.fileName}`);
    }

    if (!canonicalStateUrlMatches(actual.canonicalUrl, visualSmokeStateInventory.find((state) => state.id === expected.stateId))) {
      failures.push(`visual smoke screenshot canonical URL mismatch: ${actual.fileName}`);
    }

    if (actual.observedUrl !== actual.canonicalUrl) {
      failures.push(`visual smoke screenshot observed URL must match its canonical URL: ${actual.fileName}`);
    }

    if (!sameJson(actual.canonicalQuery, expected.canonicalQuery)) {
      failures.push(`visual smoke screenshot canonical query mismatch: ${actual.fileName}`);
    }

    if (!sameJson(actual.canonicalTabState, expected.canonicalTabState)) {
      failures.push(`visual smoke screenshot canonical tab state mismatch: ${actual.fileName}`);
    }

    if (expected.canonicalTabState?.kind?.endsWith("-tab") && !sameJson(actual.observedTabState, expected.canonicalTabState)) {
      failures.push(`visual smoke screenshot observed tab state mismatch: ${actual.fileName}`);
    }

    if (actual.reviewStatus !== "required-review") {
      failures.push(`visual smoke screenshot must remain pending human review: ${actual.fileName}`);
    }

    for (const layoutCheck of visualSmokeLayoutChecks) {
      if (!actual.layoutChecks?.includes(layoutCheck)) {
        failures.push(`visual smoke screenshot ${actual.fileName} is missing layout check ${layoutCheck}`);
      }

      const layoutResult = Array.isArray(actual.layoutCheckResults)
        ? actual.layoutCheckResults.find((result) => result?.check === layoutCheck)
        : undefined;
      if (!layoutResult) {
        failures.push(`visual smoke screenshot ${actual.fileName} is missing passed layout check result ${layoutCheck}`);
      } else if (layoutResult.status !== "passed") {
        failures.push(`visual smoke screenshot ${actual.fileName} layout check ${layoutCheck} must have status passed.`);
      }
    }

    validateThemeContrastResult(actual.themeContrastResult, actual.fileName, failures);
  }

  for (const audit of manifest.routeAudits ?? []) {
    const actualCount = screenshots.filter((screenshot) => screenshot.routeName === audit.routeName).length;
    if (audit.screenshotCount !== actualCount) {
      failures.push(`visual smoke route audit mismatch: ${audit.routeName} expected ${audit.screenshotCount} screenshots, found ${actualCount}`);
    }
  }

  for (const expectedAudit of expectedVisualSmokeRouteAudits()) {
    const actualAudit = (manifest.routeAudits ?? []).find((audit) => audit.stateId === expectedAudit.stateId);
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


    for (const [field, expectedValue] of [
      ["routeName", expectedAudit.routeName],
      ["routeKey", expectedAudit.routeKey],
      ["materialRoute", expectedAudit.materialRoute],
      ["uiState", expectedAudit.uiState],
      ["canonicalUrlTemplate", expectedAudit.canonicalUrlTemplate],
      ["expectedText", expectedAudit.expectedText],
      ["expectedStateText", expectedAudit.expectedStateText],
    ]) {
      if ((actualAudit[field] ?? null) !== (expectedValue ?? null)) {
        failures.push(`visual smoke state audit ${expectedAudit.stateId} ${field} mismatch`);
      }
    }

    if (!sameJson(actualAudit.canonicalTabState, expectedAudit.canonicalTabState)) {
      failures.push(`visual smoke state audit ${expectedAudit.stateId} canonicalTabState mismatch`);
    }

    for (const reviewCheck of visualSmokeReviewChecks) {
      if (!actualAudit.reviewChecks?.includes(reviewCheck)) {
        failures.push(`visual smoke route audit ${expectedAudit.routeName} is missing review check ${reviewCheck}`);
      }
    }
  }

  if (!sameJson(manifest.stateInventory, expectedVisualSmokeRouteAudits())) {
    failures.push("visual smoke manifest stateInventory must exactly match the canonical inventory-derived audit rows");
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
    inventoryVersion: VISUAL_SMOKE_INVENTORY_VERSION,
    inventoryStateCount: visualSmokeStateInventory.length,
    routeCount: expectedVisualSmokeRouteAudits().length,
    accountantWorkbenchRouteCount: accountantWorkbenchRoutes.length,
    screenshotCount: screenshots.length,
    expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
    themes: visualSmokeThemes,
    viewports: visualSmokeViewports.map((viewport) => viewport.name),
    viewportDimensions: visualSmokeViewports,
    requiredMaterialRoutes: REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES,
    requiredUiStates: REQUIRED_VISUAL_SMOKE_UI_STATES,
    totalBytes,
    layoutChecksPassed: true,
    layoutCheckResultCount: screenshotSummaries.reduce(
      (total, screenshot) => total + screenshot.layoutCheckResults.length,
      0,
    ),
    themeContrastChecksPassed: true,
    contrastCheckResultCount: screenshotSummaries.filter((screenshot) => screenshot.themeContrastResult?.check === visualSmokeContrastCheck).length,
    minimumContrastRatio: Math.min(
      ...screenshotSummaries.map((screenshot) => Number(screenshot.themeContrastResult?.minimumContrastRatio ?? 0)),
    ),
    semanticDistinctnessPassed: true,
    semanticContentHashCount: new Set(screenshotSummaries.map((screenshot) => screenshot.semanticContentSha256)).size,
    routeCoverage: expectedVisualSmokeRouteAudits().map((audit) => ({
      stateId: audit.stateId,
      routeName: audit.routeName,
      routeKey: audit.routeKey,
      materialRoute: audit.materialRoute,
      uiState: audit.uiState,
      canonicalUrlTemplate: audit.canonicalUrlTemplate,
      canonicalTabState: audit.canonicalTabState,
      expectedText: audit.expectedText,
      expectedStateText: audit.expectedStateText,
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

function validateThemeContrastResult(result, fileName, failures) {
  if (!result) {
    failures.push(`visual smoke screenshot ${fileName} is missing automated theme contrast result.`);
    return;
  }

  if (result.check !== visualSmokeContrastCheck) {
    failures.push(`visual smoke screenshot ${fileName} themeContrastResult.check must be ${visualSmokeContrastCheck}.`);
  }

  if (result.status !== "passed") {
    failures.push(`visual smoke screenshot ${fileName} themeContrastResult.status must be passed.`);
  }

  if (Number(result.sampledTextCount) <= 0) {
    failures.push(`visual smoke screenshot ${fileName} themeContrastResult.sampledTextCount must be greater than zero.`);
  }

  if (Number(result.failingTextCount) !== 0) {
    failures.push(`visual smoke screenshot ${fileName} themeContrastResult.failingTextCount must be zero.`);
  }

  if (Number(result.failingUiComponentCount) !== 0) {
    failures.push(`visual smoke screenshot ${fileName} themeContrastResult.failingUiComponentCount must be zero.`);
  }

  if (Number(result.sampledNormalTextCount) <= 0) {
    failures.push(`visual smoke screenshot ${fileName} themeContrastResult.sampledNormalTextCount must be greater than zero.`);
  }

  for (const [field, label] of [
    ["sampledInteractiveTextCount", "interactive text"],
    ["sampledUiComponentCount", "UI component"],
  ]) {
    if (Number(result[field]) <= 0) {
      failures.push(`visual smoke screenshot ${fileName} themeContrastResult.${field} must prove at least one ${label} sample.`);
    }
  }

  if (Number(result.requiredMinimumContrastRatio) !== MIN_VISUAL_SMOKE_CONTRAST_RATIO) {
    failures.push(
      `visual smoke screenshot ${fileName} themeContrastResult.requiredMinimumContrastRatio must be ${MIN_VISUAL_SMOKE_CONTRAST_RATIO}.`,
    );
  }

  for (const [field, expected] of [
    ["requiredNormalTextContrastRatio", MIN_NORMAL_TEXT_CONTRAST_RATIO],
    ["requiredLargeTextContrastRatio", MIN_LARGE_TEXT_CONTRAST_RATIO],
    ["requiredUiComponentContrastRatio", MIN_UI_COMPONENT_CONTRAST_RATIO],
  ]) {
    if (Number(result[field]) !== expected) {
      failures.push(`visual smoke screenshot ${fileName} themeContrastResult.${field} must be ${expected}.`);
    }
  }

  for (const [field, expected] of [
    ["minimumNormalTextContrastRatio", MIN_NORMAL_TEXT_CONTRAST_RATIO],
    ["minimumLargeTextContrastRatio", MIN_LARGE_TEXT_CONTRAST_RATIO],
    ["minimumUiComponentContrastRatio", MIN_UI_COMPONENT_CONTRAST_RATIO],
  ]) {
    if (Number(result[field]) < expected) {
      failures.push(`visual smoke screenshot ${fileName} themeContrastResult.${field} must be at least ${expected}.`);
    }
  }

  if (Number(result.minimumContrastRatio) < MIN_VISUAL_SMOKE_CONTRAST_RATIO) {
    failures.push(
      `visual smoke screenshot ${fileName} themeContrastResult.minimumContrastRatio must be at least ${MIN_VISUAL_SMOKE_CONTRAST_RATIO}.`,
    );
  }
}

function resolveArtifactPath(artifactPath) {
  return path.isAbsolute(artifactPath) ? artifactPath : path.resolve(artifactPath);
}

function screenshotKey(screenshot) {
  return `${screenshot.stateId ?? screenshot.routeName}/${screenshot.theme}/${screenshot.viewportName}`;
}

function sameJson(actual, expected) {
  return JSON.stringify(actual) === JSON.stringify(expected);
}

function addCaptureIdentity(groups, key, screenshot) {
  const existing = groups.get(key) ?? [];
  existing.push({
    stateId: screenshot.stateId ?? screenshot.routeName,
    fileName: screenshot.fileName,
    theme: screenshot.theme,
    viewportName: screenshot.viewportName,
  });
  groups.set(key, existing);
}

function reportSemanticallyIdenticalCaptures(groups, failures, evidenceLabel) {
  for (const captures of groups.values()) {
    const stateIds = new Set(captures.map((capture) => capture.stateId));
    if (stateIds.size <= 1) continue;
    failures.push(
      `visual smoke manifest contains semantically identical ${evidenceLabel} captures for ` +
      `${captures[0].theme}/${captures[0].viewportName}: ${captures.map((capture) => capture.stateId).join(", ")}`,
    );
  }
}

function readPngEvidence(bytes, fileName) {
  const pngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  const hasPngSignature = pngSignature.every((value, index) => bytes[index] === value);
  const hasIhdrChunk = bytes.length >= 29 && bytes.toString("ascii", 12, 16) === "IHDR";

  if (!hasPngSignature || !hasIhdrChunk) {
    throw new Error(`visual smoke screenshot is not a PNG with an IHDR header: ${fileName}`);
  }

  const width = bytes.readUInt32BE(16);
  const height = bytes.readUInt32BE(20);
  const bitDepth = bytes[24];
  const colorType = bytes[25];
  const idatChunks = [];
  let chunkCount = 0;
  let hasIend = false;
  let offset = 8;
  while (offset + 12 <= bytes.length) {
    const length = bytes.readUInt32BE(offset);
    const type = bytes.toString("ascii", offset + 4, offset + 8);
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    const crcEnd = dataEnd + 4;
    if (crcEnd > bytes.length) {
      throw new Error(`visual smoke screenshot has a truncated PNG chunk: ${fileName}`);
    }

    chunkCount += 1;
    if (type === "IDAT") {
      idatChunks.push(bytes.subarray(dataStart, dataEnd));
    }
    if (type === "IEND") {
      hasIend = true;
      break;
    }
    offset = crcEnd;
  }

  if (idatChunks.length === 0 || !hasIend) {
    throw new Error(`visual smoke screenshot is missing PNG image data: ${fileName}`);
  }

  const compressedPixels = Buffer.concat(idatChunks);
  const pixelEvidenceKey = [
    width,
    height,
    bitDepth,
    colorType,
    createHash("sha256").update(compressedPixels).digest("hex"),
  ].join(":");
  let metrics = pngPixelEvidenceCache.get(pixelEvidenceKey);
  if (!metrics) {
    metrics = analyzePngPixels({
      fileName,
      width,
      height,
      bitDepth,
      colorType,
      compressedPixels,
    });
    pngPixelEvidenceCache.set(pixelEvidenceKey, metrics);
  }

  if (
    metrics.sampledDistinctColorCount < MIN_VISUAL_SMOKE_DISTINCT_COLORS ||
    metrics.luminanceRange < MIN_VISUAL_SMOKE_LUMINANCE_RANGE
  ) {
    throw new Error(
      `visual smoke screenshot appears visually blank or low-information: ${fileName} ` +
      `distinctColors=${metrics.sampledDistinctColorCount}, luminanceRange=${metrics.luminanceRange}`,
    );
  }

  return {
    width: bytes.readUInt32BE(16),
    height: bytes.readUInt32BE(20),
    bitDepth,
    colorType,
    chunkCount,
    idatByteSize: idatChunks.reduce((sum, chunk) => sum + chunk.length, 0),
    ...metrics,
  };
}

function analyzePngPixels({ fileName, width, height, bitDepth, colorType, compressedPixels }) {
  if (bitDepth !== 8 || ![0, 2, 6].includes(colorType)) {
    throw new Error(`visual smoke screenshot uses unsupported PNG color format: ${fileName}`);
  }

  const channels = colorType === 0 ? 1 : colorType === 2 ? 3 : 4;
  const bytesPerPixel = channels;
  const stride = width * channels;
  const expectedLength = height * (stride + 1);
  const inflated = inflateSync(compressedPixels);
  if (inflated.length < expectedLength) {
    throw new Error(`visual smoke screenshot has truncated PNG pixel data: ${fileName}`);
  }

  const previous = Buffer.alloc(stride);
  const current = Buffer.alloc(stride);
  const colorBuckets = new Set();
  const sampleEvery = Math.max(1, Math.floor((width * height) / 4000));
  let pixelIndex = 0;
  let sampleCount = 0;
  let minLuminance = Number.POSITIVE_INFINITY;
  let maxLuminance = Number.NEGATIVE_INFINITY;

  for (let y = 0; y < height; y += 1) {
    const rowStart = y * (stride + 1);
    const filter = inflated[rowStart];
    const row = inflated.subarray(rowStart + 1, rowStart + 1 + stride);
    unfilterPngRow(row, current, previous, filter, bytesPerPixel, fileName);

    for (let x = 0; x < width; x += 1) {
      if (pixelIndex % sampleEvery === 0) {
        const offset = x * channels;
        const r = colorType === 0 ? current[offset] : current[offset];
        const g = colorType === 0 ? current[offset] : current[offset + 1];
        const b = colorType === 0 ? current[offset] : current[offset + 2];
        const luminance = Math.round(0.2126 * r + 0.7152 * g + 0.0722 * b);
        minLuminance = Math.min(minLuminance, luminance);
        maxLuminance = Math.max(maxLuminance, luminance);
        colorBuckets.add(`${r >> 4}:${g >> 4}:${b >> 4}`);
        sampleCount += 1;
      }
      pixelIndex += 1;
    }

    previous.set(current);
  }

  return {
    pixelSampleCount: sampleCount,
    sampledDistinctColorCount: colorBuckets.size,
    luminanceRange: Math.max(0, maxLuminance - minLuminance),
  };
}

function unfilterPngRow(source, target, previous, filter, bytesPerPixel, fileName) {
  for (let i = 0; i < source.length; i += 1) {
    const raw = source[i];
    const left = i >= bytesPerPixel ? target[i - bytesPerPixel] : 0;
    const up = previous[i] ?? 0;
    const upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
    if (filter === 0) {
      target[i] = raw;
    } else if (filter === 1) {
      target[i] = (raw + left) & 0xff;
    } else if (filter === 2) {
      target[i] = (raw + up) & 0xff;
    } else if (filter === 3) {
      target[i] = (raw + Math.floor((left + up) / 2)) & 0xff;
    } else if (filter === 4) {
      target[i] = (raw + paethPredictor(left, up, upLeft)) & 0xff;
    } else {
      throw new Error(`visual smoke screenshot uses unsupported PNG row filter ${filter}: ${fileName}`);
    }
  }
}

function paethPredictor(left, up, upLeft) {
  const estimate = left + up - upLeft;
  const leftDistance = Math.abs(estimate - left);
  const upDistance = Math.abs(estimate - up);
  const upLeftDistance = Math.abs(estimate - upLeft);
  if (leftDistance <= upDistance && leftDistance <= upLeftDistance) return left;
  if (upDistance <= upLeftDistance) return up;
  return upLeft;
}
