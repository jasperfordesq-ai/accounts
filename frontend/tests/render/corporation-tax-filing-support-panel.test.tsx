import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { CorporationTaxFilingSupportPanel } from "@/components/period/CorporationTaxFilingSupportPanel";
import { corporationTaxFilingSupportFixture } from "../fixtures/corporation-tax-filing-support";

describe("CorporationTaxFilingSupportPanel", () => {
  it("makes the non-CT1 boundary, due schedule, evidence, mappings and manual gate explicit", () => {
    render(
      <CorporationTaxFilingSupportPanel
        companyId={7}
        periodId={3}
        response={corporationTaxFilingSupportFixture()}
      />,
    );

    expect(screen.getByRole("heading", { name: /Support worksheet only.*not a CT1 return/i })).toBeInTheDocument();
    expect(screen.getByText(/Nothing on this screen is submitted to Revenue/)).toBeInTheDocument();
    expect(screen.getByText("Safe harbour: Missed")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Corporation Tax due schedule" })).toHaveAttribute("tabindex", "0");
    expect(screen.getByText("Accelerated current-period liability")).toBeInTheDocument();
    expect(screen.getByText("Retained ROS payment receipt PTL2-2025-00081.")).toBeInTheDocument();
    expect(screen.getByText("Sales / Receipts / Turnover")).toBeInTheDocument();
    expect(screen.getByText("Exact published label")).toBeInTheDocument();
    expect(screen.getByText(/named qualified accountant approve/)).toBeInTheDocument();

    expect(screen.getByRole("link", { name: "Support worksheet CSV" })).toHaveAttribute(
      "href",
      "/api/companies/7/periods/3/revenue/ct1-support/worksheet.csv",
    );
    expect(screen.getByRole("link", { name: "Evidence JSON" })).toHaveAttribute(
      "href",
      "/api/companies/7/periods/3/revenue/ct1-support/worksheet",
    );
  });

  it("captures reviewed bases and retained payment evidence without a submission action", async () => {
    const user = userEvent.setup();
    const saveReview = vi.fn();
    const recordPayment = vi.fn();
    const deletePayment = vi.fn();
    render(
      <CorporationTaxFilingSupportPanel
        companyId={7}
        periodId={3}
        response={corporationTaxFilingSupportFixture()}
        canWrite
        onSaveReview={saveReview}
        onRecordPayment={recordPayment}
        onDeletePayment={deletePayment}
      />,
    );

    await user.click(screen.getByText("Maintain preliminary-tax basis review"));
    const note = screen.getByLabelText("Preparation evidence note");
    await user.clear(note);
    await user.type(note, "Updated against retained signed CT1 evidence and ROS confirmations.");
    await user.click(screen.getByRole("button", { name: "Save preliminary-tax review" }));
    expect(saveReview).toHaveBeenCalledWith(expect.objectContaining({
      priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239: 6_000,
      evidenceNote: "Updated against retained signed CT1 evidence and ROS confirmations.",
    }));

    await user.click(screen.getByText("Record retained payment evidence"));
    await user.type(screen.getByLabelText("Payment date"), "2026-09-23");
    await user.type(screen.getByLabelText("Amount *"), "2000");
    await user.type(screen.getByLabelText("Retained payment evidence reference"), "ROS balance receipt evidence 2026-09-23-001.");
    await user.type(screen.getByLabelText("External payment reference (optional)"), "ROS-BAL-001");
    await user.click(screen.getByRole("button", { name: "Record payment evidence" }));
    expect(recordPayment).toHaveBeenCalledWith({
      paymentDate: "2026-09-23",
      amount: 2_000,
      kind: "PreliminarySecondOrSingle",
      evidenceReference: "ROS balance receipt evidence 2026-09-23-001.",
      externalPaymentReference: "ROS-BAL-001",
    });

    await user.click(screen.getByRole("button", { name: "Remove payment evidence dated 2025-11-23" }));
    expect(screen.getByRole("alertdialog", { name: "Remove incorrect payment evidence?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove evidence row" }));
    expect(deletePayment).toHaveBeenCalledWith(81);
    expect(screen.queryByRole("button", { name: /submit/i })).not.toBeInTheDocument();
  });
});
