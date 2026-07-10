import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const css = fs.readFileSync(path.join(here, "../src/app/globals.css"), "utf8");

function reducedMotionBlock(source) {
  const marker = "@media (prefers-reduced-motion: reduce)";
  const start = source.indexOf(marker);
  assert.notEqual(start, -1, "globals.css must define a reduced-motion media query");

  let depth = 0;
  let opened = false;
  for (let index = start; index < source.length; index += 1) {
    if (source[index] === "{") {
      opened = true;
      depth += 1;
    } else if (source[index] === "}") {
      depth -= 1;
      if (opened && depth === 0) return source.slice(start, index + 1);
    }
  }
  assert.fail("reduced-motion media query must be balanced");
}

test("reduced motion disables application and utility animations", () => {
  const block = reducedMotionBlock(css);

  for (const className of [
    ".skeleton",
    ".skeleton-shimmer",
    ".animate-fade-in",
    ".animate-slide-down",
    ".animate-backdrop",
    ".animate-spin",
  ]) {
    assert.match(block, new RegExp(className.replace(".", String.raw`\.`)));
  }
  assert.match(block, /animation:\s*none\s*!important/);
  assert.match(block, /animation-iteration-count:\s*1\s*!important/);
});

test("reduced motion disables smooth scrolling, transitions, and hover lift", () => {
  const block = reducedMotionBlock(css);

  assert.match(block, /scroll-behavior:\s*auto\s*!important/);
  assert.match(block, /transition-duration:\s*0\.01ms\s*!important/);
  assert.match(block, /\.card-hover:hover\s*\{[^}]*transform:\s*none\s*!important/s);
});
