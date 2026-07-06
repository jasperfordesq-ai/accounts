import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyDetailWorkbench } from "@/components/company/CompanyDetailWorkbench";
import type { CharityInfo, Company } from "@/lib/api";
import { installFetchMock } from "./harness";

describe("CompanyDetailWorkbench", () => {
  it("renders the accountant workbench shell and composed company panels", async () => {
    const fetchMock = installFetchMock();

    render(<CompanyDetailWorkbench {...props()} />);

    expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute("href", "/");
    expect(screen.getByRole("heading", { name: "Connacht Workbench Limited", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("t/a Connacht Digital")).toBeInTheDocument();
    expect(screen.getByText("Core path")).toBeInTheDocument();
    expect(screen.getAllByText("Trading").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("1 period").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("button", { name: "Edit company" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Delete company" })).toBeInTheDocument();

    expect(screen.getByText("Company command centre")).toBeInTheDocument();
    expect(screen.getByText("Statutory Profile")).toBeInTheDocument();
    expect(screen.getByText("Officers & Signatories")).toBeInTheDocument();
    expect(screen.getByText("Share Capital")).toBeInTheDocument();
    expect(screen.getByText("Accounting Periods")).toBeInTheDocument();

    await waitFor(() => expect(fetchMock.requests).toHaveLength(1));
    expect(fetchMock.one("GET", "/companies/7/share-capital")).toBeDefined();
  });
});

function props(): Parameters<typeof CompanyDetailWorkbench>[0] {
  return {
    company: sampleCompany(),
    canWriteWorkingPapers: true,
    editing: false,
    editForm: {
      legalName: "Connacht Workbench Limited",
      tradingName: "Connacht Digital",
      croNumber: "123456",
      taxReference: "123456T",
      companyType: "Private",
      isDormant: false,
      isTrading: true,
    },
    savingCompany: false,
    showNewPeriod: false,
    periodStart: "",
    periodEnd: "",
    isFirstYear: false,
    creatingPeriod: false,
    editingOfficerId: null,
    editOfficerName: "",
    editOfficerRole: "Director",
    savingOfficer: false,
    showAddOfficer: false,
    newOfficerName: "",
    newOfficerRole: "Director",
    charityInfo: null,
    charityForm: emptyCharityInfo(),
    editingCharity: false,
    savingCharity: false,
    showDeleteModal: false,
    deleting: false,
    onStartEditCompany: vi.fn(),
    onEditFormChange: vi.fn(),
    onSaveCompany: vi.fn(),
    onCancelEditCompany: vi.fn(),
    onDeleteCompanyRequest: vi.fn(),
    onConfirmDeleteCompany: vi.fn(),
    onCancelDeleteCompany: vi.fn(),
    onShowNewPeriod: vi.fn(),
    onCancelNewPeriod: vi.fn(),
    onPeriodStartChange: vi.fn(),
    onPeriodEndChange: vi.fn(),
    onFirstYearChange: vi.fn(),
    onCreatePeriod: vi.fn(),
    onShowAddOfficer: vi.fn(),
    onNewOfficerNameChange: vi.fn(),
    onNewOfficerRoleChange: vi.fn(),
    onCancelAddOfficer: vi.fn(),
    onAddOfficer: vi.fn(),
    onStartEditOfficer: vi.fn(),
    onEditOfficerNameChange: vi.fn(),
    onEditOfficerRoleChange: vi.fn(),
    onSaveOfficer: vi.fn(),
    onCancelEditOfficer: vi.fn(),
    onDeleteOfficer: vi.fn(),
    onStartEditCharity: vi.fn(),
    onCancelEditCharity: vi.fn(),
    onSaveCharity: vi.fn(),
    onCharityFormChange: vi.fn(),
  };
}

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Connacht Workbench Limited",
    tradingName: "Connacht Digital",
    croNumber: "123456",
    taxReference: "123456T",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    ardMonth: 9,
    registeredOfficeAddress1: "1 Review Quay",
    registeredOfficeCity: "Galway",
    registeredOfficeCounty: "Galway",
    registeredOfficeEircode: "H91 TEST",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: true,
    isEmployer: false,
    hasStock: false,
    ownsAssets: true,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
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
        id: 3,
        companyId: 7,
        periodStart: "2026-01-01",
        periodEnd: "2026-12-31",
        status: "Review",
        isFirstYear: false,
        memberAuditNoticeReceived: false,
        goingConcernConfirmed: true,
        sizeClassification: {
          id: 13,
          turnover: 700_000,
          balanceSheetTotal: 250_000,
          avgEmployees: 6,
          calculatedClass: "Micro",
        },
        filingRegime: {
          id: 23,
          canUseMicro: true,
          canFileAbridged: true,
          auditExempt: true,
          electedRegime: "Micro",
        },
      },
    ],
  };
}

function emptyCharityInfo(): CharityInfo {
  return {
    grossIncome: 0,
    sorpTier: 1,
    governanceCodeCompliant: false,
    hasInternationalTransfers: false,
    trusteeRemunerationPaid: false,
    trusteeRemunerationAmount: 0,
  };
}
