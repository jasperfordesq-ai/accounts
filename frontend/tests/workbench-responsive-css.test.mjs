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
    assert.match(css, /min-width:\s*0/);
  });
});
