import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndInventorySection } from "@/components/period/YearEndInventorySection";
import type { InventoryItem } from "@/lib/api";

describe("YearEndInventorySection", () => {
  it("renders inventory rows and calls add/delete with edited valuation fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<InventoryHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("Finished goods")).toBeInTheDocument();
    expect(screen.getAllByText("FIFO").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("\u20ac8,400.00")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Inventory item description" }), "Work in progress");
    await user.clear(screen.getByRole("spinbutton", { name: "Inventory item value" }));
    await user.type(screen.getByRole("spinbutton", { name: "Inventory item value" }), "2150");
    await user.selectOptions(screen.getByRole("combobox", { name: "Valuation method" }), "WeightedAverage");
    await user.click(screen.getByRole("button", { name: "Add inventory item" }));
    await user.click(screen.getByRole("button", { name: "Delete inventory item Finished goods" }));
    expect(onDelete).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Remove inventory item Finished goods?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove record" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ description: "Work in progress" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ value: 2150 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ valuationMethod: "WeightedAverage" }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(11);
  });
});

function InventoryHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: InventoryItem) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<InventoryItem>({
    description: "",
    value: 0,
    valuationMethod: "FIFO",
  });

  function handleChange(nextDraft: InventoryItem) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndInventorySection
      items={[{ id: 11, description: "Finished goods", value: 8400, valuationMethod: "FIFO" }]}
      draft={draft}
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
