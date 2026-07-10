import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyAnnualReturnDatePanel } from "@/components/company/CompanyAnnualReturnDatePanel";
import type { Company } from "@/lib/api";
import { installFetchMock } from "./harness";

describe("CompanyAnnualReturnDatePanel", () => {
  it("shows the exact date, effective source and retained integrity evidence", async () => {
    const fetchMock = installFetchMock((request) => request.url.includes("annual-return-dates") ? {
      body: [{
        id: 41,
        companyId: 7,
        previousAnnualReturnDate: null,
        annualReturnDate: "2026-09-15",
        effectiveFrom: "2026-09-15",
        source: "CroRecord",
        evidenceReference: "CRO-CORE-ARD-2026",
        evidenceSha256: "a".repeat(64),
        changeReason: null,
        recordedByUserId: "reviewer@example.ie",
        recordedByDisplayName: "Qualified Reviewer",
        recordedAtUtc: "2026-07-10T09:00:00Z",
        recordSha256: "b".repeat(64),
      }],
    } : undefined);

    render(<CompanyAnnualReturnDatePanel company={company()} canWrite onChanged={vi.fn()} />);

    expect(screen.getByText("Annual Return Date evidence")).toBeInTheDocument();
    expect(screen.getByText("15 Sept 2026")).toBeInTheDocument();
    expect((await screen.findAllByText("CRO-CORE-ARD-2026")).length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("CRO CORE record").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/Qualified Reviewer/)).toBeInTheDocument();
    expect(fetchMock.one("GET", "/companies/7/annual-return-dates")).toBeDefined();
  });

  it("blocks guessed legacy dates and exposes evidence-backed correction fields", async () => {
    installFetchMock();
    render(<CompanyAnnualReturnDatePanel company={{ ...company(), annualReturnDate: undefined }} canWrite onChanged={vi.fn()} />);

    await waitFor(() => expect(screen.getByText("Deadline calculation blocked")).toBeInTheDocument());
    expect(screen.getByText(/not converted into a guessed day/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm exact ARD" }));

    expect(screen.getByLabelText("Exact ARD *")).toHaveAttribute("type", "date");
    expect(screen.getByLabelText("Effective from *")).toHaveAttribute("type", "date");
    expect(screen.getByLabelText("Retained evidence reference *")).toBeInTheDocument();
    expect(screen.getByText(/does not submit a B1, B73 or court application/i)).toBeInTheDocument();
  });
});

function company(): Company {
  return {
    id: 7,
    legalName: "Exact ARD Limited",
    companyType: "Private",
    incorporationDate: "2020-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
  };
}
