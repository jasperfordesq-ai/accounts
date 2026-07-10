import { afterEach, describe, expect, it } from "vitest";
import { createDebtor, getPeriod } from "@/lib/api";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

afterEach(() => {
  clearCookies();
});

describe("period accounting concurrency", () => {
  it("sends the last successfully-read ETag and keeps stale writes blocked until reload", async () => {
    let readCount = 0;
    const requests = installFetchMock((request) => {
      if (request.method === "GET") {
        readCount += 1;
        const etag = readCount === 1 ? '"period-v1"' : '"period-v2"';
        return {
          status: 200,
          body: {
            id: 3030,
            companyId: 4242,
            periodStart: "2025-01-01",
            periodEnd: "2025-12-31",
            status: "Draft",
            isFirstYear: false,
            memberAuditNoticeReceived: false,
            goingConcernConfirmed: true,
          },
          headers: { ETag: etag },
        };
      }

      const supplied = request.headers.get("If-Match");
      if (supplied === '"period-v1"') {
        return {
          status: 409,
          body: {
            error: "Accounting data changed. Reload and reconcile.",
            reloadRequired: true,
            reconcileRequired: true,
          },
          headers: { ETag: '"period-v2"' },
        };
      }

      return { status: 201, body: { id: 99 }, headers: { ETag: '"period-v3"' } };
    });
    setCsrfCookie("period-etag-csrf");

    await getPeriod(4242, 3030);
    const debtor = { name: "Trade debtor", amount: 1250, type: "Trade" };
    await expect(createDebtor(4242, 3030, debtor)).rejects.toMatchObject({ status: 409 });
    await expect(createDebtor(4242, 3030, debtor)).rejects.toMatchObject({ status: 409 });

    const blockedWrites = requests.byMethod("POST");
    expect(blockedWrites).toHaveLength(2);
    expect(blockedWrites[0]?.headers.get("If-Match")).toBe('"period-v1"');
    expect(blockedWrites[1]?.headers.get("If-Match")).toBe('"period-v1"');

    await getPeriod(4242, 3030);
    await createDebtor(4242, 3030, debtor);
    const reconciledWrite = requests.byMethod("POST")[2];
    expect(reconciledWrite?.headers.get("If-Match")).toBe('"period-v2"');
  });
});
