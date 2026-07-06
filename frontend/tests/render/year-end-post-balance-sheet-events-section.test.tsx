import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndPostBalanceSheetEventsSection } from "@/components/period/YearEndPostBalanceSheetEventsSection";
import type { PostBalanceSheetEvent } from "@/lib/api";

describe("YearEndPostBalanceSheetEventsSection", () => {
  it("renders event treatment and calls add/delete with edited event fields", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onAdd = vi.fn();
    const onDelete = vi.fn();

    render(<PostBalanceSheetEventsHarness onChange={onChange} onAdd={onAdd} onDelete={onDelete} />);

    expect(screen.getByText("Major contract signed")).toBeInTheDocument();
    expect(screen.getByText("30/4/2026")).toBeInTheDocument();
    expect(screen.getByText("Non-adjusting")).toBeInTheDocument();
    expect(screen.getByText("Impact: \u20ac7,500.00")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Event description" }), "Customer insolvency");
    await user.type(screen.getByLabelText("Event date"), "2026-05-15");
    await user.type(screen.getByRole("spinbutton", { name: "Financial impact" }), "2500");
    await user.click(screen.getByRole("checkbox", { name: "Adjusting" }));
    await user.click(screen.getByRole("button", { name: "Add post-balance sheet event" }));
    await user.click(screen.getByRole("button", { name: "Delete event Major contract signed" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ description: "Customer insolvency" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ eventDate: "2026-05-15" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ financialImpact: 2500 }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ isAdjusting: true }));
    expect(onAdd).toHaveBeenCalledTimes(1);
    expect(onDelete).toHaveBeenCalledWith(31);
  });
});

function PostBalanceSheetEventsHarness({
  onChange,
  onAdd,
  onDelete,
}: {
  onChange: (draft: PostBalanceSheetEvent) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}) {
  const [draft, setDraft] = useState<PostBalanceSheetEvent>({
    description: "",
    eventDate: "",
    isAdjusting: false,
    financialImpact: 0,
  });

  function handleChange(nextDraft: PostBalanceSheetEvent) {
    setDraft(nextDraft);
    onChange(nextDraft);
  }

  return (
    <YearEndPostBalanceSheetEventsSection
      events={[{
        id: 31,
        description: "Major contract signed",
        eventDate: "2026-04-30",
        isAdjusting: false,
        financialImpact: 7500,
      }]}
      draft={draft}
      saving={false}
      onDraftChange={handleChange}
      onAdd={onAdd}
      onDelete={onDelete}
    />
  );
}
