import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

describe("workbench responsive table CSS", () => {
  it("turns dense workbench tables into labelled mobile rows", async () => {
    const css = await readFile(new URL("../src/app/globals.css", import.meta.url), "utf8");

    assert.match(css, /@media\s*\(max-width:\s*767px\)/);
    assert.match(css, /\.workbench-data-table/);
    assert.match(css, /td::before/);
    assert.match(css, /attr\(data-label\)/);
    assert.match(css, /\.workbench-data-table tbody td > \*/);
    assert.match(css, /\.workbench-data-table tfoot/);
    assert.match(css, /\.workbench-data-table tfoot td::before/);
    assert.match(css, /min-width:\s*0/);
  });

  it("keeps the filing action bar in normal flow on mobile to avoid sticky text overlap", async () => {
    const source = await readFile(new URL("../src/components/workbench.tsx", import.meta.url), "utf8");
    const match = source.match(/function FilingActionBar[\s\S]*?className="([^"]+)"/);

    assert.ok(match, "FilingActionBar should render a className that can be inspected.");
    assert.match(match[1], /\bsm:sticky\b/);
    assert.doesNotMatch(match[1], /(^|\s)sticky(\s|$)/);
  });
});
