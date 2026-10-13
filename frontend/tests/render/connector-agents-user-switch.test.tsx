import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { clearCookies, setCsrfCookie } from "./harness";

// PR #88 review: an Owner switching between users must never see (or act on) the previous user's
// agents. Responses here are held open so a slow answer for one user can land after the next
// user's view has rendered.

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

vi.mock("@/components/AuthProvider", () => ({
  useAuth: () => ({
    user: { userId: 1, displayName: "Olive Owner", role: "Owner", tenantName: "Example Firm" },
    isOwner: true,
  }),
}));

import { ConnectorAgentsWorkbench } from "@/components/connector/ConnectorAgentsWorkbench";

const ADA_FAMILY = "9b2d6a3e-1f4c-4c5e-8d7a-2b3c4d5e6f70";
const BEN_FAMILY = "1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f";

function firmUser(userId: number, displayName: string) {
  return {
    userId,
    email: `${displayName.split(" ")[0].toLowerCase()}@example.ie`,
    displayName,
    role: "Accountant",
    isActive: true,
    mustChangePassword: false,
    isLocked: false,
    lockedUntilUtc: null,
    mfaEnabled: true,
    companyIds: [],
    inviteAcceptedAtUtc: null,
    deactivatedAtUtc: null,
    offboardedAtUtc: null,
    sessionVersion: 1,
  };
}

function agent(userId: number, familyId: string, deviceLabel: string) {
  return {
    familyId,
    userId,
    accessLevel: "Write",
    dataScope: "Withheld",
    deviceLabel,
    clientVersion: "oauth",
    connectedAtUtc: "2026-10-12T09:00:00Z",
    lastUsedAtUtc: "2026-10-12T10:30:00Z",
    expiresAtUtc: "2027-01-10T09:00:00Z",
  };
}

function activityFor(userId: number, reason: string) {
  return {
    data: [{
      id: userId,
      sessionFamilyId: userId === 7 ? ADA_FAMILY : BEN_FAMILY,
      accessLevel: "Write",
      dataScope: "Withheld",
      method: "POST",
      routePattern: "/api/companies/{companyId}/officers",
      resourceId: null,
      companyId: 3,
      statusCode: 201,
      reason,
      occurredAtUtc: "2026-10-12T10:30:00Z",
    }],
    page: 1,
    pageSize: 25,
    hasMore: false,
  };
}

interface Held {
  url: string;
  release(): void;
}

/**
 * A fetch whose answers for the URLs in `hold` wait until released. Everything else answers at once.
 */
function installHeldFetch(answer: (url: string, method: string) => { status?: number; body?: unknown }) {
  const held: Held[] = [];
  const requests: { url: string; method: string }[] = [];
  let holding: ((url: string) => boolean) = () => false;
  const respond = (url: string, method: string) => {
    const { status = 200, body } = answer(url, method);
    return {
      ok: status >= 200 && status < 300,
      status,
      statusText: String(status),
      headers: new Headers(),
      json: async () => body ?? {},
      text: async () => (body == null ? "" : JSON.stringify(body)),
    } as Response;
  };
  global.fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const method = (init?.method ?? "GET").toUpperCase();
    requests.push({ url, method });
    if (!holding(url)) return Promise.resolve(respond(url, method));
    return new Promise<Response>((resolve) => {
      held.push({ url, release: () => resolve(respond(url, method)) });
    });
  }) as unknown as typeof fetch;
  return {
    requests,
    hold(predicate: (url: string) => boolean) {
      holding = predicate;
    },
    async releaseAll(match: (url: string) => boolean) {
      const ready = held.filter((h) => match(h.url));
      for (const h of ready) held.splice(held.indexOf(h), 1);
      await act(async () => {
        for (const h of ready) h.release();
        await new Promise((resolve) => setTimeout(resolve, 0));
      });
      return ready.length;
    },
  };
}

function api(url: string): { status?: number; body?: unknown } {
  if (url === "/api/admin/users") return { body: [firmUser(7, "Ada Accountant"), firmUser(8, "Ben Bookkeeper")] };
  if (url === "/api/auth/connector/sessions?userId=7") return { body: [agent(7, ADA_FAMILY, "Ada's Claude")] };
  if (url === "/api/auth/connector/sessions?userId=8") return { body: [agent(8, BEN_FAMILY, "Ben's Codex")] };
  if (url === "/api/auth/connector/sessions") return { body: [] };
  if (url.startsWith("/api/auth/connector/activity?userId=7")) return { body: activityFor(7, "Ada's activity") };
  if (url.startsWith("/api/auth/connector/activity?userId=8")) return { body: activityFor(8, "Ben's activity") };
  if (url.startsWith("/api/auth/connector/activity")) return { body: { data: [], page: 1, pageSize: 25, hasMore: false } };
  if (url.includes("/revoke")) return { status: 204 };
  return { status: 404, body: {} };
}

