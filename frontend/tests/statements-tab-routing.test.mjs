import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  FINANCIAL_STATEMENT_TAB_ID_SET,
  FINANCIAL_STATEMENT_TAB_IDS,
} from "../src/lib/statementTabs.ts";
import { enumSearchParam, patchSearchHref } from "../src/lib/interactionState.ts";

const statementsRoute = new URL(
  "../src/app/companies/[companyId]/periods/[periodId]/statements/page.tsx",
  import.meta.url,
);

test("statement tabs round-trip through a validated same-route URL", async () => {
  assert.equal(FINANCIAL_STATEMENT_TAB_IDS.length, 8);
  assert.equal(
    enumSearchParam(
      new URLSearchParams("statementTab=tax-computation"),
      "statementTab",
      FINANCIAL_STATEMENT_TAB_ID_SET,
      "trial-balance",
    ),
    "tax-computation",
  );
  assert.equal(
    enumSearchParam(
      new URLSearchParams("statementTab=unknown"),
      "statementTab",
      FINANCIAL_STATEMENT_TAB_ID_SET,
      "trial-balance",
    ),
    "trial-balance",
  );
  assert.equal(
    patchSearchHref("/companies/7/periods/3/statements", "?returnTo=%2F", {
      statementTab: "directors-report",
    }),
    "/companies/7/periods/3/statements?returnTo=%2F&statementTab=directors-report",
  );

  const source = await readFile(statementsRoute, "utf8");
  assert.match(source, /usePathname, useRouter, useSearchParams/);
  assert.match(source, /router\.push\(patchSearchHref\(pathname, currentSearch/);
  assert.match(source, /statementTab: tab === "trial-balance" \? null : tab/);
  assert.match(source, /const request = statementRequestSequence\.begin\(\)[\s\S]*?await loadResourceGroup[\s\S]*?if \(!request\.isLatest\(\)\) return/);
});
