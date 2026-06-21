// frontend-loans-no-ui: prove the year-end Loans section now has a real entry form (it used to
// misdirect to a non-existent "Company Setup" screen). On submit it issues exactly
// POST /api/companies/{id}/loans with the entered payload and the CSRF header, and the DueWithinYear /
// DueAfterYear split (which feeds creditors due within / after one year on the balance sheet) cross-adds.
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LoansManager } from "@/components/LoansManager";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

beforeEach(() => {
  setCsrfCookie("csrf-loan-token");
});

afterEach(() => {
  clearCookies();
});

describe("LoansManager", () => {
  it("renders the add-loan form once the (empty) list has loaded", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<LoansManager companyId={7} periodEnd="2025-12-31" />);

    expect(await screen.findByRole("button", { name: /add loan/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Lender")).toBeInTheDocument();
    // balanceAsOfDate defaults to the period end so the loan lands in this period.
    expect(screen.getByLabelText("Balance as-of date")).toHaveValue("2025-12-31");
  });

  it("issues POST /api/companies/7/loans with the entered payload, due split and CSRF header", async () => {
    const fetchMock = installFetchMock((req) => {
      if (req.method === "POST") {
        return { status: 201, body: { ...(req.body as object), id: 5, companyId: 7 } };
      }
      return { status: 200, body: [] };
    });

    render(<LoansManager companyId={7} periodEnd="2025-12-31" />);
    const addButton = await screen.findByRole("button", { name: /add loan/i });

    fireEvent.change(screen.getByLabelText("Lender"), { target: { value: "Bank of Ireland" } });
    fireEvent.change(screen.getByLabelText("Original amount"), { target: { value: "50000" } });
    fireEvent.change(screen.getByLabelText("Balance outstanding"), { target: { value: "40000" } });
    fireEvent.change(screen.getByLabelText("Interest rate"), { target: { value: "4.5" } });
    fireEvent.change(screen.getByLabelText("Drawdown date"), { target: { value: "2024-01-01" } });
    fireEvent.change(screen.getByLabelText("Amount due within one year"), { target: { value: "10000" } });

    await userEvent.click(addButton);

    await waitFor(() => {
      const request = fetchMock.one("POST", "/api/companies/7/loans");
      expect(request.csrf).toBe("csrf-loan-token");
      expect(request.body).toMatchObject({
        lender: "Bank of Ireland",
        originalAmount: 50000,
        balance: 40000,
        interestRate: 4.5,
        drawdownDate: "2024-01-01",
        balanceAsOfDate: "2025-12-31",
        dueWithinYear: 10000,
        // balance (40000) - dueWithinYear (10000) -> cross-adds to the long-term portion
        dueAfterYear: 30000,
        isDirectorLoan: false,
      });
    });

    expect(await screen.findByText("Bank of Ireland")).toBeInTheDocument();
  });

  it("does not POST when the balance as-of date precedes the drawdown date", async () => {
    const fetchMock = installFetchMock(() => ({ status: 200, body: [] }));

    render(<LoansManager companyId={7} periodEnd="2025-12-31" />);
    const addButton = await screen.findByRole("button", { name: /add loan/i });

    fireEvent.change(screen.getByLabelText("Lender"), { target: { value: "Late Lender" } });
    fireEvent.change(screen.getByLabelText("Drawdown date"), { target: { value: "2026-01-01" } });
    // balanceAsOfDate defaulted to 2025-12-31, which is before the drawdown date.

    await userEvent.click(addButton);

    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });
});
