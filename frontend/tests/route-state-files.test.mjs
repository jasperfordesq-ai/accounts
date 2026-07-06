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

test("period import route delegates import workflow composition", () => {
  const periodRoute = new URL("companies/[companyId]/periods/[periodId]/page.tsx", appDir);
  const source = readFileSync(periodRoute, "utf8");

  assert.match(source, /PeriodImportWorkspace/, "period route should import the focused import workspace");
  assert.match(source, /<PeriodImportWorkspace[\s\S]*classificationHref=/, "period route should render PeriodImportWorkspace with workflow links");
  assert.match(source, /<PeriodImportWorkspace[\s\S]*onUploadFile=\{handleFileUpload\}/, "period route should pass upload orchestration into PeriodImportWorkspace");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Bank Accounts<\/Card\.Title>/, "period route should not own bank account import panel markup");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Opening Balances & Reserves<\/Card\.Title>/, "period route should not own opening-balance panel markup");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Import Transactions<\/Card\.Title>/, "period route should not own CSV upload panel markup");
});

test("period import workspace composes the accountant import panels", () => {
  const componentFile = new URL("../src/components/period/PeriodImportWorkspace.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "PeriodImportWorkspace should exist as the focused import workflow component");

  const source = readFileSync(componentFile, "utf8");
  for (const marker of [
    "Company Size Classification",
    "Bank Accounts",
    "Chart of Accounts",
    "Opening Balances & Reserves",
    "Import Transactions",
    "Import Status",
  ]) {
    assert.match(source, new RegExp(marker.replace(/[&]/g, "\\&")), `PeriodImportWorkspace should render ${marker}`);
  }
});

test("period categorise route delegates transaction review composition", () => {
  const periodRoute = new URL("companies/[companyId]/periods/[periodId]/page.tsx", appDir);
  const source = readFileSync(periodRoute, "utf8");

  assert.match(source, /PeriodCategoriseWorkspace/, "period route should import the focused categorisation workspace");
  assert.match(source, /<PeriodCategoriseWorkspace[\s\S]*transactions=\{transactions\}/, "period route should render PeriodCategoriseWorkspace with transaction data");
  assert.match(source, /<PeriodCategoriseWorkspace[\s\S]*onCategoriseTransaction=\{handleCategoriseTransaction\}/, "period route should pass categorisation orchestration into PeriodCategoriseWorkspace");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Categorisation Overview<\/Card\.Title>/, "period route should not own categorisation overview markup");
  assert.doesNotMatch(source, /Transaction Rules/, "period route should not own transaction-rules markup");
  assert.doesNotMatch(source, /Bulk categorisation/, "period route should not own bulk-categorisation markup");
});

test("period categorise workspace composes transaction review panels", () => {
  const componentFile = new URL("../src/components/period/PeriodCategoriseWorkspace.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "PeriodCategoriseWorkspace should exist as the focused transaction review component");

  const source = readFileSync(componentFile, "utf8");
  for (const marker of [
    "Categorisation Overview",
    "Transaction Rules",
    "Bulk categorisation",
    "Categorisation Progress",
    "Import transactions to begin categorisation",
    "Showing",
  ]) {
    assert.match(source, new RegExp(marker), `PeriodCategoriseWorkspace should render ${marker}`);
  }
});

test("period year-end route delegates questionnaire and completeness composition", () => {
  const periodRoute = new URL("companies/[companyId]/periods/[periodId]/page.tsx", appDir);
  const source = readFileSync(periodRoute, "utf8");

  assert.match(source, /PeriodYearEndWorkspace/, "period route should import the focused year-end workspace");
  assert.match(source, /<PeriodYearEndWorkspace[\s\S]*yearEnd=\{yearEnd\}/, "period route should render PeriodYearEndWorkspace with summary data");
  assert.match(source, /<PeriodYearEndWorkspace[\s\S]*questionnaireHref=/, "period route should pass the questionnaire route into PeriodYearEndWorkspace");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Year-End Completeness<\/Card\.Title>/, "period route should not own year-end completeness markup");
  assert.doesNotMatch(source, /Year-End Questionnaire/, "period route should not own questionnaire CTA markup");
  assert.doesNotMatch(source, /Complete the import and categorisation steps first/, "period route should not own year-end empty-state markup");
});

