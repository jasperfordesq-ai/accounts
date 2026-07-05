// frontend-role-gating: the money-entry surfaces honour canWriteWorkingPapers. A read-only role (e.g.
// Client) sees neither the add forms nor the per-row delete controls — only a read-only notice. The
// backend remains the authority; this is UX hardening so ineligible roles aren't shown dead controls.
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ShareCapitalCard } from "@/components/ShareCapitalCard";
import { LoansManager } from "@/components/LoansManager";
import { DirectorLoansManager } from "@/components/DirectorLoansManager";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

beforeEach(() => {
  setCsrfCookie("csrf-role-token");
});

afterEach(() => {
  clearCookies();
});

describe("role gating (canWrite=false)", () => {
  it("ShareCapitalCard: hides the add form and per-row delete for read-only roles", async () => {
    installFetchMock(() => ({
      status: 200,
      body: [
        { id: 1, companyId: 7, shareClass: "Ordinary", nominalValue: 1, numberIssued: 100, totalValue: 100, isFullyPaid: true, issueDate: "2025-01-01" },
      ],
    }));
    render(<ShareCapitalCard companyId={7} canWrite={false} />);

    expect(await screen.findByText(/read-only access to share capital/i)).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /read-only workflow access/i })).toBeInTheDocument();
    expect(screen.getByText(/Evidence remains visible; editing requires Owner or Accountant access/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /issue shares/i })).toBeNull();
    expect(screen.queryByLabelText(/delete .* share capital/i)).toBeNull();
    // the figures are still visible (read-only view)
    expect(screen.getByText(/100 Ordinary shares/)).toBeInTheDocument();
  });

  it("LoansManager: hides the add-loan form for read-only roles", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<LoansManager companyId={7} periodEnd="2025-12-31" canWrite={false} />);

    expect(await screen.findByText(/read-only access to loans/i)).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /read-only workflow access/i })).toBeInTheDocument();
    expect(screen.getByText(/Evidence remains visible; editing requires Owner or Accountant access/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add loan/i })).toBeNull();
    expect(screen.queryByLabelText("Lender")).toBeNull();
  });

  it("DirectorLoansManager: hides the add form for read-only roles", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(
      <DirectorLoansManager
        companyId={7}
        periodId={3}
        directors={[{ id: 9, name: "Jane Director" }]}
        canWrite={false}
      />,
    );

    expect(await screen.findByText(/read-only access to director loans/i)).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /read-only workflow access/i })).toBeInTheDocument();
    expect(screen.getByText(/Evidence remains visible; editing requires Owner or Accountant access/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add director loan/i })).toBeNull();
    expect(screen.queryByLabelText("Director")).toBeNull();
  });

  it("default (canWrite omitted) keeps the entry forms available", async () => {
    installFetchMock(() => ({ status: 200, body: [] }));
    render(<ShareCapitalCard companyId={7} />);

    expect(await screen.findByRole("button", { name: /issue shares/i })).toBeInTheDocument();
    expect(screen.queryByText(/read-only access/i)).toBeNull();
  });
});
