import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import LoginPage from "@/app/login/page";
import { ApiError } from "@/lib/api";

const mocks = vi.hoisted(() => ({
  login: vi.fn(),
  completeMfaChallenge: vi.fn(),
  replace: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: mocks.replace }),
}));

vi.mock("@/components/AuthProvider", () => ({
  useAuth: () => ({ login: mocks.login, completeMfaChallenge: mocks.completeMfaChallenge }),
}));

describe("login keyboard accessibility", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/login");
    mocks.login.mockResolvedValue({
      userId: 1,
      tenantId: 2,
      tenantName: "Keyboard Accountants",
      tenantSlug: "keyboard-accountants",
      email: "reviewer@example.test",
      displayName: "Keyboard Reviewer",
      role: "Reviewer",
      allowedCompanyIds: [],
      mustChangePassword: false,
    });
  });

  it("completes sign-in without pointer interaction", async () => {
    const user = userEvent.setup();
    render(<LoginPage />);

    await user.tab();
    expect(screen.getByRole("textbox", { name: "Workspace slug" })).toHaveFocus();
    await user.keyboard("keyboard-accountants");
    await user.tab();
    expect(screen.getByRole("textbox", { name: "Email" })).toHaveFocus();
    await user.keyboard("reviewer@example.test");
    await user.tab();
    expect(screen.getByLabelText("Password")).toHaveFocus();
    await user.keyboard("correct horse battery staple");
    await user.keyboard("{Enter}");

    await waitFor(() => {
      expect(mocks.login).toHaveBeenCalledWith(
        "keyboard-accountants",
        "reviewer@example.test",
        "correct horse battery staple",
      );
      expect(mocks.replace).toHaveBeenCalledWith("/");
    });
  });

  it("requires and normalises the administrator-supplied workspace slug", async () => {
    const user = userEvent.setup();
    render(<LoginPage />);

    expect(screen.getByText(/Private Server setup prints this value/)).toBeInTheDocument();
    await user.type(screen.getByRole("textbox", { name: "Email" }), "reviewer@example.test");
    await user.type(screen.getByLabelText("Password"), "correct horse battery staple");
    expect(screen.getByRole("button", { name: "Sign in" })).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Workspace slug" }), "Keyboard-Accountants");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() => expect(mocks.login).toHaveBeenCalledWith(
      "keyboard-accountants",
      "reviewer@example.test",
      "correct horse battery staple",
    ));
  });

  it("does not reveal which tenant-qualified credential was rejected", async () => {
    const user = userEvent.setup();
    mocks.login.mockRejectedValue(new ApiError(401, "Unauthorized", ""));
    render(<LoginPage />);

    await user.type(screen.getByRole("textbox", { name: "Workspace slug" }), "unknown-workspace");
    await user.type(screen.getByRole("textbox", { name: "Email" }), "reviewer@example.test");
    await user.type(screen.getByLabelText("Password"), "wrong password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Invalid workspace, email, or password.");
    expect(alert).not.toHaveTextContent(/workspace not found|email not found|incorrect password/i);
  });

  it("preserves mandatory password rotation after first MFA enrolment recovery codes", async () => {
    const user = userEvent.setup();
    mocks.login.mockResolvedValue({
      challengeToken: "mfa-challenge",
      requiresEnrollment: true,
      expiresAtUtc: "2026-07-10T21:00:00Z",
      enrollmentSecret: "JBSWY3DPEHPK3PXP",
      otpAuthUri: "otpauth://totp/Accounts:test",
    });
    mocks.completeMfaChallenge.mockResolvedValue({
      user: {
        userId: 1, tenantId: 2, tenantName: "Keyboard Accountants", tenantSlug: "keyboard-accountants", email: "reviewer@example.test",
        displayName: "Keyboard Reviewer", role: "Reviewer", allowedCompanyIds: [], mustChangePassword: true,
      },
      recoveryCodes: ["recovery-one", "recovery-two"],
    });
    render(<LoginPage />);

    await user.type(screen.getByRole("textbox", { name: "Workspace slug" }), "keyboard-accountants");
    await user.type(screen.getByRole("textbox", { name: "Email" }), "reviewer@example.test");
    await user.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    await user.type(await screen.findByLabelText("6-digit authenticator code"), "123456");
    await user.click(screen.getByRole("button", { name: "Verify and enable MFA" }));

    expect(await screen.findByText("Save your recovery codes")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "I have stored these codes" }));
    expect(mocks.replace).toHaveBeenCalledWith("/change-password?returnTo=%2F");
  });

  it("completes an already-enrolled TOTP challenge with explicit null enrollment fields", async () => {
    const user = userEvent.setup();
    mocks.login.mockResolvedValue({
      challengeToken: "mfa-login-challenge",
      requiresEnrollment: false,
      expiresAtUtc: "2026-07-10T21:00:00Z",
      enrollmentSecret: null,
      otpAuthUri: null,
    });
    mocks.completeMfaChallenge.mockResolvedValue({
      user: {
        userId: 1, tenantId: 2, tenantName: "Keyboard Accountants", tenantSlug: "keyboard-accountants", email: "reviewer@example.test",
        displayName: "Keyboard Reviewer", role: "Reviewer", allowedCompanyIds: [], mustChangePassword: false,
      },
      recoveryCodes: [],
    });
    render(<LoginPage />);

    await user.type(screen.getByRole("textbox", { name: "Workspace slug" }), "keyboard-accountants");
    await user.type(screen.getByRole("textbox", { name: "Email" }), "reviewer@example.test");
    await user.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByLabelText("6-digit authenticator code")).toBeVisible();
    expect(screen.queryByLabelText("Authenticator setup key")).not.toBeInTheDocument();
    await user.type(screen.getByLabelText("6-digit authenticator code"), "654321");
    await user.click(screen.getByRole("button", { name: "Verify and sign in" }));

    await waitFor(() => {
      expect(mocks.completeMfaChallenge).toHaveBeenCalledWith(
        "mfa-login-challenge",
        "654321",
        undefined,
      );
      expect(mocks.replace).toHaveBeenCalledWith("/");
    });
  });
});
