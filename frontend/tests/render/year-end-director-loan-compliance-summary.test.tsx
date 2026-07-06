import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { YearEndDirectorLoanComplianceSummary } from "@/components/period/YearEndDirectorLoanComplianceSummary";
import type { DirectorLoanCompliance } from "@/lib/api";

describe("YearEndDirectorLoanComplianceSummary", () => {
  it("renders warning, threshold metrics and SAP status for overdrawn director loans", () => {
    render(<YearEndDirectorLoanComplianceSummary compliance={overdrawnCompliance} />);

    expect(screen.getByText(/s\.236 \/ overdrawn-DLA compliance summary/i)).toBeInTheDocument();
    expect(screen.getByText("Director loans exceed the 10% net asset threshold")).toBeInTheDocument();
    expect(screen.getByText("Total Loans")).toBeInTheDocument();
    expect(screen.getByText("\u20ac18,500.00")).toBeInTheDocument();
    expect(screen.getByText("Net Assets")).toBeInTheDocument();
    expect(screen.getByText("\u20ac120,000.00")).toBeInTheDocument();
    expect(screen.getByText("10% Threshold")).toBeInTheDocument();
    expect(screen.getByText("\u20ac12,000.00")).toBeInTheDocument();
    expect(screen.getByText("Exceeds Threshold")).toBeInTheDocument();
    expect(screen.getByText("Shareholder Approval Process (SAP) required under s.239 Companies Act 2014")).toBeInTheDocument();
  });

  it("renders nothing when there are no director-loan rows to review", () => {
    const { container } = render(
      <YearEndDirectorLoanComplianceSummary compliance={{ ...overdrawnCompliance, loans: [] }} />,
    );

    expect(container).toBeEmptyDOMElement();
  });
});

const overdrawnCompliance: DirectorLoanCompliance = {
  totalDirectorLoans: 18500,
  netAssets: 120000,
  thresholdAmount: 12000,
  exceedsThreshold: true,
  sapRequired: true,
  statutoryInterestDue: 0,
  warning: "Director loans exceed the 10% net asset threshold",
  loans: [
    {
      id: 1,
      directorName: "Jane Director",
      openingBalance: 0,
      maxDuringYear: 18500,
      closingBalance: 18500,
      interestCharged: 0,
      isDocumented: false,
      exceedsThreshold: true,
    },
  ],
};
