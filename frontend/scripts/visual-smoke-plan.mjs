export const VISUAL_SMOKE_ARTIFACT_NAME = "visual-smoke-screenshots";

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
    openFilingTab: false,
  },
  {
    name: "production-readiness",
    label: "Production readiness",
    description: "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
    routeKey: "readiness",
    expectedText: "Production Readiness Checklist",
    openFilingTab: false,
  },
  {
    name: "company-detail",
    label: "Company detail",
    description: "Company statutory profile, officers, charity facts and accounting periods.",
    routeKey: "company",
    expectedText: "Accounting Periods",
    openFilingTab: false,
  },
  {
    name: "period-workspace",
    label: "Period workspace",
    description: "Import, classification, year-end, statements and filing readiness overview.",
    routeKey: "period",
    expectedText: "Filing readiness",
    openFilingTab: false,
  },
  {
    name: "filing-review",
    label: "Filing review",
    description: "Period filing tab with evidence checklist, source links, outputs and filing state.",
    routeKey: "filing",
    expectedText: "Filing readiness profile",
    openFilingTab: true,
  },
  {
    name: "workbench-preview",
    label: "Workbench preview",
    description: "Internal component preview for accountant workflow primitives and route states.",
    routeKey: "workbenchPreview",
    expectedText: "Workbench Component Preview",
    openFilingTab: false,
  },
];

export function expectedVisualSmokeScreenshotCount() {
  return visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length;
}
