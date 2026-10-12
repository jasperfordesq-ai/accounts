import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CompanyChartOfAccountsPanel } from "@/components/company/CompanyChartOfAccountsPanel";
import type { Company } from "@/lib/api";
import { installFetchMock, setCsrfCookie } from "./harness";

vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ reauthenticate: vi.fn() }) }));

const PLAN_URL = "/api/companies/7/categories/chart-adoption/plan";
const ADOPT_URL = "/api/companies/7/categories/chart-adoption";

describe("CompanyChartOfAccountsPanel", () => {
  it("reviews the plan, states that transactions are not moved, and adopts exactly that plan", async () => {
    const user = userEvent.setup();
    setCsrfCookie("csrf-token");
    const onAdopted = vi.fn();
    const fetchMock = installFetchMock((request) => request.url.endsWith(PLAN_URL) || request.url.endsWith(ADOPT_URL)
      ? { body: plan() }
      : undefined);
    render(<CompanyChartOfAccountsPanel company={company()} canAdopt onAdopted={onAdopted} />);

    expect(screen.getByText("Standard trading company")).toBeInTheDocument();
    expect(fetchMock.requests).toHaveLength(0);
    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));

    expect(await screen.findByText("Your transactions are not moved. Re-categorise them to the new accounts yourself.")).toBeInTheDocument();
    expect(screen.getByText("1400 Bank Current Account → 1200 Bank current account")).toBeInTheDocument();
    expect(screen.getByText(/2500 → G-2500 Director Loan Account \(legacy\): 30 transactions/)).toBeInTheDocument();
    expect(screen.getByText("Archived: no entries, hidden from pickers (1)")).toBeInTheDocument();
    expect(screen.getByText("New charity / CLG accounts (1)")).toBeInTheDocument();
    expect(fetchMock.one("POST", "/chart-adoption/plan").csrf).toBe("csrf-token");

    await user.click(screen.getByRole("button", { name: "Adopt chart of accounts" }));

    await waitFor(() => expect(onAdopted).toHaveBeenCalledTimes(1));
    const adopt = fetchMock.requests.find((request) => request.url.endsWith(ADOPT_URL));
    expect(adopt?.body).toEqual({ variant: "CharityClg", planSha256: "a".repeat(64), acknowledgeStaleFilingArtifacts: false });
  });

  it("states before the adopt button that adoption is one-way", async () => {
    const user = userEvent.setup();
    setCsrfCookie("csrf-token");
    installFetchMock((request) => request.url.endsWith(PLAN_URL) ? { body: plan() } : undefined);
    render(<CompanyChartOfAccountsPanel company={company()} canAdopt onAdopted={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));

    const notice = await screen.findByText(
      "Adopting the charity / CLG chart is one-way: there is no automatic way to change back to the standard trading chart.",
    );
    const adopt = screen.getByRole("button", { name: "Adopt chart of accounts" });
    expect(notice.compareDocumentPosition(adopt) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("cannot adopt while the plan is blocked, and requires acknowledgement of stale CRO packs", async () => {
    const user = userEvent.setup();
    const stale = {
      periodId: 3, periodStart: "2025-01-01", periodEnd: "2025-12-31", status: "Review",
      croPackageStatus: "Generated", croFilingStatus: "PackageGenerated",
    };
    let blocked = true;
    installFetchMock((request) => request.url.endsWith(PLAN_URL)
      ? { body: { ...plan(), staleFilingArtifacts: [stale], blockedBy: blocked ? ["The period 01/01/2024–31/12/2024 is finalised."] : [] } }
      : undefined);
    const { unmount } = render(<CompanyChartOfAccountsPanel company={company()} canAdopt />);
    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));
    expect(await screen.findByText("The period 01/01/2024–31/12/2024 is finalised.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Adopt chart of accounts" })).toBeDisabled();
    unmount();

    blocked = false;
    render(<CompanyChartOfAccountsPanel company={company()} canAdopt />);
    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));
    const adopt = await screen.findByRole("button", { name: "Adopt chart of accounts" });
    expect(adopt).toBeDisabled();
    await user.click(screen.getByRole("checkbox", { name: "I understand these filing artefacts become stale." }));
    expect(adopt).toBeEnabled();
  });

  it("asks for recent verification beside, not inside, the adoption controls", async () => {
    const user = userEvent.setup();
    installFetchMock((request) => {
      if (request.url.endsWith(PLAN_URL)) return { body: plan() };
      if (request.url.endsWith(ADOPT_URL)) return { status: 428, body: { code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." } };
      return undefined;
    });
    render(<CompanyChartOfAccountsPanel company={company()} canAdopt />);
    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));
    await user.click(await screen.findByRole("button", { name: "Adopt chart of accounts" }));

    const prompt = await screen.findByRole("form", { name: "Recent verification" });
    expect(prompt.parentElement?.closest("form")).toBeNull();
    expect(screen.getByRole("button", { name: "Adopt chart of accounts" })).toBeInTheDocument();
  });

  it("shows the server's current plan when the reviewed plan no longer matches", async () => {
    const user = userEvent.setup();
    const changed = { ...plan(), planSha256: "b".repeat(64), retained: [{ categoryId: 9, code: "2600", name: "Bank Loan", transactionCount: 1, journalCount: 0, openingBalanceCount: 0 }] };
    installFetchMock((request) => {
      if (request.url.endsWith(PLAN_URL)) return { body: plan() };
      if (request.url.endsWith(ADOPT_URL)) return { status: 409, body: { error: "The accounts changed since the plan was reviewed.", code: "chart_adoption_plan_changed", plan: changed } };
      return undefined;
    });
    render(<CompanyChartOfAccountsPanel company={company()} canAdopt />);
    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));
    await user.click(await screen.findByRole("button", { name: "Adopt chart of accounts" }));

    expect(await screen.findByText("The accounts changed since the plan was reviewed.")).toBeInTheDocument();
    expect(screen.getByText("2600 Bank Loan: 1 transaction")).toBeInTheDocument();
  });

  it("shows how many transaction rules point at legacy and archived accounts, and that imports skip archived ones", async () => {
    const user = userEvent.setup();
    installFetchMock((request) => request.url.endsWith(PLAN_URL) ? { body: plan() } : undefined);
    render(<CompanyChartOfAccountsPanel company={company()} canAdopt />);
    await user.click(screen.getByRole("button", { name: "Adopt the charity / CLG chart" }));

    expect(await screen.findByText(/30 transactions \(2 transaction rules point here\)/)).toBeInTheDocument();
    expect(screen.getByText(/1200 → G-1200 Prepayments \(1 transaction rule points here\)/)).toBeInTheDocument();
    expect(screen.getByText("Imports skip transaction rules that point at archived accounts; point them at a new account if you still need them.")).toBeInTheDocument();
  });

  it("offers adoption only to a company limited by guarantee", () => {
    installFetchMock();
    render(<CompanyChartOfAccountsPanel company={{ ...company(), companyType: "Private", isCharitableOrganisation: false }} canAdopt />);
    expect(screen.getByText("Standard trading company")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Adopt the charity / CLG chart" })).not.toBeInTheDocument();
  });

  it("offers adoption only to an Owner of a company on the standard trading chart", () => {
    installFetchMock();
    const { rerender } = render(<CompanyChartOfAccountsPanel company={company()} canAdopt={false} />);
    expect(screen.queryByRole("button", { name: "Adopt the charity / CLG chart" })).not.toBeInTheDocument();

    rerender(<CompanyChartOfAccountsPanel company={{ ...company(), chartOfAccountsVariant: "CharityClg" }} canAdopt />);
    expect(screen.getByText("Charity / company limited by guarantee")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Adopt the charity / CLG chart" })).not.toBeInTheDocument();
  });
});

