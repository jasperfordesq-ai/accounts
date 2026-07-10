export const VISUAL_SMOKE_ARTIFACT_NAME = "visual-smoke-screenshots";
export const VISUAL_SMOKE_INVENTORY_VERSION = "canonical-material-states-v1";

export const ACCOUNTANT_WORKFLOW_STAGES = [
  "Setup",
  "Import",
  "Classify",
  "Year-End",
  "Statements",
  "Notes",
  "Review",
  "Filing",
];

export const visualSmokeViewports = [
  { name: "mobile", width: 390, height: 844 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "desktop", width: 1440, height: 1000 },
];

export const visualSmokeThemes = ["light", "dark"];

export const visualSmokeLayoutChecks = [
  "browser-console-errors",
  "page-horizontal-overflow",
  "visible-text-overlap",
];

export const visualSmokeContrastCheck = "theme-contrast";

export const MIN_VISUAL_SMOKE_CONTRAST_RATIO = 3;
export const MIN_NORMAL_TEXT_CONTRAST_RATIO = 4.5;
export const MIN_LARGE_TEXT_CONTRAST_RATIO = 3;
export const MIN_UI_COMPONENT_CONTRAST_RATIO = 3;

export function passedVisualSmokeLayoutResults() {
  return [
    {
      check: "browser-console-errors",
      status: "passed",
      evidence: "No browser console errors or page errors were emitted before screenshot capture.",
    },
    {
      check: "page-horizontal-overflow",
      status: "passed",
      evidence: "The route document width stayed within the planned viewport, excluding intentional internal scrollers.",
    },
    {
      check: "visible-text-overlap",
      status: "passed",
      evidence: "Rendered visible text blocks did not overlap beyond the visual smoke tolerance.",
    },
  ];
}

export function passedVisualSmokeContrastResult({
  sampledTextCount = 1,
  sampledNormalTextCount = sampledTextCount,
  sampledLargeTextCount = 0,
  sampledInteractiveTextCount = 1,
  sampledPlaceholderCount = 0,
  sampledUiComponentCount = 1,
  sampledGradientTextCount = 0,
  minimumContrastRatio = MIN_VISUAL_SMOKE_CONTRAST_RATIO,
  minimumNormalTextContrastRatio = MIN_NORMAL_TEXT_CONTRAST_RATIO,
  minimumLargeTextContrastRatio = MIN_LARGE_TEXT_CONTRAST_RATIO,
  minimumUiComponentContrastRatio = MIN_UI_COMPONENT_CONTRAST_RATIO,
} = {}) {
  return {
    check: visualSmokeContrastCheck,
    status: "passed",
    minimumContrastRatio,
    requiredMinimumContrastRatio: MIN_VISUAL_SMOKE_CONTRAST_RATIO,
    requiredNormalTextContrastRatio: MIN_NORMAL_TEXT_CONTRAST_RATIO,
    requiredLargeTextContrastRatio: MIN_LARGE_TEXT_CONTRAST_RATIO,
    requiredUiComponentContrastRatio: MIN_UI_COMPONENT_CONTRAST_RATIO,
    minimumNormalTextContrastRatio,
    minimumLargeTextContrastRatio,
    minimumUiComponentContrastRatio,
    sampledTextCount,
    sampledNormalTextCount,
    sampledLargeTextCount,
    sampledInteractiveTextCount,
    sampledPlaceholderCount,
    sampledUiComponentCount,
    sampledGradientTextCount,
    failingTextCount: 0,
    failingUiComponentCount: 0,
    evidence: "Rendered normal and large text, links, buttons, form values/placeholders, disabled states, UI component boundaries and resolvable gradients met their WCAG AA contrast floors before screenshot capture.",
  };
}

export const visualSmokeReviewChecks = [
  "accountant-workflow-hierarchy",
  "table-scanability",
  "theme-contrast",
  "responsive-density",
  "loading-error-empty-states",
  "canonical-url-tab-state",
  "semantic-capture-distinctness",
  "stale-conflict-states",
];

export const REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES = [
  "login",
  "password-change",
  "onboarding",
  "classification",
  "categorisation",
  "year-end",
  "adjustments",
  "notes",
  "charity",
  "statement-trial-balance",
  "statement-source-trail",
  "statement-profit-and-loss",
  "statement-balance-sheet",
  "statement-tax-computation",
  "statement-cash-flow",
  "statement-equity-changes",
  "statement-directors-report",
  "filing",
];

