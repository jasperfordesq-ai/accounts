import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const srcRoot = path.join(here, "../src");

function sourceFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return sourceFiles(fullPath);
    return /\.(?:ts|tsx)$/.test(entry.name) ? [fullPath] : [];
  });
}

test("external filing actions are labelled as evidence recording rather than platform submission", () => {
  const offenders = [];
  const explicitlyBounded = /does not submit|never submits|not submitted to|no direct .*submission|outside this system|external .*record/i;
  const misleading = [
    /\b(?:submit|file)\s+(?:to|with)\s+(?:the\s+)?(?:CRO|ROS|Revenue|Charities Regulator)\b/i,
    /\bmark(?:ed)?\s+(?:as\s+)?submitted(?:\s+to\s+CRO)?\b/i,
    /\bapprove\s+or\s+submit\b/i,
    />\s*submit(?:ted)?\s*</i,
  ];

  for (const file of sourceFiles(srcRoot)) {
    const relative = path.relative(srcRoot, file).replaceAll("\\", "/");
    const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
    lines.forEach((line, index) => {
      if (explicitlyBounded.test(line)) return;
      if (misleading.some((pattern) => pattern.test(line))) {
        offenders.push(`${relative}:${index + 1}: ${line.trim()}`);
      }
    });
  }

  assert.deepEqual(
    offenders,
    [],
    `UI copy must distinguish external filing-status evidence from platform submission:\n${offenders.join("\n")}`,
  );
});

test("CRO and charity workflow controls name the external-recording boundary", () => {
  const cro = fs.readFileSync(path.join(srcRoot, "components/period/FilingReviewCentre.tsx"), "utf8");
  const charity = fs.readFileSync(path.join(srcRoot, "app/companies/[companyId]/periods/[periodId]/charity/page.tsx"), "utf8");

  assert.match(cro, /Record external submission/);
  assert.match(cro, /submitted outside this system/);
  assert.match(charity, /Record external submission/);
  assert.match(charity, /no direct Regulator submission/);
});
