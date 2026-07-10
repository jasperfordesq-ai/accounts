import { cleanup, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  canPerformAction,
  permissionActionCatalog,
  type PlatformRole,
} from "@/lib/permissions";

const roles = ["Owner", "Accountant", "Reviewer", "Client"] satisfies PlatformRole[];
const routeIds = [...new Set(permissionActionCatalog.map((action) => action.routeId))];

function RouteActionProbe({ role, routeId }: { role: PlatformRole; routeId: string }) {
  return (
    <section aria-label={`${routeId} permission actions`}>
      {permissionActionCatalog
        .filter((action) => action.routeId === routeId && canPerformAction(role, action.id))
        .map((action) => (
          <button key={action.id} type="button" aria-label={action.id}>
            {action.id}
          </button>
        ))}
    </section>
  );
}

describe.each(roles)("%s full UI action catalog", (role) => {
  it("keeps every allowed route action and removes every forbidden route action", () => {
    for (const routeId of routeIds) {
      render(<RouteActionProbe role={role} routeId={routeId} />);

      for (const action of permissionActionCatalog.filter((candidate) => candidate.routeId === routeId)) {
        const control = screen.queryByRole("button", { name: action.id });
        expect(Boolean(control), `${role} ${action.id}`).toBe(canPerformAction(role, action.id));
      }

      cleanup();
    }
  });
});
