import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(
  new URL("../src/components/readiness/ProductionReadinessWorkbench.tsx", import.meta.url),
  "utf8",
);
const navigation = readFileSync(
  new URL("../src/components/readiness/ProductionReadinessNavigation.tsx", import.meta.url),
  "utf8",
);
const readinessSource = `${component}\n${navigation}`;
const styles = readFileSync(new URL("../src/app/globals.css", import.meta.url), "utf8");

test("production readiness progressive disclosure preserves printable evidence", () => {
  assert.match(component, /data-readiness-progressive-disclosure="true"/);
  assert.match(component, /data-mobile-initial-max-viewports="8"/);
  assert.match(navigation, /data-mobile-priority-within-viewports="2"/);
  assert.match(navigation, /blockers\.slice\(0, 2\)/, "sticky mobile summary must stay compact");
  assert.match(navigation, /blockers\.slice\(0, 4\)/, "initial priority disclosure must bound its blocker rows");
  assert.match(navigation, /className="readiness-disclosure/);
  assert.match(styles, /details\.readiness-disclosure:not\(\[open\]\) > :not\(summary\) \{\s*display: block !important;/);
  assert.match(styles, /\[data-readiness-section\] \{\s*display: block !important;/);
});

test("production readiness navigation has stable anchors for every disclosure group", () => {
  const ids = [...navigation.matchAll(/id: "(readiness-[a-z-]+)"/g)].map((match) => match[1]);
  assert.deepEqual(ids, [
    "readiness-priority",
    "readiness-release-evidence",
    "readiness-law-taxonomy",
    "readiness-release-controls",
    "readiness-accountant-review",
    "readiness-audit-operations",
    "readiness-statutory-filing",
    "readiness-coverage-boundaries",
  ]);
  assert.match(navigation, /href=\{`#\$\{section\.id\}`\}/);
  assert.match(component, /window\.addEventListener\("popstate", restoreSectionFromLocation\)/);
  assert.match(component, /window\.addEventListener\("hashchange", restoreSectionFromLocation\)/);
});

test("supporting ledgers remain present and authentic human gates stay blocking", () => {
  for (const title of [
    "Human release evidence",
    "Source-law review ledger",
    "Release blocker register",
    "Accountant walkthrough evidence matrix",
    "Production audit evidence pack",
    "Golden evidence ledger",
    "Unsupported/manual handoff",
  ]) {
    assert.match(readinessSource, new RegExp(title.replace("/", "\\/")));
  }
  assert.match(readinessSource, /Human and external acceptance stays blocking/);
  assert.match(readinessSource, /this UI cannot self-accept those gates/);
  assert.doesNotMatch(readinessSource, /blocksRelease\s*:\s*false/);
});
