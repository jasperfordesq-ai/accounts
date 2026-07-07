# Irish Statutory Accounts Production Platform

Canonical engineering guidance for this repository lives in **[CLAUDE.md](CLAUDE.md)**:
architecture, tech stack, development commands, entity/service/endpoint maps,
authentication/authorization/security, and production deployment.

This file is the active agent handoff for the current production-readiness goal. Claude
sessions should read this file first, then continue with the goal below rather than
starting a new plan from scratch.

@CLAUDE.md

Project repository: https://github.com/jasperfordesq-ai/accounts

## Active Goal Handoff

Goal: finish the Irish statutory accounts platform so it is production-ready code-wise,
backend-wise, and visually, with explicit qualified-accountant review gates before any
real CRO/Revenue filing use.

Current working posture:

- Keep `main` as the integration branch.
- Do not mark the goal complete until current evidence proves every production-readiness
  requirement: backend statutory/golden corpus coverage, frontend accountant workbench
  quality, typed API drift protection, visual QA artifacts, operations/security evidence,
  and qualified-accountant sign-off.
- The app may generate CRO/Revenue-ready packs, but final real-world use must remain
  blocked by named qualified-accountant review.
- Direct CRO/ROS submission must stay unsupported; the app records workflow states only.

## What Has Been Achieved In This Session

Committed and pushed work on `main` includes:

- `056ec50 Add period workflow action queue`
- `419290e Add legal basis snapshots to golden corpus`
- `af14cfc Surface legal basis evidence in readiness UI`
- `d585752 Add legal source review board to filing centre`
- `91158b3 Add production audit evidence pack`
- `a73e781 Add operations evidence pack`
- `c44f78e Add accountant workflow evidence pack`
- `d74f105 Add workbench visual acceptance register`
- `4efb2b1 Polish dense workbench table scanning`
- `42bed69 Extract workflow decision summary primitive`
- `1e9ad58 Align company command centre summary`
- `edb59c1 Add golden verifier manifest evidence`

Backend/accounting-engine progress:

- Production readiness reporting now includes source-law snapshots, source-law
  traceability, source-law maintenance protocol, source-law review ledger, Revenue
  taxonomy ranges, statutory rules coverage, golden filing corpus, golden evidence
  ledger, and golden verifier manifest evidence.
- Golden filing scenarios are represented for micro LTD, small abridged LTD, DAC small,
  CLG charity, and medium/audit-required manual handoff.
- Backend tests prove golden corpus behavior including PDF text markers, iXBRL XML,
  tax computation, notes, filing readiness, signatory gates, CLG charity readiness,
  small abridgement, and medium/auditor handoff behavior.
- Production auditability evidence has been surfaced for who changed what, who approved
  what, generated-output evidence, readiness snapshots, audit integrity checkpoints,
  and named-accountant approval records.
- Operations/security evidence has been added for Sentry/error routing, structured logs,
  dependency policy, controlled migrations, production seed blocking, and backup/restore
  drill reporting.

Frontend UI/UX progress:

- The accountant workbench has been moved toward a consistent professional product
  experience rather than stitched-together pages.
- Company, period, and filing review command centres now use the shared
  `WorkflowDecisionSummary` primitive for:
  - What is wrong?
  - What is ready?
  - What must I do next?
- Dense workbench tables now have improved scanability, sticky first-column behavior,
  mobile labels, stable scroll affordances, and render coverage.
- Filing review surfaces legal source links, evidence checklist, production decision
  ledger, accountant sign-off packet, external ROS/iXBRL gate, and recorded CRO workflow
  actions.
- Production readiness UI now exposes legal basis evidence, golden corpus evidence,
  release blockers, visual QA coverage, audit evidence, operations evidence, accountant
  workflow evidence, and visual acceptance register.

Frontend code/design-system progress:

- Shared workbench primitives now include and are used across routes:
  `PageShell`, `WorkflowRail`, `ReviewPanel`, `EvidenceChecklist`, `DataGrid`,
  `StatusBadge`, `FilingActionBar`, release blocker summary, and
  `WorkflowDecisionSummary`.
- Several route-heavy areas have been extracted into focused workspace components for
  period import, categorise, year-end, adjustments, statements, filing, company detail,
  and production readiness.
- The typed frontend API contract now rejects drift in golden corpus evidence packs,
  legal basis snapshots, golden evidence ledger, golden verifier manifest, source-law
  traceability, accountant acceptance criteria, visual QA coverage, release blockers,
  release verification manifest, audit evidence, and operations evidence.
- A workbench preview route exists and is included in visual QA planning.

## Verification Already Run

Recent successful local verification includes:

- Backend focused test:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~ProductionReadinessReport_ExposesFlattenedGoldenVerifierManifestForReleaseEvidence`
- Frontend unit tests:
  `npm run test:unit` - 100 passed
- Frontend type-check:
  `npx tsc --noEmit`
- Production readiness render tests:
  `npm run test:render -- production-readiness-panel production-readiness-workbench`
- Previous committed slices also passed frontend lint, build, render suites, API client
  verification, and npm audit.

CI status:

- GitHub Actions currently fails before jobs start because GitHub reports:
  "The job was not started because recent account payments have failed or your spending
  limit needs to be increased."
- Treat this as an external account/billing blocker, not evidence that the code failed.
  Local verification remains the only available signal until GitHub billing is fixed.

## What Is Left To Do

Highest-priority next steps:

1. Fix the GitHub billing/spending-limit issue so CI can run backend, frontend,
   production compose config, and production stack smoke jobs again.
2. Run a full local production gate after CI is unblocked or before the next release:
   - `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art`
   - `npm run lint`
   - `npx tsc --noEmit`
   - `npm run build`
   - `npm run test:unit`
   - `npm run test:render`
   - `node scripts/verify-api-client.mjs`
   - `npm audit --audit-level=low`
3. Generate the visual smoke screenshot artifact set for light/dark desktop/mobile,
   verify the screenshot manifest, and perform a human visual review.
4. Complete a seeded accountant walkthrough across the golden corpus:
   micro LTD, small abridged LTD, DAC small, CLG charity, and medium/audit-required
   manual handoff.
5. Record qualified-accountant acceptance evidence for outputs, gates, wording,
   legal/source evidence, visual workflow, and manual handoff behavior.
6. Run and retain operations evidence:
   backup/restore drill, migration safety check, production seed blocking check,
   Sentry/error routing check, structured log sample, dependency audit evidence.
7. Continue UI polish route by route, especially any surfaces that still feel too
   card-heavy, too sparse, inconsistent in dark mode, or not dense enough for daily
   accountant use.
8. Keep extracting route-heavy frontend code into focused workflow components only when
   it reduces real complexity or improves testable reuse.

## Estimated Completion

As of July 7, 2026:

- Code implementation is roughly 70-75% complete.
- Production assurance is roughly 55-60% complete.
- Overall goal is roughly 60% complete, with about 40% left.

The remaining 40% is not just coding. It is proof: visual QA artifacts, full CI,
operations drills, accountant walkthrough, and named professional sign-off.

## Claude Continuation Instruction

When opening a Claude session, continue from this section:

**[Active Goal Handoff](AGENTS.md#active-goal-handoff)**

Do not restart the project plan. Inspect the current worktree and latest commits, then
continue with the highest-priority unfinished item above.
