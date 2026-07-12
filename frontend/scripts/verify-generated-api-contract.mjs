import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const frontendDirectory = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const schemaPath = resolve(frontendDirectory, "../backend/Accounts.Api/OpenApi/accounts-api-v1.json");
const generatedPath = resolve(frontendDirectory, "src/lib/generated/accounts-api-v1.ts");
const cliPath = resolve(frontendDirectory, "node_modules/openapi-typescript/bin/cli.js");
const schema = JSON.parse(readFileSync(schemaPath, "utf8"));

assert.match(schema.openapi, /^3\.1\./, "the backend contract must be OpenAPI 3.1");
assert.equal(schema.info?.title, "Accounts.Api | v1", "unexpected API document identity");
assert.ok(Object.keys(schema.paths ?? {}).length >= 130, "the generated document lost material API routes");
assert.ok(Object.keys(schema.components?.schemas ?? {}).length >= 80, "the generated document lost DTO schemas");

const criticalResponses = [
  ["/api/auth/login", "post"],
  ["/api/auth/me", "get"],
  ["/api/companies/{companyId}/periods/{periodId}/statements/trial-balance", "get"],
  ["/api/companies/{companyId}/periods/{periodId}/statements/profit-and-loss", "get"],
  ["/api/companies/{companyId}/periods/{periodId}/statements/balance-sheet", "get"],
  ["/api/companies/{companyId}/periods/{periodId}/revenue/tax-computation", "get"],
  ["/api/companies/{companyId}/periods/{periodId}/revenue/filing-support", "get"],
  ["/api/companies/{companyId}/periods/{periodId}/filing/readiness-profile", "get"],
];

for (const [path, method] of criticalResponses) {
  const operation = schema.paths?.[path]?.[method];
  assert.ok(operation, `OpenAPI is missing ${method.toUpperCase()} ${path}`);
  const jsonSchema = operation.responses?.["200"]?.content?.["application/json"]?.schema;
  assert.ok(jsonSchema, `OpenAPI is missing the typed 200 JSON response for ${method.toUpperCase()} ${path}`);
}

for (const [path, method] of [
  ["/api/auth/login", "post"],
  ["/api/auth/password", "post"],
  ["/api/companies", "post"],
  ["/api/companies/{companyId}/periods", "post"],
]) {
  const requestSchema = schema.paths?.[path]?.[method]?.requestBody?.content?.["application/json"]?.schema;
  assert.ok(requestSchema, `OpenAPI is missing the typed request body for ${method.toUpperCase()} ${path}`);
}

const loginInput = schema.components?.schemas?.LoginInput;
assert.ok(loginInput, "OpenAPI is missing LoginInput");
assert.deepEqual(
  [...(loginInput.required ?? [])].sort(),
  ["email", "password", "tenantSlug"],
  "LoginInput must require a workspace slug, email, and password",
);
assert.ok(loginInput.properties?.tenantSlug, "LoginInput must expose tenantSlug");

const temporaryDirectory = mkdtempSync(join(tmpdir(), "accounts-openapi-types-"));
try {
  const temporaryOutput = join(temporaryDirectory, "accounts-api-v1.ts");
  const generation = spawnSync(
    process.execPath,
    [cliPath, schemaPath, "-o", temporaryOutput],
    { cwd: frontendDirectory, encoding: "utf8" },
  );
  assert.equal(
    generation.status,
    0,
    `openapi-typescript failed:\n${generation.stdout ?? ""}\n${generation.stderr ?? ""}`,
  );
  assert.equal(
    readFileSync(generatedPath, "utf8"),
    readFileSync(temporaryOutput, "utf8"),
    "generated API types are stale; run npm run generate:api-contract",
  );
} finally {
  rmSync(temporaryDirectory, { recursive: true, force: true });
}

console.log(
  `Generated API contract verified (${Object.keys(schema.paths).length} paths, ${Object.keys(schema.components.schemas).length} schemas).`,
);
