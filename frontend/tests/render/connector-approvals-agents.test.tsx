import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearCookies, installFetchMock, setCsrfCookie, type RecordedRequest, type RouteResponse } from "./harness";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const authState = vi.hoisted(() => ({
  isOwner: false,
  user: { userId: 7, displayName: "Ada Accountant", role: "Accountant", tenantName: "Example Firm" },
}));
vi.mock("@/components/AuthProvider", () => ({
  useAuth: () => ({ user: authState.user, isOwner: authState.isOwner }),
}));

import { ConnectorApprovalsWorkbench } from "@/components/connector/ConnectorApprovalsWorkbench";
import { ConnectorAgentsWorkbench } from "@/components/connector/ConnectorAgentsWorkbench";
import { SESSION_EXPIRED_EVENT } from "@/lib/api";

// The field is optional: it reads "Authenticator code (if two-factor sign-in is on)".
const AUTHENTICATOR_CODE = /^Authenticator code/;
const approvalId = "0f8fad5b-d9cb-469f-a165-70867728950e";
const otherApprovalId = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

function approval(id: string, summary: string, overrides: Record<string, unknown> = {}) {
  return {
    approvalId: id,
    summary,
    method: "DELETE",
    routePattern: "/api/companies/{companyId}/officers/{officerId}",
    resourceId: "12",
    createdAtUtc: new Date(Date.now() - 60_000).toISOString(),
    expiresAtUtc: new Date(Date.now() + 4 * 60_000).toISOString(),
    approvedAtUtc: null,
    consumedAtUtc: null,
    ...overrides,
  };
}

