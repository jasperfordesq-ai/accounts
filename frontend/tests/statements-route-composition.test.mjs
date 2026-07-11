import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { test } from "node:test";

test("financial statements route delegates preview UI to a focused workbench component", () => {
  const routeSource = readFileSync(
    new URL("../src/app/companies/[companyId]/periods/[periodId]/statements/page.tsx", import.meta.url),
    "utf8",
  );

  assert.match(routeSource, /FinancialStatementsWorkbench/);
  assert.match(routeSource, /<FinancialStatementsWorkbench[\s\S]*company=\{company\}/);
  assert.match(routeSource, /<FinancialStatementsWorkbench[\s\S]*trialBalance=\{trialBalance\}/);
  assert.match(routeSource, /<FinancialStatementsWorkbench[\s\S]*onRetry=\{\(\) => loadShell\(shellState\.failedResourceKeys\)\}/);
  assert.match(routeSource, /<FinancialStatementsWorkbench[\s\S]*onRetryStatements=\{\(\) => loadStatements\(statementState\.failedResourceKeys\)\}/);
  assert.match(routeSource, /canLoadDirectorsReport[\s\S]*\? getDirectorsReportData\(cId, pId\)[\s\S]*: Promise\.resolve\(null\)/,
    "the route must not issue a predictably failing directors-report request before a filing regime exists");
  assert.doesNotMatch(routeSource, /Promise\.all\(\[loadShell\(\), loadStatements\(\)\]\)/,
    "statement reads should wait for the authorized period and its filing-regime prerequisite");
  assert.match(routeSource, /periodMatchesRoute = period\?\.id === pId && period\.companyId === cId/,
    "statement reads must not reuse stale period state after dynamic-route navigation");
  assert.match(routeSource, /if \(!periodMatchesRoute\) return;[\s\S]*void loadStatements\(\)/,
    "statement reads should begin only after the period shell resolves for the active route");
  assert.doesNotMatch(routeSource, /<TabsRoot>/);
  assert.doesNotMatch(routeSource, /Financial statements tabs/);
  assert.doesNotMatch(routeSource, /<table className="w-full text-sm/);
});

test("financial statements workbench owns the shared shell, print action and statement tabs", () => {
  const componentFile = new URL("../src/components/statements/FinancialStatementsWorkbench.tsx", import.meta.url);

  assert.ok(existsSync(componentFile), "FinancialStatementsWorkbench should exist as the focused statements preview component");

  const componentSource = readFileSync(componentFile, "utf8");

  assert.match(componentSource, /PageShell/);
  assert.match(componentSource, /TabsRoot/);
  assert.match(componentSource, /Financial statements tabs/);
  assert.match(componentSource, /Trial Balance/);
  assert.match(componentSource, /Source Trail/);
  assert.match(componentSource, /Directors&apos; Report/);
  assert.match(componentSource, /window\.print\(\)/);
});
