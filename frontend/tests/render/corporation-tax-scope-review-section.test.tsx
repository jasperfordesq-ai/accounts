import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { CorporationTaxScopeReviewSection } from "@/components/period/CorporationTaxScopeReviewSection";
import type { CorporationTaxScopeReviewInput } from "@/lib/api";

describe("CorporationTaxScopeReviewSection", () => {
  it("labels support-only scope, captures unsupported cases, and retains an accessible save name", async () => {
    const user = userEvent.setup();
    const onSave = vi.fn();
    render(<Harness onSave={onSave} />);

    expect(screen.getByText(/not a CT1 return/i)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Is the company a close company?"), "yes");
    await user.selectOptions(screen.getByLabelText("If close, is it a service company?"), "yes");
    await user.selectOptions(screen.getByLabelText("Is the company a close company?"), "no");
    expect(screen.getByLabelText("If close, is it a service company?")).toHaveValue("unknown");
    expect(screen.getByLabelText("If close, is it a service company?")).toBeDisabled();
    await user.selectOptions(screen.getByLabelText("Is the company a close company?"), "yes");
    await user.click(screen.getByLabelText("Group or consortium relief/claim"));
    await user.selectOptions(screen.getByLabelText("Loss treatment / election"), "GroupRelief");
    await user.type(screen.getByLabelText("Scope evidence note"), "Retained scope questionnaire evidence reference.");
    await user.click(screen.getByRole("button", { name: "Save tax scope and loss ledger" }));

    expect(onSave).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText("Is the company a close company?")).toHaveValue("yes");
    expect(screen.getByLabelText("Group or consortium relief/claim")).toBeChecked();
  });

  it("keeps retained scope available while surfacing a calculation failure", () => {
    render(
      <CorporationTaxScopeReviewSection
        canWrite={false}
        form={emptyForm()}
        result={{
          review: null,
          computationFailure: "The bank control account is required before tax support can be produced.",
        }}
        saving={false}
        onFormChange={vi.fn()}
        onSave={vi.fn()}
      />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Tax support calculation unavailable");
    expect(screen.getByRole("alert")).toHaveTextContent("bank control account");
    expect(screen.getByText("No corporation-tax scope declaration is retained.")).toBeInTheDocument();
  });
});

function Harness({ onSave }: { onSave: () => void }) {
  const [form, setForm] = useState<CorporationTaxScopeReviewInput>(emptyForm());
  return (
    <CorporationTaxScopeReviewSection
      form={form}
      result={null}
      saving={false}
      onFormChange={setForm}
      onSave={onSave}
    />
  );
}

function emptyForm(): CorporationTaxScopeReviewInput {
  return {
    isCloseCompany: null,
    isServiceCompany: null,
    hasGroupOrConsortiumRelief: false,
    hasChargeableGains: false,
    hasForeignIncomeOrTaxCredits: false,
    hasExceptedTrade: false,
    hasOtherReliefsOrSpecialRegimes: false,
    declaredPassiveIncomePresent: false,
    passiveIncomeClassificationReviewed: false,
    lossTreatment: "Unreviewed",
    broughtForwardTradingLoss: 0,
    evidenceNote: "",
  };
}
