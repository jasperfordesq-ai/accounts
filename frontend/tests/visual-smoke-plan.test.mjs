import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  VISUAL_SMOKE_ARTIFACT_NAME,
  visualSmokeLayoutChecks,
  visualSmokeReviewProtocol,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
  expectedVisualSmokeScreenshotCount,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeManifest,
  MIN_VISUAL_SMOKE_CONTRAST_RATIO,
  passedVisualSmokeContrastResult,
  passedVisualSmokeLayoutResults,
  visualSmokeContrastCheck,
} from "../scripts/visual-smoke-plan.mjs";

describe("visual smoke plan", () => {
  it("covers the accountant workbench routes in light/dark desktop/mobile", () => {
    assert.equal(VISUAL_SMOKE_ARTIFACT_NAME, "visual-smoke-screenshots");
    assert.deepEqual(visualSmokeThemes, ["light", "dark"]);
    assert.deepEqual(ACCOUNTANT_WORKFLOW_STAGES, [
      "Setup",
      "Import",
      "Classify",
      "Year-End",
      "Statements",
      "Notes",
      "Review",
      "Filing",
    ]);
    assert.deepEqual(visualSmokeLayoutChecks, [
      "browser-console-errors",
      "page-horizontal-overflow",
      "visible-text-overlap",
    ]);
    assert.equal(visualSmokeContrastCheck, "theme-contrast");
    assert.equal(MIN_VISUAL_SMOKE_CONTRAST_RATIO, 3);
    assert.deepEqual(visualSmokeReviewChecks, [
      "accountant-workflow-hierarchy",
      "table-scanability",
      "theme-contrast",
      "mobile-density",
      "loading-error-empty-states",
    ]);
    assert.deepEqual(visualSmokeReviewProtocol, {
      protocolVersion: "visual-review-v1",
      reviewerRole: "Design reviewer",
      status: "required-review",
      signOffGate: "visual-qa-screenshot-review",
      failurePolicy: "Block release if any accountant workbench route has console errors, horizontal overflow, visible text overlap, inaccessible contrast, unreadable table density, or unresolved light/dark/mobile defects.",
      acceptanceCriteria: [
        "Every configured route is captured in light desktop, dark desktop, light mobile and dark mobile.",
        "No browser console errors, horizontal overflow or visible text overlap are present.",
        "Accountant workflow hierarchy, table scanability, theme contrast, mobile density and route states are professionally acceptable.",
        "A named visual QA reviewer records screenshot-manifest acceptance before real filing release.",
      ],
      requiredEvidence: [
        "visual-smoke-manifest.json",
        "visual-smoke-evidence-report.json",
        "accountant-workbench-evidence-report.json",
        "28 visual smoke screenshots",
        "screenshot SHA-256 checksums",
        "screenshot PNG dimensions",
        "screenshot nonblank pixel diversity evidence",
        "per-screenshot automated theme contrast smoke evidence",
        "route audit summary",
        "named visual QA reviewer sign-off",
      ],
    });
    assert.deepEqual(
      visualSmokeViewports.map(({ name, width, height }) => ({ name, width, height })),
      [
        { name: "desktop", width: 1440, height: 1000 },
        { name: "mobile", width: 390, height: 844 },
      ],
    );
    assert.equal(expectedVisualSmokeScreenshotCount(), 28);
    assert.equal(expectedVisualSmokeArtifacts().length, 28);
    assert.deepEqual(expectedVisualSmokeArtifacts()[0], {
      routeName: "dashboard",
      routeKey: "dashboard",
      theme: "light",
      viewportName: "desktop",
      fileName: "dashboard-light-desktop.png",
      artifactPath: "artifacts/visual-smoke/dashboard-light-desktop.png",
      expectedText: "Firm command centre",
      openFilingTab: false,
      reviewStatus: "required-review",
      layoutChecks: visualSmokeLayoutChecks,
      layoutCheckResults: passedVisualSmokeLayoutResults(),
      themeContrastResult: passedVisualSmokeContrastResult(),
    });
    assert.equal(
      expectedVisualSmokeArtifacts().at(-1)?.artifactPath,
      "artifacts/visual-smoke/workbench-preview-dark-mobile.png",
    );
    assert.equal(expectedVisualSmokeArtifacts().at(-1)?.routeKey, "workbenchPreview");
    assert.deepEqual(expectedVisualSmokeRouteAudits()[0], {
      routeName: "dashboard",
      routeKey: "dashboard",
      label: "Dashboard",
      workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
      screenshotCount: 4,
      reviewStatus: "required-review",
      reviewChecks: visualSmokeReviewChecks,
    });
    assert.deepEqual(expectedVisualSmokeManifest(), {
      artifactName: VISUAL_SMOKE_ARTIFACT_NAME,
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: 28,
      layoutChecks: visualSmokeLayoutChecks,
      reviewChecks: visualSmokeReviewChecks,
      reviewProtocol: visualSmokeReviewProtocol,
      routeAudits: expectedVisualSmokeRouteAudits(),
      screenshots: expectedVisualSmokeArtifacts(),
    });

    assert.deepEqual(
      visualSmokeRoutes.map((route) => route.name),
      [
        "dashboard",
        "production-readiness",
        "company-detail",
        "period-workspace",
        "filing-review",
        "financial-statements",
        "workbench-preview",
      ],
    );
    assert.equal(visualSmokeRoutes.find((route) => route.name === "filing-review")?.openFilingTab, true);
    assert.deepEqual(
      visualSmokeRoutes.find((route) => route.name === "period-workspace")?.workflowStages,
      ACCOUNTANT_WORKFLOW_STAGES,
    );
    assert.deepEqual(
      [...new Set(visualSmokeRoutes.flatMap((route) => route.workflowStages))].sort(),
      [...ACCOUNTANT_WORKFLOW_STAGES].sort(),
    );
    assert.ok(
      visualSmokeRoutes.every((route) => route.workflowStages.length > 0),
      "every visual smoke route must state the accountant workflow stages it proves",
    );
    assert.equal(
      visualSmokeRoutes.find((route) => route.name === "company-detail")?.expectedText,
      "Company command centre",
    );
    assert.equal(
      visualSmokeRoutes.find((route) => route.name === "period-workspace")?.expectedText,
      "Filing readiness",
    );
    assert.equal(
      visualSmokeRoutes.find((route) => route.name === "financial-statements")?.expectedText,
      "Financial Statements",
    );
    assert.deepEqual(
      visualSmokeRoutes.find((route) => route.name === "financial-statements")?.workflowStages,
      ["Statements"],
    );
    assert.ok(
      expectedVisualSmokeArtifacts().some(
        (artifact) =>
          artifact.routeName === "financial-statements"
          && artifact.routeKey === "financialStatements"
          && artifact.artifactPath === "artifacts/visual-smoke/financial-statements-light-desktop.png",
      ),
    );
    assert.equal(
      visualSmokeRoutes.find((route) => route.name === "workbench-preview")?.expectedText,
      "Workbench Component Preview",
    );
  });

  it("discovers dashboard period workspace links before creating fallback smoke data", async () => {
    const script = await readFile(new URL("../scripts/visual-smoke.mjs", import.meta.url), "utf8");
    const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));

    assert.match(script, /withScreenshotEvidence/);
    assert.match(script, /function companyHrefFromPeriodHref/);
    assert.match(script, /writeFile/);
    assert.match(script, /visual-smoke-manifest\.json/);
    assert.match(script, /routeAudits/);
    assert.match(script, /reviewProtocol/);
    assert.match(script, /Firm command centre/);
    assert.doesNotMatch(script, /mainText\(page, "Dashboard", \{ exact: true \}\)/);
    assert.match(script, /a\[href\^="\/companies\/"\]\[href\*="\/periods\/"\]/);
    assert.match(script, /Company command centre/);
    assert.doesNotMatch(script, /mainText\(page, "Accounting Periods"\)/);
    assert.ok(
      script.indexOf("companyHrefFromPeriodHref") < script.indexOf("createSmokeCompany(page)"),
      "existing dashboard period links must be resolved before fallback smoke company creation",
    );
    assert.equal(packageJson.scripts["test:visual:verify"], "node scripts/verify-visual-smoke-artifacts.mjs");
    assert.equal(packageJson.scripts["test:visual:workbench"], "node scripts/verify-accountant-workbench-evidence.mjs");
  });
});
