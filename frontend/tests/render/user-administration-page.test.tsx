import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const auth = vi.hoisted(() => ({ reauthenticate: vi.fn() }));
const api = vi.hoisted(() => ({ getCompanies: vi.fn() }));
const identity = vi.hoisted(() => ({
  listUsers: vi.fn(), inviteUser: vi.fn(), createUser: vi.fn(), beginPasswordReset: vi.fn(),
  changeUserRole: vi.fn(), setUserCompanies: vi.fn(), setUserActive: vi.fn(), unlockUser: vi.fn(),
  revokeUserSessions: vi.fn(), offboardUser: vi.fn(),
}));

vi.mock("@/components/AuthProvider", () => ({ useAuth: () => auth }));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), getCompanies: api.getCompanies }));
vi.mock("@/lib/identity", () => identity);

import UserAdministrationPage from "@/app/settings/users/page";

const owner = {
  userId: 1, email: "owner@example.ie", displayName: "Firm Owner", role: "Owner", isActive: true,
  mustChangePassword: false, isLocked: false, lockedUntilUtc: null, mfaEnabled: true, companyIds: [7],
  inviteAcceptedAtUtc: "2026-07-10T10:00:00Z", deactivatedAtUtc: null, offboardedAtUtc: null, sessionVersion: 3,
};

describe("Owner user administration", () => {
  beforeEach(() => {
    Object.values(identity).forEach((mock) => mock.mockReset());
    auth.reauthenticate.mockReset().mockResolvedValue(owner);
    identity.listUsers.mockResolvedValue([owner]);
    api.getCompanies.mockReset().mockResolvedValue([{ id: 7, legalName: "Example Limited" }]);
  });

  it("shows lifecycle, MFA and company-scope state with an accessible destructive confirmation", async () => {
    const user = userEvent.setup();
    render(<UserAdministrationPage />);
    expect(await screen.findByText("Firm Owner")).toBeInTheDocument();
    expect(screen.getByText("MFA enabled")).toBeInTheDocument();
    expect(screen.getByLabelText("Role for Firm Owner")).toHaveValue("Owner");
    expect(screen.getByRole("checkbox", { name: "Example Limited" })).toBeChecked();
    expect(screen.getByRole("button", { name: "Revoke sessions" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Offboard" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Offboard" }));
    expect(screen.getByRole("alertdialog", { name: "Offboard this user?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("creates a one-time invitation link and identifies its expiry", async () => {
    const user = userEvent.setup();
    identity.inviteUser.mockResolvedValue({
      user: { ...owner, userId: 2, email: "reviewer@example.ie", displayName: "New Reviewer", role: "Reviewer", mfaEnabled: false, companyIds: [] },
      actionToken: "single-display-token",
      expiresAtUtc: "2026-07-11T10:00:00Z",
    });
    render(<UserAdministrationPage />);
    await screen.findByText("Firm Owner");
    await user.type(screen.getByLabelText("Display name"), "New Reviewer");
    await user.type(screen.getByLabelText("Email"), "reviewer@example.ie");
    await user.selectOptions(screen.getByRole("combobox", { name: "Role" }), "Reviewer");
    await user.click(screen.getByRole("button", { name: "Create invitation" }));

    await waitFor(() => expect(identity.inviteUser).toHaveBeenCalledWith({
      displayName: "New Reviewer", email: "reviewer@example.ie", role: "Reviewer", companyIds: [],
    }));
    expect(await screen.findByText("One-time invitation link")).toBeInTheDocument();
    expect(screen.getByText(/accept-invite\?token=single-display-token/)).toBeInTheDocument();
    expect(screen.getByText(/displayed once and expires/i)).toBeInTheDocument();
  });
});
