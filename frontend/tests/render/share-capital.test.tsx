// frontend-share-capital-no-ui: prove the company-scoped Share Capital entry form renders and, on
// submit, issues exactly POST /api/companies/{id}/share-capital with the entered payload and the CSRF
// header — so issued shares can reach BalanceSheet.capitalAndReserves.shareCapital / SOCIE through the UI.
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ShareCapitalCard } from "@/components/ShareCapitalCard";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

beforeEach(() => {
  setCsrfCookie("csrf-share-token");
});

afterEach(() => {
  clearCookies();
});

describe("ShareCapitalCard", () => {
  it("renders the entry form once the (empty) list has loaded", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<ShareCapitalCard companyId={7} />);

    expect(await screen.findByRole("button", { name: /issue shares/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Number of shares issued")).toBeInTheDocument();
    expect(screen.getByText(/no share capital recorded yet/i)).toBeInTheDocument();
  });

  it("issues POST /api/companies/7/share-capital with the entered payload and CSRF header", async () => {
    const fetchMock = installFetchMock((req) => {
      if (req.method === "POST") {
        return { status: 201, body: { ...(req.body as object), id: 11, companyId: 7 } };
      }
      return { status: 200, body: [] };
    });

    render(<ShareCapitalCard companyId={7} />);
    const addButton = await screen.findByRole("button", { name: /issue shares/i });

    fireEvent.change(screen.getByLabelText("Number of shares issued"), { target: { value: "100" } });
    fireEvent.change(screen.getByLabelText("Nominal value per share"), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText("Share issue date"), { target: { value: "2025-06-01" } });

    await userEvent.click(addButton);

    await waitFor(() => {
      const request = fetchMock.one("POST", "/api/companies/7/share-capital");
      expect(request.csrf).toBe("csrf-share-token");
      expect(request.headers.get("Content-Type")).toBe("application/json");
      expect(request.body).toMatchObject({
        shareClass: "Ordinary",
        numberIssued: 100,
        nominalValue: 1,
        totalValue: 100,
        isFullyPaid: true,
        issueDate: "2025-06-01",
      });
    });

    // The created row is reflected back into the list (optimistic append).
    expect(await screen.findByText(/100 Ordinary shares/)).toBeInTheDocument();
  });

  it("does not POST when required fields are missing (client-side guard)", async () => {
    const fetchMock = installFetchMock(() => ({ status: 200, body: [] }));

    render(<ShareCapitalCard companyId={7} />);
    const addButton = await screen.findByRole("button", { name: /issue shares/i });

    // numberIssued stays 0 and issueDate blank — submit must be blocked before any POST.
    await userEvent.click(addButton);

    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });
});
