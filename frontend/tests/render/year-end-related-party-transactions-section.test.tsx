import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndRelatedPartyTransactionsSection } from "@/components/period/YearEndRelatedPartyTransactionsSection";
import type { RelatedPartyTransaction } from "@/lib/api";

describe("YearEndRelatedPartyTransactionsSection", () => {
  it("renders related-party rows and calls add/delete with edited disclosure fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<RelatedPartyTransactionsHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("Jane Director")).toBeInTheDocument();
    expect(screen.getAllByText("Director").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Loan").length).toBeGreaterThan(0);
    expect(screen.getByText("Balance owed: \u20ac1,500.00")).toBeInTheDocument();
    expect(screen.getByText("\u20ac8,250.00")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Party name" }), "Connected supplier");
    await user.selectOptions(screen.getByRole("combobox", { name: "Relationship" }), "Connected Person");
    await user.selectOptions(screen.getByRole("combobox", { name: "Transaction type" }), "Management Fee");
    await user.type(screen.getByRole("spinbutton", { name: "Transaction amount" }), "4200");
    await user.click(screen.getByRole("button", { name: "Add related party transaction" }));
    await user.click(screen.getByRole("button", { name: "Delete transaction with Jane Director" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ partyName: "Connected supplier" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ relationship: "Connected Person" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ transactionType: "Management Fee" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ amount: 4200 }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(41);
  });
});

function RelatedPartyTransactionsHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: RelatedPartyTransaction) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<RelatedPartyTransaction>({
    partyName: "",
    relationship: "Director",
    transactionType: "Sale",
    amount: 0,
  });

  function handleChange(nextDraft: RelatedPartyTransaction) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndRelatedPartyTransactionsSection
      transactions={[{
        id: 41,
        partyName: "Jane Director",
        relationship: "Director",
        transactionType: "Loan",
        amount: 8250,
        balanceOwed: 1500,
      }]}
      draft={draft}
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
