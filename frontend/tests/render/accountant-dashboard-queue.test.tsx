import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { AccountantDashboardQueue } from "@/components/dashboard/AccountantDashboardQueue";
import type { Company, FilingDeadline, ProductionReleaseBlocker } from "@/lib/api";

describe("AccountantDashboardQueue", () => {
  it("surfaces deadlines, blockers, reviewer ownership and next actions", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <AccountantDashboardQueue
        companies={[sampleCompany(), unsupportedCompany(), noPeriodCompany()]}
        deadlines={{
          7: sampleDeadline({ companyId: 7, periodId: 3, deadlineType: "CRO", dueDate: "2026-07-10" }),
          8: sampleDeadline({ companyId: 8, periodId: 4, deadlineType: "Revenue", dueDate: "2026-06-20" }),
        }}
        today="2026-07-03"
        productionReleaseBlockers={sampleProductionReleaseBlockers()}
      />,
    );

    expect(screen.getByText("Accountant Work Queue")).toBeInTheDocument();
    expect(screen.getByText("Active production work across the firm.")).toBeInTheDocument();
    const workflow = screen.getByRole("navigation", { name: "Accountant Workflow" });
    expect(workflow).toBeInTheDocument();
    expect(screen.getByText("8 stages")).toBeInTheDocument();
    expect(workflow).toHaveTextContent("Setup");
    expect(workflow).toHaveTextContent("Import");
    expect(workflow).toHaveTextContent("Classify");
    expect(workflow).toHaveTextContent("Year-End");
    expect(workflow).toHaveTextContent("Statements");
    expect(workflow).toHaveTextContent("Notes");
    expect(workflow).toHaveTextContent("Review");
    expect(workflow).toHaveTextContent("Filing");
    expect(screen.getByText("Start with company setup, then move period work through evidence, statements, review and filing.")).toBeInTheDocument();
    expect(screen.getByText("Urgent clients")).toBeInTheDocument();
    expect(screen.getByText("2 urgent")).toBeInTheDocument();
    expect(screen.getByText("Due-soon deadlines")).toBeInTheDocument();
    expect(screen.getByText("1 deadline")).toBeInTheDocument();
    expect(screen.getByText("Manual handoffs")).toBeInTheDocument();
    expect(screen.getByText("1 handoff")).toBeInTheDocument();
    expect(screen.getByText("Unassigned reviewers")).toBeInTheDocument();
    expect(screen.getByText("2 unassigned")).toBeInTheDocument();
    const releaseBlockers = screen.getByRole("region", { name: "Production release blockers" });
    expect(within(releaseBlockers).getByText("Production release blockers")).toBeInTheDocument();
    expect(within(releaseBlockers).getByText("2 blockers")).toBeInTheDocument();
    expect(within(releaseBlockers).getByText("Backend code")).toBeInTheDocument();
    expect(within(releaseBlockers).getByText("Qualified accountant sign-off required")).toBeInTheDocument();
    expect(within(releaseBlockers).getByText("named-accountant-approval-record")).toBeInTheDocument();
    expect(within(releaseBlockers).getByText("Run qualified-accountant acceptance on the golden corpus.")).toBeInTheDocument();
    expect(within(releaseBlockers).getByRole("link", { name: "Open production readiness" })).toHaveAttribute("href", "/production-readiness");
    const triage = screen.getByRole("region", { name: "Queue triage" });
    expect(triage.compareDocumentPosition(workflow) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(triage).getByText("Highest-risk client")).toBeInTheDocument();
    expect(within(triage).getByText("Atlantic Public Limited Company")).toBeInTheDocument();
    expect(within(triage).getByText("What is wrong")).toBeInTheDocument();
    expect(within(triage).getByText("Manual handoff: PLC/public-company workflow requires manual review")).toBeInTheDocument();
    expect(within(triage).getByText("What is ready")).toBeInTheDocument();
    expect(within(triage).getByText("Active period exists; manual-handoff risk is identified.")).toBeInTheDocument();
    expect(within(triage).getByText("Reviewer ownership")).toBeInTheDocument();
    expect(within(triage).getByText("Unassigned reviewer")).toBeInTheDocument();
    expect(within(triage).getByText("What must I do next")).toBeInTheDocument();
    expect(within(triage).getByRole("link", { name: "Review handoff" })).toHaveAttribute("href", "/companies/8/periods/4?tab=filing");
    const reviewerActionQueue = screen.getByRole("region", { name: "Reviewer action queue" });
    expect(within(reviewerActionQueue).getByText("Reviewer action queue")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("3 priority items")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("2 missing reviewer")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("First three items needing practice attention.")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("Atlantic Public Limited Company")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getAllByText("Unassigned reviewer")).toHaveLength(2);
    expect(within(reviewerActionQueue).getByText("Manual handoff")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("Revenue due 20 Jun 2026")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("Review handoff")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("New Client Limited")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getAllByText("Assign before approval")).toHaveLength(2);
    expect(within(reviewerActionQueue).getByText("Create period")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("Connacht Visual Limited")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("Niamh Reviewer")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("CRO due 10 Jul 2026")).toBeInTheDocument();
    expect(within(reviewerActionQueue).getByText("Open filing")).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Accountant work queue" })).toBeInTheDocument();
    expect(screen.getByText("3 of 3 rows")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Deadline" })).toHaveAttribute("aria-sort", "ascending");
    expect(screen.getByRole("columnheader", { name: "Assigned reviewer" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Next action" })).toBeInTheDocument();
    expect(screen.getByText("Queue totals")).toBeInTheDocument();
    expect(screen.getByText("1 due soon / 1 overdue")).toBeInTheDocument();
    expect(screen.getByText("2 urgent / 1 manual handoff")).toBeInTheDocument();
    expect(screen.getByText("2 unassigned reviewers")).toBeInTheDocument();
    expect(screen.getByText("3 active clients")).toBeInTheDocument();
    expect(screen.getAllByRole("row")[1]).toHaveTextContent("Atlantic Public Limited Company");
    expect(screen.getAllByRole("row")[2]).toHaveTextContent("New Client Limited");
    expect(screen.getAllByRole("row")[3]).toHaveTextContent("Connacht Visual Limited");
    expect(container.querySelector(".workbench-data-grid")).toHaveAttribute("data-responsive", "card");
    const companyCells = container.querySelectorAll('td[data-label="Company"]');
    const deadlineCells = container.querySelectorAll('td[data-label="Deadline"]');
    const blockerCells = container.querySelectorAll('td[data-label="Blockers"]');
    const reviewerCells = container.querySelectorAll('td[data-label="Assigned reviewer"]');
    const actionCells = container.querySelectorAll('td[data-label="Next action"]');
    expect(companyCells[2]).toHaveTextContent("Connacht Visual Limited");
    expect(deadlineCells[2]).toHaveTextContent("CRO due 10 Jul 2026");
    expect(blockerCells[2]).toHaveTextContent("No dashboard-level blockers");
    expect(reviewerCells[2]).toHaveTextContent("Niamh Reviewer");
    expect(actionCells[2]).toHaveTextContent("Open filing");

    await user.click(screen.getByRole("button", { name: "Sort by Company" }));
    expect(screen.getByRole("columnheader", { name: "Company" })).toHaveAttribute("aria-sort", "ascending");
    await user.click(screen.getByRole("button", { name: "Sort by Company" }));
    expect(screen.getByRole("columnheader", { name: "Company" })).toHaveAttribute("aria-sort", "descending");

    expect(screen.getAllByText("Connacht Visual Limited").length).toBeGreaterThan(1);
    expect(screen.getAllByText("CRO due 10 Jul 2026").length).toBeGreaterThan(1);
    expect(screen.getByText("Due soon")).toBeInTheDocument();
    expect(screen.getAllByText("Niamh Reviewer").length).toBeGreaterThan(1);
    expect(screen.getAllByText("niamh.reviewer@example.ie").length).toBeGreaterThan(1);
    expect(screen.getAllByText("Unassigned")).toHaveLength(2);
    expect(screen.getAllByRole("link", { name: "Open filing" }).at(-1)).toHaveAttribute("href", "/companies/7/periods/3?tab=filing");

    expect(screen.getAllByText("Atlantic Public Limited Company").length).toBeGreaterThan(1);
    expect(screen.getAllByText("Manual handoff").length).toBeGreaterThan(1);
    expect(screen.getAllByText("PLC/public-company workflow requires manual review").length).toBeGreaterThan(1);
    expect(screen.getAllByRole("link", { name: "Review handoff" })).toHaveLength(3);
    expect(screen.getAllByRole("link", { name: "Review handoff" }).at(-1)).toHaveAttribute("href", "/companies/8/periods/4?tab=filing");

    expect(screen.getAllByText("New Client Limited").length).toBeGreaterThan(1);
    expect(screen.getAllByText("No period").length).toBeGreaterThan(1);
    expect(screen.getAllByRole("link", { name: "Create period" }).at(-1)).toHaveAttribute("href", "/companies/9");

    expect(screen.getAllByRole("row")[1]).toHaveAttribute("data-tone", "bad");
    await user.type(screen.getByRole("searchbox", { name: "Filter Accountant work queue" }), "niamh");
    expect(screen.getByText("1 of 3 rows")).toBeInTheDocument();
    const filteredTable = screen.getByRole("table", { name: "Accountant work queue" });
    expect(within(filteredTable).getByText("Connacht Visual Limited")).toBeInTheDocument();
    expect(within(filteredTable).queryByText("Atlantic Public Limited Company")).not.toBeInTheDocument();
  });
});

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Connacht Visual Limited",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
    assignedReviewerName: "Niamh Reviewer",
    assignedReviewerEmail: "niamh.reviewer@example.ie",
    latestPeriod: {
      id: 3,
      companyId: 7,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Review",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
      sizeClassification: {
        id: 1,
        turnover: 700000,
        balanceSheetTotal: 300000,
        avgEmployees: 8,
        calculatedClass: "Micro",
      },
    },
  };
}

