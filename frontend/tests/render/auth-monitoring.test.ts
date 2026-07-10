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

    await expect(login("client@example.ie", "NeverSendThis")).rejects.toThrow(/fetch failed/);
    expect(monitoring.report).toHaveBeenCalledWith("auth-service-unavailable");
    expect(JSON.stringify(monitoring.report.mock.calls)).not.toMatch(/client@example\.ie|NeverSendThis/);
  });
});
