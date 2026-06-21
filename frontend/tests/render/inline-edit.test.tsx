// frontend-inline-edit-yearend: editing an existing money row issues a PUT (via the update* client),
// not a delete + re-add, so the row id and its audit continuity are preserved. Each component reuses a
// single form: clicking the row's edit control populates it and the submit button issues the PUT.
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ShareCapitalCard } from "@/components/ShareCapitalCard";
import { LoansManager } from "@/components/LoansManager";
import { DirectorLoansManager } from "@/components/DirectorLoansManager";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

beforeEach(() => {
  setCsrfCookie("csrf-edit-token");
});

afterEach(() => {
  clearCookies();
});

describe("inline edit (PUT, preserving id)", () => {
  it("ShareCapitalCard edits a row via PUT /share-capital/{id}", async () => {
    const existing = {
      id: 11, companyId: 7, shareClass: "Ordinary", nominalValue: 1, numberIssued: 100,
      totalValue: 100, isFullyPaid: true, issueDate: "2025-01-01",
    };
    const fetchMock = installFetchMock((req) => {
      if (req.method === "PUT") return { status: 200, body: { ...(req.body as object), id: 11 } };
      return { status: 200, body: [existing] };
    });

    render(<ShareCapitalCard companyId={7} />);
    fireEvent.click(await screen.findByLabelText("Edit Ordinary share capital"));

    // The form is now populated; bump the number issued and save.
    fireEvent.change(screen.getByLabelText("Number of shares issued"), { target: { value: "250" } });
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => {
      const request = fetchMock.one("PUT", "/api/companies/7/share-capital/11");
      expect(request.csrf).toBe("csrf-edit-token");
      expect(request.body).toMatchObject({ numberIssued: 250, nominalValue: 1, totalValue: 250 });
    });
    // No POST (would have been a delete + re-add).
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });

  it("LoansManager edits a row via PUT /loans/{id}", async () => {
    const existing = {
      id: 5, companyId: 7, lender: "Bank of Ireland", originalAmount: 50000, balance: 40000,
      drawdownDate: "2024-01-01", balanceAsOfDate: "2025-12-31", interestRate: 4.5,
      isDirectorLoan: false, dueWithinYear: 10000, dueAfterYear: 30000,
    };
    const fetchMock = installFetchMock((req) => {
      if (req.method === "PUT") return { status: 200, body: { ...(req.body as object), id: 5 } };
      return { status: 200, body: [existing] };
    });

    render(<LoansManager companyId={7} periodEnd="2025-12-31" />);
    fireEvent.click(await screen.findByLabelText("Edit loan from Bank of Ireland"));

    fireEvent.change(screen.getByLabelText("Balance outstanding"), { target: { value: "35000" } });
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => {
      const request = fetchMock.one("PUT", "/api/companies/7/loans/5");
      expect(request.csrf).toBe("csrf-edit-token");
      // balance 35000 - dueWithinYear 10000 -> dueAfterYear recomputed to 25000
      expect(request.body).toMatchObject({ lender: "Bank of Ireland", balance: 35000, dueAfterYear: 25000 });
    });
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });

  it("DirectorLoansManager edits a row via PUT /director-loans/{id}", async () => {
    const existing = {
      id: 4, periodId: 3, directorId: 9, openingBalance: 1000, advances: 5000, repayments: 2000,
      closingBalance: 4000, interestRate: 0, interestCharged: 0, isDocumented: false, maxBalanceDuringYear: 4000,
    };
    const fetchMock = installFetchMock((req) => {
      if (req.method === "PUT") return { status: 200, body: { ...(req.body as object), id: 4 } };
      return { status: 200, body: [existing] };
    });

    render(
      <DirectorLoansManager companyId={7} periodId={3} directors={[{ id: 9, name: "Jane Director" }]} />,
    );
    fireEvent.click(await screen.findByLabelText("Edit director loan for Jane Director"));

    fireEvent.change(screen.getByLabelText("Repayments by director"), { target: { value: "1000" } });
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => {
      const request = fetchMock.one("PUT", "/api/companies/7/periods/3/director-loans/4");
      expect(request.csrf).toBe("csrf-edit-token");
      // closing recomputed: opening 1000 + advances 5000 - repayments 1000 = 5000
      expect(request.body).toMatchObject({ directorId: 9, repayments: 1000, closingBalance: 5000 });
    });
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });
});
