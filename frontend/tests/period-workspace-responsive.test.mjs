import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

describe("period workspace responsive layout", () => {
  it("keeps the opening balance ledger from rendering cramped mobile headers", async () => {
    const source = await readFile(
      new URL("../src/components/period/PeriodImportWorkspace.tsx", import.meta.url),
      "utf8",
    );

    assert.match(source, /hidden grid-cols-12[\s\S]*Account[\s\S]*Debit[\s\S]*Credit[\s\S]*Evidence[\s\S]*Action/);
    assert.match(source, /md:grid-cols-12/);
    assert.match(source, /MobileField label="Debit"/);
    assert.match(source, /MobileField label="Credit"/);
    assert.match(source, /MobileField label="Evidence"/);
    assert.match(source, /MobileField label="Action"/);

    assert.match(source, /hidden grid-cols-12[\s\S]*Account[\s\S]*Currency[\s\S]*Opening balance[\s\S]*Import target/);
    assert.match(source, /MobileField label="Currency"/);
    assert.match(source, /MobileField label="Opening balance"/);
    assert.match(source, /MobileField label="Import target"/);
    assert.match(source, /md:hidden">\{label\}<\/span>/);
  });
});
