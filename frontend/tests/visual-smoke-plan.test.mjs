import assert from "node:assert/strict";
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
    assert.equal(expectedVisualSmokeScreenshotCount(), 20);

    assert.deepEqual(
      visualSmokeRoutes.map((route) => route.name),
      ["dashboard", "production-readiness", "company-detail", "period-workspace", "filing-review"],
    );
    assert.equal(visualSmokeRoutes.find((route) => route.name === "filing-review")?.openFilingTab, true);
    assert.equal(
      visualSmokeRoutes.find((route) => route.name === "period-workspace")?.expectedText,
      "Filing readiness",
    );
  });
});
