import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndMoneyListSection } from "@/components/period/YearEndMoneyListSection";
import type { Creditor, Debtor } from "@/lib/api";

describe("YearEndMoneyListSection", () => {
  it("renders debtor rows and calls add/delete with edited entry fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<DebtorHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("Trade customer")).toBeInTheDocument();
    expect(screen.getByText("Invoice 42")).toBeInTheDocument();
    expect(screen.getByText("\u20ac1,250.00")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Debtor name" }), "New debtor");
    await user.clear(screen.getByRole("spinbutton", { name: "Debtor amount" }));
    await user.type(screen.getByRole("spinbutton", { name: "Debtor amount" }), "99");
    await user.selectOptions(screen.getByRole("combobox", { name: "Debtor type" }), "Prepayment");
    await user.click(screen.getByRole("button", { name: "Add debtor" }));
    await user.click(screen.getByRole("button", { name: "Delete debtor Trade customer" }));
    expect(onDelete).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Remove debtor Trade customer?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove record" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ name: "New debtor" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ amount: 99 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ type: "Prepayment" }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(5);
  });

  it("renders creditor maturity cues and due-within-year editing", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <YearEndMoneyListSection<Creditor>
        mode="creditors"
        items={[{ id: 9, name: "Supplier Limited", amount: 840, type: "Accrual", dueWithinYear: false }]}
        draft={{ name: "Supplier", amount: 10, type: "Trade", dueWithinYear: true }}
        typeOptions={["Trade", "Other", "Accrual", "Tax"]}
        namePlaceholder="Who do you owe?"
        saving={false}
        onDraftChange={onChange}
        onAdd={vi.fn()}
        onDelete={vi.fn()}
      />,
    );

    expect(screen.getByText("Supplier Limited")).toBeInTheDocument();
    expect(screen.getByText("Due > 1 year")).toBeInTheDocument();

    await user.selectOptions(screen.getByRole("combobox", { name: "Due within one year" }), "no");

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ dueWithinYear: false }));
  });
});

function DebtorHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: Debtor) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<Debtor>({ name: "", amount: 0, type: "Trade" });

  function handleChange(nextDraft: Debtor) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndMoneyListSection<Debtor>
      mode="debtors"
      items={[{ id: 5, name: "Trade customer", amount: 1250, type: "Trade", notes: "Invoice 42" }]}
      draft={draft}
      typeOptions={["Trade", "Other", "Prepayment"]}
      namePlaceholder="Who owes you?"
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
