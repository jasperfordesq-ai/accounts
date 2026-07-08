import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  visualSmokeLayoutChecks,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";

const REPORT_FILE_NAME = "accountant-workbench-evidence-report.json";

function arg(name, fallback) {
  const prefix = `--${name}=`;
  const value = process.argv.find((item) => item.startsWith(prefix));
  return value ? value.slice(prefix.length) : process.env[name.toUpperCase().replaceAll("-", "_")] ?? fallback;
}

export async function verifyAccountantWorkbenchEvidence(options = {}) {
  const visualReportPath = path.resolve(options.visualReportPath ?? "artifacts/visual-smoke/visual-smoke-evidence-report.json");
  const reportPath = path.resolve(options.reportPath ?? path.join(path.dirname(visualReportPath), REPORT_FILE_NAME));
  const checkedAtUtc = options.checkedAtUtc ?? new Date().toISOString();
  const visualReport = JSON.parse(await readFile(visualReportPath, "utf8"));
  const failures = [];

  if (visualReport.status !== "passed") {
    failures.push("visual-smoke-evidence-report.json must have status passed.");
  }

  if (visualReport.routeCount !== visualSmokeRoutes.length) {
    failures.push(`visual smoke routeCount must be ${visualSmokeRoutes.length}, found ${visualReport.routeCount}.`);
  }

  if (visualReport.screenshotCount !== visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length) {
    failures.push(
      `visual smoke screenshotCount must be ${visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length}, found ${visualReport.screenshotCount}.`,
    );
  }

  const routeReadiness = visualSmokeRoutes.map((route) => {
    const coverage = visualReport.routeCoverage?.find((item) => item.routeName === route.name);
    const screenshots = (visualReport.screenshots ?? []).filter((item) => item.routeName === route.name);
    const screenshotKeys = new Set(screenshots.map((item) => `${item.theme}/${item.viewportName}`));
    const missingThemeViewportPairs = visualSmokeThemes.flatMap((theme) =>
      visualSmokeViewports
        .map((viewport) => `${theme}/${viewport.name}`)
        .filter((key) => !screenshotKeys.has(key)),
    );

    if (!coverage) {
      failures.push(`accountant workbench evidence is missing route coverage for ${route.name}.`);
    } else {
      if (coverage.screenshotCount !== visualSmokeThemes.length * visualSmokeViewports.length) {
        failures.push(`route ${route.name} must have 4 screenshots, found ${coverage.screenshotCount}.`);
      }

      if (coverage.reviewStatus !== "required-review") {
        failures.push(`route ${route.name} reviewStatus must remain required-review before named visual QA sign-off.`);
      }

      for (const check of visualSmokeReviewChecks) {
        if (!coverage.requiredReviewChecks?.includes(check)) {
          failures.push(`route ${route.name} is missing review check ${check}.`);
        }
      }
    }

    for (const missing of missingThemeViewportPairs) {
      failures.push(`route ${route.name} is missing screenshot coverage ${missing}.`);
    }

    if (!route.workflowStages?.length) {
      failures.push(`route ${route.name} must declare accountant workflow stages.`);
    }

    return {
      routeName: route.name,
      routeKey: route.routeKey,
      label: route.label,
      workflowStages: route.workflowStages,
      expectedText: route.expectedText,
      screenshotCount: screenshots.length,
      themeViewportCoverage: [...screenshotKeys].sort(),
      requiredReviewChecks: coverage?.requiredReviewChecks ?? [],
      reviewStatus: coverage?.reviewStatus ?? "missing",
    };
  });

  const coveredStages = new Set(visualSmokeRoutes.flatMap((route) => route.workflowStages ?? []));
  for (const stage of ACCOUNTANT_WORKFLOW_STAGES) {
    if (!coveredStages.has(stage)) {
      failures.push(`accountant workflow stage ${stage} is not covered by a visual smoke route.`);
    }
  }

  if (failures.length > 0) {
    throw new Error(failures.join("\n"));
  }

  const report = {
    ok: true,
    status: "passed",
    checkedAtUtc,
    visualSmokeEvidenceReportPath: visualReportPath,
    evidenceReportFileName: REPORT_FILE_NAME,
    routeCount: visualSmokeRoutes.length,
    screenshotCount: visualReport.screenshotCount,
    expectedScreenshotCount: visualSmokeRoutes.length * visualSmokeThemes.length * visualSmokeViewports.length,
    workflowStageCount: ACCOUNTANT_WORKFLOW_STAGES.length,
    requiredCoverage: {
      routeCodes: visualSmokeRoutes.map((route) => route.name),
      routeKeys: visualSmokeRoutes.map((route) => route.routeKey),
      workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
      themes: visualSmokeThemes,
      viewports: visualSmokeViewports.map((viewport) => viewport.name),
      reviewChecks: visualSmokeReviewChecks,
      layoutChecks: visualSmokeLayoutChecks,
      evidenceFiles: [
        "visual-smoke-manifest.json",
        "visual-smoke-evidence-report.json",
        REPORT_FILE_NAME,
      ],
    },
    routeReadiness,
  };

  await mkdir(path.dirname(reportPath), { recursive: true });
  await writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  report.reportPath = reportPath;
  return report;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  verifyAccountantWorkbenchEvidence({
    visualReportPath: arg("visual-report", "artifacts/visual-smoke/visual-smoke-evidence-report.json"),
    reportPath: arg("report-path", undefined),
  })
    .then((result) => {
      console.log(JSON.stringify(result, null, 2));
    })
    .catch((error) => {
      console.error(error);
      process.exitCode = 1;
    });
}
