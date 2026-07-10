import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  createLatestRequestSequence,
  enumSearchParam,
  numericIdentifierSearchParam,
  patchSearchHref,
  positiveIntegerSearchParam,
} from "../src/lib/interactionState.ts";

describe("interaction URL state", () => {
  it("preserves unrelated deep-link state while adding, changing and clearing supported filters", () => {
    const first = patchSearchHref(
      "/companies/4/periods/9",
      "?returnTo=%2F&tab=categorise&txStatus=uncategorised",
      { txCategory: 17, txPage: 2 },
    );
    assert.equal(
      first,
      "/companies/4/periods/9?returnTo=%2F&tab=categorise&txStatus=uncategorised&txCategory=17&txPage=2",
    );

    const cleared = patchSearchHref("/companies/4/periods/9", new URL(first, "https://accounts.test").searchParams, {
      txStatus: null,
      txPage: null,
    });
    assert.equal(cleared, "/companies/4/periods/9?returnTo=%2F&tab=categorise&txCategory=17");
  });

  it("rejects unsupported, malformed and out-of-range restored state", () => {
    const params = new URLSearchParams("tab=unknown&txPage=-2&txPageSize=75&txCategory=abc&txBank=0");
    assert.equal(enumSearchParam(params, "tab", new Set(["import", "categorise"]), "import"), "import");
    assert.equal(positiveIntegerSearchParam(params, "txPage", 1), 1);
    assert.equal(positiveIntegerSearchParam(params, "txPageSize", 50, new Set([25, 50, 100])), 50);
    assert.equal(numericIdentifierSearchParam(params, "txCategory"), "");
    assert.equal(numericIdentifierSearchParam(params, "txBank"), "");
  });
});
describe("latest-request sequencing", () => {
  it("invalidates a slow earlier response as soon as a newer request begins", () => {
    const sequence = createLatestRequestSequence();
    const slow = sequence.begin();
    const newest = sequence.begin();

    assert.equal(slow.isLatest(), false);
    assert.equal(newest.isLatest(), true);

    sequence.invalidate();
    assert.equal(newest.isLatest(), false);
  });
});