export const REQUIRED_VISUAL_SMOKE_UI_STATES = [
  "loading",
  "empty",
  "maximum-data",
  "error",
  "partial-error",
  "permission-denied",
  "read-only",
  "stale",
  "conflict",
];

const routeTab = (id, label) => ({ kind: "route", id, label });
const periodTab = (id, label) => ({ kind: "period-tab", id, label });
const statementTab = (id, label) => ({ kind: "statement-tab", id, label });
const charityTab = (id, label) => ({ kind: "charity-tab", id, label });
const previewState = (id, label) => ({ kind: "preview-state", id, label });

const accountantRoutes = [
  {
    name: "dashboard",
    label: "Dashboard",
    description: "Accountant queue, blockers, deadlines and production readiness overview.",
    routeKey: "dashboard",
    expectedText: "Firm command centre",
    workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
  },
  {
    name: "production-readiness",
    label: "Production readiness",
    description: "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
    routeKey: "readiness",
    expectedText: "Production Readiness Checklist",
    workflowStages: ["Review", "Filing"],
  },
  {
    name: "company-detail",
    label: "Company detail",
    description: "Company command centre, statutory profile, officers, charity facts and accounting periods.",
    routeKey: "company",
    expectedText: "Company command centre",
    workflowStages: ["Setup"],
  },
  {
    name: "period-workspace",
    label: "Period workspace",
    description: "Canonical import-tab period workspace with classification, year-end, statements and filing readiness context.",
    routeKey: "period",
    expectedText: "Filing readiness",
    workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
  },
  {
    name: "filing-review",
    label: "Filing review",
    description: "Canonical filing-tab state with evidence checklist, source links, outputs and recorded filing workflow.",
    routeKey: "filing",
    expectedText: "Filing readiness profile",
    workflowStages: ["Review", "Filing"],
  },
  {
    name: "financial-statements",
    label: "Financial statements",
    description: "Canonical trial-balance tab in the statement preview and source-trail workbench.",
    routeKey: "financialStatements",
    expectedText: "Financial Statements",
    workflowStages: ["Statements"],
  },
  {
    name: "workbench-preview",
    label: "Workbench preview",
    description: "Internal deterministic preview for accountant workflow primitives and dense populated content.",
    routeKey: "workbenchPreview",
    expectedText: "Workbench Component Preview",
    workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
  },
];

/**
 * The seven-route matrix remains the qualified-accountant workbench acceptance subset. The full
 * screenshot manifest is driven by visualSmokeStateInventory below.
 */
export const accountantWorkbenchRoutes = accountantRoutes.map((route) => ({
  ...route,
  openFilingTab: false,
}));

// Backwards-compatible export for the accountant workbench evidence generator.
export const visualSmokeRoutes = accountantWorkbenchRoutes;

function inventoryState({
  name,
  label,
  description,
  routeKey,
  canonicalPathTemplate,
  canonicalQuery = {},
  canonicalTabState = routeTab("default", "No tab selection"),
  expectedText,
  expectedStateText = expectedText,
  workflowStages,
  authMode = "authenticated",
  materialRoute = null,
  uiState = "populated",
}) {
  return {
    id: name,
    name,
    label,
    description,
    routeKey,
    canonicalPathTemplate,
    canonicalQuery,
    canonicalTabState,
    expectedText,
    expectedStateText,
    workflowStages,
    authMode,
    materialRoute,
    uiState,
    reviewStatus: "required-review",
    openFilingTab: false,
  };
}

const accountantRoute = (name) => accountantWorkbenchRoutes.find((route) => route.name === name);

