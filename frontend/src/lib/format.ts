export function formatCompanyType(value?: string): string {
  if (!value) return "Company";

  const labels: Record<string, string> = {
    Private: "LTD",
    PrivateUnlimited: "Unlimited company",
    DesignatedActivityCompany: "DAC",
    CompanyLimitedByGuarantee: "CLG",
    PublicLimitedCompany: "PLC",
    LTD: "LTD",
    DAC: "DAC",
    CLG: "CLG",
    PLC: "PLC",
    UC: "Unlimited company",
  };

  return labels[value] ?? value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function formatDateIE(value?: string): string {
  if (!value) return "-";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return date.toLocaleDateString("en-IE", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
}

export function formatPeriodRange(start?: string, end?: string): string {
  if (!start || !end) return "Period not set";
  return `${formatDateIE(start)} to ${formatDateIE(end)}`;
}
