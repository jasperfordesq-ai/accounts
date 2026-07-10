import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { PeriodAuditTrailPanel } from "@/components/period/PeriodAuditTrailPanel";
import type { AuditLogEntry } from "@/lib/api";

describe("PeriodAuditTrailPanel", () => {
  it("shows an empty audit evidence state when no period events are recorded", () => {
    render(<PeriodAuditTrailPanel {...panelProps()} />);

    expect(screen.getByText("Period audit trail")).toBeInTheDocument();
    expect(screen.getByText("No audit events recorded for this period yet.")).toBeInTheDocument();
  });

  it("renders reviewer actions, old/new payload evidence and total event coverage", () => {
    render(
      <PeriodAuditTrailPanel
        {...panelProps({
          auditLog: [
          sampleAuditEntry({
            id: 11,
            action: "size-classification.saved",
            entityType: "SizeClassification",
            entityId: 4,
            userId: "reviewer@example.ie",
            oldValueJson: "{\"turnover\":90000,\"avgEmployees\":2}",
            newValueJson: "{\"turnover\":120000,\"avgEmployees\":3}",
          }),
          sampleAuditEntry({
            id: 12,
            action: "filing.approved",
            entityType: "CroFilingPackage",
            entityId: 8,
            userId: "",
          }),
          ],
          auditTotal: 2,
        })}
      />,
    );

    expect(screen.getByText("Review actions and evidence changes for this period. Every retained event is reachable.")).toBeInTheDocument();
    expect(screen.getByText("size-classification.saved")).toBeInTheDocument();
    expect(screen.getByText("SizeClassification #4")).toBeInTheDocument();
    expect(screen.getByText("reviewer@example.ie")).toBeInTheDocument();
    expect(screen.getByText("filing.approved")).toBeInTheDocument();
    expect(screen.getByText("System")).toBeInTheDocument();
    expect(screen.getByText("Audit details")).toBeInTheDocument();
    expect(screen.getByText("Old value")).toBeInTheDocument();
    expect(screen.getByText("New value")).toBeInTheDocument();
    expect(screen.getByText(/"turnover": 90000/)).toBeInTheDocument();
    expect(screen.getByText(/"avgEmployees": 3/)).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Period audit events table" })).toHaveAttribute("tabindex", "0");
    expect(screen.getByText(/Swipe horizontally, or focus this table/)).toBeVisible();
    expect(screen.getByText("Showing 1–2 of 2 audit events")).toBeInTheDocument();
  });

  it("reaches the stable third page of a 125-event audit trail", async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    const onPageSizeChange = vi.fn();
    const pageThreeEvents = Array.from({ length: 25 }, (_, index) => sampleAuditEntry({
      id: 25 - index,
      entityId: 25 - index,
      action: `audit.event.${25 - index}`,
      timestamp: new Date(Date.UTC(2026, 6, 3, 12, 30 - index)).toISOString(),
    }));

    render(
      <PeriodAuditTrailPanel
        {...panelProps({
          auditLog: pageThreeEvents,
          auditTotal: 125,
          page: 3,
          pageSize: 50,
          totalPages: 3,
          onPageChange,
          onPageSizeChange,
        })}
      />,
    );

    expect(screen.getByRole("navigation", { name: "Audit log pagination" })).toBeInTheDocument();
    expect(screen.getByText("Showing 101–125 of 125 audit events")).toBeInTheDocument();
    expect(screen.getByText("Page 3 of 3")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next audit page" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Previous audit page" }));
    expect(onPageChange).toHaveBeenCalledWith(2);

    await user.selectOptions(screen.getByRole("combobox", { name: "Events per page" }), "100");
    expect(onPageSizeChange).toHaveBeenCalledWith(100);
  });

  it("exposes loading and retryable error states without claiming the trail is empty", async () => {
    const user = userEvent.setup();
    const onRetry = vi.fn();
    const { rerender } = render(<PeriodAuditTrailPanel {...panelProps({ loading: true, onRetry })} />);

    expect(screen.getByRole("status")).toHaveTextContent("Loading audit events");

    rerender(<PeriodAuditTrailPanel {...panelProps({ error: "Audit service unavailable", onRetry })} />);
    expect(screen.getByRole("alert")).toHaveTextContent("Audit service unavailable");
    expect(screen.queryByText("No audit events recorded for this period yet.")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Retry audit events" }));
    expect(onRetry).toHaveBeenCalledOnce();
  });
});

function panelProps(
  overrides: Partial<ComponentProps<typeof PeriodAuditTrailPanel>> = {},
): ComponentProps<typeof PeriodAuditTrailPanel> {
  return {
    auditLog: [],
    auditTotal: 0,
    page: 1,
    pageSize: 50,
    totalPages: 1,
    loading: false,
    error: null,
    onPageChange: vi.fn(),
    onPageSizeChange: vi.fn(),
    onRetry: vi.fn(),
    ...overrides,
  };
}

function sampleAuditEntry(overrides: Partial<AuditLogEntry>): AuditLogEntry {
  return {
    id: 1,
    companyId: 7,
    periodId: 3,
    entityType: "Entity",
    entityId: 1,
    action: "updated",
    timestamp: "2026-07-03T12:30:00Z",
    ...overrides,
  };
}
