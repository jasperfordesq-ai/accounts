import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndPayrollSection } from "@/components/period/YearEndPayrollSection";
import type { PayrollSummary } from "@/lib/api";

describe("YearEndPayrollSection", () => {
  it("captures payroll and staff-cost fields before saving the working paper", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(<PayrollHarness onChange={onChange} onSave={onSave} />);

    await user.clear(screen.getByRole("spinbutton", { name: "Number of staff" }));
    await user.type(screen.getByRole("spinbutton", { name: "Number of staff" }), "4");
    await user.clear(screen.getByRole("spinbutton", { name: "Gross wages" }));
    await user.type(screen.getByRole("spinbutton", { name: "Gross wages" }), "62000");
    await user.clear(screen.getByRole("spinbutton", { name: "Employer PRSI" }));
    await user.type(screen.getByRole("spinbutton", { name: "Employer PRSI" }), "6820");
    await user.clear(screen.getByRole("spinbutton", { name: "Pension contributions" }));
    await user.type(screen.getByRole("spinbutton", { name: "Pension contributions" }), "2400");
    await user.click(screen.getByRole("button", { name: "Save payroll" }));

    expect(screen.getByText(/payroll and staff costs/i)).toBeInTheDocument();
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ staffCount: 4 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ grossWages: 62000 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ employerPrsi: 6820 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ pensionContributions: 2400 }));
    expect(onSave).toHaveBeenCalledTimes(1);
  });
});

function PayrollHarness({
  onChange,
  onSave,
}: {
  onChange: (form: PayrollSummary) => void;
  onSave: () => void;
}) {
  const [form, setForm] = useState<PayrollSummary>({
    staffCount: 1,
    grossWages: 100,
    employerPrsi: 10,
    pensionContributions: 5,
  });

  function handleChange(nextForm: PayrollSummary) {
    setForm(nextForm);
    onChange(nextForm);
  }

  return (
    <YearEndPayrollSection
      form={form}
      saving={false}
      onFormChange={handleChange}
      onSave={onSave}
    />
  );
}
