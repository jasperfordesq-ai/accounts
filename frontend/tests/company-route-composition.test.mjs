import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

test("company detail route renders the company command centre workbench overview", () => {
  const source = readFileSync(
    new URL("../src/app/companies/[companyId]/page.tsx", import.meta.url),
    "utf8",
  );

  assert.match(source, /CompanyDetailWorkbench/);
  assert.match(source, /<CompanyDetailWorkbench[\s\S]*company=\{company\}/);
  assert.doesNotMatch(source, /<CompanyWorkspaceOverview\s+company=\{company\}/);
});

test("company detail workbench owns the shared page shell and company panels", () => {
  const componentSource = readFileSync(
    new URL("../src/components/company/CompanyDetailWorkbench.tsx", import.meta.url),
    "utf8",
  );

  assert.match(componentSource, /PageShell/);
  assert.match(componentSource, /CompanyWorkspaceOverview/);
  assert.match(componentSource, /CompanyStatutoryProfile/);
  assert.match(componentSource, /CompanyOfficersPanel/);
  assert.match(componentSource, /CompanyPeriodsWorkbench/);
  assert.match(componentSource, /ShareCapitalCard/);
});
