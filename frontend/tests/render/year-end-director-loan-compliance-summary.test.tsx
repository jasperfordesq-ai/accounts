import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { YearEndDirectorLoanComplianceSummary } from "@/components/period/YearEndDirectorLoanComplianceSummary";
import type { DirectorLoanCompliance } from "@/lib/api";

describe("YearEndDirectorLoanComplianceSummary", () => {
  it("renders the strict relevant-assets result, blockers and retained legal sources", () => {
    render(<YearEndDirectorLoanComplianceSummary compliance={blockedCompliance} />);

    expect(screen.getByRole("heading", { name: "Director-loan statutory evidence" })).toBeInTheDocument();
    expect(screen.getByText("Final output blocked")).toBeInTheDocument();
    expect(screen.getByText(/not strictly below the section 240 threshold/i)).toBeInTheDocument();
    expect(screen.getByText("Maximum aggregate exposure")).toBeInTheDocument();
    expect(screen.getByText("Relevant assets")).toBeInTheDocument();
    expect(screen.getByText("Strict 10% threshold")).toBeInTheDocument();
    expect(screen.getByText("Not strictly below")).toBeInTheDocument();
    expect(screen.queryByText(/SAP required under s\.239/i)).not.toBeInTheDocument();
    expect(screen.getByText("Current statutory source links")).toBeInTheDocument();
  });

  it("renders nothing when there are no director-loan rows to review", () => {
    const { container } = render(
      <YearEndDirectorLoanComplianceSummary compliance={{ ...blockedCompliance, loans: [] }} />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});

const legalSources = Array.from({ length: 13 }, (_, index) => ({
  code: `source-${index + 1}`,
  title: `Companies Act source ${index + 1}`,
  url: `https://revisedacts.lawreform.ie/source/${index + 1}`,
}));

const blocker = "Jane Director: Aggregate maximum exposure of €18,500.00 is not strictly below the section 240 threshold of €12,000.00.";

const blockedCompliance: DirectorLoanCompliance = {
  totalDirectorLoans: 18500,
  aggregateMaximumExposure: 18500,
  disclosureAggregateMaximumExposure: 18500,
  disclosureOpeningNetAssets: 100000,
  disclosureClosingNetAssets: 120000,
  section236PresumedInterest: 0,
  hasUnresolvedComplianceBlockers: true,
  requiresAlternativeLegalBasis: true,
  blockingIssues: [blocker],
  warnings: [],
  warning: blocker,
  legalSources,
  signOffPacket: {
    state: "evidence-incomplete",
    readyForArrangementReview: false,
    readyForFinalOutput: false,
    openBlockers: [blocker],
    openWarnings: [],
    legalSources,
  },
  loans: [
    {
      id: 1,
      counterpartyName: "Jane Director",
      relatedDirectorName: "Jane Director",
      counterpartyType: "Director",
      arrangementType: "Loan",
      arrangementDate: "2026-01-01",
      openingBalance: 0,
      advances: 18500,
      repayments: 0,
      allowanceMade: 0,
      maxDuringYear: 18500,
      closingBalance: 18500,
      interestRate: 0,
      interestCharged: 0,
      section236PresumedInterest: 0,
      termsStatus: "WrittenComplete",
      mainConditions: "Written repayment and interest terms",
      complianceBasis: "Section240BelowTenPercent",
      relevantAssets: 120000,
      section240Threshold: 12000,
      section240StrictlyBelowThreshold: false,
      section307DisclosureRequired: true,
      reviewDecision: "Unreviewed",
      reviewedBy: undefined,
      reviewerRole: undefined,
      reviewedAtUtc: undefined,
      readyForFinalOutput: false,
      blockingIssues: [blocker],
      warnings: [],
      balanceMovements: [
        {
          movementDate: "2026-01-01",
          movementType: "Advance",
          amount: 18500,
          evidenceReference: "bank-ledger#advance",
        },
      ],
    },
  ],
};
