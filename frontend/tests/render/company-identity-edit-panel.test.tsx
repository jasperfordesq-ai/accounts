import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyIdentityEditPanel, type CompanyEditFormValues } from "@/components/company/CompanyIdentityEditPanel";

describe("CompanyIdentityEditPanel", () => {
  it("renders company identity fields and wires edit actions", () => {
    const form = sampleForm();
    const onFormChange = vi.fn();
    const onSave = vi.fn();
    const onCancel = vi.fn();

    render(
      <CompanyIdentityEditPanel
        form={form}
        saving={false}
        onFormChange={onFormChange}
        onSave={onSave}
        onCancel={onCancel}
      />,
    );

    expect(screen.getByText("Edit Company Identity")).toBeInTheDocument();
    expect(screen.getByText("Legal identity, CRO references and trading flags used across statutory workflows.")).toBeInTheDocument();
    expect(screen.getByLabelText("Legal Name")).toHaveValue("Atlantic Accounts Limited");
    expect(screen.getByLabelText("Trading Name")).toHaveValue("Atlantic Accounts");
    expect(screen.getByLabelText("CRO Number")).toHaveValue("123456");
    expect(screen.getByLabelText("Tax Reference")).toHaveValue("123456T");
    expect(screen.getByLabelText("Company Type")).toHaveValue("Private");
    expect(screen.getByLabelText("Is Trading")).toBeChecked();
    expect(screen.getByLabelText("Is Dormant")).not.toBeChecked();

    fireEvent.change(screen.getByLabelText("Legal Name"), { target: { value: "Atlantic Advisory Limited" } });
    fireEvent.change(screen.getByLabelText("Company Type"), { target: { value: "CompanyLimitedByGuarantee" } });
    fireEvent.click(screen.getByLabelText("Is Dormant"));
    fireEvent.click(screen.getByRole("button", { name: "Save company changes" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel company editing" }));

    expect(onFormChange).toHaveBeenCalledWith({ ...form, legalName: "Atlantic Advisory Limited" });
    expect(onFormChange).toHaveBeenCalledWith({ ...form, companyType: "CompanyLimitedByGuarantee" });
    expect(onFormChange).toHaveBeenCalledWith({ ...form, isDormant: true });
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("surfaces the saving state without losing field context", () => {
    render(
      <CompanyIdentityEditPanel
        form={sampleForm()}
        saving
        onFormChange={vi.fn()}
        onSave={vi.fn()}
        onCancel={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "Save company changes" })).toBeDisabled();
    expect(screen.getByText("Saving...")).toBeInTheDocument();
    expect(screen.getByText("Draft changes")).toBeInTheDocument();
  });
});

function sampleForm(): CompanyEditFormValues {
  return {
    legalName: "Atlantic Accounts Limited",
    tradingName: "Atlantic Accounts",
    croNumber: "123456",
    taxReference: "123456T",
    companyType: "Private",
    isTrading: true,
    isDormant: false,
  };
}
