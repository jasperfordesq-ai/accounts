import assert from "node:assert/strict";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  expectedAccountantWorkbenchScreenshotCount,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeScreenshotCount,
  MIN_VISUAL_SMOKE_CONTRAST_RATIO,
  passedVisualSmokeContrastResult,
  passedVisualSmokeAccessibilityResult,
  visualSmokeLayoutChecks,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeStateInventory,
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
      assert.equal(result.screenshotCount, expectedAccountantWorkbenchScreenshotCount());
      assert.equal(result.expectedScreenshotCount, expectedAccountantWorkbenchScreenshotCount());
      assert.equal(result.visualSmokeTotalScreenshotCount, expectedVisualSmokeScreenshotCount());
      assert.equal(result.visualSmokeExpectedScreenshotCount, expectedVisualSmokeScreenshotCount());
      assert.deepEqual(result.requiredCoverage.workflowStages, ACCOUNTANT_WORKFLOW_STAGES);
      assert.deepEqual(result.requiredCoverage.routeCodes, visualSmokeRoutes.map((route) => route.name));
      assert.deepEqual(result.requiredCoverage.reviewChecks, visualSmokeReviewChecks);
      assert.deepEqual(result.requiredCoverage.layoutCheckEvidence, visualSmokeLayoutChecks.map((check) => `${check}:passed`));
      assert.deepEqual(result.requiredCoverage.contrastCheckEvidence, [
        "theme-contrast:passed",
        "minimum-ratio:3",
      ]);
      assert.equal(result.routeAcceptanceCount, visualSmokeRoutes.length);
      assert.ok(result.requiredCoverage.expectedTextChecks.includes("route expected accountant decision text"));
      assert.ok(result.requiredCoverage.expectedTextChecks.includes("visual smoke screenshots carry route expected accountant decision text"));
      assert.ok(result.requiredCoverage.expectedTextChecks.includes("visual smoke screenshots carry passed layout check results"));
      assert.ok(result.requiredCoverage.expectedTextChecks.includes("visual smoke screenshots carry passed automated theme contrast results"));
      assert.ok(result.requiredCoverage.routeAcceptanceEvidence.includes("filing-review-qualified-accountant-route-acceptance"));
      assert.equal(result.requiredCoverage.routeAcceptanceSignOffGate, "qualified-accountant-route-acceptance");
      assert.ok(result.requiredCoverage.evidenceFiles.includes("visual-smoke-evidence-report.json"));
      assert.ok(result.requiredCoverage.evidenceFiles.includes("accountant-workbench-evidence-report.json"));
      assert.equal(result.routeReadiness.find((route) => route.routeName === "filing-review")?.screenshotCount, 6);
      assert.equal(
        result.routeReadiness.find((route) => route.routeName === "filing-review")?.layoutCheckResultCount,
        visualSmokeThemes.length * visualSmokeViewports.length * visualSmokeLayoutChecks.length,
      );
      assert.equal(
        result.routeReadiness.find((route) => route.routeName === "filing-review")?.expectedTextEvidenceCount,
        visualSmokeThemes.length * visualSmokeViewports.length,
      );
      assert.equal(
        result.routeReadiness.find((route) => route.routeName === "filing-review")?.contrastCheckResultCount,
        visualSmokeThemes.length * visualSmokeViewports.length,
      );
      assert.equal(result.routeReadiness.find((route) => route.routeName === "filing-review")?.minimumContrastRatio, MIN_VISUAL_SMOKE_CONTRAST_RATIO);
      const writtenReport = JSON.parse(await readFile(reportPath, "utf8"));
      assert.deepEqual(writtenReport.requiredCoverage.themes, ["light", "dark"]);
      assert.deepEqual(
        writtenReport.routeAcceptance.find((route) => route.routeName === "production-readiness"),
        {
          routeName: "production-readiness",
          routeKey: "readiness",
          label: "Production readiness",
          workflowStages: ["Review", "Filing"],
          expectedText: "Production Readiness Checklist",
          requiredAcceptanceEvidence: [
            "production-readiness-accountant-route-acceptance-note",
            "production-readiness-visual-smoke-screenshots-reviewed",
            "production-readiness-qualified-accountant-route-acceptance",
          ],
          screenshotReviewEvidence: "production-readiness-light-dark-mobile-tablet-desktop-screenshot-review",
          signOffGate: "qualified-accountant-route-acceptance",
          reviewStatus: "required-review",
          blocksRelease: true,
        },
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects visual evidence that omits per-screenshot contrast pass results", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    const screenshot = report.screenshots.find((item) =>
      item.routeName === "dashboard" && item.theme === "light" && item.viewportName === "desktop");
    screenshot.themeContrastResult = {
      ...screenshot.themeContrastResult,
      status: "failed",
    };

    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /route dashboard screenshot dashboard-light-desktop\.png theme contrast status must be passed/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects visual evidence with an axe-core WCAG violation", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    const screenshot = report.screenshots.find((item) =>
      item.routeName === "dashboard" && item.theme === "light" && item.viewportName === "desktop");
    screenshot.accessibilityResult = {
      ...screenshot.accessibilityResult,
      violationCount: 1,
      violations: [{ id: "label", impact: "serious", nodeCount: 1 }],
    };
    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /dashboard-light-desktop\.png accessibility result must retain zero violations/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects route evidence that omits a required review check", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    const coverage = report.routeCoverage.find((item) => item.routeName === "dashboard");
    coverage.requiredReviewChecks = coverage.requiredReviewChecks.filter(
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

  it("rejects visual evidence when route keys drift from the visual smoke plan", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    report.routeCoverage.find((item) => item.routeName === "dashboard").routeKey = "wrong-dashboard-key";
    report.screenshots.find((item) => item.routeName === "dashboard").routeKey = "wrong-dashboard-key";

    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /route dashboard routeKey must be dashboard, found wrong-dashboard-key/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects visual evidence when screenshots omit the route expected text proof", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    report.screenshots.find((item) =>
      item.routeName === "dashboard" && item.theme === "light" && item.viewportName === "desktop").expectedText = "Wrong route heading";

    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /route dashboard screenshot dashboard-light-desktop\.png expectedText must be Firm command centre, found Wrong route heading/,
      );
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("rejects visual evidence that omits per-screenshot layout check pass results", async () => {
    const { verifyAccountantWorkbenchEvidence } = await import("../scripts/verify-accountant-workbench-evidence.mjs");
    const dir = await mkTempDir();
    const visualReportPath = path.join(dir, "visual-smoke-evidence-report.json");
    const report = visualSmokeReport();
    const screenshot = report.screenshots.find((item) =>
      item.routeName === "dashboard" && item.theme === "light" && item.viewportName === "desktop");
    screenshot.layoutCheckResults = screenshot.layoutCheckResults.filter(
      (result) => result.check !== "page-horizontal-overflow",
    );

    await writeFile(visualReportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

    try {
      await assert.rejects(
        () => verifyAccountantWorkbenchEvidence({ visualReportPath }),
        /route dashboard screenshot dashboard-light-desktop\.png is missing passed layout check result page-horizontal-overflow/,
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
    routeCount: visualSmokeStateInventory.length,
    screenshotCount: expectedVisualSmokeScreenshotCount(),
    expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
    routeCoverage: expectedVisualSmokeRouteAudits().map((state) => ({
      stateId: state.stateId,
      routeName: state.routeName,
      routeKey: state.routeKey,
      screenshotCount: visualSmokeThemes.length * visualSmokeViewports.length,
      requiredReviewChecks: visualSmokeReviewChecks,
      reviewStatus: "required-review",
    })),
    screenshots: expectedVisualSmokeArtifacts().map((artifact) => ({
          stateId: artifact.stateId,
          routeName: artifact.routeName,
          routeKey: artifact.routeKey,
          theme: artifact.theme,
          viewportName: artifact.viewportName,
          fileName: artifact.fileName,
          expectedText: artifact.expectedText,
          byteSize: 128,
          sha256: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          reviewStatus: "required-review",
          layoutCheckResults: visualSmokeLayoutChecks.map((check) => ({
            check,
            status: "passed",
            evidence: `${check} passed`,
          })),
          themeContrastResult: passedVisualSmokeContrastResult({
            sampledTextCount: 12,
            sampledNormalTextCount: 8,
            sampledLargeTextCount: 1,
            sampledInteractiveTextCount: 3,
            sampledPlaceholderCount: 1,
            sampledUiComponentCount: 4,
            sampledGradientTextCount: 1,
          }),
          accessibilityResult: passedVisualSmokeAccessibilityResult({ passCount: 24 }),
        })),
  };
}

async function mkTempDir() {
  const dir = path.join(os.tmpdir(), `accountant-workbench-evidence-${Date.now()}-${Math.random().toString(16).slice(2)}`);
  await mkdir(dir, { recursive: true });
  return dir;
}
