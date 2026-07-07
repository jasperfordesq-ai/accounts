# GitHub Actions Billing Blocker Runbook

Date opened: 2026-07-07

## Current blocker

GitHub Actions is not providing production-readiness evidence because workflow jobs are stopped before they start with the account-level message:

> The job was not started because recent account payments have failed or your spending limit needs to be increased.

Treat this as an external GitHub billing/spending-limit blocker, not as a passing or failing code signal. Until Actions can start jobs again, local verification is the only available engineering signal and must be retained with the release evidence pack.

## Release policy while CI is unavailable

- Do not mark a production release ready while GitHub Actions cannot start.
- Do not treat a local-only run as a substitute for the named qualified-accountant sign-off gate.
- Direct CRO/ROS submission remains unsupported; the product may only record workflow states until final accountant approval and external filing-system validation evidence exist.
- Every local fallback run must record the exact command, timestamp, operator, machine/context, exit status, and retained output location.

## Local fallback evidence checklist

Run the following from a clean worktree after pulling the intended release commit:

```powershell
cd backend
dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art
```

```bash
cd frontend
npm run lint
npx tsc --noEmit
npm run build
npm run test:unit
npm run test:render
node scripts/verify-api-client.mjs
npm audit --audit-level=low
```

For production-stack and visual evidence, run against production-like secrets and seeded data:

```bash
pwsh ./scripts/smoke-production.ps1
```

```bash
cd frontend
npm run test:visual -- --output-dir=../.tmp/visual-smoke
npm run test:visual:verify -- --manifest=../.tmp/visual-smoke/visual-smoke-manifest.json
npm run test:visual:review-packet -- --manifest=../.tmp/visual-smoke/visual-smoke-manifest.json --output=../.tmp/visual-smoke/visual-smoke-review-packet.md
```

## Account remediation owner actions

1. Repository owner resolves the failed GitHub payment or increases the Actions spending limit.
2. Re-run the latest `main` workflow for the release commit.
3. Attach the completed backend, frontend, production compose, production smoke, visual-smoke, backup/restore, and audit/dependency evidence to the release pack.
4. Keep this runbook in the evidence pack with the local fallback output so reviewers can distinguish code evidence from the external CI-account blocker.

## Exit criteria

This blocker is closed only when GitHub Actions jobs start and complete on the intended release commit, and the release evidence pack contains both:

- the successful CI run URL/artifacts; and
- any local fallback output used while the billing/spending-limit outage was active.
