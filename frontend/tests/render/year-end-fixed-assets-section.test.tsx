import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndFixedAssetsSection } from "@/components/period/YearEndFixedAssetsSection";
import type { FixedAsset } from "@/lib/api";

describe("YearEndFixedAssetsSection", () => {
  it("renders asset rows and calls add/delete with edited working-paper fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<FixedAssetHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("Company van")).toBeInTheDocument();
    expect(screen.getAllByText("Vehicles").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("5yr StraightLine")).toBeInTheDocument();
    expect(screen.getByText("Acquired 15/2/2026")).toBeInTheDocument();
    expect(screen.getByText("\u20ac24,500.00")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Asset name" }), "Laptop fleet");
    await user.selectOptions(screen.getByRole("combobox", { name: "Asset category" }), "IT");
    await user.clear(screen.getByRole("spinbutton", { name: "Asset cost" }));
    await user.type(screen.getByRole("spinbutton", { name: "Asset cost" }), "3100");
    await user.type(screen.getByLabelText("Asset acquisition date"), "2026-03-31");
    await user.clear(screen.getByRole("spinbutton", { name: "Useful life in years" }));
    await user.type(screen.getByRole("spinbutton", { name: "Useful life in years" }), "3");
    await user.selectOptions(screen.getByRole("combobox", { name: "Depreciation method" }), "ReducingBalance");
    await user.click(screen.getByRole("button", { name: "Add asset" }));
    await user.click(screen.getByRole("button", { name: "Delete asset Company van" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ name: "Laptop fleet" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ category: "IT" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ cost: 3100 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ acquisitionDate: "2026-03-31" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ usefulLifeYears: 3 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ depreciationMethod: "ReducingBalance" }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(7);
  });
});

function FixedAssetHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: FixedAsset) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<FixedAsset>({
    name: "",
    category: "Equipment",
    cost: 0,
    acquisitionDate: "",
    usefulLifeYears: 5,
    depreciationMethod: "StraightLine",
  });

  function handleChange(nextDraft: FixedAsset) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndFixedAssetsSection
      assets={[{
        id: 7,
        name: "Company van",
        category: "Vehicles",
        cost: 24500,
        acquisitionDate: "2026-02-15",
        usefulLifeYears: 5,
        depreciationMethod: "StraightLine",
      }]}
      draft={draft}
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
