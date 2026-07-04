import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";
import {
  VISUAL_SMOKE_ARTIFACT_NAME,
  visualSmokeLayoutChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
  expectedVisualSmokeScreenshotCount,
} from "../scripts/visual-smoke-plan.mjs";

describe("visual smoke plan", () => {
  it("covers the accountant workbench routes in light/dark desktop/mobile", () => {
    assert.equal(VISUAL_SMOKE_ARTIFACT_NAME, "visual-smoke-screenshots");
    assert.deepEqual(visualSmokeThemes, ["light", "dark"]);
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
