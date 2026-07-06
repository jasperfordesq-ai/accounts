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
    ["companies/new", "Company onboarding"],
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

test("dynamic accountant workflow routes have local not-found states", () => {
  const routeExpectations = [
    ["companies/[companyId]", "Company workspace not found"],
    ["companies/[companyId]/periods/[periodId]", "Period workspace not found"],
  ];

  for (const [route, title] of routeExpectations) {
    const routeDir = new URL(`${route}/`, appDir);
    const notFoundFile = new URL("not-found.tsx", routeDir);

    assert.ok(existsSync(notFoundFile), `${route}/not-found.tsx must exist for local missing-resource recovery`);

    const source = readFileSync(notFoundFile, "utf8");
    assert.match(source, /WorkbenchEmptyState/, `${route}/not-found.tsx should use the workbench empty-state primitive`);
    assert.match(source, new RegExp(title), `${route}/not-found.tsx should name the missing workspace`);
    assert.match(source, /Return to dashboard|Return to company/, `${route}/not-found.tsx should offer an accountant-safe recovery action`);
  }
});

test("period filing route wires reviewer permission into the filing workspace", () => {
  const periodRoute = new URL("companies/[companyId]/periods/[periodId]/page.tsx", appDir);
  const source = readFileSync(periodRoute, "utf8");

  assert.match(source, /useAuth/, "period route should read authenticated workflow permissions");
  assert.match(source, /const\s*\{\s*canReview\s*\}\s*=\s*useAuth\(\)/, "period route should read canReview from auth context");
  assert.match(source, /PeriodFilingWorkspace/, "period route should import the focused filing workspace");
  assert.match(source, /<PeriodFilingWorkspace[\s\S]*canReview=\{canReview\}/, "period route should pass canReview into PeriodFilingWorkspace");
  assert.doesNotMatch(source, /import\s+\{\s*FilingReviewCentre\s*\}/, "period route should not import the low-level filing review centre directly");
  assert.doesNotMatch(source, /import\s+\{\s*FilingDeadlinesPanel\s*\}/, "period route should not import filing deadlines panel directly");
  assert.doesNotMatch(source, /import\s+\{\s*FilingOutputsPanel\s*\}/, "period route should not import filing outputs panel directly");
  assert.doesNotMatch(source, /import\s+\{\s*PeriodAuditTrailPanel\s*\}/, "period route should not import filing audit trail panel directly");
  assert.doesNotMatch(source, /import\s+\{\s*StatutoryWarningsPanel\s*\}/, "period route should not import statutory warnings panel directly");
});

test("period filing workspace composes the accountant review panels", () => {
  const componentFile = new URL("../src/components/period/PeriodFilingWorkspace.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "PeriodFilingWorkspace should exist as the focused filing workflow component");

  const source = readFileSync(componentFile, "utf8");
  for (const componentName of [
    "FilingReviewCentre",
    "FilingDeadlinesPanel",
    "StatutoryWarningsPanel",
    "FilingOutputsPanel",
    "PeriodAuditTrailPanel",
  ]) {
    assert.match(source, new RegExp(`<${componentName}`), `PeriodFilingWorkspace should render ${componentName}`);
  }

  assert.match(source, /Review Notes/, "PeriodFilingWorkspace should keep the filing notes review action");
});