test("period year-end workspace composes questionnaire, completeness and summary panels", () => {
  const componentFile = new URL("../src/components/period/PeriodYearEndWorkspace.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "PeriodYearEndWorkspace should exist as the focused year-end workflow component");

  const source = readFileSync(componentFile, "utf8");
  for (const marker of [
    "Year-End Questionnaire",
    "Year-End Completeness",
    "Debtors",
    "Creditors",
    "Fixed Assets",
    "Inventory",
    "Tax Liabilities",
    "Complete the import and categorisation steps first",
  ]) {
    assert.match(source, new RegExp(marker), `PeriodYearEndWorkspace should render ${marker}`);
  }
});

test("period adjustments route delegates generation, filters and approval composition", () => {
  const periodRoute = new URL("companies/[companyId]/periods/[periodId]/page.tsx", appDir);
  const source = readFileSync(periodRoute, "utf8");

  assert.match(source, /PeriodAdjustmentsWorkspace/, "period route should import the focused adjustments workspace");
  assert.match(source, /<PeriodAdjustmentsWorkspace[\s\S]*adjustments=\{adjustments\}/, "period route should render PeriodAdjustmentsWorkspace with adjustment data");
  assert.match(source, /<PeriodAdjustmentsWorkspace[\s\S]*onGenerateAdjustments=\{handleGenerateAdjustments\}/, "period route should pass generation orchestration into PeriodAdjustmentsWorkspace");
  assert.match(source, /<PeriodAdjustmentsWorkspace[\s\S]*onApproveAdjustment=\{handleApproveAdjustment\}/, "period route should pass approval orchestration into PeriodAdjustmentsWorkspace");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Period Adjustments<\/Card\.Title>/, "period route should not own adjustment action-card markup");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Adjustment Summary<\/Card\.Title>/, "period route should not own adjustment summary markup");
  assert.doesNotMatch(source, /<Card\.Title[^>]*>Adjustment Details<\/Card\.Title>/, "period route should not own adjustment detail markup");
});

test("period adjustments workspace composes generation, summary, filters and review cards", () => {
  const componentFile = new URL("../src/components/period/PeriodAdjustmentsWorkspace.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "PeriodAdjustmentsWorkspace should exist as the focused adjustments workflow component");

  const source = readFileSync(componentFile, "utf8");
  for (const marker of [
    "Period Adjustments",
    "Generate Adjustments",
    "Adjustment Summary",
    "Adjustment Details",
    "Approval status",
    "Apply Filters",
    "Pending Approval",
    "No adjustments yet",
  ]) {
    assert.match(source, new RegExp(marker), `PeriodAdjustmentsWorkspace should render ${marker}`);
  }
});

test("period statements route delegates readiness and downstream statement navigation", () => {
  const periodRoute = new URL("companies/[companyId]/periods/[periodId]/page.tsx", appDir);
  const source = readFileSync(periodRoute, "utf8");

  assert.match(source, /PeriodStatementsWorkspace/, "period route should import the focused statements workspace");
  assert.match(source, /<PeriodStatementsWorkspace[\s\S]*readiness=\{readiness\}/, "period route should render PeriodStatementsWorkspace with readiness data");
  assert.match(source, /<PeriodStatementsWorkspace[\s\S]*isCharity=\{Boolean\(company\?\.isCharitableOrganisation\)\}/, "period route should pass charity reporting eligibility into PeriodStatementsWorkspace");
  assert.match(source, /<PeriodStatementsWorkspace[\s\S]*statementsHref=/, "period route should pass statements navigation into PeriodStatementsWorkspace");
  assert.doesNotMatch(source, /<StatementsReadinessPanel readiness=\{readiness\} \/>/, "period route should not own statements readiness markup directly");
  assert.doesNotMatch(source, /View Financial Statements/, "period route should not own statements navigation markup");
  assert.doesNotMatch(source, /Charity Reporting \(SoFA\)/, "period route should not own charity reporting navigation markup");
});

test("period statements workspace composes readiness, notes and charity navigation", () => {
  const componentFile = new URL("../src/components/period/PeriodStatementsWorkspace.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "PeriodStatementsWorkspace should exist as the focused statements workflow component");

  const source = readFileSync(componentFile, "utf8");
  for (const marker of [
    "StatementsReadinessPanel",
    "View Financial Statements",
    "Manage Notes",
    "Charity Reporting \\(SoFA\\)",
    "Open Charity Reporting",
  ]) {
    assert.match(source, new RegExp(marker), `PeriodStatementsWorkspace should render ${marker}`);
  }
});
