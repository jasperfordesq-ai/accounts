import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

test("company detail route renders the company command centre workbench overview", () => {
  const source = readFileSync(
    new URL("../src/app/companies/[companyId]/page.tsx", import.meta.url),
    "utf8",
  );

  assert.match(source, /CompanyWorkspaceOverview/);
  assert.match(source, /<CompanyWorkspaceOverview\s+company=\{company\}/);
});
