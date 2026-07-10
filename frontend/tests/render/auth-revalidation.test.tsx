import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AuthProvider } from "@/components/AuthProvider";
import { ApiError } from "@/lib/api";
import type { AuthUser } from "@/lib/auth";

const replace = vi.hoisted(() => vi.fn());
const routeState = vi.hoisted(() => ({ pathname: "/companies/7" }));
const getCurrentUser = vi.hoisted(() => vi.fn<() => Promise<AuthUser>>());

vi.mock("next/navigation", () => ({
  usePathname: () => routeState.pathname,
  useRouter: () => ({ replace }),
}));

vi.mock("@/lib/auth", async () => {
  const actual = await vi.importActual<typeof import("@/lib/auth")>("@/lib/auth");
  return { ...actual, getCurrentUser };
});

describe("AuthProvider background revalidation", () => {
  beforeEach(() => {
    routeState.pathname = "/companies/7";
    replace.mockReset();
    getCurrentUser.mockReset();
  });

  it("checks the session once initially and does not blank or refetch on client navigation", async () => {
    getCurrentUser.mockResolvedValue(user());
    const view = render(
      <AuthProvider>
        <p>Retained workbench</p>
      </AuthProvider>,
    );

    expect(await screen.findByText("Retained workbench")).toBeInTheDocument();
    expect(getCurrentUser).toHaveBeenCalledTimes(1);

    routeState.pathname = "/companies/7/periods/3";
    view.rerender(
      <AuthProvider>
        <p>Retained workbench</p>
      </AuthProvider>,
    );

    expect(screen.getByText("Retained workbench")).toBeInTheDocument();
    expect(screen.queryByLabelText("Loading accountant workspace")).not.toBeInTheDocument();
    expect(getCurrentUser).toHaveBeenCalledTimes(1);
  });

  it("shows service unavailable instead of redirecting an initial infrastructure failure to login", async () => {
    getCurrentUser.mockRejectedValue(new ApiError(503, "Service Unavailable", "upstream unavailable"));
    render(
      <AuthProvider>
        <p>Protected workbench</p>
      </AuthProvider>,
    );

    expect(await screen.findByRole("heading", { name: "Authentication service unavailable" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry session check" })).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalledWith(expect.stringContaining("/login"));
  });

  it("still redirects a genuine 401 through the safe return-to login flow", async () => {
    getCurrentUser.mockRejectedValue(new ApiError(401, "Unauthorized", ""));
    render(
      <AuthProvider>
        <p>Protected workbench</p>
      </AuthProvider>,
    );

    await waitFor(() => expect(replace).toHaveBeenCalledWith(
      expect.stringMatching(/^\/login\?returnTo=/),
    ));
  });

  it("retains the authenticated workbench when a focus revalidation gets a 500", async () => {
    getCurrentUser
      .mockResolvedValueOnce(user())
      .mockRejectedValueOnce(new ApiError(500, "Internal Server Error", "provider unavailable"));
    render(
      <AuthProvider>
        <p>Unsaved accounting work</p>
      </AuthProvider>,
    );

    expect(await screen.findByText("Unsaved accounting work")).toBeInTheDocument();
    fireEvent.focus(window);

    expect(await screen.findByText(/Authentication service is temporarily unavailable/)).toBeInTheDocument();
    expect(screen.getByText("Unsaved accounting work")).toBeInTheDocument();
    expect(screen.queryByLabelText("Loading accountant workspace")).not.toBeInTheDocument();
    expect(getCurrentUser).toHaveBeenCalledTimes(2);
    expect(replace).not.toHaveBeenCalledWith(expect.stringContaining("/login"));
  });
});

function user(): AuthUser {
  return {
    userId: 7,
    tenantId: 1,
    tenantName: "Auth Revalidation Firm",
    email: "owner@example.ie",
    displayName: "Owner User",
    role: "Owner",
    allowedCompanyIds: [7],
    mustChangePassword: false,
  };
}
