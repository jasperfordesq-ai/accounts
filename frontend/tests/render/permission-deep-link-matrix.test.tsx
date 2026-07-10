import { cleanup, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AuthProvider } from "@/components/AuthProvider";
import { authenticatedRoutePolicies, permissionsForRole, type PlatformRole } from "@/lib/permissions";

const routeState = vi.hoisted(() => ({ pathname: "/", role: "Owner" }));
const replace = vi.hoisted(() => vi.fn());

vi.mock("next/navigation", () => ({
  usePathname: () => routeState.pathname,
  useRouter: () => ({ replace }),
}));

vi.mock("@/lib/auth", () => ({
  getCurrentUser: vi.fn(async () => ({
    userId: 7,
    tenantId: 1,
    tenantName: "Permission Matrix Firm",
    email: `${routeState.role.toLowerCase()}@example.ie`,
    displayName: `${routeState.role} User`,
    role: routeState.role,
    allowedCompanyIds: [7],
    mustChangePassword: false,
  })),
  login: vi.fn(),
  logout: vi.fn(),
  changePassword: vi.fn(),
}));

describe.each(["Owner", "Accountant", "Reviewer", "Client"] satisfies PlatformRole[])(
  "%s deep-link permissions",
  (role) => {
    it("applies the route matrix before rendering every authenticated route", async () => {
      routeState.role = role;

      for (const policy of authenticatedRoutePolicies) {
        routeState.pathname = policy.pathTemplate
          .replace(":companyId", "7")
          .replace(":periodId", "3");
        replace.mockClear();

        const view = render(
          <AuthProvider>
            <p>{`Loaded ${policy.id}`}</p>
          </AuthProvider>,
        );

        const isDenied = !permissionsForRole(role)[policy.requiredPermission];
        const isDeniedReleaseEvidence = isDenied && policy.id === "production-readiness";
        const isDeniedWorkingPapers = isDenied && policy.requiredPermission === "canReadInternalWorkingPapers";

        if (isDenied) {
          expect(await screen.findByText(
            isDeniedReleaseEvidence
              ? "Release-review permission required"
              : isDeniedWorkingPapers
                ? "Internal-working-paper permission required"
                : "Owner permission required",
          )).toBeInTheDocument();
          expect(screen.queryByText(`Loaded ${policy.id}`)).not.toBeInTheDocument();
        } else {
          expect(await screen.findByText(`Loaded ${policy.id}`)).toBeInTheDocument();
          expect(screen.queryByText("Owner permission required")).not.toBeInTheDocument();
          expect(screen.queryByText("Release-review permission required")).not.toBeInTheDocument();
        }

        view.unmount();
        cleanup();
      }
    });
  },
);
