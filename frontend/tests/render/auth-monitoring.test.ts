import { afterEach, describe, expect, it, vi } from "vitest";
import { getCurrentUser, login } from "@/lib/auth";
import { ApiError } from "@/lib/api";

const monitoring = vi.hoisted(() => ({ report: vi.fn(async () => {}) }));

vi.mock("@/lib/clientMonitoring", () => ({
  reportClientMonitoringEvent: monitoring.report,
}));

describe("authentication monitoring privacy", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    monitoring.report.mockClear();
  });

  it("reports service and contract failures without response or identity data", async () => {
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        error: "client@example.ie password=NeverSendThis",
      }), { status: 503, statusText: "Service Unavailable" }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        userId: 9,
        email: "client@example.ie",
        password: "NeverSendThis",
      }), { status: 200 })));

    await expect(getCurrentUser()).rejects.toBeInstanceOf(ApiError);
    await expect(getCurrentUser()).rejects.toThrow(/Invalid authentication user response contract/);

    expect(monitoring.report).toHaveBeenCalledTimes(2);
    expect(monitoring.report).toHaveBeenNthCalledWith(1, "auth-service-unavailable");
    expect(monitoring.report).toHaveBeenNthCalledWith(2, "auth-service-unavailable");
    expect(JSON.stringify(monitoring.report.mock.calls)).not.toMatch(/client@example\.ie|NeverSendThis|password/);
  });

  it("does not report expected authentication rejection", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", {
      status: 401,
      statusText: "Unauthorized",
    })));

    await expect(getCurrentUser()).rejects.toMatchObject({ status: 401 });
    expect(monitoring.report).not.toHaveBeenCalled();
  });

  it("does not forward login credentials when the network fails", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => {
      throw new TypeError("fetch failed for client@example.ie password=NeverSendThis");
    }));

    await expect(login("client-workspace", "client@example.ie", "NeverSendThis")).rejects.toThrow(/fetch failed/);
    expect(monitoring.report).toHaveBeenCalledWith("auth-service-unavailable");
    expect(JSON.stringify(monitoring.report.mock.calls)).not.toMatch(/client@example\.ie|NeverSendThis/);
  });

  it("sends the workspace slug in the typed login request", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({
      userId: 9,
      tenantId: 3,
      tenantName: "Client Workspace",
      tenantSlug: "client-workspace",
      email: "client@example.ie",
      displayName: "Client User",
      role: "Client",
      allowedCompanyIds: [],
      mustChangePassword: false,
      mfaVerified: false,
      mfaMethod: null,
    }), { status: 200, headers: { "content-type": "application/json" } }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(login("client-workspace", "client@example.ie", "NeverSendThis")).resolves.toMatchObject({
      tenantSlug: "client-workspace",
      email: "client@example.ie",
    });

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/login", expect.objectContaining({
      method: "POST",
      body: JSON.stringify({
        tenantSlug: "client-workspace",
        email: "client@example.ie",
        password: "NeverSendThis",
      }),
    }));
  });
});
