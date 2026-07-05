import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  VISUAL_SMOKE_ARTIFACT_NAME,
  visualSmokeLayoutChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
  expectedVisualSmokeScreenshotCount,
  expectedVisualSmokeArtifacts,
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
    assert.deepEqual(
      visualSmokeViewports.map(({ name, width, height }) => ({ name, width, height })),
      [
        { name: "desktop", width: 1440, height: 1000 },
        { name: "mobile", width: 390, height: 844 },
      ],
    );
    assert.equal(expectedVisualSmokeScreenshotCount(), 24);
    assert.equal(expectedVisualSmokeArtifacts().length, 24);
    assert.deepEqual(expectedVisualSmokeArtifacts()[0], {
      routeName: "dashboard",
      theme: "light",
      viewportName: "desktop",
      fileName: "dashboard-light-desktop.png",
      artifactPath: "artifacts/visual-smoke/dashboard-light-desktop.png",
      expectedText: "Production Readiness",
      openFilingTab: false,
      reviewStatus: "required-review",
      layoutChecks: visualSmokeLayoutChecks,
    });
    assert.equal(
      expectedVisualSmokeArtifacts().at(-1)?.artifactPath,
      "artifacts/visual-smoke/workbench-preview-dark-mobile.png",
    );

    assert.deepEqual(
      visualSmokeRoutes.map((route) => route.name),
      [
        "dashboard",
        "production-readiness",
        "company-detail",
        "period-workspace",
        "filing-review",
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
      visualSmokeRoutes.find((route) => route.name === "workbench-preview")?.expectedText,
      "Workbench Component Preview",
    );
  });

  it("discovers dashboard period workspace links before creating fallback smoke data", async () => {
    const script = await readFile(new URL("../scripts/visual-smoke.mjs", import.meta.url), "utf8");

    assert.match(script, /function companyHrefFromPeriodHref/);
    assert.match(script, /a\[href\^="\/companies\/"\]\[href\*="\/periods\/"\]/);
    assert.match(script, /Company command centre/);
    assert.doesNotMatch(script, /mainText\(page, "Accounting Periods"\)/);
    assert.ok(
      script.indexOf("companyHrefFromPeriodHref") < script.indexOf("createSmokeCompany(page)"),
      "existing dashboard period links must be resolved before fallback smoke company creation",
    );
  });
});
