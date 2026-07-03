import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyCharityInfoPanel } from "@/components/company/CompanyCharityInfoPanel";
import type { CharityInfo } from "@/lib/api";

describe("CompanyCharityInfoPanel", () => {
  it("surfaces charity reporting facts and edit action", () => {
    const onStartEdit = vi.fn();

    render(
      <CompanyCharityInfoPanel
        charityInfo={sampleCharityInfo()}
        charityForm={sampleCharityInfo()}
        editing={false}
        saving={false}
        onStartEdit={onStartEdit}
        onCancelEdit={vi.fn()}
        onSave={vi.fn()}
        onFormChange={vi.fn()}
      />,
    );

    expect(screen.getByText("Charity Reporting")).toBeInTheDocument();
    expect(screen.getByText("Charities Regulator and SORP facts for CLG charity workflows.")).toBeInTheDocument();
    expect(screen.getByText("CHY12345")).toBeInTheDocument();
    expect(screen.getAllByText("Tier 1").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText("Governance confirmed")).toBeInTheDocument();
    expect(screen.getByText("Trustee remuneration")).toBeInTheDocument();
    expect(screen.getByText("International transfers")).toBeInTheDocument();
    expect(screen.getByText("Advance community accounting education.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Edit charity reporting" }));

    expect(onStartEdit).toHaveBeenCalledTimes(1);
  });

  it("edits charity form fields and wires save/cancel actions", () => {
    const onFormChange = vi.fn();
    const onSave = vi.fn();
    const onCancelEdit = vi.fn();
    const form = sampleCharityInfo();

    render(
      <CompanyCharityInfoPanel
        charityInfo={form}
        charityForm={form}
        editing
        saving={false}
        onStartEdit={vi.fn()}
        onCancelEdit={onCancelEdit}
        onSave={onSave}
        onFormChange={onFormChange}
      />,
    );

    fireEvent.change(screen.getByLabelText("Charity number"), { target: { value: "CHY99999" } });
    fireEvent.change(screen.getByLabelText("Gross income"), { target: { value: "300000" } });
    fireEvent.change(screen.getByLabelText("SORP tier"), { target: { value: "2" } });
    fireEvent.click(screen.getByRole("button", { name: "Save charity reporting" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel charity reporting edit" }));

    expect(onFormChange).toHaveBeenCalledWith({ ...form, charityNumber: "CHY99999" });
    expect(onFormChange).toHaveBeenCalledWith({ ...form, grossIncome: 300000 });
    expect(onFormChange).toHaveBeenCalledWith({ ...form, sorpTier: 2 });
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onCancelEdit).toHaveBeenCalledTimes(1);
  });

  it("keeps empty charity records read-only for users without write access", () => {
    render(
      <CompanyCharityInfoPanel
        charityInfo={null}
        charityForm={sampleCharityInfo()}
        editing={false}
        saving={false}
        canWrite={false}
        onStartEdit={vi.fn()}
        onCancelEdit={vi.fn()}
        onSave={vi.fn()}
        onFormChange={vi.fn()}
      />,
    );

    expect(screen.getByText("No charity information recorded")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add charity reporting" })).not.toBeInTheDocument();
    expect(screen.getByText("Read only")).toBeInTheDocument();
  });
});

function sampleCharityInfo(): CharityInfo {
  return {
    id: 4,
    companyId: 7,
    charityNumber: "CHY12345",
    grossIncome: 250000,
    sorpTier: 1,
    charitableObjectives: "Advance community accounting education.",
    principalActivities: "Training, governance clinics and financial literacy programmes.",
    governanceCodeCompliant: true,
    hasInternationalTransfers: true,
    internationalTransferDetails: "Grant transfer to EU partner.",
    trusteeRemunerationPaid: true,
    trusteeRemunerationAmount: 1200,
  };
}
