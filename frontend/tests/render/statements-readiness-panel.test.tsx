import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatementsReadinessPanel } from "@/components/period/StatementsReadinessPanel";
import type { ReadinessScore } from "@/lib/api";

describe("StatementsReadinessPanel", () => {
  it("renders filing readiness, balance status, missing items and warnings", () => {
    render(<StatementsReadinessPanel readiness={sampleReadiness()} />);

    expect(screen.getByRole("heading", { name: "Filing Readiness" })).toBeInTheDocument();
    expect(screen.getByText("Assessment of whether the accounts are ready for filing")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Completeness" })).toHaveAttribute("aria-valuenow", "82");
    expect(screen.getByText("82%")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Filing readiness" })).toHaveAttribute("aria-valuenow", "71");
    expect(screen.getByText("71%")).toBeInTheDocument();
    expect(screen.getByText("Balance sheet does not balance")).toBeInTheDocument();
    expect(screen.getByText("Missing Items")).toBeInTheDocument();
    expect(screen.getByText("CRO accounts PDF not generated")).toBeInTheDocument();
    expect(screen.getByText("Warnings")).toBeInTheDocument();
    expect(screen.getByText("External ROS validation remains required")).toBeInTheDocument();
  });

  it("renders a professional empty state while readiness data is unavailable", () => {
    render(<StatementsReadinessPanel readiness={null} />);

    expect(screen.getByText("Readiness data is not available yet.")).toBeInTheDocument();
    expect(screen.getByText("Complete the year-end process and generate adjustments first.")).toBeInTheDocument();
  });
});

function sampleReadiness(): ReadinessScore {
  return {
    completenessPercent: 82,
    filingReadinessPercent: 71,
    balanceSheetBalances: false,
    missingItems: ["CRO accounts PDF not generated"],
    warnings: ["External ROS validation remains required"],
  };
}
