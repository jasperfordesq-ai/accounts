import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

const periodRoute = new URL("../src/components/period/PeriodWorkspaceRoute.tsx", import.meta.url);
const periodQuery = new URL("../src/lib/periodWorkspaceQuery.ts", import.meta.url);
const charityRoute = new URL("../src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx", import.meta.url);

describe("supported route interaction state", () => {
  it("round-trips period tabs and transaction/adjustment filters through navigation history", async () => {
    const source = `${await readFile(periodRoute, "utf8")}\n${await readFile(periodQuery, "utf8")}`;

    assert.match(source, /usePathname, useRouter, useSearchParams/);
    assert.match(source, /router\.push\(patchSearchHref\(pathname, currentSearch, patch\), \{ scroll: false \}\)/);
    for (const key of [
      "tab", "txStatus", "txCategory", "txBank", "txSearch", "txPage", "txPageSize",
      "txSort", "txDirection", "adjApproval", "adjSource",
    ]) {
      assert.match(source, new RegExp(`(?:get\\(\\"${key}\\"\\)|${key}:)`), `${key} must be restored or written`);
    }
    assert.match(source, /setSelectedWorkspaceTab\(query\.selectedWorkspaceTab\)/);
  });

  it("round-trips charity tabs and uses the same focus, announcement and request-order controls", async () => {
    const source = await readFile(charityRoute, "utf8");

    assert.match(source, /normaliseCharityTab\(searchParams\.get\("tab"\)\)/);
    assert.match(source, /selectCharityTab/);
    assert.match(source, /patchSearchHref\(pathname, currentSearch, \{ tab:/);
    assert.match(source, /useLatestRequestSequence/);
    assert.match(source, /captureInteractionFocus/);
    assert.match(source, /<InteractionAnnouncement announcement=\{announcement\}/);
  });

  it("guards overlapping period loads before any older result mutates retained state", async () => {
    const source = await readFile(periodRoute, "utf8");
    assert.match(source, /const request = transactionRequestSequence\.begin\(\)[\s\S]*?await getTransactions[\s\S]*?if \(!request\.isLatest\(\)\) return/);
    assert.match(source, /const request = adjustmentRequestSequence\.begin\(\)[\s\S]*?await Promise\.all[\s\S]*?if \(!request\.isLatest\(\)\) return/);
    assert.match(source, /const request = auditRequestSequence\.begin\(\)[\s\S]*?await getAuditLog[\s\S]*?if \(!request\.isLatest\(\)\) return/);
  });
});