describe("agent approvals page", () => {
  beforeEach(() => {
    clearCookies();
    setCsrfCookie("csrf-token");
    toast.success.mockReset();
    toast.error.mockReset();
  });

  afterEach(() => {
    window.history.replaceState({}, "", "/");
  });

  it("explains where approvals come from when none are waiting", async () => {
    installFetchMock(() => ({ body: [] }));
    render(<ConnectorApprovalsWorkbench />);
    expect(await screen.findByText("No approvals waiting")).toBeInTheDocument();
    expect(screen.getByText(/Approvals come from AI agents you have connected/)).toBeInTheDocument();
  });

  it("highlights the deep-linked approval and grants it with password and authenticator code", async () => {
    window.history.replaceState({}, "", `/settings/approvals?id=${approvalId}`);
    const user = userEvent.setup();
    let granted = false;
    const fetchMock = installFetchMock((request: RecordedRequest): RouteResponse => {
      if (request.url === "/api/auth/connector/approvals") {
        return {
          body: [
            approval(otherApprovalId, "Delete bank account, company Example Limited"),
            approval(approvalId, "Delete officers, company Example Limited, officer A Director", granted ? { approvedAtUtc: new Date().toISOString() } : {}),
          ],
        };
      }
      if (request.url === "/api/auth/connector/approve") {
        granted = true;
        return {
          body: { ok: true, approvalId, summary: "Delete officers, company Example Limited, officer A Director", expiresAtUtc: new Date(Date.now() + 240_000).toISOString() },
        };
      }
      return { status: 404, body: {} };
    });
    render(<ConnectorApprovalsWorkbench />);

    const list = await screen.findByRole("list", { name: "Pending agent approvals" });
    const rows = within(list).getAllByRole("listitem");
    expect(rows).toHaveLength(2);
    const highlighted = rows.find((row) => row.getAttribute("data-highlighted") === "true");
    expect(highlighted).toHaveTextContent("officer A Director");
    expect(highlighted).toHaveTextContent("DELETE /api/companies/{companyId}/officers/{officerId} (12)");
    expect(highlighted).toHaveTextContent(/Expires in [34]:\d\d/);

    await user.click(within(highlighted!).getByRole("button", { name: /^Approve Delete officers/ }));
    const dialog = await screen.findByRole("dialog");
    const confirm = within(dialog).getByRole("button", { name: "Approve action" });
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText("Password"), "correct horse battery staple");
    await user.type(within(dialog).getByLabelText(AUTHENTICATOR_CODE), "654321");
    await user.click(within(dialog).getByRole("button", { name: "Approve action" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(expect.stringContaining("Approved: Delete officers")));
    const grant = fetchMock.one("POST", "/api/auth/connector/approve");
    expect(grant.body).toEqual({ approvalId, password: "correct horse battery staple", totpCode: "654321" });
    expect(grant.csrf).toBe("csrf-token");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(await screen.findByText("Approved, waiting for the agent")).toBeInTheDocument();
  });

  it("grants with the password alone when two-factor sign-in is off", async () => {
    const user = userEvent.setup();
    const fetchMock = installFetchMock((request) => {
      if (request.url === "/api/auth/connector/approvals") return { body: [approval(approvalId, "Delete officers")] };
      if (request.url === "/api/auth/connector/approve") {
        return { body: { ok: true, approvalId, summary: "Delete officers", expiresAtUtc: new Date(Date.now() + 240_000).toISOString() } };
      }
      return { status: 404, body: {} };
    });
    render(<ConnectorApprovalsWorkbench />);

    await user.click(await screen.findByRole("button", { name: "Approve Delete officers" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(AUTHENTICATOR_CODE)).not.toBeRequired();
    await user.type(within(dialog).getByLabelText("Password"), "correct horse battery staple");
    const confirm = within(dialog).getByRole("button", { name: "Approve action" });
    expect(confirm).toBeEnabled();
    await user.click(confirm);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(expect.stringContaining("Approved: Delete officers")));
    expect(fetchMock.one("POST", "/api/auth/connector/approve").body).toEqual({ approvalId, password: "correct horse battery staple", totpCode: "" });
  });

  it("shows the refusal message in the dialog without signing the person out", async () => {
    const user = userEvent.setup();
    const sessionExpired = vi.fn();
    window.addEventListener(SESSION_EXPIRED_EVENT, sessionExpired);
    installFetchMock((request) => {
      if (request.url === "/api/auth/connector/approvals") return { body: [approval(approvalId, "Delete officers")] };
      if (request.url === "/api/auth/connector/approve") {
        return {
          status: 401,
          body: { error: "The approval was not granted. Check the password and authenticator code, and that the approval has not expired.", code: "APPROVAL_REFUSED" },
        };
      }
      return { status: 404, body: {} };
    });
    render(<ConnectorApprovalsWorkbench />);

    await user.click(await screen.findByRole("button", { name: "Approve Delete officers" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText("Password"), "wrong");
    await user.type(within(dialog).getByLabelText(AUTHENTICATOR_CODE), "000000");
    await user.click(within(dialog).getByRole("button", { name: "Approve action" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("The approval was not granted.");
    // A refused code is cleared so the person types a fresh one, and the dialog says why that matters.
    expect(within(dialog).getByLabelText(AUTHENTICATOR_CODE)).toHaveValue("");
    expect(within(dialog).getByText(/Each authenticator code works once/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Expires in \d+:\d{2}/)).toBeInTheDocument();
    expect(sessionExpired).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
    window.removeEventListener(SESSION_EXPIRED_EVENT, sessionExpired);
  });
});

const familyId = "9b2d6a3e-1f4c-4c5e-8d7a-2b3c4d5e6f70";

function session(overrides: Record<string, unknown> = {}) {
  return {
    familyId,
    userId: 7,
    accessLevel: "Write",
    dataScope: "Withheld",
    deviceLabel: "Claude",
    clientVersion: "oauth",
    connectedAtUtc: "2026-10-12T09:00:00Z",
    lastUsedAtUtc: "2026-10-12T10:30:00Z",
    expiresAtUtc: "2027-01-10T09:00:00Z",
    ...overrides,
  };
}

const activityRow = {
  id: 41,
  sessionFamilyId: familyId,
  accessLevel: "Write",
  dataScope: "Withheld",
  method: "POST",
  routePattern: "/api/companies/{companyId}/officers",
  resourceId: null,
  companyId: 3,
  statusCode: 201,
  reason: "Adding the new director",
  occurredAtUtc: "2026-10-12T10:30:00Z",
};

describe("connected agents page", () => {
  beforeEach(() => {
    clearCookies();
    setCsrfCookie("csrf-token");
    toast.success.mockReset();
    toast.error.mockReset();
    authState.isOwner = false;
    authState.user = { userId: 7, displayName: "Ada Accountant", role: "Accountant", tenantName: "Example Firm" };
  });

  it("shows the MCP resource the server advertises, not the address this page was opened on", async () => {
    installFetchMock((request) => {
      if (request.url === "/.well-known/oauth-protected-resource/mcp") {
        return { body: { resource: "https://filingbridge.tail1234.ts.net/mcp", authorization_servers: ["https://filingbridge.tail1234.ts.net"] } };
      }
      if (request.url === "/api/auth/connector/sessions") return { body: [] };
      if (request.url.startsWith("/api/auth/connector/activity")) return { body: { data: [], page: 1, pageSize: 25, hasMore: false } };
      return { status: 404, body: {} };
    });
    render(<ConnectorAgentsWorkbench />);

    await waitFor(() => expect(screen.getByTestId("mcp-remote-url")).toHaveTextContent("https://filingbridge.tail1234.ts.net/mcp"));
    expect(screen.getByText(/claude mcp add --transport http filingbridge https:\/\/filingbridge\.tail1234\.ts\.net\/mcp/)).toBeInTheDocument();
    expect(screen.getByText("filingbridge-mcp connect --base-url https://filingbridge.tail1234.ts.net")).toBeInTheDocument();
  });

  it("disconnects an agent after confirmation and refreshes the list", async () => {
    const user = userEvent.setup();
    let revoked = false;
    const fetchMock = installFetchMock((request) => {
      if (request.url === "/api/auth/connector/sessions") return { body: revoked ? [] : [session()] };
      if (request.url.startsWith("/api/auth/connector/activity")) return { body: { data: [activityRow], page: 1, pageSize: 25, hasMore: true } };
      if (request.url === `/api/auth/connector/sessions/${familyId}/revoke`) {
        revoked = true;
        return { status: 204 };
      }
      return { status: 404, body: {} };
    });
    render(<ConnectorAgentsWorkbench />);

    expect(await screen.findByText("Write access")).toBeInTheDocument();
    expect(screen.getByText("Personal data withheld")).toBeInTheDocument();
    expect(await screen.findByText("Adding the new director")).toBeInTheDocument();
    expect(screen.getByTestId("mcp-remote-url")).toHaveTextContent(`${window.location.origin}/mcp`);
    expect(screen.getByText(`filingbridge-mcp connect --base-url ${window.location.origin}`)).toBeInTheDocument();
    expect(screen.queryByLabelText("Show agents connected by")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Disconnect Claude" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
    await user.click(within(dialog).getByRole("button", { name: "Disconnect agent" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Claude has been disconnected."));
    const revoke = fetchMock.one("POST", "/revoke");
    expect(revoke.url).toBe(`/api/auth/connector/sessions/${familyId}/revoke`);
    expect(revoke.csrf).toBe("csrf-token");
    expect(await screen.findByText("No connected agents")).toBeInTheDocument();
  });

  it("pages through activity", async () => {
    const user = userEvent.setup();
    const fetchMock = installFetchMock((request) => {
      if (request.url === "/api/auth/connector/sessions") return { body: [] };
      if (request.url.startsWith("/api/auth/connector/activity")) {
        const page = Number(new URLSearchParams(request.url.split("?")[1]).get("page"));
        return { body: { data: [{ ...activityRow, id: page, reason: `Row on page ${page}` }], page, pageSize: 25, hasMore: page < 2 } };
      }
      return { status: 404, body: {} };
    });
    render(<ConnectorAgentsWorkbench />);

    expect(await screen.findByText("Row on page 1")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Older" }));
    expect(await screen.findByText("Row on page 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Older" })).toBeDisabled();
    expect(fetchMock.requests.some((request) => request.url.includes("page=2&pageSize=25"))).toBe(true);
  });

  it("lets an Owner view and disconnect another user's agents", async () => {
    authState.isOwner = true;
    authState.user = { userId: 1, displayName: "Olive Owner", role: "Owner", tenantName: "Example Firm" };
    const user = userEvent.setup();
    const fetchMock = installFetchMock((request) => {
      if (request.url === "/api/admin/users") {
        return {
          body: [{
            userId: 7,
            email: "ada@example.ie",
            displayName: "Ada Accountant",
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
          }],
        };
      }
      if (request.url.startsWith("/api/auth/connector/sessions?userId=7")) return { body: [session()] };
      if (request.url === "/api/auth/connector/sessions") return { body: [] };
      if (request.url.startsWith("/api/auth/connector/activity")) return { body: { data: [], page: 1, pageSize: 25, hasMore: false } };
      if (request.url.includes("/revoke")) return { status: 204 };
      return { status: 404, body: {} };
    });
    render(<ConnectorAgentsWorkbench />);

    const picker = await screen.findByLabelText("Show agents connected by");
    await screen.findByRole("option", { name: "Ada Accountant (Accountant)" });
    await user.selectOptions(picker, "7");
    expect(await screen.findByText("Agents connected by Ada Accountant")).toBeInTheDocument();
    await user.click(await screen.findByRole("button", { name: "Disconnect Claude" }));
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Disconnect agent" }));

    await waitFor(() => expect(fetchMock.byMethod("POST")).toHaveLength(1));
    expect(fetchMock.byMethod("POST")[0].url).toBe(`/api/auth/connector/sessions/${familyId}/revoke?userId=7`);
    expect(fetchMock.requests.some((request) => request.url === "/api/auth/connector/activity?userId=7&page=1&pageSize=25")).toBe(true);
  });
});
