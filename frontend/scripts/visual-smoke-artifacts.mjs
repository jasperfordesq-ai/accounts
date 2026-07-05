import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import path from "node:path";

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

export async function verifyVisualSmokeManifest(manifestPath) {
  const resolvedManifestPath = path.resolve(manifestPath);
  const manifest = JSON.parse(await readFile(resolvedManifestPath, "utf8"));
  const screenshots = Array.isArray(manifest.screenshots) ? manifest.screenshots : [];

  if (manifest.expectedScreenshotCount !== screenshots.length) {
    throw new Error(
      `visual smoke manifest screenshot count mismatch: expected ${manifest.expectedScreenshotCount}, found ${screenshots.length}`,
    );
  }

  if (!manifest.reviewProtocol?.requiredEvidence?.includes("screenshot SHA-256 checksums")) {
    throw new Error("visual smoke manifest review protocol must require screenshot SHA-256 checksums");
  }

  let totalBytes = 0;
  for (const screenshot of screenshots) {
    const evidence = await withScreenshotEvidence({
      ...screenshot,
      artifactPath: resolveArtifactPath(screenshot.artifactPath),
    });

    if (screenshot.byteSize !== evidence.byteSize) {
      throw new Error(`visual smoke screenshot byte size mismatch: ${screenshot.fileName}`);
    }

    if (screenshot.sha256 !== evidence.sha256) {
      throw new Error(`visual smoke screenshot hash mismatch: ${screenshot.fileName}`);
    }

    totalBytes += evidence.byteSize;
  }

  for (const audit of manifest.routeAudits ?? []) {
    const actualCount = screenshots.filter((screenshot) => screenshot.routeName === audit.routeName).length;
    if (audit.screenshotCount !== actualCount) {
      throw new Error(
        `visual smoke route audit mismatch: ${audit.routeName} expected ${audit.screenshotCount} screenshots, found ${actualCount}`,
      );
    }
  }

  return {
    ok: true,
    manifestPath: resolvedManifestPath,
    screenshotCount: screenshots.length,
    totalBytes,
  };
}

function resolveArtifactPath(artifactPath) {
  return path.isAbsolute(artifactPath) ? artifactPath : path.resolve(artifactPath);
}
