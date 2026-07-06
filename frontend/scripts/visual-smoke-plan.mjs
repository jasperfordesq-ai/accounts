export const VISUAL_SMOKE_ARTIFACT_NAME = "visual-smoke-screenshots";

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
  { name: "desktop", width: 1440, height: 1000 },
  { name: "mobile", width: 390, height: 844 },
];

export const visualSmokeThemes = ["light", "dark"];

export const visualSmokeLayoutChecks = [
  "browser-console-errors",
  "page-horizontal-overflow",
  "visible-text-overlap",
];

export const visualSmokeReviewChecks = [
  "accountant-workflow-hierarchy",
  "table-scanability",
  "theme-contrast",
  "mobile-density",
  "loading-error-empty-states",
];

export const visualSmokeReviewProtocol = {
  protocolVersion: "visual-review-v1",
  reviewerRole: "Design reviewer",
  status: "required-review",
  signOffGate: "visual-qa-screenshot-review",
  failurePolicy: "Block release if any accountant workbench route has console errors, horizontal overflow, visible text overlap, inaccessible contrast, unreadable table density, or unresolved light/dark/mobile defects.",
  acceptanceCriteria: [
    "Every configured route is captured in light desktop, dark desktop, light mobile and dark mobile.",
    "No browser console errors, horizontal overflow or visible text overlap are present.",
    "Accountant workflow hierarchy, table scanability, theme contrast, mobile density and route states are professionally acceptable.",
    "A named visual QA reviewer records screenshot-manifest acceptance before real filing release.",
  ],
  requiredEvidence: [
    "visual-smoke-manifest.json",
    "24 visual smoke screenshots",
    "screenshot SHA-256 checksums",
    "route audit summary",
    "named visual QA reviewer sign-off",
  ],
};

export const visualSmokeRoutes = [
  {
    name: "dashboard",
    label: "Dashboard",
    description: "Accountant queue, blockers, deadlines and production readiness overview.",
    routeKey: "dashboard",
    expectedText: "Firm command centre",
    workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
    openFilingTab: false,
  },
  {
    name: "production-readiness",
    label: "Production readiness",
    description: "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
    routeKey: "readiness",
    expectedText: "Production Readiness Checklist",
    workflowStages: ["Review", "Filing"],
    openFilingTab: false,
  },
  {
    name: "company-detail",
    label: "Company detail",
    description: "Company command centre, statutory profile, officers, charity facts and accounting periods.",
    routeKey: "company",
    expectedText: "Company command centre",
    workflowStages: ["Setup"],
    openFilingTab: false,
  },
  {
    name: "period-workspace",
    label: "Period workspace",
    description: "Import, classification, year-end, statements and filing readiness overview.",
    routeKey: "period",
    expectedText: "Filing readiness",
    workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
    openFilingTab: false,
  },
  {
    name: "filing-review",
    label: "Filing review",
    description: "Period filing tab with evidence checklist, source links, outputs and filing state.",
    routeKey: "filing",
    expectedText: "Filing readiness profile",
    workflowStages: ["Review", "Filing"],
    openFilingTab: true,
  },
  {
    name: "workbench-preview",
    label: "Workbench preview",
    description: "Internal component preview for accountant workflow primitives and route states.",
    routeKey: "workbenchPreview",
    expectedText: "Workbench Component Preview",
    workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
    openFilingTab: false,
  },
];

export function expectedVisualSmokeScreenshotCount() {
  return visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length;
}

export function expectedVisualSmokeArtifacts(outputDir = "artifacts/visual-smoke") {
  return visualSmokeViewports.flatMap((viewport) =>
    visualSmokeThemes.flatMap((theme) =>
      visualSmokeRoutes.map((route) => {
        const fileName = `${route.name}-${theme}-${viewport.name}.png`;
        return {
          routeName: route.name,
          routeKey: route.routeKey,
          theme,
          viewportName: viewport.name,
          fileName,
          artifactPath: `${outputDir}/${fileName}`,
          expectedText: route.expectedText,
          openFilingTab: route.openFilingTab,
          reviewStatus: "required-review",
          layoutChecks: visualSmokeLayoutChecks,
        };
      }),
    ),
  );
}

export function expectedVisualSmokeRouteAudits() {
  return visualSmokeRoutes.map((route) => ({
    routeName: route.name,
    routeKey: route.routeKey,
    label: route.label,
    workflowStages: route.workflowStages,
    screenshotCount: visualSmokeThemes.length * visualSmokeViewports.length,
    reviewStatus: "required-review",
    reviewChecks: visualSmokeReviewChecks,
  }));
}

export function expectedVisualSmokeManifest(outputDir = "artifacts/visual-smoke") {
  return {
    artifactName: VISUAL_SMOKE_ARTIFACT_NAME,
    manifestFileName: "visual-smoke-manifest.json",
    expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
    layoutChecks: visualSmokeLayoutChecks,
    reviewChecks: visualSmokeReviewChecks,
    reviewProtocol: visualSmokeReviewProtocol,
    routeAudits: expectedVisualSmokeRouteAudits(),
    screenshots: expectedVisualSmokeArtifacts(outputDir),
  };
}