export const visualSmokeStateInventory = [
  inventoryState({
    name: "login",
    label: "Login",
    description: "Unauthenticated firm-user sign-in form.",
    routeKey: "login",
    canonicalPathTemplate: "/login",
    expectedText: "Sign in",
    workflowStages: ["Setup"],
    authMode: "anonymous",
    materialRoute: "login",
  }),
  inventoryState({
    name: "password-change",
    label: "Password change",
    description: "Authenticated password rotation form.",
    routeKey: "changePassword",
    canonicalPathTemplate: "/change-password",
    expectedText: "Set a new password",
    workflowStages: ["Setup"],
    materialRoute: "password-change",
  }),
  inventoryState({
    ...accountantRoute("dashboard"),
    canonicalPathTemplate: "/",
    canonicalTabState: routeTab("dashboard", "Dashboard root"),
  }),
  inventoryState({
    name: "onboarding",
    label: "Company onboarding",
    description: "Blank company onboarding workflow at its legal-identity step.",
    routeKey: "onboarding",
    canonicalPathTemplate: "/companies/new",
    expectedText: "New Company",
    workflowStages: ["Setup"],
    materialRoute: "onboarding",
    uiState: "blank-form",
  }),
  inventoryState({
    ...accountantRoute("production-readiness"),
    canonicalPathTemplate: "/production-readiness",
    canonicalTabState: routeTab("readiness", "Production readiness"),
  }),
  inventoryState({
    ...accountantRoute("company-detail"),
    canonicalPathTemplate: "/companies/{companyId}",
    canonicalTabState: routeTab("company", "Company command centre"),
  }),
  inventoryState({
    ...accountantRoute("period-workspace"),
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}",
    canonicalTabState: periodTab("import", "Import"),
    expectedStateText: "Import Transactions",
  }),
  inventoryState({
    name: "classification",
    label: "Company size classification",
    description: "Period classification interview and statutory decision evidence.",
    routeKey: "classification",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}/classify",
    expectedText: "Company Size Classification",
    workflowStages: ["Classify"],
    materialRoute: "classification",
  }),
  inventoryState({
    name: "categorisation",
    label: "Transaction categorisation",
    description: "Categorisation filters, rules, progress and transaction evidence table.",
    routeKey: "period",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}",
    canonicalQuery: { tab: "categorise" },
    canonicalTabState: periodTab("categorise", "Categorise"),
    expectedText: "Categorisation Overview",
    workflowStages: ["Import"],
    materialRoute: "categorisation",
  }),
  inventoryState({
    name: "year-end",
    label: "Year-end questionnaire",
    description: "Year-end accounting inputs, statutory representations and completeness review.",
    routeKey: "yearEnd",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}/year-end",
    expectedText: "Year-End Questionnaire",
    workflowStages: ["Year-End"],
    materialRoute: "year-end",
  }),
  inventoryState({
    name: "adjustments",
    label: "Period adjustments",
    description: "Adjustment generation, filters, summaries and approval evidence.",
    routeKey: "period",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}",
    canonicalQuery: { tab: "adjustments" },
    canonicalTabState: periodTab("adjustments", "Adjustments"),
    expectedText: "Period Adjustments",
    workflowStages: ["Year-End", "Review"],
    materialRoute: "adjustments",
  }),
  inventoryState({
    name: "notes",
    label: "Financial statement notes",
    description: "Regime-aware note checklist, generation, inclusion and editing workflow.",
    routeKey: "notes",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}/notes",
    expectedText: "Notes to the Financial Statements",
    workflowStages: ["Notes"],
    materialRoute: "notes",
  }),
  inventoryState({
    name: "charity",
    label: "Charity reporting",
    description: "Charity/SORP reporting decision and statement-of-financial-activities tab.",
    routeKey: "charity",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}/charity",
    canonicalTabState: charityTab("sofa", "Statement of Financial Activities"),
    expectedText: "Charity Reporting",
    expectedStateText: "Statement of Financial Activities",
    workflowStages: ["Statements", "Notes", "Review"],
    materialRoute: "charity",
  }),
  inventoryState({
    ...accountantRoute("financial-statements"),
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}/statements",
    canonicalTabState: statementTab("trial-balance", "Trial Balance"),
    expectedStateText: "Trial Balance",
    materialRoute: "statement-trial-balance",
  }),
  ...[
    ["statement-source-trail", "sources", "Source Trail", "Figure Source Trail"],
    ["statement-profit-and-loss", "pnl", "Profit & Loss", "Profit & Loss Account"],
    ["statement-balance-sheet", "balance-sheet", "Balance Sheet", "Balance Sheet"],
    ["statement-tax-computation", "tax-computation", "Tax Computation", "Corporation Tax Support Data"],
    ["statement-cash-flow", "cash-flow", "Cash Flow", "Cash Flow Statement"],
    ["statement-equity-changes", "equity-changes", "Equity Changes", "Statement of Changes in Equity"],
    ["statement-directors-report", "directors-report", "Directors' Report", "Directors' Report"],
  ].map(([name, tabId, tabLabel, expectedStateText]) => inventoryState({
    name,
    label: tabLabel,
    description: `Financial statements ${tabLabel} tab.`,
    routeKey: "financialStatements",
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}/statements",
    canonicalQuery: { statementTab: tabId },
    canonicalTabState: statementTab(tabId, tabLabel),
    expectedText: "Financial Statements",
    expectedStateText,
    workflowStages: ["Statements"],
    materialRoute: name,
  })),
  inventoryState({
    ...accountantRoute("filing-review"),
    canonicalPathTemplate: "/companies/{companyId}/periods/{periodId}",
    canonicalQuery: { tab: "filing" },
    canonicalTabState: periodTab("filing", "Filing"),
    materialRoute: "filing",
  }),
  inventoryState({
    ...accountantRoute("workbench-preview"),
    canonicalPathTemplate: "/workbench-preview",
    canonicalTabState: routeTab("preview", "Workbench component preview"),
  }),
  ...[
    ["loading", "Canonical loading state", "Loading canonical accountant workspace"],
    ["empty", "Canonical empty state", "No canonical accounting records"],
    ["maximum-data", "Canonical maximum-data state", "Maximum-data review table"],
    ["error", "Canonical error state", "Canonical workspace could not be loaded"],
    ["partial-error", "Canonical partial-error state", "Filing evidence unavailable"],
    ["permission-denied", "Canonical permission-denied state", "Permission denied"],
    ["read-only", "Canonical read-only state", "Read-only workflow access"],
    ["stale", "Canonical stale state", "Refreshing statement evidence; retained data may be stale."],
    ["conflict", "Canonical conflict state", "Accounting record changed by another reviewer"],
  ].map(([uiState, expectedText, expectedStateText]) => inventoryState({
    name: `state-${uiState}`,
    label: expectedText,
    description: `Deterministic ${uiState} workbench state for visual release review.`,
    routeKey: "workbenchPreview",
    canonicalPathTemplate: "/workbench-preview",
    canonicalQuery: { state: uiState },
    canonicalTabState: previewState(uiState, expectedText),
    expectedText,
    expectedStateText,
    workflowStages: ["Review"],
    uiState,
  })),
];

