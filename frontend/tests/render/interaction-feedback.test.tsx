import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { InteractionAnnouncement } from "@/components/InteractionAnnouncement";
import {
  captureInteractionFocus,
  useInteractionAnnouncements,
} from "@/lib/interactionState";

function AnnouncementHarness() {
  const { announcement, announce } = useInteractionAnnouncements();
  const [count, setCount] = useState(0);
  return (
    <>
      <InteractionAnnouncement announcement={announcement} />
      <button type="button" onClick={() => { setCount((value) => value + 1); announce("success", "Review evidence saved"); }}>
        Save success {count}
      </button>
      <button type="button" onClick={() => announce("error", "Review evidence was not saved")}>Save error</button>
    </>
  );
}

describe("interaction feedback", () => {
  beforeEach(() => {
    vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    });
    vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  });

  it("restores the initiating control and captured viewport after an async row refresh", async () => {
    render(
      <>
        <button type="button">Save changed row</button>
        <button type="button">Temporary focus</button>
      </>,
    );
    const trigger = screen.getByRole("button", { name: "Save changed row" });
    const temporary = screen.getByRole("button", { name: "Temporary focus" });
    trigger.focus();
    const focus = captureInteractionFocus();
    temporary.focus();

    focus.restore();

    await waitFor(() => expect(trigger).toHaveFocus());
    expect(window.scrollTo).toHaveBeenCalledWith({ left: 0, top: 0, behavior: "auto" });
  });

  it("announces repeatable success politely and errors assertively", async () => {
    const user = userEvent.setup();
    render(<AnnouncementHarness />);

    await user.click(screen.getByRole("button", { name: /Save success/ }));
    expect(screen.getByRole("status")).toHaveTextContent("Review evidence saved");
    expect(screen.getByRole("status")).toHaveAttribute("aria-live", "polite");

    await user.click(screen.getByRole("button", { name: "Save error" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Review evidence was not saved");
    expect(screen.getByRole("alert")).toHaveAttribute("aria-live", "assertive");
  });
});
