import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { test } from "node:test";

const appDir = new URL("../src/app/", import.meta.url);

test("Next app shell has production route-state files wired to workbench primitives", () => {
  const expectations = [
    ["loading.tsx", "WorkbenchLoadingState"],
    ["error.tsx", "WorkbenchErrorState"],
    ["not-found.tsx", "WorkbenchEmptyState"],
  ];

  for (const [fileName, componentName] of expectations) {
    const fileUrl = new URL(fileName, appDir);
    assert.ok(existsSync(fileUrl), `${fileName} must exist for consistent production route states`);

    const source = readFileSync(fileUrl, "utf8");
    assert.match(source, new RegExp(componentName), `${fileName} should render ${componentName}`);
  }
});

test("main accountant workflow routes have local loading and error states", () => {
  const routeExpectations = [
    ["production-readiness", "Production readiness"],
    ["companies/[companyId]", "Company workspace"],
    ["companies/[companyId]/periods/[periodId]", "Period workspace"],
    ["companies/[companyId]/periods/[periodId]/classify", "Classification workspace"],
    ["companies/[companyId]/periods/[periodId]/year-end", "Year-end workspace"],
    ["companies/[companyId]/periods/[periodId]/statements", "Statements workspace"],
    ["companies/[companyId]/periods/[periodId]/notes", "Notes workspace"],
    ["companies/[companyId]/periods/[periodId]/charity", "Charity workspace"],
  ];

  for (const [route, title] of routeExpectations) {
    const routeDir = new URL(`${route}/`, appDir);
    const loadingFile = new URL("loading.tsx", routeDir);
    const errorFile = new URL("error.tsx", routeDir);

    assert.ok(existsSync(loadingFile), `${route}/loading.tsx must exist for local route loading feedback`);
    assert.ok(existsSync(errorFile), `${route}/error.tsx must exist for local route error recovery`);

    const loadingSource = readFileSync(loadingFile, "utf8");
    const errorSource = readFileSync(errorFile, "utf8");
    assert.match(loadingSource, /WorkbenchLoadingState/, `${route}/loading.tsx should use the workbench loading primitive`);
    assert.match(errorSource, /WorkbenchErrorState/, `${route}/error.tsx should use the workbench error primitive`);
    assert.match(`${loadingSource}\n${errorSource}`, new RegExp(title), `${route} states should name the local workspace`);
  }
});
