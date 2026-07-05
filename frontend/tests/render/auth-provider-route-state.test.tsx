import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AuthProvider } from "@/components/AuthProvider";
import type { AuthUser } from "@/lib/auth";

const replace = vi.fn();

vi.mock("next/navigation", () => ({
  usePathname: () => "/companies/7/periods/3",
  useRouter: () => ({ replace }),
}));

vi.mock("@/lib/auth", async () => {
  const actual = await vi.importActual<typeof import("@/lib/auth")>("@/lib/auth");
  return {
    ...actual,
    getCurrentUser: vi.fn<() => Promise<AuthUser>>(
      () => new Promise(() => undefined),
    ),
  };
});

describe("AuthProvider route states", () => {
  it("uses the shared workbench loading state while protected sessions are checked", () => {
    render(
      <AuthProvider>
        <p>Protected workspace</p>
      </AuthProvider>,
    );

    expect(screen.getByLabelText("Loading accountant workspace")).toBeInTheDocument();
    expect(screen.getByText("Preparing statutory evidence, deadlines and filing workflow state.")).toBeInTheDocument();
    expect(screen.queryByText("Loading account session")).not.toBeInTheDocument();
    expect(screen.queryByText("Protected workspace")).not.toBeInTheDocument();
  });
});
