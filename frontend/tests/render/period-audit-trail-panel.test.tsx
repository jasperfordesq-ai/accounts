import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PeriodAuditTrailPanel } from "@/components/period/PeriodAuditTrailPanel";
import type { AuditLogEntry } from "@/lib/api";

describe("PeriodAuditTrailPanel", () => {
  it("shows an empty audit evidence state when no period events are recorded", () => {
    render(<PeriodAuditTrailPanel auditLog={[]} auditTotal={0} />);

    expect(screen.getByText("Period audit trail")).toBeInTheDocument();
    expect(screen.getByText("No audit events recorded for this period yet.")).toBeInTheDocument();
  });

  it("renders reviewer actions, old/new payload evidence and total event coverage", () => {
    render(
      <PeriodAuditTrailPanel
        auditLog={[
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
        ]}
        auditTotal={5}
      />,
    );

    expect(screen.getByText("Recent review actions and evidence changes for this period.")).toBeInTheDocument();
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
    expect(screen.getByText("Showing latest 2 of 5 audit events.")).toBeInTheDocument();
  });
});

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
