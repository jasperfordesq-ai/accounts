import assert from "node:assert/strict";
import test from "node:test";

import {
  ApiContractError,
  generateAccountantWorkingPaperPack,
  getAccountantWorkingPaperPack,
} from "../src/lib/api.ts";
import { accountantWorkingPaperPackFixture } from "./fixtures/accountant-working-paper-pack.ts";

function response(body) {
  return {
    ok: true,
    status: 200,
    statusText: "OK",
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  };
}

test("working-paper API client uses explicit retained GET and generation POST contracts", async () => {
  const requests = [];
  global.fetch = async (input, init = {}) => {
    requests.push({ path: String(input), method: init.method ?? "GET", body: init.body });
    return response(structuredClone(accountantWorkingPaperPackFixture));
  };

  const retained = await getAccountantWorkingPaperPack(7, 3);
  const generated = await generateAccountantWorkingPaperPack(7, 3);

  assert.equal(retained.outputKind, "internal-accountant-working-paper-pack");
  assert.equal(generated.directSubmissionSupported, false);
  assert.deepEqual(requests.map(({ path, method }) => ({ path, method })), [
    { path: "/api/companies/7/periods/3/working-papers", method: "GET" },
    { path: "/api/companies/7/periods/3/working-papers/generate", method: "POST" },
  ]);
});

test("working-paper API client rejects a plausible filing-artifact claim", async () => {
  const malformed = structuredClone(accountantWorkingPaperPackFixture);
  malformed.isFilingArtifact = true;
  global.fetch = async () => response(malformed);

  await assert.rejects(
    () => getAccountantWorkingPaperPack(7, 3),
    (error) => error instanceof ApiContractError
      && error.contract === "retained internal accountant working-paper pack",
  );
});
