import { AlertTriangle, ArrowRight, Building2, CheckCircle2, FileText, ShieldCheck, Users } from "lucide-react";
import Link from "next/link";
import type { AccountingPeriod, Company } from "@/lib/api";
import { formatCompanyType, formatPeriodRange } from "@/lib/format";
import { IssueDigest, MetricStrip, ReviewPanel, StatusBadge, WorkflowRail, type WorkflowItem } from "@/components/workbench";

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

  const workflowItems: WorkflowItem[] = [
    {
      id: "setup",
      label: "Setup",
      detail: company.legalName && company.companyType ? formatCompanyType(company.companyType) : "Identity incomplete",
      state: company.legalName && company.companyType ? "done" : "active",
      icon: <Building2 className="h-4 w-4 text-sky-600 dark:text-sky-300" />,
    },
    {
      id: "officers",
      label: "Officers",
      detail: officerCount > 0 ? `${officerCount} recorded` : "Director and secretary evidence required",
      state: officerCount > 0 ? "done" : "active",
      icon: <Users className="h-4 w-4 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "profile",
      label: "Statutory profile",
      detail: manualReason ? "Manual handoff risk" : "Core support path",
      state: manualReason ? "blocked" : "done",
      icon: <ShieldCheck className="h-4 w-4 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "periods",
      label: "Periods",
      detail: periods.length > 0 ? `${periods.length} production periods` : "No accounting period yet",
      state: periods.length > 0 ? "done" : missingItems.length > 0 ? "todo" : "active",
      href: nextAction.code === "open-latest-period" ? nextAction.href ?? undefined : undefined,
      icon: <FileText className="h-4 w-4 text-cyan-600 dark:text-cyan-300" />,
    },
    {
      id: "review",
      label: "Review",
      detail: manualReason ? "Professional ownership required" : latestPeriod ? "Open latest workbench" : "Waiting for period",
      state: manualReason ? "blocked" : latestPeriod ? "active" : "todo",
      href: latestPeriod ? `/companies/${company.id}/periods/${latestPeriod.id}` : undefined,
      icon: manualReason
        ? <AlertTriangle className="h-4 w-4 text-red-600 dark:text-red-300" />
        : <CheckCircle2 className="h-4 w-4 text-emerald-600 dark:text-emerald-300" />,
    },
  ];

  return (
    <div className="mb-8 space-y-4">
      <ReviewPanel
        title="Company command centre"
        description="Company-level support path, setup readiness and next accountant action before entering a period workspace."
        actions={<StatusBadge tone={supportTone}>{manualReason ? "Manual professional review required" : "Core workflow"}</StatusBadge>}
      >
        <div className="grid overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] md:grid-cols-3 md:divide-x md:divide-y-0 divide-y divide-[var(--border)]">
          <div className="min-w-0 p-4">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">What is wrong?</p>
            <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">
              {manualReason ? "Manual professional review required" : missingItems.length > 0 ? `${missingItems.length} setup gaps` : "No company-level blockers"}
            </p>
            <p className={`mt-3 text-sm leading-6 ${manualReason ? "text-red-800 dark:text-red-100" : missingItems.length > 0 ? "text-amber-800 dark:text-amber-100" : "text-emerald-800 dark:text-emerald-100"}`}>
              {manualReason ?? missingItems[0] ?? "Company setup is ready for period-level review."}
            </p>
          </div>

          <div className="min-w-0 p-4">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">What is ready?</p>
            <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">
              {readyItems.length} setup {readyItems.length === 1 ? "area" : "areas"} ready
            </p>
            <p className="mt-3 text-sm leading-6 text-[var(--muted-foreground)]">
              {readyItems.length > 0 ? readyItems.join(", ") : "No setup areas are complete yet"}
            </p>
          </div>

          <div className="min-w-0 p-4">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">What must I do next?</p>
            <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">{nextAction.label}</p>
            <p className="mt-3 text-sm leading-6 text-[var(--muted-foreground)]">{nextAction.detail}</p>
            {nextAction.href && (
              <Link
                href={nextAction.href}
                className="mt-4 inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
              >
                {nextAction.label}
                <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            )}
          </div>
        </div>
      </ReviewPanel>

      <MetricStrip
        metrics={[
          { label: "Officers", value: `${officerCount} ${officerCount === 1 ? "officer" : "officers"}`, tone: officerCount > 0 ? "good" : "warn" },
          { label: "Periods", value: `${periods.length} ${periods.length === 1 ? "period" : "periods"}`, tone: periods.length > 0 ? "good" : "warn" },
          { label: "Latest size", value: latestSize, tone: latestSize === "Unclassified" ? "warn" : "good" },
          { label: "Latest regime", value: latestRegime, tone: latestRegime === "Regime not elected" ? "warn" : "good" },
        ]}
      />

      <WorkflowRail title="Company setup workflow" items={workflowItems} />

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
