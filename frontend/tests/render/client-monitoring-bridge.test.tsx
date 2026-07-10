import { render, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ClientMonitoringBridge } from "@/components/ClientMonitoringBridge";

const monitoring = vi.hoisted(() => ({ report: vi.fn(async () => {}) }));

vi.mock("@/lib/clientMonitoring", () => ({
  reportClientMonitoringEvent: monitoring.report,
}));

describe("ClientMonitoringBridge", () => {
  afterEach(() => monitoring.report.mockClear());

  it("reports window and rejected-promise failures without forwarding their sensitive values", async () => {
    render(<ClientMonitoringBridge />);

    window.dispatchEvent(new ErrorEvent("error", {
      message: "client@example.ie password=NeverSendThis amount=999999",
      error: new Error("client@example.ie password=NeverSendThis"),
    }));
    const rejection = new Event("unhandledrejection");
    Object.defineProperty(rejection, "reason", {
      value: { account: "client@example.ie", secret: "NeverSendThis" },
    });
    window.dispatchEvent(rejection);

    await waitFor(() => expect(monitoring.report).toHaveBeenCalledTimes(2));
    expect(monitoring.report).toHaveBeenNthCalledWith(1, "unhandled-client-exception");
    expect(monitoring.report).toHaveBeenNthCalledWith(2, "unhandled-client-exception");
    expect(JSON.stringify(monitoring.report.mock.calls)).not.toMatch(/client@example\.ie|NeverSendThis|999999/);
  });

  it("removes global listeners on unmount", () => {
    const { unmount } = render(<ClientMonitoringBridge />);
    unmount();
    window.dispatchEvent(new Event("error"));
    expect(monitoring.report).not.toHaveBeenCalled();
  });
});