async function picker() {
  const select = await screen.findByLabelText("Show agents connected by");
  await screen.findByRole("option", { name: "Ben Bookkeeper (Accountant)" });
  return select;
}

describe("connected agents page, switching users", () => {
  beforeEach(() => {
    clearCookies();
    setCsrfCookie("csrf-token");
    toast.success.mockReset();
    toast.error.mockReset();
  });

  it("ignores a slow answer for the previous user once another user is shown", async () => {
    const user = userEvent.setup();
    const fetchMock = installHeldFetch((url) => api(url));
    render(<ConnectorAgentsWorkbench />);
    const select = await picker();
    expect(await screen.findByText("No connected agents")).toBeInTheDocument();

    // Ada's answers are slow; Ben's are not.
    fetchMock.hold((url) => url.includes("userId=7"));
    await user.selectOptions(select, "7");
    await waitFor(() => expect(fetchMock.requests.some((r) => r.url === "/api/auth/connector/sessions?userId=7")).toBe(true));
    fetchMock.hold(() => false);
    await user.selectOptions(select, "8");
    expect(await screen.findByText("Ben's Codex")).toBeInTheDocument();
    expect(await screen.findByText("Ben's activity")).toBeInTheDocument();

    // Ada's answers finally arrive: they must not replace Ben's view.
    expect(await fetchMock.releaseAll((url) => url.includes("userId=7"))).toBe(2);
    expect(screen.getByText("Agents connected by Ben Bookkeeper")).toBeInTheDocument();
    expect(screen.getByText("Ben's Codex")).toBeInTheDocument();
    expect(screen.queryByText("Ada's Claude")).not.toBeInTheDocument();
    expect(screen.getByText("Ben's activity")).toBeInTheDocument();
    expect(screen.queryByText("Ada's activity")).not.toBeInTheDocument();
  });

  it("does not leave the previous user's agents on screen, or clickable, while the next user loads", async () => {
    const user = userEvent.setup();
    const fetchMock = installHeldFetch((url) => api(url));
    render(<ConnectorAgentsWorkbench />);
    const select = await picker();

    await user.selectOptions(select, "7");
    expect(await screen.findByRole("button", { name: "Disconnect Ada's Claude" })).toBeInTheDocument();
    expect(await screen.findByText("Ada's activity")).toBeInTheDocument();

    fetchMock.hold((url) => url.includes("userId=8"));
    await user.selectOptions(select, "8");
    expect(screen.getByText("Agents connected by Ben Bookkeeper")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Disconnect Ada's Claude" })).not.toBeInTheDocument();
    expect(screen.queryByText("Ada's Claude")).not.toBeInTheDocument();
    expect(screen.queryByText("Ada's activity")).not.toBeInTheDocument();
    expect(screen.getByText("Loading connected agents")).toBeInTheDocument();

    await fetchMock.releaseAll((url) => url.includes("userId=8"));
    expect(await screen.findByText("Ben's Codex")).toBeInTheDocument();
    expect(fetchMock.requests.filter((r) => r.method === "POST")).toHaveLength(0);
  });

  it("disconnects the agent named on the card for the user who owns it, even if the view changed", async () => {
    const user = userEvent.setup();
    const fetchMock = installHeldFetch((url) => api(url));
    render(<ConnectorAgentsWorkbench />);
    const select = await picker();

    await user.selectOptions(select, "7");
    await user.click(await screen.findByRole("button", { name: "Disconnect Ada's Claude" }));
    const dialog = await screen.findByRole("alertdialog");

    // The selection changes underneath the open confirmation.
    fireEvent.change(select, { target: { value: "8" } });
    await user.click(within(dialog).getByRole("button", { name: "Disconnect agent" }));

    await waitFor(() => expect(fetchMock.requests.filter((r) => r.method === "POST")).toHaveLength(1));
    const revoke = fetchMock.requests.find((r) => r.method === "POST")!;
    expect(revoke.url).toBe(`/api/auth/connector/sessions/${ADA_FAMILY}/revoke?userId=7`);
  });
});
