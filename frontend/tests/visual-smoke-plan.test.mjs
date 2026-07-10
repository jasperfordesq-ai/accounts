import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { describe, it } from "node:test";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  accountantWorkbenchRoutes,
  canonicalStateUrlMatches,
  canonicalUrlTemplateForState,
  expectedAccountantWorkbenchScreenshotCount,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeManifest,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeScreenshotCount,
  MIN_LARGE_TEXT_CONTRAST_RATIO,
  MIN_NORMAL_TEXT_CONTRAST_RATIO,
  MIN_UI_COMPONENT_CONTRAST_RATIO,
  REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES,
  REQUIRED_VISUAL_SMOKE_UI_STATES,
  resolveVisualSmokeStateHref,
  VISUAL_SMOKE_ARTIFACT_NAME,
  VISUAL_SMOKE_INVENTORY_VERSION,
  visualSmokeLayoutChecks,
  visualSmokeReviewChecks,
  visualSmokeReviewProtocol,
  visualSmokeStateInventory,
  visualSmokeThemes,
  visualSmokeViewports,
} from "../scripts/visual-smoke-plan.mjs";

describe("canonical visual smoke plan", () => {
  it("derives 192 captures from 32 canonical states, two themes and three exact viewports", () => {
    assert.equal(VISUAL_SMOKE_ARTIFACT_NAME, "visual-smoke-screenshots");
    assert.equal(VISUAL_SMOKE_INVENTORY_VERSION, "canonical-material-states-v1");
    assert.equal(visualSmokeStateInventory.length, 32);
    assert.deepEqual(visualSmokeThemes, ["light", "dark"]);
    assert.deepEqual(visualSmokeViewports, [
      { name: "mobile", width: 390, height: 844 },
      { name: "tablet", width: 768, height: 1024 },
      { name: "desktop", width: 1440, height: 1000 },
    ]);
    assert.equal(expectedVisualSmokeScreenshotCount(), 32 * 2 * 3);
    assert.equal(expectedVisualSmokeArtifacts().length, expectedVisualSmokeScreenshotCount());
    assert.equal(accountantWorkbenchRoutes.length, 7);
    assert.equal(expectedAccountantWorkbenchScreenshotCount(), 7 * 2 * 3);
    assert.deepEqual(visualSmokeLayoutChecks, [
      "browser-console-errors",
      "page-horizontal-overflow",
      "visible-text-overlap",
    ]);
    assert.equal(MIN_NORMAL_TEXT_CONTRAST_RATIO, 4.5);
    assert.equal(MIN_LARGE_TEXT_CONTRAST_RATIO, 3);
    assert.equal(MIN_UI_COMPONENT_CONTRAST_RATIO, 3);
  });

  it("covers every named material route, all eight statement tabs and every exceptional state", () => {
    const materialRoutes = new Set(visualSmokeStateInventory.map((state) => state.materialRoute).filter(Boolean));
    const uiStates = new Set(visualSmokeStateInventory.map((state) => state.uiState));

    for (const requiredRoute of REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES) {
      assert.ok(materialRoutes.has(requiredRoute), `missing material route ${requiredRoute}`);
    }
    for (const requiredState of REQUIRED_VISUAL_SMOKE_UI_STATES) {
      assert.ok(uiStates.has(requiredState), `missing UI state ${requiredState}`);
    }

    assert.deepEqual(
      visualSmokeStateInventory
        .filter((state) => state.canonicalTabState.kind === "statement-tab")
        .map((state) => state.canonicalTabState.id),
      [
        "trial-balance",
        "sources",
        "pnl",
        "balance-sheet",
        "tax-computation",
        "cash-flow",
        "equity-changes",
        "directors-report",
      ],
    );
    assert.deepEqual(
      [...new Set(accountantWorkbenchRoutes.flatMap((route) => route.workflowStages))].sort(),
      [...ACCOUNTANT_WORKFLOW_STAGES].sort(),
    );
  });

  it("records canonical URL/tab, expected text and pending human-review evidence per state", () => {
    const artifacts = expectedVisualSmokeArtifacts();
    const login = artifacts[0];
    const filing = artifacts.find((artifact) =>
      artifact.stateId === "filing-review" && artifact.theme === "light" && artifact.viewportName === "mobile");
    const directors = artifacts.find((artifact) =>
      artifact.stateId === "statement-directors-report" && artifact.theme === "dark" && artifact.viewportName === "tablet");

    assert.deepEqual(login, {
      stateId: "login",
      routeName: "login",
      routeKey: "login",
      materialRoute: "login",
      uiState: "populated",
      authMode: "anonymous",
      theme: "light",
      viewportName: "mobile",
      fileName: "login-light-mobile.png",
      artifactPath: "artifacts/visual-smoke/login-light-mobile.png",
      expectedText: "Sign in",
      expectedStateText: "Sign in",
      canonicalUrlTemplate: "/login",
      canonicalQuery: {},
      canonicalTabState: { kind: "route", id: "default", label: "No tab selection" },
      openFilingTab: false,
      reviewStatus: "required-review",
      layoutChecks: visualSmokeLayoutChecks,
      layoutCheckResults: artifacts[0].layoutCheckResults,
      themeContrastResult: artifacts[0].themeContrastResult,
    });
    assert.equal(filing?.canonicalUrlTemplate, "/companies/{companyId}/periods/{periodId}?tab=filing");
    assert.deepEqual(filing?.canonicalTabState, { kind: "period-tab", id: "filing", label: "Filing" });
    assert.equal(filing?.expectedStateText, "Filing readiness profile");
    assert.equal(directors?.expectedStateText, "Directors' Report");
    assert.equal(directors?.reviewStatus, "required-review");
  });

  it("makes canonical inventory identity and route-state URLs deterministic", () => {
    const ids = visualSmokeStateInventory.map((state) => state.id);
    const semanticPlanKeys = visualSmokeStateInventory.map((state) => JSON.stringify({
      url: canonicalUrlTemplateForState(state),
      tab: state.canonicalTabState,
      uiState: state.uiState,
      expectedStateText: state.expectedStateText,
    }));
    assert.equal(new Set(ids).size, ids.length, "state IDs must be unique");
    assert.equal(new Set(semanticPlanKeys).size, semanticPlanKeys.length, "planned semantic states must be unique");

    const filing = visualSmokeStateInventory.find((state) => state.id === "filing-review");
    const period = visualSmokeStateInventory.find((state) => state.id === "period-workspace");
    const bases = {
      period: "/companies/41/periods/52?tab=filing&unexpected=deep-link",
      filing: "/companies/41/periods/52?tab=filing&unexpected=deep-link",
    };

    assert.equal(resolveVisualSmokeStateHref(period, bases), "/companies/41/periods/52");
    assert.equal(resolveVisualSmokeStateHref(filing, bases), "/companies/41/periods/52?tab=filing");
    assert.equal(canonicalStateUrlMatches("/companies/41/periods/52", period), true);
    assert.equal(canonicalStateUrlMatches("/companies/41/periods/52?tab=filing", period), false);
    assert.equal(canonicalStateUrlMatches("/companies/41/periods/52?tab=filing", filing), true);
  });

  it("builds a manifest directly from the inventory and keeps human review blocked", () => {
    const manifest = expectedVisualSmokeManifest();
    assert.equal(manifest.inventoryVersion, VISUAL_SMOKE_INVENTORY_VERSION);
    assert.equal(manifest.inventoryStateCount, visualSmokeStateInventory.length);
    assert.equal(manifest.expectedScreenshotCount, 192);
    assert.deepEqual(manifest.requiredMaterialRoutes, REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES);
    assert.deepEqual(manifest.requiredUiStates, REQUIRED_VISUAL_SMOKE_UI_STATES);
    assert.deepEqual(manifest.stateInventory, expectedVisualSmokeRouteAudits());
    assert.equal(manifest.routeAudits.length, visualSmokeStateInventory.length);
    assert.equal(manifest.routeAudits.every((audit) => audit.screenshotCount === 6), true);
    assert.equal(manifest.screenshots.length, 192);
    assert.equal(visualSmokeReviewProtocol.status, "required-review");
    assert.ok(visualSmokeReviewChecks.includes("canonical-url-tab-state"));
    assert.ok(visualSmokeReviewChecks.includes("semantic-capture-distinctness"));
    assert.ok(visualSmokeReviewProtocol.requiredEvidence.includes("192 canonical material-state screenshots"));
    assert.ok(visualSmokeReviewProtocol.requiredEvidence.includes("named visual QA reviewer sign-off"));
  });

  it("strips discovered deep-link queries before creating canonical period and filing states", async () => {
    const script = await readFile(new URL("../scripts/visual-smoke.mjs", import.meta.url), "utf8");
    const { periodPathFromHref } = await import("../scripts/visual-smoke.mjs");

    assert.equal(
      periodPathFromHref("/companies/41/periods/52?tab=filing", "https://accounts.example"),
      "/companies/41/periods/52",
    );
    assert.match(script, /period:\s*periodPath/);
    assert.match(script, /filing:\s*periodPath/);
    assert.match(script, /resolveVisualSmokeStateHref/);
    assert.doesNotMatch(script, /filingTab\.click\(\)/);
    assert.ok(
      script.indexOf("companyHrefFromPeriodHref") < script.indexOf("createSmokeCompany(page)"),
      "existing dashboard period links must be resolved before fallback smoke company creation",
    );
  });
});
