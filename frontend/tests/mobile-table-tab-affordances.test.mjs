import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

const frontendRoot = fileURLToPath(new URL("..", import.meta.url));
const sourceRoot = path.join(frontendRoot, "src");

describe("mobile table, grid and tab affordances", () => {
  it("keeps categorisation transactions semantic and exposes every named mobile field", async () => {
    const source = await readFile(
      path.join(sourceRoot, "components", "period", "PeriodCategoriseWorkspace.tsx"),
      "utf8",
    );

    assert.match(source, /<DataGrid[\s\S]*caption="Transactions to categorise"/);
    assert.match(source, /mobilePresentation="cards"/);
    for (const marker of [
      '"Date"',
      '"Description"',
      '"Amount and entry"',
      '"Category"',
      '"Confidence"',
      '"Debit"',
      '"Credit"',
    ]) {
      assert.ok(source.includes(marker), `categorisation mobile rows should retain ${marker}`);
    }
    assert.doesNotMatch(source, /className="grid grid-cols-12[^\n]*"/);
    assert.match(source, /<ul[\s\S]*aria-label="Transaction rules"/);
  });

  it("uses labelled card rows for officer and period actions", async () => {
    const officerSource = await readFile(
      path.join(sourceRoot, "components", "company", "CompanyOfficersPanel.tsx"),
      "utf8",
    );
    const periodSource = await readFile(
      path.join(sourceRoot, "components", "company", "CompanyPeriodsWorkbench.tsx"),
      "utf8",
    );

    assert.match(officerSource, /columns=\{\["Officer", "Role", "Status", "Actions"\]\}[\s\S]*mobilePresentation="cards"/);
    assert.match(periodSource, /columns=\{\["Period", "Status", "Size and regime", "Evidence cues", "Next action"\]\}[\s\S]*mobilePresentation="cards"/);
  });

  it("advertises tab overflow and preserves keyboard tab semantics on all material tab sets", async () => {
    const periodSource = await readFile(
      path.join(sourceRoot, "components", "period", "PeriodWorkspaceRoute.tsx"),
      "utf8",
    );
    const statementSource = await readFile(
      path.join(sourceRoot, "components", "statements", "FinancialStatementsWorkbench.tsx"),
      "utf8",
    );
    const charitySource = await readFile(
      path.join(sourceRoot, "app", "companies", "[companyId]", "periods", "[periodId]", "charity", "page.tsx"),
      "utf8",
    );

    for (const [label, source] of [
      ["period", periodSource],
      ["statement", statementSource],
      ["charity", charitySource],
    ]) {
      assert.match(source, /data-overflow-tablist="true"/, `${label} tabs should expose overflow`);
      assert.match(source, /Swipe to reveal more/, `${label} tabs should show touch instructions`);
      assert.match(source, /Left and Right Arrow keys/, `${label} tabs should show keyboard instructions`);
    }

    assert.match(charitySource, /role="tablist"/);
    assert.match(charitySource, /role="tab"/);
    assert.match(charitySource, /role="tabpanel"/);
    assert.match(charitySource, /aria-selected=\{activeTab === tab\.id\}/);
    assert.match(charitySource, /event\.key === "ArrowRight"/);
    assert.match(charitySource, /event\.key === "ArrowLeft"/);
    assert.match(charitySource, /event\.key === "Home"/);
    assert.match(charitySource, /event\.key === "End"/);
  });

  it("does not retain an unlabelled horizontal scroller in frontend source", async () => {
    const files = await sourceFiles(sourceRoot);
    const offenders = [];

    for (const file of files.filter((item) => item.endsWith(".tsx"))) {
      const source = await readFile(file, "utf8");
      for (const match of source.matchAll(/overflow-x-auto/g)) {
        const index = match.index ?? 0;
        const context = source.slice(Math.max(0, index - 600), index + 700);
        if (!/data-(?:overflow-tablist|horizontal-scroll-region|workbench-table-shell)=/.test(context)) {
          offenders.push(path.relative(sourceRoot, file));
        }
      }
    }

    assert.deepEqual(offenders, [], `unlabelled horizontal scrollers: ${offenders.join(", ")}`);
  });
});

async function sourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.map(async (entry) => {
    const item = path.join(directory, entry.name);
    return entry.isDirectory() ? sourceFiles(item) : [item];
  }));
  return nested.flat();
}
