import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");

function source(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

test("all material workbench routes consume the canonical role capabilities", () => {
  const company = source("src/app/companies/[companyId]/page.tsx");
  assert.match(company, /canDeleteCompany=\{permissions\.canDeleteCompany\}/);
  assert.match(company, /canWriteWorkingPapers=\{canWriteWorkingPapers\}/);

  const period = source("src/components/period/PeriodWorkspaceRoute.tsx");
  for (const component of ["PeriodImportWorkspace", "PeriodCategoriseWorkspace", "PeriodAdjustmentsWorkspace"]) {
    assert.match(period, new RegExp(`<${component}[\\s\\S]*?canWrite=\\{canWriteWorkingPapers\\}`));
  }
  assert.match(period, /<PeriodAdjustmentsWorkspace[\s\S]*?canApprove=\{canApprove\}/);
  assert.match(period, /<PeriodFilingWorkspace[\s\S]*?canApprove=\{canApprove\}[\s\S]*?canRead=\{canRead\}[\s\S]*?canReview=\{canReview\}[\s\S]*?canWriteWorkingPapers=\{canWriteWorkingPapers\}/);

  const routeCapabilities = [
    ["src/app/companies/[companyId]/periods/[periodId]/classify/page.tsx", "canWriteWorkingPapers"],
    ["src/app/companies/[companyId]/periods/[periodId]/notes/page.tsx", "canWriteWorkingPapers"],
    ["src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx", "canApprove, canReview, canWriteWorkingPapers"],
    ["src/components/period/YearEndQuestionnaireRoute.tsx", "canWriteWorkingPapers, canReview"],
  ];
  for (const [relativePath, destructure] of routeCapabilities) {
    assert.match(source(relativePath), new RegExp(`const \\{ ${destructure} \\} = useAuth\\(\\)`));
  }

  const dashboard = source("src/app/page.tsx");
  assert.match(dashboard, /const \{[^}]*canCreateCompany[^}]*\} = useAuth\(\)/s);
  assert.match(dashboard, /const \{[^}]*canDeleteCompany[^}]*\} = useAuth\(\)/s);
  assert.match(dashboard, /const \{[^}]*canReviewReleaseEvidence[^}]*\} = useAuth\(\)/s);
  assert.match(dashboard, /const \{[^}]*canReadInternalWorkingPapers[^}]*\} = useAuth\(\)/s);
  assert.match(dashboard, /const \{[^}]*isOwner[^}]*\} = useAuth\(\)/s);
  assert.match(dashboard, /if \(!canDeleteCompany\)/);
  assert.match(dashboard, /if \(canReviewReleaseEvidence\) void loadReadiness\(\)/);

  const authProvider = source("src/components/AuthProvider.tsx");
  assert.match(authProvider, /!canAccessRoute\(user\.role, pathname\)/);
  assert.match(authProvider, /Owner permission required/);
});

