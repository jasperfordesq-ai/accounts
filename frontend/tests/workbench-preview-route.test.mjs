import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { test } from "node:test";
import {
  visualSmokeRoutes,
  expectedVisualSmokeScreenshotCount,
} from "../scripts/visual-smoke-plan.mjs";

test("workbench component preview route is wired into visual QA", () => {
  const pageUrl = new URL("../src/app/workbench-preview/page.tsx", import.meta.url);
  assert.ok(existsSync(pageUrl), "The workbench component preview route should exist.");

  const source = readFileSync(pageUrl, "utf8");
  assert.match(source, /WorkbenchPreview/);

  const previewRoute = visualSmokeRoutes.find((route) => route.name === "workbench-preview");
  assert.ok(previewRoute, "Visual smoke should cover the workbench component preview.");
  assert.equal(previewRoute.routeKey, "workbenchPreview");
  assert.equal(previewRoute.expectedText, "Workbench Component Preview");
  assert.equal(expectedVisualSmokeScreenshotCount(), 192);
});
