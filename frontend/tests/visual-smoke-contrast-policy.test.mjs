import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  MIN_LARGE_TEXT_CONTRAST_RATIO,
  MIN_NORMAL_TEXT_CONTRAST_RATIO,
  MIN_UI_COMPONENT_CONTRAST_RATIO,
  passedVisualSmokeContrastResult,
} from "../scripts/visual-smoke-plan.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const visualSmokeSource = fs.readFileSync(path.join(here, "../scripts/visual-smoke.mjs"), "utf8");

test("contrast evidence carries the WCAG AA thresholds and sample families", () => {
  const result = passedVisualSmokeContrastResult({
    sampledTextCount: 9,
    sampledNormalTextCount: 5,
    sampledLargeTextCount: 1,
    sampledInteractiveTextCount: 2,
    sampledPlaceholderCount: 1,
    sampledUiComponentCount: 3,
    sampledGradientTextCount: 2,
    minimumContrastRatio: 3.2,
    minimumNormalTextContrastRatio: 4.7,
    minimumLargeTextContrastRatio: 3.4,
    minimumUiComponentContrastRatio: 3.2,
  });

  assert.equal(result.requiredNormalTextContrastRatio, MIN_NORMAL_TEXT_CONTRAST_RATIO);
  assert.equal(result.requiredLargeTextContrastRatio, MIN_LARGE_TEXT_CONTRAST_RATIO);
  assert.equal(result.requiredUiComponentContrastRatio, MIN_UI_COMPONENT_CONTRAST_RATIO);
  assert.equal(result.sampledNormalTextCount, 5);
  assert.equal(result.sampledLargeTextCount, 1);
  assert.equal(result.sampledInteractiveTextCount, 2);
  assert.equal(result.sampledPlaceholderCount, 1);
  assert.equal(result.sampledUiComponentCount, 3);
  assert.equal(result.sampledGradientTextCount, 2);
  assert.equal(result.minimumNormalTextContrastRatio, 4.7);
  assert.equal(result.minimumLargeTextContrastRatio, 3.4);
  assert.equal(result.minimumUiComponentContrastRatio, 3.2);
  assert.equal(result.failingTextCount, 0);
  assert.equal(result.failingUiComponentCount, 0);
});

test("browser contrast collection includes interactive, placeholder, disabled, and gradient states", () => {
  assert.doesNotMatch(visualSmokeSource, /closest\("a, button, \[role='button'\]"\)\) return NodeFilter\.FILTER_REJECT/);
  assert.match(visualSmokeSource, /"a\[href\]"/);
  assert.match(visualSmokeSource, /"\[role='tab'\]"/);
  assert.match(visualSmokeSource, /getComputedStyle\(element, "::placeholder"\)/);
  assert.match(visualSmokeSource, /matches\(":disabled, \[aria-disabled='true'\]"\)/);
  assert.match(visualSmokeSource, /gradientColors\(style\.backgroundImage\)/);
  assert.match(visualSmokeSource, /interactive control has no machine-verifiable visual boundary/);
  assert.match(visualSmokeSource, /\[data-contrast-boundary\]/);
  assert.match(visualSmokeSource, /ratio < sample\.requiredRatio/);
});
