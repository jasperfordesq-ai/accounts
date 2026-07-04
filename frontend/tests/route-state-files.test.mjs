import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { test } from "node:test";

const appDir = new URL("../src/app/", import.meta.url);

test("Next app shell has production route-state files wired to workbench primitives", () => {
  const expectations = [
    ["loading.tsx", "WorkbenchLoadingState"],
    ["error.tsx", "WorkbenchErrorState"],
    ["not-found.tsx", "WorkbenchEmptyState"],
  ];

  for (const [fileName, componentName] of expectations) {
    const fileUrl = new URL(fileName, appDir);
    assert.ok(existsSync(fileUrl), `${fileName} must exist for consistent production route states`);

    const source = readFileSync(fileUrl, "utf8");
    assert.match(source, new RegExp(componentName), `${fileName} should render ${componentName}`);
  }
});
