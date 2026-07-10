import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { permissionsForRole, type PlatformRole } from "@/lib/permissions";

const authState = vi.hoisted(() => ({ role: "Owner", canCreateCompany: true, isOwner: true }));

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
}));

vi.mock("@/components/AuthProvider", () => ({
  useAuth: () => {
    return {
      user: {
        displayName: `${authState.role} User`,
        role: authState.role,
        tenantName: "Permission Matrix Firm",
      },
      canCreateCompany: authState.canCreateCompany,
      isOwner: authState.isOwner,
      logout: vi.fn(),
      logoutError: null,
    };
  },
}));

import { AppNavbar } from "@/components/AppNavbar";

describe.each(["Owner", "Accountant", "Reviewer", "Client"] satisfies PlatformRole[])(
  "%s navigation actions",
  (role) => {
    it("shows only canonical role-allowed navigation controls", () => {
      authState.role = role;
      authState.canCreateCompany = permissionsForRole(role).canCreateCompany;
      authState.isOwner = permissionsForRole(role).canManageUsers;
      render(<AppNavbar />);

      expect(screen.getAllByRole("link", { name: /Dashboard/i }).length).toBeGreaterThan(0);
      expect(screen.getAllByRole("link", { name: /Change password|Password/i }).length).toBeGreaterThan(0);
      expect(screen.getAllByRole("button", { name: /Sign out/i }).length).toBeGreaterThan(0);
      expect(Boolean(screen.queryByRole("link", { name: /New Company/i }))).toBe(role === "Owner");
      expect(Boolean(screen.queryByRole("link", { name: /Users|User administration/i }))).toBe(role === "Owner");
    });
  },
);

it("closes the mobile navigation on Escape and returns focus to its trigger", async () => {
  const user = userEvent.setup();
  authState.role = "Owner";
  authState.canCreateCompany = true;
  authState.isOwner = true;
  render(<AppNavbar />);

  const trigger = screen.getByRole("button", { name: "Open menu" });
  await user.click(trigger);
  expect(screen.getByRole("group", { name: "Mobile primary navigation" })).toBeInTheDocument();

  await user.keyboard("{Escape}");

  expect(screen.queryByRole("group", { name: "Mobile primary navigation" })).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
});
