import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

describe("workbench responsive table CSS", () => {
  it("turns dense workbench tables into labelled mobile rows", async () => {
    const css = await readFile(new URL("../src/app/globals.css", import.meta.url), "utf8");

    assert.match(css, /@media\s*\(max-width:\s*767px\)/);
    assert.match(css, /data-responsive="card"/);
    assert.match(css, /td::before/);
    assert.match(css, /attr\(data-label\)/);
    assert.match(css, /data-workbench-table-shell="true"/);
    assert.match(css, /data-responsive="scroll"/);
    assert.match(css, /data-horizontal-scroll-region="true"/);
    assert.match(css, /position:\s*sticky/);
    assert.match(css, /scroll-padding-inline:\s*1rem/);
    assert.match(css, /min-width:\s*0/);
  });

  it("stacks fixed twelve-column editor grids into usable single-column mobile forms", async () => {
    const css = await readFile(new URL("../src/app/globals.css", import.meta.url), "utf8");
    const loanSource = await readFile(new URL("../src/components/LoansManager.tsx", import.meta.url), "utf8");
    const yearEndSource = await readFile(new URL("../src/components/period/YearEndFixedAssetsSection.tsx", import.meta.url), "utf8");

    assert.match(css, /\.mobile-form-grid\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)/);
    assert.match(css, /\.mobile-form-grid\s*>\s*\*[\s\S]*grid-column:\s*1\s*\/\s*-1/);
    assert.match(loanSource, /mobile-form-grid grid grid-cols-12/);
    assert.match(yearEndSource, /mobile-form-grid grid grid-cols-12/);
  });

  it("keeps the filing action bar in normal flow to avoid covering evidence text", async () => {
    const source = await readFile(new URL("../src/components/workbench.tsx", import.meta.url), "utf8");
    const match = source.match(/function FilingActionBar[\s\S]*?className="([^"]+)"/);

    assert.ok(match, "FilingActionBar should render a className that can be inspected.");
    assert.doesNotMatch(match[1], /\bsm:sticky\b/);
    assert.doesNotMatch(match[1], /\bsm:bottom-4\b/);
    assert.doesNotMatch(match[1], /(^|\s)sticky(\s|$)/);
  });
});
