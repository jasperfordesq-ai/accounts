import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { YearEndQuestionnaireHeader } from "@/components/period/YearEndQuestionnaireHeader";

describe("YearEndQuestionnaireHeader", () => {
  it("renders period context, back navigation and accessible section progress", () => {
    render(
      <YearEndQuestionnaireHeader
        companyId={7}
        periodId={3}
        backHref="/companies/7/periods/3"
        companyName="Connacht Visual Limited"
        periodLabel="1 Jan 2026 - 31 Dec 2026"
        completedCount={8}
        totalSections={12}
      />,
    );

    expect(screen.getByRole("link", { name: "Back to Period Workspace" }))
      .toHaveAttribute("href", "/companies/7/periods/3");
    expect(screen.getByRole("link", { name: "Back to Period Workspace" }))
      .toHaveClass("border", "border-[var(--control-border)]", "min-h-10");
    expect(screen.getByRole("heading", { name: "Year-End Questionnaire" })).toBeInTheDocument();
    expect(screen.getByText("Connacht Visual Limited - 1 Jan 2026 - 31 Dec 2026")).toBeInTheDocument();
    expect(screen.getByText("8 of 12 sections completed")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Year-end questionnaire progress" })).toHaveAttribute("aria-valuenow", "67");
    expect(screen.getByRole("progressbar", { name: "Year-end questionnaire progress" })).toHaveAttribute("aria-valuemin", "0");
    expect(screen.getByRole("progressbar", { name: "Year-end questionnaire progress" })).toHaveAttribute("aria-valuemax", "100");
  });
});