function plan() {
  return {
    companyId: 7,
    currentVariant: "GenericTrading",
    targetVariant: "CharityClg",
    renames: [
      { categoryId: 1, role: "BankControl", fromCode: "1400", fromName: "Bank Current Account", toCode: "1200", toName: "Bank current account" },
      { categoryId: 2, role: "RetainedEarnings", fromCode: "3100", fromName: "Retained Earnings", toCode: "3000", toName: "Members' funds brought forward" },
    ],
    legacyRecodes: [
      {
        categoryId: 3, fromCode: "2500", fromName: "Director Loan Account", toCode: "G-2500", toName: "Director Loan Account (legacy)",
        transactionCount: 30, journalCount: 0, openingBalanceCount: 0, ruleCount: 2, reason: "CharityRole", releasedRole: "DirectorLoanLiability",
      },
    ],
    archives: [{ categoryId: 4, fromCode: "1200", name: "Prepayments", toCode: "G-1200", ruleCount: 1, releasedRole: "Prepayments" }],
    retained: [],
    inserts: [{ code: "2230", name: "Director's loan account", type: "Liability", statementLine: "OtherCreditorsWithinYear", cashFlowClass: "OtherFinancing", systemRole: "DirectorLoanLiability" }],
    staleFilingArtifacts: [],
    blockedBy: [],
    planSha256: "a".repeat(64),
  };
}

function company(): Company {
  return {
    id: 7,
    legalName: "Example Community CLG",
    companyType: "CompanyLimitedByGuarantee",
    incorporationDate: "2017-07-20",
    financialYearStartMonth: 1,
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
    isCharitableOrganisation: true,
    chartOfAccountsVariant: "GenericTrading",
  };
}
