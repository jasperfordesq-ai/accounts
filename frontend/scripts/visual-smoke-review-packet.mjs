import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import {
  expectedVisualSmokeScreenshotCount,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";
import { verifyVisualSmokeManifest } from "./visual-smoke-artifacts.mjs";

function arg(name, fallback) {
  const prefix = `--${name}=`;
  const value = process.argv.find((item) => item.startsWith(prefix));
  return value ? value.slice(prefix.length) : process.env[name.toUpperCase().replaceAll("-", "_")] ?? fallback;
}

export async function buildVisualSmokeReviewPacket(manifestPath, outputPath) {
  const verification = await verifyVisualSmokeManifest(manifestPath);
  const resolvedManifestPath = path.resolve(manifestPath);
  const manifest = JSON.parse(await readFile(resolvedManifestPath, "utf8"));
  const packet = renderVisualSmokeReviewPacket(manifest, verification, resolvedManifestPath);

  if (outputPath) {
    await writeFile(outputPath, packet, "utf8");
  }

  return {
    ...verification,
    outputPath: outputPath ? path.resolve(outputPath) : null,
    packet,
  };
}

export function renderVisualSmokeReviewPacket(manifest, verification, manifestPath) {
  assertCompleteCaptureMatrix(manifest);

  const generatedAt = manifest.generatedAt ?? new Date().toISOString();
  const routeSections = visualSmokeRoutes.map((route) => renderRouteSection(route, manifest));
  const reviewEvidence = manifest.reviewProtocol?.requiredEvidence ?? [];

  return `# Visual Smoke Human Review Packet\n\n` +
    `Generated at: ${generatedAt}\n\n` +
    `Manifest: ${manifestPath}\n\n` +
    `Verification: ${verification.screenshotCount} screenshots, ${verification.totalBytes} bytes, SHA-256 hashes verified.\n\n` +
    `## Release gate\n\n` +
    `- Gate: ${manifest.reviewProtocol?.signOffGate ?? "visual-qa-screenshot-review"}\n` +
    `- Reviewer role: ${manifest.reviewProtocol?.reviewerRole ?? "Design reviewer"}\n` +
    `- Status: pending named human review; automated screenshot existence/hash checks are not professional visual acceptance.\n` +
    `- Failure policy: ${manifest.reviewProtocol?.failurePolicy ?? "Block release for unresolved visual defects."}\n\n` +
    `## Required evidence\n\n` +
    reviewEvidence.map((item) => `- [ ] ${item}`).join("\n") +
    `\n\n## Review checks\n\n` +
    visualSmokeReviewChecks.map((item) => `- [ ] ${humanise(item)}`).join("\n") +
    `\n\n## Route review matrix\n\n` +
    routeSections.join("\n\n") +
    `\n\n## Sign-off\n\n` +
    `- Reviewer name: ______________________________\n` +
    `- Qualification/role: __________________________\n` +
    `- Review date: _________________________________\n` +
    `- Decision: [ ] Accepted  [ ] Rejected pending fixes\n` +
    `- Notes / defects: _____________________________\n`;
}

function renderRouteSection(route, manifest) {
  const screenshots = manifest.screenshots
    .filter((screenshot) => screenshot.routeName === route.name)
    .sort((left, right) => `${left.theme}-${left.viewportName}`.localeCompare(`${right.theme}-${right.viewportName}`));

  return `### ${route.label}\n\n` +
    `Route key: \`${route.routeKey}\`\n\n` +
    `Expected text: ${route.expectedText}\n\n` +
    screenshots.map((screenshot) => (
      `- [ ] ${screenshot.theme}/${screenshot.viewportName}: \`${screenshot.fileName}\` ` +
      `(${screenshot.byteSize} bytes, ${screenshot.sha256})`
    )).join("\n");
}

function assertCompleteCaptureMatrix(manifest) {
  const screenshots = Array.isArray(manifest.screenshots) ? manifest.screenshots : [];
  if (screenshots.length !== expectedVisualSmokeScreenshotCount()) {
    throw new Error(`visual smoke review packet requires ${expectedVisualSmokeScreenshotCount()} screenshots, found ${screenshots.length}`);
  }

  const expectedKeys = new Set();
  for (const route of visualSmokeRoutes) {
    for (const theme of visualSmokeThemes) {
      for (const viewport of visualSmokeViewports) {
        expectedKeys.add(`${route.name}:${theme}:${viewport.name}`);
      }
    }
  }

  const actualKeys = new Set(screenshots.map((screenshot) => `${screenshot.routeName}:${screenshot.theme}:${screenshot.viewportName}`));
  const missing = [...expectedKeys].filter((key) => !actualKeys.has(key));
  if (missing.length > 0) {
    throw new Error(`visual smoke review packet missing captures: ${missing.join(", ")}`);
  }
}

function humanise(value) {
  return value.replaceAll("-", " ");
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const manifestPath = arg("manifest", "artifacts/visual-smoke/visual-smoke-manifest.json");
  const outputPath = arg("output", "artifacts/visual-smoke/visual-smoke-review-packet.md");

  buildVisualSmokeReviewPacket(manifestPath, outputPath)
    .then((result) => {
      console.log(JSON.stringify({ ok: true, manifestPath: result.manifestPath, outputPath: result.outputPath }, null, 2));
    })
    .catch((error) => {
      console.error(error);
      process.exitCode = 1;
    });
}
