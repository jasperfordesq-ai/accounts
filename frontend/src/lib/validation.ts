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
    .optional()
    .or(z.literal(""))
    .refine(
      (val) => {
        if (!val) return true;
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
      companyAddressSchema.parse(data);
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
