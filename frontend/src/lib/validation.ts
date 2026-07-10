import { z } from "zod";

/** Validation schema for company creation (step 1 — legal details) */
export const companyLegalSchema = z.object({
  legalName: z
    .string()
    .min(1, "Legal name is required")
    .max(200, "Legal name must be under 200 characters"),
  tradingName: z.string().max(200).optional().or(z.literal("")),
  croNumber: z
    .string()
    .regex(/^[0-9]{0,10}$/, "CRO number must be numeric (up to 10 digits)")
    .optional()
    .or(z.literal("")),
  taxReference: z
    .string()
    .regex(/^[0-9]{0,7}[A-Za-z]?$/, "Tax reference should be up to 7 digits followed by an optional letter")
    .optional()
    .or(z.literal("")),
  companyType: z.string().min(1),
  incorporationDate: z
    .string()
    .min(1, "Incorporation date is required")
    .refine(
      (val) => {
        const d = new Date(val);
        return !isNaN(d.getTime()) && d <= new Date();
      },
      { message: "Incorporation date cannot be in the future" },
    ),
});

/** Validation schema for company address (step 3) */
export const companyAddressSchema = z.object({
  address1: z.string().max(200).optional().or(z.literal("")),
  address2: z.string().max(200).optional().or(z.literal("")),
  city: z.string().max(100).optional().or(z.literal("")),
  county: z.string().optional().or(z.literal("")),
  eircode: z
    .string()
    .regex(/^([A-Za-z0-9]{3}\s?[A-Za-z0-9]{4})?$/, "Invalid Eircode format (e.g. D02 AF30)")
    .optional()
    .or(z.literal("")),
});

/** Required first-period and opening-ledger setup submitted by the atomic onboarding command. */
export const companyOnboardingSetupSchema = companyAddressSchema.extend({
  incorporationDate: z.string().min(1, "Incorporation date is required"),
  annualReturnDate: z.string().min(1, "Exact Annual Return Date is required"),
  annualReturnDateEvidenceReference: z.string().trim().min(1, "A CRO evidence reference is required").max(300),
  firstPeriodEnd: z.string().min(1, "First-period end date is required"),
  bankAccountName: z.string().trim().min(1, "Bank account name is required").max(200),
  bankIban: z.string().max(34, "IBAN must be 34 characters or fewer").optional().or(z.literal("")),
  openingBalance: z.string().refine(
    (value) => Number.isFinite(Number(value)),
    "Opening balance must be a valid amount",
  ),
}).superRefine((value, context) => {
  const start = new Date(`${value.incorporationDate}T00:00:00Z`);
  const end = new Date(`${value.firstPeriodEnd}T00:00:00Z`);
  const annualReturnDate = new Date(`${value.annualReturnDate}T00:00:00Z`);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || Number.isNaN(annualReturnDate.getTime())) return;
  if (annualReturnDate < start) {
    context.addIssue({
      code: "custom",
      path: ["annualReturnDate"],
      message: "Annual Return Date cannot be before incorporation",
    });
  }
  if (end < start) {
    context.addIssue({
      code: "custom",
      path: ["firstPeriodEnd"],
      message: "First-period end cannot be before the incorporation date",
    });
    return;
  }
  const maximum = new Date(start);
  maximum.setUTCMonth(maximum.getUTCMonth() + 18);
  maximum.setUTCDate(maximum.getUTCDate() - 1);
  if (end > maximum) {
    context.addIssue({
      code: "custom",
      path: ["firstPeriodEnd"],
      message: "First accounting period cannot exceed 18 months",
    });
  }
});

/** Validation schema for officers (step 4) */
export const officerSchema = z.object({
  name: z.string().min(1, "Officer name is required").max(200),
  role: z.enum(["Director", "Secretary", "Chairperson", "Shareholder"]),
});

/** Validate a step and return field-level errors */
export function validateStep(
  step: number,
  data: Record<string, unknown>,
): Record<string, string> {
  const errors: Record<string, string> = {};

  try {
    if (step === 0) {
      companyLegalSchema.parse(data);
    } else if (step === 2) {
      companyOnboardingSetupSchema.parse(data);
    }
  } catch (err) {
    if (err instanceof z.ZodError) {
      for (const issue of err.issues) {
        const field = issue.path[0]?.toString();
        if (field && !errors[field]) {
          errors[field] = issue.message;
        }
      }
    }
  }

  return errors;
}
