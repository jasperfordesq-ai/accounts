import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

describe("period workspace responsive layout", () => {
  it("keeps the opening balance ledger from rendering cramped mobile headers", async () => {
    const source = await readFile(
      new URL("../src/app/companies/[companyId]/periods/[periodId]/page.tsx", import.meta.url),
      "utf8",
    );

    assert.match(source, /hidden grid-cols-12[\s\S]*Account[\s\S]*Debit[\s\S]*Credit[\s\S]*Evidence[\s\S]*Action/);
    assert.match(source, /md:grid-cols-12/);
    assert.match(source, /md:hidden">\s*Debit\s*<\/span>/);
    assert.match(source, /md:hidden">\s*Credit\s*<\/span>/);
    assert.match(source, /md:hidden">\s*Evidence\s*<\/span>/);
    assert.match(source, /md:hidden">\s*Action\s*<\/span>/);

    assert.match(source, /hidden grid-cols-12[\s\S]*Account[\s\S]*Currency[\s\S]*Opening balance[\s\S]*Import target/);
    assert.match(source, /md:hidden">\s*Currency\s*<\/span>/);
    assert.match(source, /md:hidden">\s*Opening balance\s*<\/span>/);
    assert.match(source, /md:hidden">\s*Import target\s*<\/span>/);
  });
});
