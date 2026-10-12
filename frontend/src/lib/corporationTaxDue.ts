import { z } from "zod";
import { postApiJson, type TaxBalance } from "./api.ts";

// A company holding a charitable tax exemption gets no automatic corporation-tax provision. An entered
// CT liability blocks final outputs until an Owner or Reviewer confirms it is due (for example on
// income outside the exemption); it is then provided for. It counts only for the liability it was
// given for, and saving a different liability clears it. The confirmation also attests the taxable profit
// outside the exemption: the corporation-tax computation removes the income within the exemption.

/** A tax balance as the API returns it, with the due confirmation of an exempt company's CT. */
export type ConfirmableTaxBalance = TaxBalance & {
  confirmedDueBy?: string | null;
  confirmedDueAt?: string | null;
  confirmedDueReason?: string | null;
  /** The liability the confirmation was given for; it counts only while it equals the liability. */
  confirmedDueLiability?: number | null;
  /** The attested taxable profit from activities outside the charitable exemption. */
  confirmedTaxableProfitOutsideExemption?: number | null;
};

const confirmedTaxBalanceSchema = z.object({
  id: z.number().int().positive(),
  periodId: z.number().int().positive(),
  taxType: z.literal("CorporationTax"),
  liability: z.number().finite().positive(),
  paid: z.number().finite(),
  balance: z.number().finite(),
  confirmedDueBy: z.string().trim().min(1),
  confirmedDueAt: z.string().min(1),
  confirmedDueReason: z.string().trim().min(1),
  confirmedDueLiability: z.number().finite().positive(),
  confirmedTaxableProfitOutsideExemption: z.number().finite().nonnegative(),
}).refine((balance) => balance.confirmedDueLiability === balance.liability, {
  path: ["confirmedDueLiability"],
  message: "The confirmation must be for the current liability",
});

/**
 * Owner/Reviewer, with recent verification: confirms the saved CT liability is due, with the reason and
 * the attested taxable profit from activities outside the charitable exemption.
 */
export function confirmCorporationTaxDue(companyId: number, periodId: number, reason: string, taxableProfitOutsideExemption: number) {
  return postApiJson<ConfirmableTaxBalance>(
    `/api/companies/${companyId}/periods/${periodId}/tax-balances/CorporationTax/confirm-due`,
    { reason, taxableProfitOutsideExemption },
    confirmedTaxBalanceSchema,
    "corporation tax due confirmation",
  );
}
