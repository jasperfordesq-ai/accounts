import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

test("npm start prepares standalone static assets before launching the Next server", () => {
  const packageJson = JSON.parse(readFileSync(new URL("../package.json", import.meta.url), "utf8"));
  assert.equal(packageJson.scripts.start, "node scripts/start-standalone.mjs");

  const source = readFileSync(new URL("../scripts/start-standalone.mjs", import.meta.url), "utf8");
  assert.match(source, /\.next["']?,\s*["']static/);
  assert.match(source, /\.next["']?,\s*["']standalone/);
  assert.match(source, /cp\(/);
  assert.match(source, /cwd:\s*standaloneDir/);
  assert.match(source, /server\.js/);
});
