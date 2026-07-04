import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";

async function loadLayoutModule() {
  try {
    return await import("../scripts/visual-smoke-layout.mjs");
  } catch (error) {
    assert.fail(`visual-smoke-layout module should exist: ${error instanceof Error ? error.message : String(error)}`);
  }
}

describe("visual smoke layout checks", () => {
  it("flags visible text blocks that overlap beyond tolerance", async () => {
    const { findOverlappingTextBlocks } = await loadLayoutModule();

    const issues = findOverlappingTextBlocks([
      textBlock("Primary CTA", "Review", 10, 10, 110, 40),
      textBlock("Deadline badge", "Overdue", 90, 20, 160, 50),
    ]);

    assert.equal(issues.length, 1);
    assert.match(issues[0].message, /Primary CTA/);
    assert.match(issues[0].message, /Deadline badge/);
  });

  it("ignores adjacent text and tiny edge contact", async () => {
    const { findOverlappingTextBlocks } = await loadLayoutModule();

    const issues = findOverlappingTextBlocks([
      textBlock("Left label", "Turnover", 10, 10, 110, 40),
      textBlock("Right value", "EUR 42,000", 111, 10, 210, 40),
      textBlock("Small touch", "Ready", 208, 38, 250, 60),
    ]);

    assert.equal(issues.length, 0);
  });

  it("ignores duplicated accessibility text at identical coordinates", async () => {
    const { findOverlappingTextBlocks } = await loadLayoutModule();

    const issues = findOverlappingTextBlocks([
      textBlock("Tab label", "Categorise", 40, 40, 100, 58),
      textBlock("Tab label duplicate", "Categorise", 40, 40, 100, 58),
      textBlock("Selected tab mirror", "Statements", 140, 40, 205, 58),
      textBlock("Selected tab mirror duplicate", "Statements", 142, 42, 207, 60),
    ]);

    assert.equal(issues.length, 0);
  });

  it("summarizes layout issues for route failures", async () => {
    const { findOverlappingTextBlocks, formatLayoutIssues } = await loadLayoutModule();
    const issues = findOverlappingTextBlocks([
      textBlock("Route title", "Dashboard", 10, 10, 120, 42),
      textBlock("Status rail", "Blocked", 80, 18, 160, 52),
    ]);

    const message = formatLayoutIssues("dashboard/light/mobile", issues);

    assert.match(message, /dashboard\/light\/mobile/);
    assert.match(message, /text layout overlap/);
    assert.match(message, /Route title/);
  });

  it("uses text-range rectangles in the smoke browser collector", async () => {
    const script = await readFile(new URL("../scripts/visual-smoke.mjs", import.meta.url), "utf8");

    assert.match(script, /document\.createTreeWalker/);
    assert.match(script, /range\.getClientRects/);
    assert.match(script, /details:not\(\[open\]\)/);
  });
});

function textBlock(label, text, left, top, right, bottom) {
  return {
    label,
    text,
    rect: {
      left,
      top,
      right,
      bottom,
      width: right - left,
      height: bottom - top,
    },
  };
}
