import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { YearEndGoingConcernSection } from "@/components/period/YearEndGoingConcernSection";

describe("YearEndGoingConcernSection", () => {
  it("captures going-concern confirmation, uncertainty notes and save actions", async () => {
    const user = userEvent.setup();
    const onConfirmedChange = vi.fn();
    const onNoteChange = vi.fn();
    const onSave = vi.fn();

    render(
      <GoingConcernHarness
        onConfirmedChange={onConfirmedChange}
        onNoteChange={onNoteChange}
        onSave={onSave}
      />,
    );

    expect(screen.queryByText(/Warning: Going concern is not confirmed/)).not.toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Going concern note" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("checkbox", { name: "The directors confirm the company is a going concern" }));

    expect(screen.getByText(/Warning: Going concern is not confirmed/)).toBeInTheDocument();

    const note = screen.getByRole("textbox", { name: "Going concern note" });
    await user.type(note, "Cash runway depends on renewed facility.");
    await user.click(screen.getByRole("button", { name: "Save Going Concern" }));

    expect(onConfirmedChange).toHaveBeenCalledWith(false);
    expect(onNoteChange).toHaveBeenCalledWith("Cash runway depends on renewed facility.");
    expect(onSave).toHaveBeenCalledTimes(1);
  });
});

function GoingConcernHarness({
  onConfirmedChange,
  onNoteChange,
  onSave,
}: {
  onConfirmedChange: (confirmed: boolean) => void;
  onNoteChange: (note: string) => void;
  onSave: () => void;
}) {
  const [confirmed, setConfirmed] = useState(true);
  const [note, setNote] = useState("");

  function handleConfirmedChange(nextConfirmed: boolean) {
    setConfirmed(nextConfirmed);
    onConfirmedChange(nextConfirmed);
  }

  function handleNoteChange(nextNote: string) {
    setNote(nextNote);
    onNoteChange(nextNote);
  }

  return (
    <YearEndGoingConcernSection
      confirmed={confirmed}
      note={note}
      saving={false}
      onConfirmedChange={handleConfirmedChange}
      onNoteChange={handleNoteChange}
      onSave={onSave}
    />
  );
}
