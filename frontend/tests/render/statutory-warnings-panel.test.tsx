import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatutoryWarningsPanel } from "@/components/period/StatutoryWarningsPanel";
import type { AuditExemptionJeopardy } from "@/lib/api";

describe("StatutoryWarningsPanel", () => {
  it("does not render when no statutory warning or disclosure note is present", () => {
    const { container } = render(
      <StatutoryWarningsPanel jeopardy={null} section307Note={null} />,
    );

    expect(container).toBeEmptyDOMElement();
  });

  it("surfaces audit-exemption risk and s.307 director-loan disclosure evidence", () => {
    render(
      <StatutoryWarningsPanel
        jeopardy={sampleJeopardy({
          isAtRisk: true,
          warning: "Two late filings may jeopardise audit exemption for the next annual return.",
        })}
        section307Note={"Director loan exceeded the statutory threshold.\nBoard approval evidence retained."}
      />,
    );

    expect(screen.getByText("Statutory warnings")).toBeInTheDocument();
    expect(screen.getByText("Audit exemption at risk")).toBeInTheDocument();
    expect(screen.getByText("2 late filings")).toBeInTheDocument();
    expect(screen.getByText("Two late filings may jeopardise audit exemption for the next annual return.")).toBeInTheDocument();
    expect(screen.getByText("s.307 Director Loan Disclosure")).toBeInTheDocument();
    expect(screen.getByText(/Director loan exceeded the statutory threshold/)).toBeInTheDocument();
    expect(screen.getByText(/Board approval evidence retained/)).toBeInTheDocument();
  });

  it("marks lost audit exemption as a blocking statutory warning", () => {
    render(
      <StatutoryWarningsPanel
        jeopardy={sampleJeopardy({
          hasLostExemption: true,
          warning: "Audit exemption has been lost because of repeated late annual returns.",
        })}
        section307Note={null}
      />,
    );

    expect(screen.getByText("Audit exemption lost")).toBeInTheDocument();
    expect(screen.getByText("Manual review required")).toBeInTheDocument();
    expect(screen.getByText("Audit exemption has been lost because of repeated late annual returns.")).toBeInTheDocument();
  });
});

function sampleJeopardy(overrides: Partial<AuditExemptionJeopardy>): AuditExemptionJeopardy {
  return {
    lateFilingCount: 2,
    isAtRisk: false,
    hasLostExemption: false,
    ...overrides,
  };
}