export const visualSmokeReviewProtocol = {
  protocolVersion: "visual-review-v2-canonical-states",
  reviewerRole: "Design reviewer",
  status: "required-review",
  signOffGate: "visual-qa-screenshot-review",
  failurePolicy: "Block release if a canonical material route/state combination is missing, duplicated, semantically identical to another intended state, or has console, overflow, overlap, contrast, responsive-density or human-review defects.",
  acceptanceCriteria: [
    "Every canonical material route/state is captured in light and dark themes at 390x844 mobile, 768x1024 tablet and 1440x1000 desktop viewports.",
    "Every capture retains its expected text, exact canonical URL/tab state, console/overflow/overlap/contrast results, dimensions, SHA-256 hash and pending human-review status.",
    "No canonical route/theme/viewport combination is missing or duplicated, and no two intended states in the same theme/viewport are semantically identical.",
    "A named visual QA reviewer records screenshot-manifest acceptance before real filing release.",
  ],
  requiredEvidence: [
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json",
    `${expectedVisualSmokeScreenshotCount()} canonical material-state screenshots`,
    "canonical state inventory and exact URL/tab evidence",
    "semantic content SHA-256 distinctness evidence",
    "screenshot SHA-256 checksums",
    "screenshot PNG dimensions",
    "screenshot nonblank pixel diversity evidence",
    "per-screenshot automated theme contrast smoke evidence",
    "state audit summary",
    "named visual QA reviewer sign-off",
  ],
};

export function expectedVisualSmokeScreenshotCount() {
  return visualSmokeStateInventory.length * visualSmokeThemes.length * visualSmokeViewports.length;
}

export function expectedAccountantWorkbenchScreenshotCount() {
  return accountantWorkbenchRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length;
}

export function canonicalUrlTemplateForState(state) {
  return appendCanonicalQuery(state.canonicalPathTemplate, state.canonicalQuery);
}

