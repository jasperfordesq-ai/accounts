import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CorporationTaxDueConfirmation } from "@/components/period/CorporationTaxDueConfirmation";
import type { ConfirmableTaxBalance } from "@/lib/corporationTaxDue";
import { installFetchMock, setCsrfCookie } from "./harness";

vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ reauthenticate: vi.fn() }) }));

const CONFIRM_URL = "/api/companies/1/periods/3/tax-balances/CorporationTax/confirm-due";
const REASON = "Income outside the exemption: trading outside the primary purpose";

function balance(overrides: Partial<ConfirmableTaxBalance> = {}): ConfirmableTaxBalance {
  return { id: 4, periodId: 3, taxType: "CorporationTax", liability: 150, paid: 0, balance: 150, ...overrides };
}

function renderConfirmation(props: Partial<Parameters<typeof CorporationTaxDueConfirmation>[0]> = {}) {
  const onConfirmed = vi.fn();
  render(
    <CorporationTaxDueConfirmation
      companyId={1}
      periodId={3}
      holdsCharitableTaxExemption
      balance={balance()}
      canConfirm
      onConfirmed={onConfirmed}
      {...props}
    />,
  );
  return onConfirmed;
}

describe("CorporationTaxDueConfirmation (exempt company)", () => {
  it("confirms the saved liability as due, with the reason, and reports the confirmed balance", async () => {
    const user = userEvent.setup();
    setCsrfCookie("csrf-token");
    const confirmed = balance({ confirmedDueBy: "Example Reviewer", confirmedDueAt: "2026-10-12T09:00:00Z", confirmedDueReason: REASON, confirmedDueLiability: 150, confirmedTaxableProfitOutsideExemption: 1200, confirmedNonTradingProfitOutsideExemption: 0 });
    const fetchMock = installFetchMock((request) => request.url.endsWith(CONFIRM_URL) ? { body: confirmed } : undefined);
    const onConfirmed = renderConfirmation();

    expect(screen.getByText(/blocks the final accounts until it is confirmed as due/)).toBeInTheDocument();
    const button = screen.getByRole("button", { name: "Confirm this liability is due" });
    expect(button).toBeDisabled();
    await user.type(screen.getByLabelText("Why is this liability due?"), REASON);
    // The attested trading and non-trading profits outside the exemption are required too.
    expect(button).toBeDisabled();
    expect(screen.getByText(/Taxable profit from trading outside your charitable exemption/)).toBeInTheDocument();
    await user.type(screen.getByLabelText("Taxable trading profit outside the charitable exemption"), "1200");
    expect(button).toBeDisabled();
    expect(screen.getByText(/such as deposit interest or rent/)).toBeInTheDocument();
    await user.type(screen.getByLabelText("Taxable non-trading profit outside the charitable exemption"), "0");
    await user.click(button);

    await waitFor(() => expect(onConfirmed).toHaveBeenCalledWith(expect.objectContaining({ confirmedDueReason: REASON, confirmedDueLiability: 150, confirmedTaxableProfitOutsideExemption: 1200, confirmedNonTradingProfitOutsideExemption: 0 })));
    const request = fetchMock.one("POST", "/tax-balances/CorporationTax/confirm-due");
    expect(request.body).toEqual({ reason: REASON, taxableProfitOutsideExemption: 1200, nonTradingProfitOutsideExemption: 0 });
    expect(request.csrf).toBe("csrf-token");
  });

  it("shows nothing for a company without the exemption or without a liability", () => {
    const { container } = render(
      <CorporationTaxDueConfirmation companyId={1} periodId={3} holdsCharitableTaxExemption={false} balance={balance()} canConfirm onConfirmed={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
    const zero = render(
      <CorporationTaxDueConfirmation companyId={1} periodId={3} holdsCharitableTaxExemption balance={balance({ liability: 0, balance: 0 })} canConfirm onConfirmed={vi.fn()} />,
    );
    expect(zero.container).toBeEmptyDOMElement();
  });

  it("shows who confirmed it and why once confirmed, without the action", () => {
    renderConfirmation({
      balance: balance({ confirmedDueBy: "Example Reviewer", confirmedDueAt: "2026-10-12T09:00:00Z", confirmedDueReason: REASON, confirmedDueLiability: 150, confirmedTaxableProfitOutsideExemption: 1200, confirmedNonTradingProfitOutsideExemption: 0 }),
    });

    expect(screen.getByText(/confirmed as due by Example Reviewer/)).toHaveTextContent(REASON);
    expect(screen.getByText(/Saving a different liability clears this confirmation/)).toHaveTextContent("Taxable profit outside the charitable exemption, as attested: €1,200.00 trading and €0.00 non-trading.");
    expect(screen.queryByRole("button", { name: "Confirm this liability is due" })).not.toBeInTheDocument();
  });

  it("ends the confirmed reason with one full stop, whatever the reason ends with", () => {
    renderConfirmation({
      balance: balance({ confirmedDueBy: "Example Reviewer", confirmedDueAt: "2026-10-12T09:00:00Z", confirmedDueReason: "Trading income.", confirmedDueLiability: 150, confirmedTaxableProfitOutsideExemption: 1200, confirmedNonTradingProfitOutsideExemption: 0 }),
    });

    const status = screen.getByText(/confirmed as due by Example Reviewer/);
    expect(status).toHaveTextContent("Trading income. Saving a different liability clears this confirmation.");
    expect(status.textContent).not.toContain("..");
  });

  it("asks again when the confirmation was given for a different amount", () => {
    renderConfirmation({
      balance: balance({ confirmedDueBy: "Example Reviewer", confirmedDueAt: "2026-10-12T09:00:00Z", confirmedDueReason: REASON, confirmedDueLiability: 120, confirmedTaxableProfitOutsideExemption: 1200, confirmedNonTradingProfitOutsideExemption: 0 }),
    });

    expect(screen.queryByText(/confirmed as due by/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm this liability is due" })).toBeInTheDocument();
  });

  it("asks again when the confirmation carries no non-trading figure", () => {
    renderConfirmation({
      balance: balance({ confirmedDueBy: "Example Reviewer", confirmedDueAt: "2026-10-12T09:00:00Z", confirmedDueReason: REASON, confirmedDueLiability: 150, confirmedTaxableProfitOutsideExemption: 1200, confirmedNonTradingProfitOutsideExemption: null }),
    });

    expect(screen.queryByText(/confirmed as due by/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm this liability is due" })).toBeInTheDocument();
  });

  it("does not offer the action to users who are not an Owner or Reviewer", () => {
    renderConfirmation({ canConfirm: false });

    expect(screen.getByText("An Owner or Reviewer must confirm it.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm this liability is due" })).not.toBeInTheDocument();
  });

  it("asks for recent verification beside, not inside, the confirmation controls", async () => {
    const user = userEvent.setup();
    installFetchMock((request) => request.url.endsWith(CONFIRM_URL)
      ? { status: 428, body: { code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." } }
      : undefined);
    renderConfirmation();

    await user.type(screen.getByLabelText("Why is this liability due?"), REASON);
    await user.type(screen.getByLabelText("Taxable trading profit outside the charitable exemption"), "0");
    await user.type(screen.getByLabelText("Taxable non-trading profit outside the charitable exemption"), "0");
    await user.click(screen.getByRole("button", { name: "Confirm this liability is due" }));

    const prompt = await screen.findByRole("form", { name: "Recent verification" });
    expect(prompt.parentElement?.closest("form")).toBeNull();
    expect(screen.getByRole("button", { name: "Confirm this liability is due" })).toBeInTheDocument();
  });
});
