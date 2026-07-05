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

export const visualSmokeRoutes = [
  {
    name: "dashboard",
    label: "Dashboard",
    description: "Accountant queue, blockers, deadlines and production readiness overview.",
    routeKey: "dashboard",
    expectedText: "Production Readiness",
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
