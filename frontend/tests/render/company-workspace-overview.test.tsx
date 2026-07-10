import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { CompanyWorkspaceOverview } from "@/components/company/CompanyWorkspaceOverview";
import type { Company } from "@/lib/api";

describe("CompanyWorkspaceOverview", () => {
  it("surfaces company-level blockers, readiness and next accountant action", () => {
    render(<CompanyWorkspaceOverview company={sampleCompany()} />);

    expect(screen.getByText("Company command centre")).toBeInTheDocument();
    const commandCentre = screen.getByRole("heading", { name: "Company command centre" }).closest("section");
    expect(commandCentre).not.toBeNull();
    expect(commandCentre!.querySelector("[data-workbench-decision-summary='true']")).toBeInTheDocument();
    expect(screen.getByText("What is wrong?")).toBeInTheDocument();
    expect(screen.getAllByText("Manual professional review required")).toHaveLength(2);
    expect(screen.getAllByText("Regulated or Fifth Schedule excluded entity requires manual review.")).toHaveLength(2);
    expect(screen.getByText("What is ready?")).toBeInTheDocument();
    expect(screen.getByText("3 setup areas ready")).toBeInTheDocument();
    expect(screen.getByText("Officers recorded, registered office recorded, latest period exists")).toBeInTheDocument();
    expect(screen.getByText("What must I do next?")).toBeInTheDocument();
    expect(screen.getAllByText("Open latest period").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("link", { name: "Open latest period" })).toHaveAttribute(
      "href",
      "/companies/7/periods/11",
    );
    expect(screen.getByText("2 officers")).toBeInTheDocument();
    expect(screen.getByText("2 periods")).toBeInTheDocument();
    expect(screen.getByText("Medium")).toBeInTheDocument();
    const workflow = screen.getByRole("navigation", { name: "Accountant Workflow" });
    expect(workflow).toBeInTheDocument();
    expect(screen.getByText("8 stages")).toBeInTheDocument();
    expect(workflow).toHaveTextContent("Setup");
    expect(workflow).toHaveTextContent("Import");
    expect(workflow).toHaveTextContent("Classify");
    expect(workflow).toHaveTextContent("Year-End");
    expect(workflow).toHaveTextContent("Statements");
    expect(workflow).toHaveTextContent("Notes");
    expect(workflow).toHaveTextContent("Review");
    expect(workflow).toHaveTextContent("Filing");
    expect(screen.getByText("Start with company setup, then move period work through evidence, statements, review and filing.")).toBeInTheDocument();
  });
});

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Atlantic Assurance Limited",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    registeredOfficeAddress1: "1 Review Quay",
    registeredOfficeCity: "Dublin",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: true,
    isEmployer: true,
    hasStock: false,
    ownsAssets: true,
    hasBorrowings: true,
    hasDirectorLoans: false,
    isListedSecurities: true,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
    officers: [
      { id: 1, companyId: 7, name: "Aisling Director", role: "Director", appointedDate: "2024-01-01" },
      { id: 2, companyId: 7, name: "Brian Secretary", role: "Secretary", appointedDate: "2024-01-01" },
    ],
    periods: [
      {
        id: 10,
        companyId: 7,
        periodStart: "2025-01-01",
        periodEnd: "2025-12-31",
        status: "Filed",
        isFirstYear: false,
        memberAuditNoticeReceived: false,
        goingConcernConfirmed: true,
        sizeClassification: {
          id: 20,
          turnover: 1_200_000,
          balanceSheetTotal: 500_000,
          avgEmployees: 12,
          calculatedClass: "Small",
        },
      },
      {
        id: 11,
        companyId: 7,
        periodStart: "2026-01-01",
        periodEnd: "2026-12-31",
        status: "Review",
        isFirstYear: false,
        memberAuditNoticeReceived: false,
        goingConcernConfirmed: true,
        sizeClassification: {
          id: 21,
          turnover: 18_000_000,
          balanceSheetTotal: 8_000_000,
          avgEmployees: 120,
          calculatedClass: "Medium",
        },
      },
    ],
  };
}
