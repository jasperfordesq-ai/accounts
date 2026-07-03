import { AlertTriangle, Building2, MapPin, ShieldCheck } from "lucide-react";
import type { ReactNode } from "react";
import type { Company } from "@/lib/api";
import { formatCompanyType, formatDateIE } from "@/lib/format";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

export function CompanyStatutoryProfile({ company }: { company: Company }) {
  const manualReason = manualHandoffReason(company);
  const addressLines = registeredOfficeLines(company);
  const flags = profileFlags(company);

  return (
    <ReviewPanel
      title="Statutory Profile"
      description="Company identity, registered office and support-path gate."
      actions={
        <StatusBadge tone={manualReason ? "bad" : "good"}>
          {manualReason ? "Manual handoff" : "Core private workflow"}
        </StatusBadge>
      }
    >
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(320px,0.8fr)]">
        <div className="grid gap-3 md:grid-cols-2">
          <ProfileBlock
            icon={<Building2 className="h-4 w-4" />}
            title="Registration"
            rows={[
              `CRO ${company.croNumber || "-"}`,
              `Tax ${company.taxReference || "-"}`,
              formatCompanyType(company.companyType),
              `Incorporated ${formatDateIE(company.incorporationDate)}`,
            ]}
          />
          <ProfileBlock
            icon={<MapPin className="h-4 w-4" />}
            title="Registered Office"
            rows={addressLines.length > 0 ? addressLines : ["No address recorded"]}
          />
        </div>

        <div className="space-y-3">
          <div className={`rounded-md border p-3 ${manualReason ? "border-red-200 bg-red-50 dark:border-red-900 dark:bg-red-950/40" : "border-emerald-200 bg-emerald-50 dark:border-emerald-800 dark:bg-emerald-950/40"}`}>
            <div className="flex items-start gap-3">
              {manualReason ? (
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-700 dark:text-red-200" />
              ) : (
                <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-emerald-700 dark:text-emerald-200" />
              )}
              <div>
                <p className={`text-sm font-semibold ${manualReason ? "text-red-900 dark:text-red-100" : "text-emerald-900 dark:text-emerald-100"}`}>
                  {manualReason ? "Manual professional review required" : "Supported core company path"}
                </p>
                <p className={`mt-1 text-xs leading-5 ${manualReason ? "text-red-800 dark:text-red-100" : "text-emerald-800 dark:text-emerald-100"}`}>
                  {manualReason ?? "LTD, DAC and CLG workflows can continue through the core workbench, subject to filing readiness gates."}
                </p>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            {flags.map((flag) => (
              <StatusBadge key={flag.label} tone={flag.tone}>{flag.label}</StatusBadge>
            ))}
          </div>
        </div>
      </div>
    </ReviewPanel>
  );
}

function ProfileBlock({
  icon,
  title,
  rows,
}: {
  icon: ReactNode;
  title: string;
  rows: string[];
}) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <div className="flex items-center gap-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]">
        {icon}
        {title}
      </div>
      <div className="mt-3 space-y-1 text-sm text-[var(--foreground)]">
        {rows.map((row) => (
          <div key={row} className="break-words">{row}</div>
        ))}
      </div>
    </div>
  );
}

function registeredOfficeLines(company: Company) {
  const lines = [
    company.registeredOfficeAddress1,
    company.registeredOfficeAddress2,
    company.registeredOfficeCity,
    company.registeredOfficeCounty ? `Co. ${company.registeredOfficeCounty}` : undefined,
    company.registeredOfficeEircode,
  ];

  return lines.filter((line): line is string => Boolean(line?.trim()));
}

function profileFlags(company: Company): { label: string; tone: "default" | "good" | "warn" | "bad" | "info" }[] {
  const flags: { label: string; tone: "default" | "good" | "warn" | "bad" | "info" }[] = [];

  flags.push(company.isTrading ? { label: "Trading", tone: "good" } : { label: "Non-trading", tone: "warn" });
  if (company.isDormant) flags.push({ label: "Dormant", tone: "warn" });
  if (company.isCharitableOrganisation) flags.push({ label: "Charity workflow", tone: "info" });
  if (company.isVatRegistered) flags.push({ label: "VAT registered", tone: "info" });
  if (company.isEmployer) flags.push({ label: "Employer", tone: "info" });
  if (company.isGroupMember || company.isHolding || company.isSubsidiary) flags.push({ label: "Group context", tone: "warn" });
  if (company.isListedSecurities || company.isCreditInstitution || company.isInsuranceUndertaking || company.isPensionFund) {
    flags.push({ label: "Regulated/excluded entity", tone: "bad" });
  }

  return flags;
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
