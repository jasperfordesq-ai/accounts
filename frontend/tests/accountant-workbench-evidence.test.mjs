import assert from "node:assert/strict";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
} from "../scripts/visual-smoke-plan.mjs";

describe("accountant workbench evidence report", () => {
  it("turns visual smoke evidence into route-level accountant workbench coverage", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const reportPath = path.join(dir, "accountant-workbench-evidence-report.json");

    await writeFile(visualReportPath, `${JSON.stringify(visualSmokeReport(), null, 2)}\n`, "utf8");

    try {
      const result = await verifyAccountantWorkbenchEvidence({
        visualReportPath,
        reportPath,
        checkedAtUtc: "2026-07-08T00:00:00.000Z",
      });

      assert.equal(result.status, "passed");
      assert.equal(result.evidenceReportFileName, "accountant-workbench-evidence-report.json");
      assert.equal(result.routeCount, 7);
      assert.equal(result.screenshotCount, 28);
      assert.deepEqual(result.requiredCoverage.workflowStages, ACCOUNTANT_WORKFLOW_STAGES);
      assert.deepEqual(result.requiredCoverage.routeCodes, visualSmokeRoutes.map((route) => route.name));
      assert.deepEqual(result.requiredCoverage.reviewChecks, visualSmokeReviewChecks);
      assert.ok(result.requiredCoverage.evidenceFiles.includes("visual-smoke-evidence-report.json"));
      assert.ok(result.requiredCoverage.evidenceFiles.includes("accountant-workbench-evidence-report.json"));
      assert.equal(result.routeReadiness.find((route) => route.routeName === "filing-review")?.screenshotCount, 4);
      assert.deepEqual(JSON.parse(await readFile(reportPath, "utf8")).requiredCoverage.themes, ["light", "dark"]);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects route evidence that omits a required review check", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    report.routeCoverage[0].requiredReviewChecks = report.routeCoverage[0].requiredReviewChecks.filter(
      (check) => check !== "table-scanability",
    );

    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /route dashboard is missing review check table-scanability/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects visual evidence that is missing a mobile or theme screenshot", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    report.screenshots = report.screenshots.filter(
      (screenshot) => !(screenshot.routeName === "dashboard" && screenshot.theme === "dark" && screenshot.viewportName === "mobile"),
    );

    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /route dashboard is missing screenshot coverage dark\/mobile/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

function visualSmokeReport() {
  return {
    ok: true,
    status: "passed",
    routeCount: visualSmokeRoutes.length,
    screenshotCount: visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length,
    expectedScreenshotCount: visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length,
    routeCoverage: visualSmokeRoutes.map((route) => ({
      routeName: route.name,
      routeKey: route.routeKey,
      screenshotCount: visualSmokeThemes.length * visualSmokeViewports.length,
      requiredReviewChecks: visualSmokeReviewChecks,
      reviewStatus: "required-review",
    })),
    screenshots: visualSmokeRoutes.flatMap((route) =>
      visualSmokeThemes.flatMap((theme) =>
        visualSmokeViewports.map((viewport) => ({
          routeName: route.name,
          routeKey: route.routeKey,
          theme,
          viewportName: viewport.name,
          fileName: `${route.name}-${theme}-${viewport.name}.png`,
          byteSize: 128,
          sha256: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          reviewStatus: "required-review",
        })),
      ),
    ),
  };
}

async function mkTempDir() {
  const dir = path.join(os.tmpdir(), `accountant-workbench-evidence-${Date.now()}-${Math.random().toString(16).slice(2)}`);
  await mkdir(dir, { recursive: true });
  return dir;
}
