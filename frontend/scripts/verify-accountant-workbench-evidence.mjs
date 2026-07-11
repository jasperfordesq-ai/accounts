import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  expectedAccountantWorkbenchScreenshotCount,
  expectedVisualSmokeScreenshotCount,
  MIN_LARGE_TEXT_CONTRAST_RATIO,
  MIN_NORMAL_TEXT_CONTRAST_RATIO,
  MIN_UI_COMPONENT_CONTRAST_RATIO,
  MIN_VISUAL_SMOKE_CONTRAST_RATIO,
  visualSmokeContrastCheck,
  visualSmokeAccessibilityCheck,
  visualSmokeAccessibilityTags,
  visualSmokeLayoutChecks,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeStateInventory,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";

const REPORT_FILE_NAME = "accountant-workbench-evidence-report.json";
const ROUTE_ACCEPTANCE_SIGN_OFF_GATE = "qualified-accountant-route-acceptance";

function routeAcceptanceEvidence(route) {
  return [
    `${route.name}-accountant-route-acceptance-note`,
    `${route.name}-visual-smoke-screenshots-reviewed`,
    `${route.name}-qualified-accountant-route-acceptance`,
  ];
}

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

  if (visualReport.routeCount !== visualSmokeStateInventory.length) {
    failures.push(`visual smoke routeCount must be ${visualSmokeStateInventory.length}, found ${visualReport.routeCount}.`);
  }

  if (visualReport.screenshotCount !== expectedVisualSmokeScreenshotCount()) {
    failures.push(
      `visual smoke screenshotCount must be ${expectedVisualSmokeScreenshotCount()}, found ${visualReport.screenshotCount}.`,
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
      if (coverage.routeKey !== route.routeKey) {
        failures.push(`route ${route.name} routeKey must be ${route.routeKey}, found ${coverage.routeKey}.`);
      }

      if (coverage.screenshotCount !== visualSmokeThemes.length * visualSmokeViewports.length) {
        failures.push(
          `route ${route.name} must have ${visualSmokeThemes.length * visualSmokeViewports.length} screenshots, found ${coverage.screenshotCount}.`,
        );
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

    for (const screenshot of screenshots) {
      if (screenshot.routeKey !== route.routeKey) {
        failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} routeKey must be ${route.routeKey}, found ${screenshot.routeKey}.`);
      }

      if (screenshot.expectedText !== route.expectedText) {
        failures.push(
          `route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} expectedText must be ${route.expectedText}, found ${screenshot.expectedText ?? "(missing)"}.`,
        );
      }

      for (const layoutCheck of visualSmokeLayoutChecks) {
        const layoutResult = Array.isArray(screenshot.layoutCheckResults)
          ? screenshot.layoutCheckResults.find((result) => result?.check === layoutCheck)
          : undefined;
        if (!layoutResult) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} is missing passed layout check result ${layoutCheck}.`);
        } else if (layoutResult.status !== "passed") {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} layout check ${layoutCheck} must have status passed.`);
        }
      }

      const themeContrastResult = screenshot.themeContrastResult;
      if (!themeContrastResult) {
        failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} is missing automated theme contrast result.`);
      } else {
        if (themeContrastResult.check !== visualSmokeContrastCheck) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} themeContrastResult.check must be ${visualSmokeContrastCheck}.`);
        }
        if (themeContrastResult.status !== "passed") {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} theme contrast status must be passed.`);
        }
        if (Number(themeContrastResult.minimumContrastRatio) < MIN_VISUAL_SMOKE_CONTRAST_RATIO) {
          failures.push(
            `route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} minimum contrast ratio must be at least ${MIN_VISUAL_SMOKE_CONTRAST_RATIO}.`,
          );
        }
        if (Number(themeContrastResult.sampledTextCount) <= 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} sampledTextCount must be greater than zero.`);
        }
        if (Number(themeContrastResult.failingTextCount) !== 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} failingTextCount must be zero.`);
        }
        if (Number(themeContrastResult.failingUiComponentCount) !== 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} failingUiComponentCount must be zero.`);
        }
        if (Number(themeContrastResult.sampledNormalTextCount) <= 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} sampledNormalTextCount must be greater than zero.`);
        }
        if (Number(themeContrastResult.sampledInteractiveTextCount) <= 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} sampledInteractiveTextCount must be greater than zero.`);
        }
        if (Number(themeContrastResult.sampledUiComponentCount) <= 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} sampledUiComponentCount must be greater than zero.`);
        }
        for (const [field, expected] of [
          ["requiredNormalTextContrastRatio", MIN_NORMAL_TEXT_CONTRAST_RATIO],
          ["requiredLargeTextContrastRatio", MIN_LARGE_TEXT_CONTRAST_RATIO],
          ["requiredUiComponentContrastRatio", MIN_UI_COMPONENT_CONTRAST_RATIO],
        ]) {
          if (Number(themeContrastResult[field]) !== expected) {
            failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} ${field} must be ${expected}.`);
          }
        }
        for (const [field, expected] of [
          ["minimumNormalTextContrastRatio", MIN_NORMAL_TEXT_CONTRAST_RATIO],
          ["minimumLargeTextContrastRatio", MIN_LARGE_TEXT_CONTRAST_RATIO],
          ["minimumUiComponentContrastRatio", MIN_UI_COMPONENT_CONTRAST_RATIO],
        ]) {
          if (Number(themeContrastResult[field]) < expected) {
            failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} ${field} must be at least ${expected}.`);
          }
        }
      }

      const accessibilityResult = screenshot.accessibilityResult;
      if (!accessibilityResult) {
        failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} is missing axe-core accessibility result.`);
      } else {
        if (accessibilityResult.check !== visualSmokeAccessibilityCheck
          || accessibilityResult.status !== "passed"
          || accessibilityResult.engine !== "axe-core") {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} accessibility result must be a passed axe-core check.`);
        }
        if (accessibilityResult.standard !== "WCAG 2.2 A/AA"
          || JSON.stringify(accessibilityResult.tags) !== JSON.stringify(visualSmokeAccessibilityTags)) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} accessibility result must cover WCAG 2.2 A/AA.`);
        }
        if (Number(accessibilityResult.violationCount) !== 0
          || !Array.isArray(accessibilityResult.violations)
          || accessibilityResult.violations.length !== 0) {
          failures.push(`route ${route.name} screenshot ${screenshot.fileName ?? "(unnamed)"} accessibility result must retain zero violations.`);
        }
      }
    }

    if (!route.workflowStages?.length) {
      failures.push(`route ${route.name} must declare accountant workflow stages.`);
    }

    if (typeof route.expectedText !== "string" || route.expectedText.trim().length === 0) {
      failures.push(`route ${route.name} must declare expected accountant decision text.`);
    }

    return {
      routeName: route.name,
      routeKey: route.routeKey,
      label: route.label,
      workflowStages: route.workflowStages,
      expectedText: route.expectedText,
      screenshotCount: screenshots.length,
      themeViewportCoverage: [...screenshotKeys].sort(),
      layoutCheckResultCount: screenshots.reduce(
        (total, screenshot) => total + (Array.isArray(screenshot.layoutCheckResults) ? screenshot.layoutCheckResults.length : 0),
        0,
      ),
      expectedTextEvidenceCount: screenshots.filter((screenshot) => screenshot.expectedText === route.expectedText).length,
      contrastCheckResultCount: screenshots.filter((screenshot) => screenshot.themeContrastResult?.check === visualSmokeContrastCheck).length,
      accessibilityCheckResultCount: screenshots.filter(
        (screenshot) => screenshot.accessibilityResult?.check === visualSmokeAccessibilityCheck,
      ).length,
      accessibilityViolationCount: screenshots.reduce(
        (total, screenshot) => total + Number(screenshot.accessibilityResult?.violationCount ?? 0),
        0,
      ),
      minimumContrastRatio: Math.min(...screenshots.map((screenshot) => Number(screenshot.themeContrastResult?.minimumContrastRatio ?? 0))),
      requiredReviewChecks: coverage?.requiredReviewChecks ?? [],
      reviewStatus: coverage?.reviewStatus ?? "missing",
    };
  });

  const routeAcceptance = visualSmokeRoutes.map((route) => ({
    routeName: route.name,
    routeKey: route.routeKey,
    label: route.label,
    workflowStages: route.workflowStages,
    expectedText: route.expectedText,
    requiredAcceptanceEvidence: routeAcceptanceEvidence(route),
    screenshotReviewEvidence: `${route.name}-light-dark-mobile-tablet-desktop-screenshot-review`,
    signOffGate: ROUTE_ACCEPTANCE_SIGN_OFF_GATE,
    reviewStatus: "required-review",
    blocksRelease: true,
  }));

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
    screenshotCount: expectedAccountantWorkbenchScreenshotCount(),
    expectedScreenshotCount: expectedAccountantWorkbenchScreenshotCount(),
    visualSmokeTotalScreenshotCount: visualReport.screenshotCount,
    visualSmokeExpectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
    workflowStageCount: ACCOUNTANT_WORKFLOW_STAGES.length,
    routeAcceptanceCount: routeAcceptance.length,
    requiredCoverage: {
      routeCodes: visualSmokeRoutes.map((route) => route.name),
      routeKeys: visualSmokeRoutes.map((route) => route.routeKey),
      workflowStages: ACCOUNTANT_WORKFLOW_STAGES,
      themes: visualSmokeThemes,
      viewports: visualSmokeViewports.map((viewport) => viewport.name),
      reviewChecks: visualSmokeReviewChecks,
      layoutChecks: visualSmokeLayoutChecks,
      expectedTextChecks: [
        "route expected accountant decision text",
        "visual smoke screenshots carry route expected accountant decision text",
        "visual smoke routeKey matches planned routeKey",
        "visual smoke screenshots carry stable routeKey",
        "visual smoke screenshots carry passed layout check results",
        "visual smoke screenshots carry passed automated theme contrast results",
        "visual smoke screenshots carry passed axe-core WCAG 2.2 A/AA results",
      ],
      layoutCheckEvidence: visualSmokeLayoutChecks.map((check) => `${check}:passed`),
      contrastCheckEvidence: [
        `${visualSmokeContrastCheck}:passed`,
        `minimum-ratio:${MIN_VISUAL_SMOKE_CONTRAST_RATIO}`,
      ],
      accessibilityCheckEvidence: [
        `${visualSmokeAccessibilityCheck}:passed`,
        ...visualSmokeAccessibilityTags.map((tag) => `${tag}:covered`),
        "violations:0",
      ],
      routeAcceptanceEvidence: routeAcceptance.flatMap((route) => route.requiredAcceptanceEvidence),
      routeAcceptanceSignOffGate: ROUTE_ACCEPTANCE_SIGN_OFF_GATE,
      evidenceFiles: [
        "visual-smoke-manifest.json",
        "visual-smoke-evidence-report.json",
        REPORT_FILE_NAME,
      ],
    },
    routeReadiness,
    routeAcceptance,
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
