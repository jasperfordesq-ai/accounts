import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { CompanyStatutoryProfile } from "@/components/company/CompanyStatutoryProfile";
import type { Company } from "@/lib/api";

describe("CompanyStatutoryProfile", () => {
  it("surfaces registration, address and unsupported-path gates", () => {
    render(<CompanyStatutoryProfile company={sampleCompany()} />);

    expect(screen.getByText("Statutory Profile")).toBeInTheDocument();
    expect(screen.getByText("Company identity, registered office and support-path gate.")).toBeInTheDocument();
    expect(screen.getByText("Manual handoff")).toBeInTheDocument();
    expect(screen.getByText("PLC/public-company workflow requires manual review.")).toBeInTheDocument();
    expect(screen.getByText("CRO 123456")).toBeInTheDocument();
    expect(screen.getByText("Tax 123456T")).toBeInTheDocument();
    expect(screen.getByText("PLC")).toBeInTheDocument();
    expect(screen.getByText("Incorporated 01 Jan 2024")).toBeInTheDocument();
    expect(screen.getByText("1 Review Quay")).toBeInTheDocument();
    expect(screen.getByText("Dublin")).toBeInTheDocument();
    expect(screen.getByText("D02 X285")).toBeInTheDocument();
    expect(screen.getByText("Charity workflow")).toBeInTheDocument();
    expect(screen.getByText("Regulated/excluded entity")).toBeInTheDocument();
  });
});

function sampleCompany(): Company {
  return {
    id: 8,
    legalName: "Atlantic Public Limited Company",
    croNumber: "123456",
    taxReference: "123456T",
    companyType: "PublicLimitedCompany",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    ardMonth: 9,
    registeredOfficeAddress1: "1 Review Quay",
    registeredOfficeCity: "Dublin",
    registeredOfficeCounty: "Dublin",
    registeredOfficeEircode: "D02 X285",
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
    isCharitableOrganisation: true,
  };
}
