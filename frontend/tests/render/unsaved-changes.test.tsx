import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConfirmModal } from "@/components/ConfirmModal";
import { UnsavedChangesProvider } from "@/components/UnsavedChangesProvider";
import { MoneyInput } from "@/components/workbench";
import {
  useGuardedRouter,
  useUnsavedChanges,
  useUnsavedNavigationGuard,
} from "@/lib/useUnsavedChanges";

const navigation = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
  back: vi.fn(),
  refresh: vi.fn(),
  forward: vi.fn(),
  prefetch: vi.fn(),
  link: vi.fn(),
  signOut: vi.fn(),
  underlyingCancel: vi.fn(),
  underlyingConfirm: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: navigation.push,
    replace: navigation.replace,
    back: navigation.back,
    refresh: navigation.refresh,
    forward: navigation.forward,
    prefetch: navigation.prefetch,
  }),
}));

function RouterControls({ dirty, secondDirty = false }: { dirty: boolean; secondDirty?: boolean }) {
  useUnsavedChanges(dirty);
  useUnsavedChanges(secondDirty);
  const router = useGuardedRouter();
  const guardNavigation = useUnsavedNavigationGuard();
  return (
    <div>
      <a href="/next" onClick={(event) => { event.preventDefault(); navigation.link(); }}>
        Next link
      </a>
      <button type="button" onClick={() => router.push("/pushed")}>Push</button>
      <button type="button" onClick={() => router.replace("/replaced")}>Replace</button>
      <button type="button" onClick={() => router.back()}>Back</button>
      <button type="button" onClick={() => router.pushAfterSave("/saved")}>Push after save</button>
      <button type="button" onClick={() => guardNavigation(navigation.signOut, "replace")}>Sign out</button>
    </div>
  );
}

function DraftEditor() {
  const [draft, setDraft] = useState("");
  useUnsavedChanges(draft !== "");
  return (
    <div>
      <label htmlFor="draft">Draft</label>
      <input id="draft" value={draft} onChange={(event) => setDraft(event.target.value)} />
      <a href="/next" onClick={(event) => { event.preventDefault(); navigation.link(); }}>
        Next link
      </a>
    </div>
  );
}

function NestedModalDraft() {
  useUnsavedChanges(true);
  return (
    <div>
      <a href="/next" onClick={(event) => event.preventDefault()}>Navigate from modal</a>
      <ConfirmModal
        open
        title="Recover Company"
        description="Retain this recovery evidence draft."
        onCancel={navigation.underlyingCancel}
        onConfirm={navigation.underlyingConfirm}
      >
        <label htmlFor="nested-draft">Recovery reason</label>
        <input id="nested-draft" defaultValue="Evidence remains intact" />
      </ConfirmModal>
    </div>
  );
}

function InvalidMoneyDraft() {
  const [value, setValue] = useState(0);
  return (
    <div>
      <MoneyInput label="Loan balance" value={value} onValueChange={setValue} allowNegative />
      <a href="/next" onClick={(event) => event.preventDefault()}>Leave money editor</a>
    </div>
  );
}

function FocusTrapHarness() {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>Open evidence dialog</button>
      <button type="button">Outside action</button>
      <ConfirmModal
        open={open}
        title="Retain review evidence"
        description="Confirm the retained review reference."
        onCancel={() => setOpen(false)}
        onConfirm={() => setOpen(false)}
      >
        <label htmlFor="review-reference">Review reference</label>
        <input id="review-reference" />
      </ConfirmModal>
    </div>
  );
}

function renderGuarded(ui: React.ReactNode) {
  return render(<UnsavedChangesProvider>{ui}</UnsavedChangesProvider>);
}

