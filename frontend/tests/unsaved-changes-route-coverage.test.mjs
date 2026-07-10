import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const frontendRoot = new URL("../", import.meta.url);

function source(relativePath) {
  return readFileSync(new URL(relativePath, frontendRoot), "utf8");
}

const editableSurfaces = [
  ["company onboarding", "src/app/companies/new/page.tsx", /useUnsavedChanges\(hasOnboardingDraft\)/],
  ["company command centre", "src/app/companies/[companyId]/page.tsx", /useUnsavedChanges\(hasCompanyDraft\)/],
  ["period workbench", "src/components/period/PeriodWorkspaceRoute.tsx", /useUnsavedChanges\(hasPeriodWorkspaceDraft\)/],
  ["classification and filing-regime election", "src/app/companies/[companyId]/periods/[periodId]/classify/page.tsx", /useUnsavedChanges\(dirty \|\| \(selectedRegime !== "" && !regimeConfirmed\)\)/],
  ["statutory notes", "src/app/companies/[companyId]/periods/[periodId]/notes/page.tsx", /useUnsavedChanges\(hasUnsavedEdits\)/],
  ["charity reporting", "src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx", /useUnsavedChanges\(hasCharityDraft\)/],
  ["year-end questionnaire", "src/components/period/YearEndQuestionnaireRoute.tsx", /useUnsavedChanges\(yearEndDraftDirty\)/],
  ["loan editor", "src/components/LoansManager.tsx", /useUnsavedChanges\(loanFormDirty\)/],
  ["director-loan editor", "src/components/DirectorLoansManager.tsx", /useUnsavedChanges\(directorLoanFormDirty\)/],
  ["share-capital editor", "src/components/ShareCapitalCard.tsx", /useUnsavedChanges\(shareCapitalFormDirty\)/],
  ["quarantined-company recovery", "src/components/dashboard/QuarantinedCompanyRecoveryPanel.tsx", /useUnsavedChanges\(selected !== null && \(confirmation !== "" \|\| reason !== ""\)\)/],
  ["incomplete money entry", "src/components/workbench.tsx", /useUnsavedChanges\(hasUncommittedMoneyDraft\)/],
];

test("every materially editable accounting surface registers a semantic dirty-state blocker", () => {
  for (const [label, relativePath, expectedGuard] of editableSurfaces) {
    const routeSource = source(relativePath);
    assert.match(routeSource, expectedGuard, `${label} must register its complete draft state`);
  }
});

test("editable routes with programmatic navigation use the guarded App Router", () => {
  for (const relativePath of [
    "src/app/companies/new/page.tsx",
    "src/app/companies/[companyId]/page.tsx",
  ]) {
    const routeSource = source(relativePath);
    assert.match(routeSource, /useGuardedRouter\(\)/, `${relativePath} should use the guarded router`);
    assert.doesNotMatch(routeSource, /import\s+\{[^}]*useRouter[^}]*\}\s+from\s+["']next\/navigation["']/, `${relativePath} must not bypass the guard`);
  }

  assert.match(source("src/app/companies/new/page.tsx"), /router\.pushAfterSave\(/, "successful onboarding should bypass only after persistence");
  assert.match(source("src/app/companies/[companyId]/page.tsx"), /router\.pushAfterSave\(/, "successful quarantine should bypass only after persistence");
});

test("the root provider covers Link, router, history, and unload navigation without monkey-patching globals", () => {
  const providerSource = source("src/components/UnsavedChangesProvider.tsx");
  const hookSource = source("src/lib/useUnsavedChanges.ts");
  const providersSource = source("src/app/providers.tsx");

  assert.match(providersSource, /<UnsavedChangesProvider>/, "the guard must wrap the application once at the root");
  assert.match(providersSource, /useGuardedRouter\(\)/, "HeroUI navigation must flow through the guarded router");
  assert.match(source("src/components/AppNavbar.tsx"), /useUnsavedNavigationGuard\(\)/, "sign-out must confirm before destroying a mounted draft");
  assert.match(providerSource, /document\.addEventListener\("click", handleClick, true\)/, "Link and breadcrumb clicks should be captured before navigation");
  assert.match(providerSource, /window\.addEventListener\("popstate", handlePopState\)/, "browser back should be guarded through popstate");
  assert.match(providerSource, /window\.addEventListener\("beforeunload", handleBeforeUnload\)/, "refresh and close should retain native unload protection");
  assert.match(providerSource, /dialogRole="alertdialog"/, "discard confirmation should use an accessible alert dialog");
  assert.match(hookSource, /push:[\s\S]*request\([\s\S]*"push"\)/, "router.push should be guarded");
  assert.match(hookSource, /replace:[\s\S]*request\([\s\S]*"replace"\)/, "router.replace should be guarded");
  assert.match(hookSource, /back: \(\) => request\([\s\S]*"back"\)/, "router.back should be guarded");

  for (const forbidden of [
    /history\.pushState\s*=/,
    /history\.replaceState\s*=/,
    /window\.onbeforeunload\s*=/,
    /router\.push\s*=/,
    /router\.replace\s*=/,
    /router\.back\s*=/,
  ]) {
    assert.doesNotMatch(`${providerSource}\n${hookSource}`, forbidden, "navigation globals must never be monkey-patched");
  }
});

test("clean defaults are not treated as accounting drafts", () => {
  const onboardingSource = source("src/app/companies/new/page.tsx");
  assert.doesNotMatch(onboardingSource, /hasOnboardingDraft = useMemo\(\(\) =>\s*step !== 0/, "moving between clean wizard steps must not prompt");

  const companySource = source("src/app/companies/[companyId]/page.tsx");
  assert.match(companySource, /governanceCodeCompliant \?\? null/, "an unanswered clean charity form must remain clean");
});

test("local tabs and accordions retain mounted draft state instead of silently discarding it", () => {
  const sectionSource = source("src/components/period/YearEndQuestionnaireSection.tsx");
  assert.match(sectionSource, /const \[hasOpened, setHasOpened\] = useState\(defaultOpen\)/, "opened year-end editors should stay mounted after collapse");
  assert.match(sectionSource, /hidden=\{!open\}/, "collapsed editors should be hidden rather than destroyed");

  const periodSource = source("src/components/period/PeriodWorkspaceRoute.tsx");
  assert.match(periodSource, /const \[selectedWorkspaceTab,[\s\S]*const \[bankForm,/, "period-tab drafts should be owned above conditional tab panels");
  const charitySource = source("src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx");
  assert.match(charitySource, /const \[newFundName,[\s\S]*const \[activeTab,/, "charity-tab drafts should be owned above conditional tab panels");
});
