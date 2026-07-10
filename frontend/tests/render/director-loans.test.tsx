import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DirectorLoansManager } from "@/components/DirectorLoansManager";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

const directors = [{ id: 9, name: "Jane Director" }];

beforeEach(() => setCsrfCookie("csrf-dl-token"));
afterEach(() => clearCookies());

describe("DirectorLoansManager", () => {
  it("renders the evidence-led arrangement form with the director pre-selected", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<DirectorLoansManager companyId={7} periodId={3} directors={directors} />);

    expect(await screen.findByRole("button", { name: "Add statutory evidence" })).toBeInTheDocument();
    expect(screen.getByLabelText("Related director")).toHaveValue("9");
    expect(screen.getByLabelText("Claimed legal basis")).toHaveValue("Unassessed");
    expect(screen.getByText(/does not replace the separate release-level qualified-accountant approval/i)).toBeInTheDocument();
  });

  it("posts dated movements with correctly derived advances, closing and maximum balances", async () => {
    const user = userEvent.setup();
    const fetchMock = installFetchMock((request) => {
      if (request.method === "POST") {
        return { status: 201, body: { ...(request.body as object), id: 4, periodId: 3 } };
      }
      return { status: 200, body: [] };
    });
    render(<DirectorLoansManager companyId={7} periodId={3} directors={directors} />);
    await screen.findByRole("button", { name: "Add statutory evidence" });

    fireEvent.change(screen.getByLabelText("Director-loan opening balance"), { target: { value: "1000" } });
    await user.click(screen.getByRole("button", { name: "Add dated movement" }));
    fireEvent.change(screen.getByLabelText("Movement date"), { target: { value: "2026-03-01" } });
    fireEvent.change(screen.getByLabelText("Movement 1 amount"), { target: { value: "5000" } });
    await user.type(screen.getByLabelText("Evidence reference"), "bank-ledger#advance");

    await user.click(screen.getByRole("button", { name: "Add dated movement" }));
    const movementDates = screen.getAllByLabelText("Movement date");
    const movementTypes = screen.getAllByLabelText("Type");
    fireEvent.change(movementDates[1], { target: { value: "2026-09-01" } });
    await user.selectOptions(movementTypes[1], "Repayment");
    fireEvent.change(screen.getByLabelText("Movement 2 amount"), { target: { value: "2000" } });
    await user.type(screen.getAllByLabelText("Evidence reference")[1], "bank-ledger#repayment");

    await user.click(screen.getByRole("button", { name: "Add statutory evidence" }));

    await waitFor(() => {
      const request = fetchMock.one("POST", "/api/companies/7/periods/3/director-loans");
      expect(request.csrf).toBe("csrf-dl-token");
      expect(request.body).toMatchObject({
        directorId: 9,
        counterpartyType: "Director",
        openingBalance: 1000,
        advances: 5000,
        repayments: 2000,
        closingBalance: 4000,
        maxBalanceDuringYear: 6000,
        balanceMovements: [
          { movementDate: "2026-03-01", movementType: "Advance", amount: 5000, evidenceReference: "bank-ledger#advance" },
          { movementDate: "2026-09-01", movementType: "Repayment", amount: 2000, evidenceReference: "bank-ledger#repayment" },
        ],
      });
    });

    expect(await screen.findByLabelText("Delete director-loan evidence for Jane Director")).toBeInTheDocument();
  });

  it("permits a section 243 group-company draft when no individual director exists", async () => {
    const user = userEvent.setup();
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<DirectorLoansManager companyId={7} periodId={3} directors={[]} />);

    expect(await screen.findByText(/add a director with a verified appointment date/i)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Counterparty type"), "GroupCompany");
    expect(screen.queryByLabelText("Related director")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Group-company legal name")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add statutory evidence" })).toBeInTheDocument();
  });
});
