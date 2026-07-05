import { readFile } from "node:fs/promises";

const workflowPath = new URL("../.github/workflows/ci.yml", import.meta.url);

const supportedMajorVersions = new Map([
  ["actions/checkout", 4],
  ["actions/setup-dotnet", 4],
  ["actions/setup-node", 4],
  ["actions/upload-artifact", 5],
]);

const workflow = await readFile(workflowPath, "utf8");
const failures = [];
const usesPattern = /^\s*uses:\s*([^@\s]+)@([^\s#]+)/gm;

for (const match of workflow.matchAll(usesPattern)) {
  const [, action, reference] = match;
  const supportedMajor = supportedMajorVersions.get(action);
  if (supportedMajor == null) continue;

  const majorMatch = /^v?(\d+)(?:\.|$)/.exec(reference);
  if (!majorMatch) {
    failures.push(`${action}@${reference} must be pinned to supported major v${supportedMajor}.`);
    continue;
  }

  const actualMajor = Number(majorMatch[1]);
  if (actualMajor !== supportedMajor) {
    failures.push(`${action}@${reference} must use supported major v${supportedMajor}.`);
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
