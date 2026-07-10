import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ErrorBoundary } from "@/components/ErrorBoundary";

const monitoring = vi.hoisted(() => ({ report: vi.fn(async () => {}) }));

vi.mock("@/lib/clientMonitoring", () => ({
  reportClientMonitoringEvent: monitoring.report,
}));

function SensitiveFailure(): never {
  throw new Error("client@example.ie password=NeverSendThis amount=999999");
}

describe("ErrorBoundary client monitoring", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    monitoring.report.mockClear();
  });

  it("reports only the controlled event code and renders a safe recovery state", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    render(
      <ErrorBoundary>
        <SensitiveFailure />
      </ErrorBoundary>,
    );

    expect(screen.getByRole("heading", { name: "Something went wrong" })).toBeInTheDocument();
    expect(screen.getByText("An unexpected error occurred. Please try again.")).toBeInTheDocument();
    await waitFor(() => expect(monitoring.report).toHaveBeenCalledWith("render-exception"));
    expect(JSON.stringify(monitoring.report.mock.calls)).not.toMatch(/client@example\.ie|NeverSendThis|999999/);
  });

  it("exposes a keyboard-operable retry control", async () => {
    const user = userEvent.setup();
    vi.spyOn(console, "error").mockImplementation(() => {});
    render(
      <ErrorBoundary>
        <SensitiveFailure />
      </ErrorBoundary>,
    );

    const retry = screen.getByRole("button", { name: "Try Again" });
    retry.focus();
    await user.keyboard("{Enter}");

    expect(screen.getByRole("heading", { name: "Something went wrong" })).toBeInTheDocument();
    expect(monitoring.report).toHaveBeenCalled();
  });
});
