import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

describe("period workbench tab routing", () => {
  it("supports workflow rail deep links through a controlled tab query parameter", async () => {
    const source = await readFile(
      new URL("../src/app/companies/[companyId]/periods/[periodId]/page.tsx", import.meta.url),
      "utf8",
    );

    assert.match(source, /useSearchParams/);
    assert.match(source, /const \[selectedWorkspaceTab,\s*setSelectedWorkspaceTab\]\s*=\s*useState/);
    assert.match(source, /setSelectedWorkspaceTab\(normaliseWorkspaceTab\(searchParams\.get\("tab"\)\)\)/);
    assert.match(source, /selectedKey=\{selectedWorkspaceTab\}/);
    assert.match(source, /onSelectionChange=\{handleWorkspaceTabChange\}/);
  });
});
