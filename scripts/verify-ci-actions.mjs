import { readFile } from "node:fs/promises";

const workflowPath = new URL("../.github/workflows/ci.yml", import.meta.url);

const minimumMajorVersions = new Map([
  ["actions/checkout", 7],
  ["actions/setup-dotnet", 5],
  ["actions/setup-node", 6],
  ["actions/upload-artifact", 7],
]);

const workflow = await readFile(workflowPath, "utf8");
const failures = [];
const usesPattern = /^\s*uses:\s*([^@\s]+)@([^\s#]+)/gm;

for (const match of workflow.matchAll(usesPattern)) {
  const [, action, reference] = match;
  const minimumMajor = minimumMajorVersions.get(action);
  if (minimumMajor == null) continue;

  const majorMatch = /^v?(\d+)(?:\.|$)/.exec(reference);
  if (!majorMatch) {
    failures.push(`${action}@${reference} must be pinned to v${minimumMajor} or newer.`);
    continue;
  }

  const actualMajor = Number(majorMatch[1]);
  if (actualMajor < minimumMajor) {
    failures.push(`${action}@${reference} must be upgraded to v${minimumMajor} or newer.`);
  }
}

if (failures.length > 0) {
  console.error("CI action policy failed:");
  for (const failure of failures) {
    console.error(`- ${failure}`);
  }
  process.exit(1);
}

console.log("CI action policy OK");
