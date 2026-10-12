import { z } from "zod";
import { ApiError, postApiJson } from "./api.ts";

// Adopting the charity / CLG chart of accounts on an existing company: a dry-run plan, then the
// execution of exactly that plan (identified by its SHA-256). Transactions are never moved.

const role = z.string().min(1);
const sha256 = z.string().regex(/^[a-f0-9]{64}$/);

const renameSchema = z.object({
  categoryId: z.number().int(),
  role,
  fromCode: z.string(),
  fromName: z.string(),
  toCode: z.string(),
  toName: z.string(),
});

const legacyRecodeSchema = z.object({
  categoryId: z.number().int(),
  fromCode: z.string(),
  fromName: z.string(),
  toCode: z.string(),
  toName: z.string(),
  transactionCount: z.number().int().nonnegative(),
  journalCount: z.number().int().nonnegative(),
  openingBalanceCount: z.number().int().nonnegative(),
  ruleCount: z.number().int().nonnegative(),
  reason: z.enum(["CharityCode", "CharityCodeRange", "CharityRole"]),
  releasedRole: role.nullable(),
});

const archiveSchema = z.object({
  categoryId: z.number().int(),
  fromCode: z.string(),
  name: z.string(),
  toCode: z.string(),
  ruleCount: z.number().int().nonnegative(),
  releasedRole: role.nullable(),
});

const retainedSchema = z.object({
  categoryId: z.number().int(),
  code: z.string(),
  name: z.string(),
  transactionCount: z.number().int().nonnegative(),
  journalCount: z.number().int().nonnegative(),
  openingBalanceCount: z.number().int().nonnegative(),
});

const insertSchema = z.object({
  code: z.string(),
  name: z.string(),
  type: z.string(),
  statementLine: z.string(),
  cashFlowClass: z.string(),
  systemRole: role.nullable(),
});

const staleArtifactSchema = z.object({
  periodId: z.number().int(),
  periodStart: z.iso.date(),
  periodEnd: z.iso.date(),
  status: z.string(),
  croPackageStatus: z.string(),
  croFilingStatus: z.string(),
});

export const chartAdoptionPlanSchema = z.object({
  companyId: z.number().int(),
  currentVariant: z.enum(["GenericTrading", "CharityClg"]),
  targetVariant: z.literal("CharityClg"),
  renames: z.array(renameSchema),
  legacyRecodes: z.array(legacyRecodeSchema),
  archives: z.array(archiveSchema),
  retained: z.array(retainedSchema),
  inserts: z.array(insertSchema),
  staleFilingArtifacts: z.array(staleArtifactSchema),
  blockedBy: z.array(z.string()),
  planSha256: sha256,
});

export type ChartAdoptionPlan = z.infer<typeof chartAdoptionPlanSchema>;

const refusalSchema = z.object({
  error: z.string(),
  code: z.string(),
  plan: chartAdoptionPlanSchema,
});

export type ChartAdoptionRefusal = z.infer<typeof refusalSchema>;

const base = (companyId: number) => `/api/companies/${companyId}/categories/chart-adoption`;

/** The dry run: changes nothing. */
export const getChartAdoptionPlan = (companyId: number) =>
  postApiJson<ChartAdoptionPlan>(`${base(companyId)}/plan`, {}, chartAdoptionPlanSchema, "chart adoption plan");

/** Executes exactly the reviewed plan; the server refuses (409) if anything changed since. */
export const adoptCharityChart = (companyId: number, plan: ChartAdoptionPlan, acknowledgeStaleFilingArtifacts: boolean) =>
  postApiJson<ChartAdoptionPlan>(
    base(companyId),
    { variant: "CharityClg", planSha256: plan.planSha256, acknowledgeStaleFilingArtifacts },
    chartAdoptionPlanSchema,
    "adopted chart of accounts plan",
  );

/** A 409 refusal carries the current plan, so the owner can review what changed. */
export function chartAdoptionRefusal(error: unknown): ChartAdoptionRefusal | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  try {
    const parsed = refusalSchema.safeParse(JSON.parse(error.body));
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}
