import assert from "node:assert/strict";
import { rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import {
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeManifest,
  expectedVisualSmokeRouteAudits,
} from "../scripts/visual-smoke-plan.mjs";


describe("visual smoke human review packet", () => {
  it("turns a complete verified screenshot manifest into a named reviewer checklist", async () => {
    const { buildVisualSmokeReviewPacket } = await import("../scripts/visual-smoke-review-packet.mjs");
    const dir = await mkdtempDir();
    const manifestPath = path.join(dir, "visual-smoke-manifest.json");
    const outputPath = path.join(dir, "visual-smoke-review-packet.md");

    try {
      const screenshots = [];
      for (const artifact of expectedVisualSmokeArtifacts(dir)) {
        const screenshotPath = path.join(dir, artifact.fileName);
        await writeFile(screenshotPath, `visual bytes for ${artifact.fileName}`, "utf8");
        const { withScreenshotEvidence } = await import("../scripts/visual-smoke-artifacts.mjs");
        screenshots.push(await withScreenshotEvidence({ ...artifact, artifactPath: screenshotPath }));
      }

      await writeFile(manifestPath, `${JSON.stringify({
        ...expectedVisualSmokeManifest(dir),
        generatedAt: "2026-07-07T10:30:00.000Z",
        routeAudits: expectedVisualSmokeRouteAudits(),
        screenshots,
      }, null, 2)}\n`, "utf8");

      const result = await buildVisualSmokeReviewPacket(manifestPath, outputPath);

      assert.equal(result.screenshotCount, screenshots.length);
      assert.match(result.packet, /# Visual Smoke Human Review Packet/);
      assert.match(result.packet, /Status: pending named human review/);
      assert.match(result.packet, /### Dashboard/);
      assert.match(result.packet, /dashboard-light-desktop\.png/);
      assert.match(result.packet, /Reviewer name: _+/);
      assert.equal(result.outputPath, outputPath);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects an incomplete capture matrix before a human review packet can be signed", async () => {
    const { renderVisualSmokeReviewPacket } = await import("../scripts/visual-smoke-review-packet.mjs");

    assert.throws(
      () => renderVisualSmokeReviewPacket({
        ...expectedVisualSmokeManifest("artifacts/visual-smoke"),
        screenshots: [],
      }, { screenshotCount: 0, totalBytes: 0 }, "manifest.json"),
      /visual smoke review packet requires 28 screenshots, found 0/,
    );
  });
});

async function mkdtempDir() {
  const { mkdtemp } = await import("node:fs/promises");
  return mkdtemp(path.join(os.tmpdir(), "accounts-visual-review-"));
}
