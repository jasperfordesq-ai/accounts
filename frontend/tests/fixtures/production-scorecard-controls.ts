import type {
  ProductionScorecardAssuranceClass,
  ProductionScorecardControl,
} from "../../src/lib/api.ts";

type ProductionScorecardCategoryCode =
  | "architecture-documentation"
  | "backend-statutory-accounting-engine"
  | "frontend-accountant-workbench"
  | "security-auth-tenant-platform-guardrails";

const CONTROLS: Record<ProductionScorecardCategoryCode, ProductionScorecardControl[]> = {
  "architecture-documentation": [
    control("canonical-engineering-guidance", "Canonical engineering guidance", 35, "code", true),
    control("release-runbook-contract", "Release runbook and evidence contract", 32, "machine", true),
    control("typed-readiness-evidence-contract", "Control-derived typed readiness assessment", 30, "machine", true),
    control(
      "canonical-documentation-reconciliation",
      "Canonical documentation matches the implementation",
      18,
      "code",
      true,
    ),
    control(
      "independent-release-review",
      "Independent legal, professional and operational review",
      35,
      "human-external",
      false,
      ["HUMAN-001", "HUMAN-002", "HUMAN-003", "HUMAN-004", "HUMAN-005", "HUMAN-006", "HUMAN-007"],
    ),
  ],
  "backend-statutory-accounting-engine": [
    control("accounting-engine-baseline", "Accounting engine and statement baseline", 65, "code", true),
    control("statutory-workflow-baseline", "Statutory workflow baseline", 45, "code", true),
    control("document-generation-baseline", "PDF, tax and iXBRL generation baseline", 35, "code", true),
    control("golden-corpus-baseline", "Automated golden corpus baseline", 35, "machine", true),
    control("filing-evidence-baseline", "Filing evidence and handoff baseline", 25, "machine", true),
    control(
      "final-release-containment",
      "Central exact-hash final release containment",
      10,
      "code",
      true,
    ),
    control(
      "accounting-correctness-remediation",
      "Accounting and classification correctness",
      35,
      "code",
      true,
    ),
    control(
      "statutory-output-correctness",
      "Statutory output and disclosure correctness",
      30,
      "code",
      false,
      ["P1-STAT-007", "P1-TAX-001", "P1-TAX-002"],
    ),
    control(
      "external-ixbrl-acceptance",
      "Complete externally validated Revenue iXBRL",
      35,
      "human-external",
      false,
      ["HUMAN-003"],
    ),
    control(
      "professional-artifact-approval",
      "Verified professional approval and signed auditor evidence",
      20,
      "human-external",
      false,
      ["HUMAN-004", "HUMAN-005"],
    ),
    control(
      "independent-golden-corpus",
      "Independently derived golden corpus",
      15,
      "human-external",
      false,
      ["P0-QA-001"],
    ),
  ],
  "frontend-accountant-workbench": [
    control("workbench-primitives", "Shared accountant workbench primitives", 35, "code", true),
    control("primary-route-baseline", "Primary accountant route baseline", 40, "code", true),
    control("frontend-readiness-contract", "Frontend readiness API contract", 30, "machine", true),
    control("visual-smoke-baseline", "Visual smoke evidence baseline", 28, "machine", true),
    control("role-aware-ui-baseline", "Role-aware UI baseline", 25, "code", true),
    control(
      "pagination-and-resource-state",
      "Complete pagination and truthful resource states",
      25,
      "code",
      true,
    ),
    control(
      "workflow-correctness",
      "Dashboard, permissions, onboarding and response-contract correctness",
      20,
      "code",
      true,
    ),
    control(
      "accessible-responsive-workbench",
      "Accessible and responsive accountant workbench",
      25,
      "machine",
      false,
      ["P1-UX-001", "P1-UX-002", "P1-UX-003", "P1-A11Y-001", "P1-VIS-002", "P1-FE-011"],
    ),
    control(
      "complete-visual-acceptance",
      "Complete visual-state matrix and named review",
      22,
      "human-external",
      false,
      ["P1-VIS-001", "HUMAN-001"],
    ),
  ],
  "security-auth-tenant-platform-guardrails": [
    control("session-csrf-password-baseline", "Session, CSRF and password baseline", 35, "code", true),
    control("tenant-access-baseline", "Tenant access-control baseline", 30, "code", true),
    control("production-startup-safety", "Production startup safety", 25, "machine", true),
    control("audit-monitoring-baseline", "Audit-integrity and monitoring baseline", 25, "machine", true),
    control("no-direct-filing-evidence", "No-direct-filing and evidence baseline", 25, "machine", true),
    control(
      "persistence-boundaries",
      "Overposting and persistence-boundary enforcement",
      30,
      "code",
      true,
    ),
    control(
      "concurrency-and-deletion",
      "Concurrent finalisation and recoverable deletion",
      20,
      "code",
      true,
    ),
    control(
      "privileged-identity-lifecycle",
      "Privileged identity lifecycle, MFA and recent authentication",
      25,
      "code",
      true,
    ),
    control(
      "supply-chain-and-operations",
      "Supply-chain, governance and production operations",
      25,
      "machine",
      false,
      ["P1-OPS-003", "P1-OPS-004", "P1-OPS-005", "P1-OPS-006", "P1-OPS-008", "P2-FE-009"],
    ),
    control(
      "defence-in-depth-and-resilience",
      "Database isolation, privacy and resilience",
      10,
      "machine",
      false,
      ["P2-EVID-001", "P2-OPS-010"],
    ),
  ],
};

export function productionScorecardControls(categoryCode: ProductionScorecardCategoryCode): ProductionScorecardControl[] {
  return CONTROLS[categoryCode].map((item) => ({
    ...item,
    evidence: [...item.evidence],
    blockingAuditItemIds: [...item.blockingAuditItemIds],
  }));
}

function control(
  code: string,
  label: string,
  weight: number,
  assuranceClass: ProductionScorecardAssuranceClass,
  passed: boolean,
  blockingAuditItemIds: string[] = [],
): ProductionScorecardControl {
  return {
    code,
    label,
    weight,
    assuranceClass,
    status: passed ? "passed" : "open",
    passed,
    evidence: [`${label} evidence fixture.`],
    blockingAuditItemIds,
  };
}