beforeEach(() => {
  Object.values(navigation).forEach((mock) => mock.mockReset());
  window.history.replaceState({}, "", "/edit");
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("application unsaved-change navigation guard", () => {
  it("lets a clean form navigate without prompting", async () => {
    const user = userEvent.setup();
    renderGuarded(<RouterControls dirty={false} />);

    await user.click(screen.getByRole("button", { name: "Push" }));

    expect(navigation.push).toHaveBeenCalledWith("/pushed");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it.each([
    ["Push", navigation.push, "/pushed"],
    ["Replace", navigation.replace, "/replaced"],
  ] as const)("guards router %s and continues only after confirmation", async (label, method, href) => {
    const user = userEvent.setup();
    renderGuarded(<RouterControls dirty />);

    await user.click(screen.getByRole("button", { name: label }));
    expect(method).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Leave without saving?" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Leave and discard" }));
    await waitFor(() => expect(method).toHaveBeenCalledWith(href));
  });

  it("guards router.back and runs it only after confirmation", async () => {
    const user = userEvent.setup();
    renderGuarded(<RouterControls dirty />);

    await user.click(screen.getByRole("button", { name: "Back" }));
    expect(navigation.back).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Leave and discard" }));
    await waitFor(() => expect(navigation.back).toHaveBeenCalledTimes(1));
  });

  it("guards a destructive navigation action before the action runs", async () => {
    const user = userEvent.setup();
    renderGuarded(<RouterControls dirty />);

    await user.click(screen.getByRole("button", { name: "Sign out" }));
    expect(navigation.signOut).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Leave and discard" }));
    await waitFor(() => expect(navigation.signOut).toHaveBeenCalledTimes(1));
  });

  it("canceling a Link navigation retains the draft, focus, and location", async () => {
    const user = userEvent.setup();
    renderGuarded(<DraftEditor />);
    const input = screen.getByRole("textbox", { name: "Draft" });
    const link = screen.getByRole("link", { name: "Next link" });

    await user.type(input, "unposted journal evidence");
    await user.click(link);

    const dialog = screen.getByRole("alertdialog", { name: "Leave without saving?" });
    expect(dialog).toHaveAttribute("aria-describedby");
    expect(navigation.link).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Stay and keep editing" })).toHaveFocus();

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(input).toHaveValue("unposted journal evidence");
    expect(link).toHaveFocus();
    expect(window.location.pathname).toBe("/edit");
    expect(navigation.link).not.toHaveBeenCalled();
  });

  it("traps Tab and Shift+Tab inside dialogs, closes on Escape, and restores trigger focus", async () => {
    const user = userEvent.setup();
    render(<FocusTrapHarness />);
    const trigger = screen.getByRole("button", { name: "Open evidence dialog" });

    await user.click(trigger);

    const dialog = screen.getByRole("dialog", { name: "Retain review evidence" });
    const firstControl = screen.getByRole("textbox", { name: "Review reference" });
    const cancel = screen.getByRole("button", { name: "Cancel" });
    const confirm = screen.getByRole("button", { name: "Confirm" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(cancel).toHaveFocus();

    firstControl.focus();
    await user.tab({ shift: true });
    expect(confirm).toHaveFocus();

    await user.tab();
    expect(firstControl).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("replays the original Link click after confirmation", async () => {
    const user = userEvent.setup();
    renderGuarded(<DraftEditor />);

    await user.type(screen.getByRole("textbox", { name: "Draft" }), "draft");
    await user.click(screen.getByRole("link", { name: "Next link" }));
    await user.click(screen.getByRole("button", { name: "Leave and discard" }));

    await waitFor(() => expect(navigation.link).toHaveBeenCalledTimes(1));
  });

  it("Escape closes only the top navigation warning when an editable modal is underneath", async () => {
    const user = userEvent.setup();
    renderGuarded(<NestedModalDraft />);

    await user.click(screen.getByRole("link", { name: "Navigate from modal" }));
    expect(screen.getByRole("dialog", { name: "Recover Company" })).toBeInTheDocument();
    expect(screen.getByRole("alertdialog", { name: "Leave without saving?" })).toBeInTheDocument();

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Recover Company" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Recovery reason" })).toHaveValue("Evidence remains intact");
    expect(navigation.underlyingCancel).not.toHaveBeenCalled();
  });

  it("protects an incomplete money-entry draft before it can reach parent form state", async () => {
    const user = userEvent.setup();
    renderGuarded(<InvalidMoneyDraft />);

    await user.type(screen.getByRole("textbox", { name: "Loan balance" }), "-");
    await user.click(screen.getByRole("link", { name: "Leave money editor" }));

    expect(screen.getByRole("alertdialog", { name: "Leave without saving?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Stay and keep editing" }));
    expect(screen.getByRole("textbox", { name: "Loan balance" })).toHaveValue("-");
  });

  it("blocks native browser history movement, restores the guard on cancel, and proceeds on confirm", async () => {
    const user = userEvent.setup();
    const back = vi.spyOn(window.history, "back");
    const forward = vi.spyOn(window.history, "forward");
    renderGuarded(<RouterControls dirty />);

    window.history.back();
    await screen.findByRole("alertdialog", { name: "Leave without saving?" });
    window.history.back();
    await waitFor(() => expect(forward).toHaveBeenCalledTimes(2));
    expect(screen.getByRole("alertdialog", { name: "Leave without saving?" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Stay and keep editing" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Stay and keep editing" }));
    expect(window.location.pathname).toBe("/edit");

    window.history.back();
    await screen.findByRole("alertdialog", { name: "Leave without saving?" });
    await user.click(screen.getByRole("button", { name: "Leave and discard" }));
    await waitFor(() => expect(back.mock.calls.length).toBeGreaterThanOrEqual(3));
  });

  it("registers unload protection only while any editor is dirty", () => {
    const add = vi.spyOn(window, "addEventListener");
    const remove = vi.spyOn(window, "removeEventListener");
    const { rerender } = renderGuarded(<RouterControls dirty={false} secondDirty />);

    const unloadHandler = (
      add.mock.calls.find(([type]) => type === "beforeunload")?.[1]
    ) as ((event: BeforeUnloadEvent) => void) | undefined;
    expect(unloadHandler).toBeDefined();
    const event = { preventDefault: vi.fn(), returnValue: undefined } as unknown as BeforeUnloadEvent;
    unloadHandler?.(event);
    expect(event.preventDefault).toHaveBeenCalledTimes(1);
    expect(event.returnValue).toBe("");

    rerender(
      <UnsavedChangesProvider>
        <RouterControls dirty={false} secondDirty={false} />
      </UnsavedChangesProvider>,
    );
    expect(remove.mock.calls.some(([type]) => type === "beforeunload")).toBe(true);
    expect(add.mock.calls.some(([type]) => type === "beforeunload")).toBe(true);
  });

  it("allows an explicit post-save navigation without a discard prompt", async () => {
    const user = userEvent.setup();
    renderGuarded(<RouterControls dirty />);

    await user.click(screen.getByRole("button", { name: "Push after save" }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    await waitFor(() => expect(navigation.push).toHaveBeenCalledWith("/saved"));
  });

  it("uses listeners and native history calls without replacing browser or router methods", () => {
    const pushState = window.history.pushState;
    const replaceState = window.history.replaceState;
    const routerPush = navigation.push;
    const routerReplace = navigation.replace;

    renderGuarded(<RouterControls dirty />);

    expect(window.history.pushState).toBe(pushState);
    expect(window.history.replaceState).toBe(replaceState);
    expect(navigation.push).toBe(routerPush);
    expect(navigation.replace).toBe(routerReplace);
  });

  it("ignores modified and same-document anchor interactions", () => {
    renderGuarded(
      <div>
        <RouterControls dirty />
        <a href="#section" onClick={(event) => event.preventDefault()}>Same document</a>
        <a href="/modified" onClick={(event) => event.preventDefault()}>Modified click</a>
      </div>,
    );

    fireEvent.click(screen.getByRole("link", { name: "Same document" }));
    fireEvent.click(screen.getByRole("link", { name: "Modified click" }), { ctrlKey: true });

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });
});
