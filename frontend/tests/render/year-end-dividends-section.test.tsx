import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndDividendsSection } from "@/components/period/YearEndDividendsSection";
import type { Dividend } from "@/lib/api";

describe("YearEndDividendsSection", () => {
  it("renders dividend rows and calls add/delete with edited declaration fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<DividendsHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("\u20ac4,200.00")).toBeInTheDocument();
    expect(screen.getByText("Declared: 1/4/2026")).toBeInTheDocument();
    expect(screen.getByText("Paid: 15/4/2026")).toBeInTheDocument();

    await user.clear(screen.getByRole("spinbutton", { name: "Dividend amount" }));
    await user.type(screen.getByRole("spinbutton", { name: "Dividend amount" }), "1800");
    await user.type(screen.getByLabelText("Date dividend declared"), "2026-05-01");
    await user.type(screen.getByLabelText("Date dividend paid"), "2026-05-15");
    await user.click(screen.getByRole("button", { name: "Add dividend" }));
    await user.click(screen.getByRole("button", { name: "Delete dividend of \u20ac4,200.00" }));
    expect(onDelete).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Remove dividend of \u20ac4,200.00?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove record" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ amount: 1800 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ dateDeclared: "2026-05-01" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ datePaid: "2026-05-15" }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(23);
  });
});

function DividendsHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: Dividend) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<Dividend>({ amount: 0, dateDeclared: "", datePaid: "" });

  function handleChange(nextDraft: Dividend) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndDividendsSection
      dividends={[{
        id: 23,
        amount: 4200,
        dateDeclared: "2026-04-01",
        datePaid: "2026-04-15",
      }]}
      draft={draft}
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
