import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndContingentLiabilitiesSection } from "@/components/period/YearEndContingentLiabilitiesSection";
import type { ContingentLiability } from "@/lib/api";

describe("YearEndContingentLiabilitiesSection", () => {
  it("renders contingency risk cues and calls add/delete with edited disclosure fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<ContingentLiabilitiesHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("Pending supplier claim")).toBeInTheDocument();
    expect(screen.getAllByText("Legal Claim").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Probable").length).toBeGreaterThan(0);
    expect(screen.getByText("\u20ac12,500.00")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Contingency description" }), "Product warranty exposure");
    await user.selectOptions(screen.getByRole("combobox", { name: "Contingency nature" }), "Warranty");
    await user.type(screen.getByRole("spinbutton", { name: "Estimated amount" }), "6400");
    await user.selectOptions(screen.getByRole("combobox", { name: "Contingency likelihood" }), "Possible");
    await user.click(screen.getByRole("button", { name: "Add contingent liability" }));
    await user.click(screen.getByRole("button", { name: "Delete contingency Pending supplier claim" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ description: "Product warranty exposure" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ nature: "Warranty" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ estimatedAmount: 6400 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ likelihood: "Possible" }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(51);
  });
});

function ContingentLiabilitiesHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: ContingentLiability) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<ContingentLiability>({
    description: "",
    nature: "Guarantee",
    estimatedAmount: 0,
    likelihood: "Possible",
  });

  function handleChange(nextDraft: ContingentLiability) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndContingentLiabilitiesSection
      contingencies={[{
        id: 51,
        description: "Pending supplier claim",
        nature: "Legal Claim",
        estimatedAmount: 12500,
        likelihood: "Probable",
      }]}
      draft={draft}
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
