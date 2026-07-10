# Generated API contract workflow

The backend is the transport-contract source of truth. A Release build emits the OpenAPI 3.1
document at:

`backend/Accounts.Api/OpenApi/accounts-api-v1.json`

`Microsoft.Extensions.ApiDescription.Server` launches the official no-op document host during the
build. `Program.cs` recognises that host and skips database connection/startup side effects while
retaining normal fail-fast production configuration checks for every real application run.

The frontend generates:

`frontend/src/lib/generated/accounts-api-v1.ts`

with the pinned `openapi-typescript` dependency. Do not hand-edit either generated artifact.

## Deliberate two-layer validation

- OpenAPI-generated types prove route, method, parameter, request-body and declared response-shape
  alignment from the backend DTOs.
- Zod schemas remain the runtime trust boundary for critical financial payloads. They are
  intentionally stricter than permissive OpenAPI numeric wire types and continue to enforce
  cross-field financial invariants.
- `frontend/src/lib/openapiContract.ts` makes TypeScript fail when a critical backend response is
  no longer typed or when a top-level critical DTO and its runtime parser diverge.

Critical typed response coverage currently includes authentication, trial balance, profit and
loss, balance sheet, cash flow, equity changes, statement readiness/source trails, corporation-tax
support, CT1 support data, corporation-tax filing support and the filing-readiness profile.

## Commands

From the repository root, with the pinned .NET SDK on `PATH`:

```powershell
dotnet restore backend/Accounts.slnx --locked-mode
dotnet build backend/Accounts.Api/Accounts.Api.csproj -c Release --no-restore
```

From `frontend/`:

```powershell
npm run generate:api-contract
npm run test:api-contract
npx tsc --noEmit --incremental false
```

CI rejects a changed backend document that was not committed. The frontend verifier independently
regenerates TypeScript into a temporary directory and byte-compares it with the committed output.
This catches both stale OpenAPI and stale generated TypeScript instead of relying on expected source
phrases.
