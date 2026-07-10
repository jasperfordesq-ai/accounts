import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

function GuardHarness({ fail = false, deferred }: { fail?: boolean; deferred?: Promise<void> }) {
  const [recordPresent, setRecordPresent] = useState(true);
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  return (
    <div>
      {recordPresent && <p>Trade debtor Alpha Limited</p>}
      <button
        type="button"
        onClick={() => requestDestructiveAction({
          recordLabel: "debtor Alpha Limited",
          consequence: "This permanently removes the €1,250.00 debtor balance and its evidence. The removal cannot be undone.",
          onConfirm: async () => {
            if (deferred) await deferred;
            if (fail) throw new Error("server rejected deletion");
            setRecordPresent(false);
          },
          successAnnouncement: "Debtor Alpha Limited was removed.",
        })}
      >
        Delete debtor
      </button>
      {destructiveActionConfirmation}
    </div>
  );
}

describe("useDestructiveActionConfirmation", () => {
  it("names the record and consequence, while cancel keeps data and restores trigger focus", async () => {
    const user = userEvent.setup();
    render(<GuardHarness />);
    const trigger = screen.getByRole("button", { name: "Delete debtor" });

    await user.click(trigger);

    expect(screen.getByRole("alertdialog", { name: "Remove debtor Alpha Limited?" })).toBeInTheDocument();
    expect(screen.getByText(/permanently removes the €1,250\.00 debtor balance/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Keep record" }));

    expect(screen.getByText("Trade debtor Alpha Limited")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Removal cancelled. Debtor Alpha Limited was kept.");
    expect(trigger).toHaveFocus();
  });

  it("waits for confirmation, removes only after success, and announces completion", async () => {
    const user = userEvent.setup();
    let resolveAction = () => {};
    const deferred = new Promise<void>((resolve) => { resolveAction = resolve; });
    render(<GuardHarness deferred={deferred} />);

    await user.click(screen.getByRole("button", { name: "Delete debtor" }));
    await user.click(screen.getByRole("button", { name: "Remove record" }));

    expect(screen.getByText("Trade debtor Alpha Limited")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Processing..." })).toBeDisabled();
    resolveAction();

    await waitFor(() => expect(screen.queryByText("Trade debtor Alpha Limited")).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent("Debtor Alpha Limited was removed.");
  });

  it("keeps the record and announces failure when the delete request rejects", async () => {
    const user = userEvent.setup();
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    render(<GuardHarness fail />);

    await user.click(screen.getByRole("button", { name: "Delete debtor" }));
    await user.click(screen.getByRole("button", { name: "Remove record" }));

    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    expect(screen.getByText("Trade debtor Alpha Limited")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Debtor Alpha Limited was not removed. The retained record is unchanged.");
    consoleError.mockRestore();
  });
});
