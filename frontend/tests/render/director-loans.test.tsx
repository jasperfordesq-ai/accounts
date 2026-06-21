// frontend-director-loans-no-entry: the Director Loans section was display-only, so
// directorLoanCompliance was always null and the s.236 / overdrawn-DLA checks never fired. Prove the
// new editor issues POST /api/companies/{id}/periods/{pid}/director-loans with the entered payload
// (directorId + derived closing/max) and the CSRF header, and guards the no-directors case.
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DirectorLoansManager } from "@/components/DirectorLoansManager";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

const directors = [{ id: 9, name: "Jane Director" }];

beforeEach(() => {
  setCsrfCookie("csrf-dl-token");
});

afterEach(() => {
  clearCookies();
});

describe("DirectorLoansManager", () => {
  it("renders the add form (with the director pre-selected) once loaded", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<DirectorLoansManager companyId={7} periodId={3} directors={directors} />);

    expect(await screen.findByRole("button", { name: /add director loan/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Director")).toHaveValue("9");
  });

  it("issues POST /api/companies/7/periods/3/director-loans with derived closing/max and CSRF header", async () => {
    const fetchMock = installFetchMock((req) => {
      if (req.method === "POST") {
        return { status: 201, body: { ...(req.body as object), id: 4, periodId: 3 } };
      }
      return { status: 200, body: [] };
    });

    render(<DirectorLoansManager companyId={7} periodId={3} directors={directors} />);
    const addButton = await screen.findByRole("button", { name: /add director loan/i });

    fireEvent.change(screen.getByLabelText("Opening balance"), { target: { value: "1000" } });
    fireEvent.change(screen.getByLabelText("Advances to director"), { target: { value: "5000" } });
    fireEvent.change(screen.getByLabelText("Repayments by director"), { target: { value: "2000" } });

    await userEvent.click(addButton);

    await waitFor(() => {
      const request = fetchMock.one("POST", "/api/companies/7/periods/3/director-loans");
      expect(request.csrf).toBe("csrf-dl-token");
      expect(request.body).toMatchObject({
        directorId: 9,
        openingBalance: 1000,
        advances: 5000,
        repayments: 2000,
        // closing = opening + advances - repayments; max defaults to the larger of opening/closing
        closingBalance: 4000,
        maxBalanceDuringYear: 4000,
        isDocumented: false,
      });
    });

    // The saved row is reflected back into the list (its delete control is unique to a rendered row).
    expect(
      await screen.findByLabelText("Delete director loan for Jane Director"),
    ).toBeInTheDocument();
  });

  it("blocks entry and explains when the company has no directors", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<DirectorLoansManager companyId={7} periodId={3} directors={[]} />);

    expect(await screen.findByText(/add a director to this company first/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add director loan/i })).toBeNull();
  });
});
