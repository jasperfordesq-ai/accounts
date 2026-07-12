import type { z } from "zod";

import type { components, paths } from "@/lib/generated/accounts-api-v1";
import type {
  authUserSchema,
  balanceSheetSchema,
  corporationTaxFilingSupportResponseSchema,
  filingReadinessProfileSchema,
  mfaChallengeSchema,
  profitAndLossSchema,
  taxComputationSchema,
  trialBalanceLineSchema,
} from "@/lib/apiContracts";

/**
 * Compile-time bridge between the backend-owned OpenAPI document and the frontend's deliberately
 * stricter Zod runtime parsers. OpenAPI describes the accepted JSON wire shape; Zod continues to
 * reject coercible numeric strings and applies financial invariants after transport validation.
 */
type JsonResponse<
  Path extends keyof paths,
  Method extends keyof paths[Path],
  Status extends PropertyKey,
> = paths[Path][Method] extends {
  responses: infer Responses;
}
  ? Status extends keyof Responses
    ? Responses[Status] extends {
        content: { "application/json": infer Payload };
      }
      ? Payload
      : never
    : never
  : never;

type JsonRequest<
  Path extends keyof paths,
  Method extends keyof paths[Path],
> = paths[Path][Method] extends {
  requestBody: { content: { "application/json": infer Payload } };
}
  ? Payload
  : never;

type Equal<Left, Right> =
  (<Value>() => Value extends Left ? 1 : 2) extends
  (<Value>() => Value extends Right ? 1 : 2)
    ? true
    : false;
type Assert<Condition extends true> = Condition;
type NotNever<Value> = [Value] extends [never] ? false : true;

export type AuthWireResponse = JsonResponse<"/api/auth/me", "get", 200>;
export type LoginWireRequest = JsonRequest<"/api/auth/login", "post">;
export type MfaChallengeWireResponse = JsonResponse<"/api/auth/login", "post", 202>;
export type ProfitAndLossWireResponse = JsonResponse<
  "/api/companies/{companyId}/periods/{periodId}/statements/profit-and-loss",
  "get",
  200
>;
export type BalanceSheetWireResponse = JsonResponse<
  "/api/companies/{companyId}/periods/{periodId}/statements/balance-sheet",
  "get",
  200
>;
export type TaxComputationWireResponse = JsonResponse<
  "/api/companies/{companyId}/periods/{periodId}/revenue/tax-computation",
  "get",
  200
>;
export type CorporationTaxFilingSupportWireResponse = JsonResponse<
  "/api/companies/{companyId}/periods/{periodId}/revenue/filing-support",
  "get",
  200
>;
export type FilingReadinessProfileWireResponse = JsonResponse<
  "/api/companies/{companyId}/periods/{periodId}/filing/readiness-profile",
  "get",
  200
>;

type _CriticalResponsesExist = [
  Assert<NotNever<AuthWireResponse>>,
  Assert<NotNever<MfaChallengeWireResponse>>,
  Assert<NotNever<ProfitAndLossWireResponse>>,
  Assert<NotNever<BalanceSheetWireResponse>>,
  Assert<NotNever<TaxComputationWireResponse>>,
  Assert<NotNever<CorporationTaxFilingSupportWireResponse>>,
  Assert<NotNever<FilingReadinessProfileWireResponse>>,
];

type _CriticalRequestsExist = [
  Assert<NotNever<LoginWireRequest>>,
  Assert<Equal<keyof LoginWireRequest, "tenantSlug" | "email" | "password">>,
];

// Top-level key equality makes a backend field addition/removal fail tsc until the corresponding
// runtime parser is deliberately updated. Nested schemas remain independently emitted as OpenAPI
// components and are checked by the generated paths above plus existing adversarial parser tests.
type _CriticalRuntimeParserKeys = [
  Assert<Equal<keyof z.output<typeof authUserSchema>, keyof components["schemas"]["AuthResponse"]>>,
  Assert<Equal<keyof z.output<typeof mfaChallengeSchema>, keyof components["schemas"]["MfaChallengeResponse"]>>,
  Assert<Equal<keyof z.output<typeof profitAndLossSchema>, keyof components["schemas"]["ProfitAndLoss"]>>,
  Assert<Equal<keyof z.output<typeof balanceSheetSchema>, keyof components["schemas"]["BalanceSheet"]>>,
  Assert<Equal<keyof z.output<typeof taxComputationSchema>, keyof components["schemas"]["TaxComputation"]>>,
  Assert<
    Equal<
      keyof z.output<typeof corporationTaxFilingSupportResponseSchema>,
      keyof components["schemas"]["Response"]
    >
  >,
  Assert<
    Equal<
      keyof z.output<typeof filingReadinessProfileSchema>,
      keyof components["schemas"]["FilingReadinessProfile"]
    >
  >,
  Assert<
    Equal<
      keyof z.output<typeof trialBalanceLineSchema>,
      keyof components["schemas"]["TrialBalanceLine"]
    >
  >,
];

export type CriticalApiContractAssertions = [
  _CriticalRequestsExist,
  _CriticalResponsesExist,
  _CriticalRuntimeParserKeys,
];

export const criticalOpenApiPaths = [
  "/api/auth/login",
  "/api/auth/me",
  "/api/companies/{companyId}/periods/{periodId}/statements/trial-balance",
  "/api/companies/{companyId}/periods/{periodId}/statements/profit-and-loss",
  "/api/companies/{companyId}/periods/{periodId}/statements/balance-sheet",
  "/api/companies/{companyId}/periods/{periodId}/revenue/tax-computation",
  "/api/companies/{companyId}/periods/{periodId}/revenue/filing-support",
  "/api/companies/{companyId}/periods/{periodId}/filing/readiness-profile",
] as const satisfies readonly (keyof paths)[];
