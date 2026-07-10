import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const srcRoot = path.join(here, "../src");

const requiredSurfaces = [
  "app/companies/new/page.tsx",
  "app/companies/[companyId]/periods/[periodId]/charity/page.tsx",
  "app/companies/[companyId]/periods/[periodId]/notes/page.tsx",
  "components/DirectorLoanEvidenceForm.tsx",
  "components/DirectorLoansManager.tsx",
  "components/LoansManager.tsx",
  "components/ShareCapitalCard.tsx",
  "components/company/CompanyOfficersPanel.tsx",
  "components/period/CorporationTaxFilingSupportPanel.tsx",
  "components/period/PeriodCategoriseWorkspace.tsx",
  "components/period/PeriodImportWorkspace.tsx",
  "components/period/YearEndContingentLiabilitiesSection.tsx",
  "components/period/YearEndDividendsSection.tsx",
  "components/period/YearEndFixedAssetsSection.tsx",
  "components/period/YearEndInventorySection.tsx",
  "components/period/YearEndMoneyListSection.tsx",
  "components/period/YearEndPostBalanceSheetEventsSection.tsx",
  "components/period/YearEndRelatedPartyTransactionsSection.tsx",
];

function sourceFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return sourceFiles(fullPath);
    return entry.name.endsWith(".tsx") ? [fullPath] : [];
  });
}

test("every audited destructive surface uses the shared guard or an explicit accessible ConfirmModal", () => {
  const missing = requiredSurfaces.filter((relative) => {
    const source = fs.readFileSync(path.join(srcRoot, relative), "utf8");
    return !/requestDestructiveAction|<ConfirmModal\b/.test(source);
  });
  assert.deepEqual(missing, [], `unguarded audited destructive surfaces:\n${missing.join("\n")}`);
});

test("new Delete/Remove controls cannot appear without a confirmation implementation in the same surface", () => {
  const offenders = [];
  for (const file of sourceFiles(srcRoot)) {
    const source = fs.readFileSync(file, "utf8");
    if (!/aria-label=\{?`?(?:Delete|Remove)\b/.test(source)) continue;
    if (/requestDestructiveAction|<ConfirmModal\b/.test(source)) continue;
    offenders.push(path.relative(srcRoot, file).replaceAll("\\", "/"));
  }
  assert.deepEqual(offenders, [], `Delete/Remove controls without confirmation:\n${offenders.join("\n")}`);
});

test("the shared guard names consequences, preserves cancel/failure, and announces outcomes", () => {
  const guard = fs.readFileSync(path.join(srcRoot, "lib/useDestructiveAction.tsx"), "utf8");
  assert.match(guard, /title=\{pending \? `Remove \$\{pending\.recordLabel\}\?`/);
  assert.match(guard, /description=\{pending\?\.consequence/);
  assert.match(guard, /Removal cancelled\./);
  assert.match(guard, /retained record is unchanged/);
  assert.match(guard, /role="status"/);
  assert.match(guard, /dialogRole="alertdialog"/);
});
