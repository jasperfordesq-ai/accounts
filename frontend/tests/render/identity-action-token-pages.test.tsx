import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ActionTokenPasswordForm } from "@/components/identity/ActionTokenPasswordForm";
import { ActionTokenFragmentForm, readActionTokenFragment } from "@/components/identity/ActionTokenFragmentForm";

const identity = vi.hoisted(() => ({
  acceptInvitation: vi.fn(),
  completePasswordReset: vi.fn(),
}));

vi.mock("@/lib/identity", () => identity);

describe("one-time identity action forms", () => {
  beforeEach(() => {
    identity.acceptInvitation.mockReset().mockResolvedValue(undefined);
    identity.completePasswordReset.mockReset().mockResolvedValue(undefined);
    window.history.replaceState({}, "", "/");
  });

  it("accepts an invitation only after matching strong passwords", async () => {
    const user = userEvent.setup();
    render(<ActionTokenPasswordForm mode="invitation" token="invite-token" />);

    const password = "Correct horse battery staple 2026!";
    await user.type(screen.getByLabelText("New password"), password);
    await user.type(screen.getByLabelText("Confirm new password"), "different password value");
    expect(screen.getByText("The passwords do not match.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Activate account" })).toBeDisabled();

    await user.clear(screen.getByLabelText("Confirm new password"));
    await user.type(screen.getByLabelText("Confirm new password"), password);
    await user.click(screen.getByRole("button", { name: "Activate account" }));

    await waitFor(() => expect(identity.acceptInvitation).toHaveBeenCalledWith("invite-token", password));
    expect(await screen.findByText(/account is active/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Continue to sign in" })).toHaveAttribute("href", "/login");
  });

  it("completes password reset without exposing the token in rendered text", async () => {
    const user = userEvent.setup();
    const token = "reset-token-that-must-not-render";
    render(<ActionTokenPasswordForm mode="password-reset" token={token} />);
    expect(screen.queryByText(token)).not.toBeInTheDocument();

    const password = "A distinct sufficiently long password 2026!";
    await user.type(screen.getByLabelText("New password"), password);
    await user.type(screen.getByLabelText("Confirm new password"), password);
    await user.click(screen.getByRole("button", { name: "Set new password" }));

    await waitFor(() => expect(identity.completePasswordReset).toHaveBeenCalledWith(token, password));
    expect(await screen.findByText(/password has been reset/i)).toBeInTheDocument();
  });

  it("fails closed when the action token is missing", () => {
    render(<ActionTokenPasswordForm mode="invitation" token="" />);
    expect(screen.getByRole("alert")).toHaveTextContent("does not contain a one-time token");
    expect(screen.getByRole("button", { name: "Activate account" })).toBeDisabled();
  });

  it("captures an invitation token from the URL fragment and immediately clears the address", async () => {
    window.history.replaceState({}, "", "/accept-invite?view=compact#token=fragment%20invitation");
    render(<ActionTokenFragmentForm mode="invitation" />);

    expect(await screen.findByRole("button", { name: "Activate account" })).toBeDisabled();
    expect(window.location.pathname).toBe("/accept-invite");
    expect(window.location.search).toBe("?view=compact");
    expect(window.location.hash).toBe("");

    const user = userEvent.setup();
    const password = "A fragment-only invitation password 2026!";
    await user.type(screen.getByLabelText("New password"), password);
    await user.type(screen.getByLabelText("Confirm new password"), password);
    await user.click(screen.getByRole("button", { name: "Activate account" }));

    await waitFor(() => expect(identity.acceptInvitation)
      .toHaveBeenCalledWith("fragment invitation", password));
  });

  it("does not consume a legacy query token and removes it from the visible address", async () => {
    window.history.replaceState({}, "", "/reset-password?token=query-secret&view=compact");
    render(<ActionTokenFragmentForm mode="password-reset" />);

    expect(await screen.findByRole("alert")).toHaveTextContent("does not contain a one-time token");
    expect(window.location.search).toBe("?view=compact");
    expect(identity.completePasswordReset).not.toHaveBeenCalled();
  });

  it("parses only a named token from a fragment", () => {
    expect(readActionTokenFragment("#token=one%20time&view=compact")).toBe("one time");
    expect(readActionTokenFragment("#view=compact")).toBe("");
    expect(readActionTokenFragment("?token=query-secret")).toBe("");
  });
});
