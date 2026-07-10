import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { parseProductionReadinessReport as parseFromApi } from "../src/lib/api.ts";
import { parseProductionReadinessReport as parseFromContract } from "../src/lib/productionReadinessContract.ts";

const lineCount = (source) => source.split(/\r?\n/u).length;

test("the API facade preserves the readiness parser while the invariant set stays isolated", async () => {
  assert.equal(parseFromApi, parseFromContract);

  const [api, readinessContract] = await Promise.all([
    readFile(new URL("../src/lib/api.ts", import.meta.url), "utf8"),
    readFile(new URL("../src/lib/productionReadinessContract.ts", import.meta.url), "utf8"),
  ]);

  assert.ok(lineCount(api) < 3_500, `api.ts grew back to ${lineCount(api)} lines`);
  assert.ok(lineCount(readinessContract) < 4_100, "readiness contract exceeded its focused-module budget");
  assert.match(api, /export \* from "\.\/productionReadinessContract\.ts";/u);
});