test("the complete action catalog is wired to a route source that consumes its capability", () => {
  const catalog = JSON.parse(source("src/lib/permission-action-catalog.json"));
  const routeSources = {
    dashboard: source("src/app/page.tsx") + source("src/components/dashboard/DashboardWorkbench.tsx"),
    "new-company": source("src/components/AuthProvider.tsx") + source("src/app/companies/new/page.tsx"),
    navigation: source("src/components/AppNavbar.tsx"),
    "user-administration": source("src/app/settings/users/page.tsx") + source("src/components/AuthProvider.tsx"),
    "change-password": source("src/app/change-password/page.tsx") + source("src/components/AuthProvider.tsx"),
    "company-detail": source("src/app/companies/[companyId]/page.tsx") + source("src/components/company/CompanyDetailWorkbench.tsx"),
    "period-workspace": source("src/components/period/PeriodWorkspaceRoute.tsx")
      + source("src/components/period/PeriodAdjustmentsWorkspace.tsx")
      + source("src/components/period/PeriodFilingWorkspace.tsx")
      + source("src/components/period/FilingOutputsPanel.tsx")
      + source("src/components/period/FilingReviewCentre.tsx"),
    classification: source("src/app/companies/[companyId]/periods/[periodId]/classify/page.tsx"),
    "year-end": source("src/components/period/YearEndQuestionnaireRoute.tsx"),
    notes: source("src/app/companies/[companyId]/periods/[periodId]/notes/page.tsx"),
    charity: source("src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx"),
    statements: source("src/app/companies/[companyId]/periods/[periodId]/statements/page.tsx")
      + source("src/components/statements/FinancialStatementsWorkbench.tsx"),
    "working-papers": source("src/app/companies/[companyId]/periods/[periodId]/working-papers/page.tsx")
      + source("src/components/period/AccountantWorkingPaperWorkbench.tsx"),
    "production-readiness": source("src/app/production-readiness/page.tsx")
      + source("src/components/AuthProvider.tsx"),
  };
  const capabilityMarkers = {
    canCreateCompany: /canCreateCompany|isOwner/,
    canDeleteCompany: /canDeleteCompany|canRecoverCompany/,
    canManageUsers: /canManageUsers|isOwner/,
    canWriteWorkingPapers: /canWriteWorkingPapers|canWrite|canGenerate/,
    canReadInternalWorkingPapers: /canReadInternalWorkingPapers|canView/,
    canReview: /canReview/,
    canApprove: /canApprove/,
    canReviewReleaseEvidence: /canReviewReleaseEvidence/,
  };

  assert.ok(catalog.length >= 65);
  for (const action of catalog) {
    const routeSource = routeSources[action.routeId];
    assert.ok(routeSource, `Missing audited route source for ${action.id} (${action.routeId}).`);
    if (action.requiredPermission !== "canRead") {
      assert.match(routeSource, capabilityMarkers[action.requiredPermission], `${action.id} must consume ${action.requiredPermission}`);
    }
  }
});

test("permission-sensitive route wiring prevents routine 403s and inert controls", () => {
  const period = source("src/components/period/PeriodWorkspaceRoute.tsx");
  assert.match(period, /if \(canWriteWorkingPapers\) \{[\s\S]*?await calculateDeadlines\(cId, pId\)/);

  const outputs = source("src/components/period/FilingOutputsPanel.tsx");
  assert.match(outputs, /canDownload=\{canRead\}[\s\S]*?title="AGM Pack"/);
  assert.match(outputs, /canDownload=\{canGenerate\}[\s\S]*?title="CRO Filing Pack"/);
  assert.match(outputs, /canDownload=\{canGenerate\}[\s\S]*?title="Signature Page"/);
  assert.match(outputs, /canDownload=\{canRead\}[\s\S]*?title="iXBRL review prototype"/);

  assert.match(source("src/components/period/PeriodAdjustmentsWorkspace.tsx"), /canApprove && !adjustment\.approvedBy/);
  assert.match(source("src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx"), /canApprove && filingStatus\?\.status !== "Approved"/);
  assert.match(source("src/components/period/FilingReviewCentre.tsx"), /croActionRequiresApproval \? canApprove : canReview/);

  const preview = source("src/components/workbench/WorkbenchPreview.tsx");
  assert.doesNotMatch(preview, /<button/);
  assert.match(preview, /Mark accountant approved preview/);

  const navbar = source("src/components/AppNavbar.tsx");
  assert.match(navbar, /hidden min-w-0 items-center gap-2 lg:flex/, "dense desktop navigation must not activate at tablet width");
  assert.match(navbar, /flex items-center gap-2 lg:hidden/, "the compact navigation must remain available through tablet width");
  assert.doesNotMatch(navbar, /hidden md:flex/, "the full tenant navigation must not overflow the 768px tablet viewport");
  assert.match(navbar, /canCreateCompany &&/);
  assert.match(navbar, /href="\/change-password"/);
  assert.match(navbar, /isOwner &&[\s\S]*?href="\/settings\/users"/);
});
