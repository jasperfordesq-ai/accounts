// frontend-unsaved-changes-guard: the shared useUnsavedChanges hook (now used across
// notes/year-end/classify/charity, previously inlined only on notes) attaches a beforeunload guard
// only while there are unsaved changes, and removes it once they are saved/cleared.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";

function Guarded({ dirty }: { dirty: boolean }) {
  useUnsavedChanges(dirty);
  return <div>guarded</div>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("useUnsavedChanges", () => {
  it("does not register a beforeunload guard when clean", () => {
    const add = vi.spyOn(window, "addEventListener");
    render(<Guarded dirty={false} />);
    expect(add.mock.calls.some(([type]) => type === "beforeunload")).toBe(false);
  });

  it("registers a beforeunload guard when dirty and removes it when it becomes clean", () => {
    const add = vi.spyOn(window, "addEventListener");
    const remove = vi.spyOn(window, "removeEventListener");

    const { rerender } = render(<Guarded dirty={true} />);
    expect(add.mock.calls.some(([type]) => type === "beforeunload")).toBe(true);

    rerender(<Guarded dirty={false} />);
    expect(remove.mock.calls.some(([type]) => type === "beforeunload")).toBe(true);
  });

  it("the guard handler cancels the unload (preventDefault + returnValue)", () => {
    let handler: ((e: BeforeUnloadEvent) => void) | undefined;
    vi.spyOn(window, "addEventListener").mockImplementation((type, fn) => {
      if (type === "beforeunload") handler = fn as (e: BeforeUnloadEvent) => void;
    });

    render(<Guarded dirty={true} />);
    expect(handler).toBeDefined();

    const event = { preventDefault: vi.fn(), returnValue: undefined } as unknown as BeforeUnloadEvent;
    handler?.(event);
    expect(event.preventDefault).toHaveBeenCalled();
    expect(event.returnValue).toBe("");
  });
});