export function resolveVisualSmokeStateHref(state, routeBases) {
  const base = routeBases[state.routeKey];
  if (typeof base !== "string" || base.length === 0) {
    throw new Error(`Missing discovered route base for ${state.id}: ${state.routeKey}`);
  }
  return appendCanonicalQuery(new URL(base, "https://visual-smoke.invalid/").pathname, state.canonicalQuery);
}

export function canonicalStateUrlMatches(actualUrl, state) {
  if (!state) return false;
  let actual;
  try {
    actual = new URL(actualUrl, "https://visual-smoke.invalid/");
  } catch {
    return false;
  }

  const expectedPathPattern = state.canonicalPathTemplate
    .replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
    .replaceAll("\\{companyId\\}", "[1-9][0-9]*")
    .replaceAll("\\{periodId\\}", "[1-9][0-9]*");
  if (!new RegExp(`^${expectedPathPattern}$`).test(actual.pathname)) return false;

  const expectedEntries = Object.entries(state.canonicalQuery).map(([key, value]) => [key, String(value)]);
  const actualEntries = [...actual.searchParams.entries()];
  return JSON.stringify(actualEntries) === JSON.stringify(expectedEntries);
}

export function expectedVisualSmokeArtifacts(outputDir = "artifacts/visual-smoke") {
  return visualSmokeViewports.flatMap((viewport) =>
    visualSmokeThemes.flatMap((theme) =>
      visualSmokeStateInventory.map((state) => {
        const fileName = `${state.id}-${theme}-${viewport.name}.png`;
        return {
          stateId: state.id,
          routeName: state.name,
          routeKey: state.routeKey,
          materialRoute: state.materialRoute,
          uiState: state.uiState,
          authMode: state.authMode,
          theme,
          viewportName: viewport.name,
          fileName,
          artifactPath: `${outputDir}/${fileName}`,
          expectedText: state.expectedText,
          expectedStateText: state.expectedStateText,
          canonicalUrlTemplate: canonicalUrlTemplateForState(state),
          canonicalQuery: state.canonicalQuery,
          canonicalTabState: state.canonicalTabState,
          openFilingTab: false,
          reviewStatus: state.reviewStatus,
          layoutChecks: visualSmokeLayoutChecks,
          layoutCheckResults: passedVisualSmokeLayoutResults(),
          themeContrastResult: passedVisualSmokeContrastResult(),
        };
      }),
    ),
  );
}

export function expectedVisualSmokeRouteAudits() {
  return visualSmokeStateInventory.map((state) => ({
    stateId: state.id,
    routeName: state.name,
    routeKey: state.routeKey,
    label: state.label,
    materialRoute: state.materialRoute,
    uiState: state.uiState,
    canonicalUrlTemplate: canonicalUrlTemplateForState(state),
    canonicalTabState: state.canonicalTabState,
    expectedText: state.expectedText,
    expectedStateText: state.expectedStateText,
    workflowStages: state.workflowStages,
    screenshotCount: visualSmokeThemes.length * visualSmokeViewports.length,
    reviewStatus: state.reviewStatus,
    reviewChecks: visualSmokeReviewChecks,
  }));
}

export function expectedVisualSmokeManifest(outputDir = "artifacts/visual-smoke") {
  return {
    artifactName: VISUAL_SMOKE_ARTIFACT_NAME,
    manifestFileName: "visual-smoke-manifest.json",
    inventoryVersion: VISUAL_SMOKE_INVENTORY_VERSION,
    inventoryStateCount: visualSmokeStateInventory.length,
    expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
    requiredMaterialRoutes: REQUIRED_VISUAL_SMOKE_MATERIAL_ROUTES,
    requiredUiStates: REQUIRED_VISUAL_SMOKE_UI_STATES,
    themes: visualSmokeThemes,
    viewportDimensions: visualSmokeViewports,
    layoutChecks: visualSmokeLayoutChecks,
    reviewChecks: visualSmokeReviewChecks,
    reviewProtocol: visualSmokeReviewProtocol,
    stateInventory: expectedVisualSmokeRouteAudits(),
    routeAudits: expectedVisualSmokeRouteAudits(),
    screenshots: expectedVisualSmokeArtifacts(outputDir),
  };
}

function appendCanonicalQuery(pathname, query) {
  const canonicalPath = String(pathname).split(/[?#]/, 1)[0] || "/";
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    search.set(key, String(value));
  }
  const queryString = search.toString();
  return queryString ? `${canonicalPath}?${queryString}` : canonicalPath;
}
