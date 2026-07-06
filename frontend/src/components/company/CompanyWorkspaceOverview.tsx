import type { AccountingPeriod, Company } from "@/lib/api";
import { formatPeriodRange } from "@/lib/format";
import { IssueDigest, MetricStrip, ReviewPanel, StatusBadge, WorkflowDecisionSummary } from "@/components/workbench";
import { AccountantWorkflowRail } from "@/components/workbench/AccountantWorkflowRail";

export function CompanyWorkspaceOverview({ company }: { company: Company }) {
  const periods = orderedPeriods(company.periods ?? []);
  const latestPeriod = periods[0] ?? null;
  const officerCount = company.officers?.length ?? 0;
  const manualReason = manualHandoffReason(company);
  const readyItems = companyReadyItems(company, latestPeriod);
  const missingItems = companyMissingItems(company);
  const nextAction = companyNextAction(company, latestPeriod);
  const latestSize = latestPeriod?.sizeClassification?.calculatedClass ?? "Unclassified";
  const latestRegime = latestPeriod?.filingRegime?.electedRegime ?? "Regime not elected";
  const supportTone = manualReason ? "bad" : missingItems.length > 0 ? "warn" : "good";
  const activeWorkflowStage = manualReason ? "Review" : latestPeriod ? "Import" : "Setup";

  return (
    <div className="mb-8 space-y-4">
      <ReviewPanel
        title="Company command centre"
        description="Company-level support path, setup readiness and next accountant action before entering a period workspace."
        actions={<StatusBadge tone={supportTone}>{manualReason ? "Manual professional review required" : "Core workflow"}</StatusBadge>}
      >
        <WorkflowDecisionSummary
          items={[
            {
              title: "What is wrong?",
              tone: supportTone,
              summary: manualReason ? "Manual professional review required" : missingItems.length > 0 ? `${missingItems.length} setup gaps` : "No company-level blockers",
              detail: manualReason ?? missingItems[0] ?? "Company setup is ready for period-level review.",
            },
            {
              title: "What is ready?",
              tone: readyItems.length > 0 ? "good" : "warn",
              summary: `${readyItems.length} setup ${readyItems.length === 1 ? "area" : "areas"} ready`,
              detail: readyItems.length > 0 ? readyItems.join(", ") : "No setup areas are complete yet",
            },
            {
              title: "What must I do next?",
              tone: nextAction.code === "open-latest-period" ? "info" : "warn",
              summary: nextAction.label,
              detail: nextAction.detail,
              action: nextAction.href ? { href: nextAction.href, label: nextAction.label } : undefined,
            },
          ]}
        />
      </ReviewPanel>

      <MetricStrip
        metrics={[
          { label: "Officers", value: `${officerCount} ${officerCount === 1 ? "officer" : "officers"}`, tone: officerCount > 0 ? "good" : "warn" },
          { label: "Periods", value: `${periods.length} ${periods.length === 1 ? "period" : "periods"}`, tone: periods.length > 0 ? "good" : "warn" },
          { label: "Latest size", value: latestSize, tone: latestSize === "Unclassified" ? "warn" : "good" },
          { label: "Latest regime", value: latestRegime, tone: latestRegime === "Regime not elected" ? "warn" : "good" },
        ]}
      />

      <AccountantWorkflowRail activeStage={activeWorkflowStage} />

      <IssueDigest
        title="Company-level issue digest"
        description="Resolve company setup gaps before relying on period-level filing readiness."
        blockers={manualReason ? [manualReason] : []}
        warnings={manualReason ? missingItems : missingItems}
      />
    </div>
  );
}

function companyReadyItems(company: Company, latestPeriod: AccountingPeriod | null) {
  const items: string[] = [];
  if ((company.officers?.length ?? 0) > 0) items.push("Officers recorded");
  if (company.registeredOfficeAddress1 && company.registeredOfficeCity) items.push("registered office recorded");
  if (latestPeriod) items.push("latest period exists");
  return items;
}

function companyMissingItems(company: Company) {
  const missing: string[] = [];
  if ((company.officers?.length ?? 0) === 0) missing.push("Director and secretary evidence is missing.");
  if (!company.registeredOfficeAddress1 || !company.registeredOfficeCity) missing.push("Registered office evidence is incomplete.");
  if ((company.periods?.length ?? 0) === 0) missing.push("Create an accounting period to start the year-end workflow.");
  return missing;
}

function companyNextAction(company: Company, latestPeriod: AccountingPeriod | null) {
  if ((company.officers?.length ?? 0) === 0) {
    return {
      code: "record-officers",
      label: "Record officers",
      detail: "Add director and secretary evidence before relying on filing readiness.",
      href: undefined,
    };
  }

  if (!company.registeredOfficeAddress1 || !company.registeredOfficeCity) {
    return {
      code: "complete-registered-office",
      label: "Complete registered office",
      detail: "Record the registered office address before preparing statutory outputs.",
      href: undefined,
    };
  }

  if (!latestPeriod) {
    return {
      code: "create-period",
      label: "Create accounting period",
      detail: "Create a period to start import, classification, year-end and filing review.",
      href: undefined,
    };
  }

  return {
    code: "open-latest-period",
    label: "Open latest period",
    detail: formatPeriodRange(latestPeriod.periodStart, latestPeriod.periodEnd),
    href: `/companies/${company.id}/periods/${latestPeriod.id}`,
  };
}

function orderedPeriods(periods: AccountingPeriod[]) {
  return [...periods].sort((a, b) => b.periodEnd.localeCompare(a.periodEnd));
}

function manualHandoffReason(company: Company) {
  if (company.companyType === "PublicLimitedCompany" || company.companyType === "PLC") {
    return "PLC/public-company workflow requires manual review.";
  }

  if (company.companyType === "PrivateUnlimited" || company.companyType === "UC") {
    return "Unlimited-company filing variants require manual review.";
  }

  if (company.isListedSecurities || company.isCreditInstitution || company.isInsuranceUndertaking || company.isPensionFund) {
    return "Regulated or Fifth Schedule excluded entity requires manual review.";
  }

  if (company.isGroupMember || company.isHolding || company.isSubsidiary) {
    return "Group or consolidation context requires manual review.";
  }

  return null;
}