function unsupportedCompany(): Company {
  return {
    ...sampleCompany(),
    id: 8,
    legalName: "Atlantic Public Limited Company",
    companyType: "PublicLimitedCompany",
    assignedReviewerName: undefined,
    assignedReviewerEmail: undefined,
    latestPeriod: {
      ...sampleCompany().latestPeriod!,
      id: 4,
      companyId: 8,
      status: "Review",
    },
  };
}

function noPeriodCompany(): Company {
  return {
    ...sampleCompany(),
    id: 9,
    legalName: "New Client Limited",
    assignedReviewerName: undefined,
    assignedReviewerEmail: undefined,
    latestPeriod: undefined,
    periods: [],
    periodCount: 0,
  };
}

function sampleDeadline({
  companyId,
  periodId,
  deadlineType,
  dueDate,
}: {
  companyId: number;
  periodId: number;
  deadlineType: string;
  dueDate: string;
}): FilingDeadline {
  return {
    id: companyId,
    companyId,
    periodId,
    deadlineType,
    calculatedDueDate: dueDate,
    dueDate,
    isLate: false,
    penaltyAmount: 0,
  };
}

function sampleProductionReleaseBlockers(): ProductionReleaseBlocker[] {
  return [
    {
      code: "backend-code:qualified-accountant-signoff",
      trackCode: "backend-code",
      trackLabel: "Backend code",
      ownerRole: "Qualified accountant",
      severity: "critical",
      riskRank: 0,
      blockingIssue: "Qualified accountant sign-off required",
      requiredEvidence: "Named accountant approval recorded against the period.",
      nextAction: "Run qualified-accountant acceptance on the golden corpus.",
      sourceActionCode: "qualified-accountant-signoff",
      releaseChecklistCode: "accountant-final-signoff",
      operationalGateCode: "qualified-accountant-review",
      evidenceArtifact: "named-accountant-approval-record",
      blocksRelease: true,
    },
    {
      code: "frontend-ui-ux:light-dark-visual-regression",
      trackCode: "frontend-ui-ux",
      trackLabel: "Frontend UI/UX",
      ownerRole: "Engineering",
      severity: "high",
      riskRank: 30,
      blockingIssue: "Light/dark visual regression required",
      requiredEvidence: "Screenshot review attached to CI or release checklist.",
      nextAction: "Review each screenshot route-by-route in light and dark mode.",
      sourceActionCode: "light-dark-visual-regression",
      releaseChecklistCode: "visual-qa-screenshot-review",
      operationalGateCode: "",
      evidenceArtifact: "light-dark-mobile-tablet-desktop-screenshot-review",
      blocksRelease: true,
    },
  ];
}
