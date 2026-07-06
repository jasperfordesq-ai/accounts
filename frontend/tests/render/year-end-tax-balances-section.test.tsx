import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndTaxBalancesSection } from "@/components/period/YearEndTaxBalancesSection";
import type { TaxBalance } from "@/lib/api";

describe("YearEndTaxBalancesSection", () => {
  it("captures liability, paid and balance for each tax type before saving", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(<TaxBalancesHarness onChange={onChange} onSave={onSave} />);

    expect(screen.getByText(/tax creditor\/debtor balances/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Corporation Tax" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "VAT" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "PAYE / PRSI" })).toBeInTheDocument();

    await user.clear(screen.getByRole("spinbutton", { name: "Corporation Tax liability" }));
    await user.type(screen.getByRole("spinbutton", { name: "Corporation Tax liability" }), "12500");
    await user.clear(screen.getByRole("spinbutton", { name: "Corporation Tax paid" }));
    await user.type(screen.getByRole("spinbutton", { name: "Corporation Tax paid" }), "3000");
    await user.clear(screen.getByRole("spinbutton", { name: "Corporation Tax balance" }));
    await user.type(screen.getByRole("spinbutton", { name: "Corporation Tax balance" }), "9500");
    await user.click(screen.getByRole("button", { name: "Save Corporation Tax balance" }));

    expect(onChange).toHaveBeenCalledWith("CorporationTax", expect.objectContaining({ liability: 12500 }));
    expect(onChange).toHaveBeenCalledWith("CorporationTax", expect.objectContaining({ paid: 3000 }));
    expect(onChange).toHaveBeenCalledWith("CorporationTax", expect.objectContaining({ balance: 9500 }));
    expect(onSave).toHaveBeenCalledWith("CorporationTax");
  });
});

function TaxBalancesHarness({
  onChange,
  onSave,
}: {
  onChange: (taxType: string, balance: TaxBalance) => void;
  onSave: (taxType: string) => void;
}) {
  const [forms, setForms] = useState<Record<string, TaxBalance>>({
    CorporationTax: { taxType: "CorporationTax", liability: 100, paid: 10, balance: 90 },
    VAT: { taxType: "VAT", liability: 0, paid: 0, balance: 0 },
    PAYE_PRSI: { taxType: "PAYE_PRSI", liability: 0, paid: 0, balance: 0 },
  });

  function handleChange(taxType: string, balance: TaxBalance) {
    setForms((current) => ({ ...current, [taxType]: balance }));
    onChange(taxType, balance);
  }

  return (
    <YearEndTaxBalancesSection
      forms={forms}
      savingKey={null}
      onFormChange={handleChange}
      onSave={onSave}
    />
  );
}
