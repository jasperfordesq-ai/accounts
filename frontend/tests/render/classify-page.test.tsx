import { Suspense } from "react";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  getCompany: vi.fn(),
  getPeriod: vi.fn(),
  getClassificationPrerequisites: vi.fn(),
  saveSizeClassification: vi.fn(),
  runClassification: vi.fn(),
  setFilingRegime: vi.fn(),
  saveMemberAuditNotice: vi.fn(),
  fetchDocumentBlob: vi.fn(),
}));

const historic = vi.hoisted(() => ({ recordHistoricSizeFigures: vi.fn(), retireHistoricSizeFigures: vi.fn(), getArchivedHistoricFigures: vi.fn() }));
const auth = vi.hoisted(() => ({ canWriteWorkingPapers: true, canApprove: false, isOwner: false, canReadInternalWorkingPapers: false, reauthenticate: vi.fn() }));

vi.mock("@/components/AuthProvider", () => ({ useAuth: () => auth }));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));
vi.mock("@/lib/historicSizeFigures", async (original) => ({
  ...(await original<typeof import("@/lib/historicSizeFigures")>()),
  ...historic,
}));

import ClassifyPage from "@/app/companies/[companyId]/periods/[periodId]/classify/page";
import { ApiError } from "@/lib/api";

const period = {
  id: 9,
  companyId: 1,
  periodStart: "2025-08-01",
  periodEnd: "2026-07-31",
  status: "Draft",
  isFirstYear: false,
  memberAuditNoticeReceived: false,
  goingConcernConfirmed: false,
};

async function renderPage(periodId = "9") {
  const params = Promise.resolve({ companyId: "1", periodId });
  await act(async () => {
    render(
      <Suspense fallback={null}>
        <ClassifyPage params={params} />
      </Suspense>,
    );
  });
}

describe("Classify page (live defects 2026-10-10)", () => {
  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    historic.recordHistoricSizeFigures.mockReset();
    auth.canWriteWorkingPapers = true;
    auth.canApprove = false;
    auth.isOwner = false;
    auth.canReadInternalWorkingPapers = false;
    api.getCompany.mockResolvedValue({ id: 1, legalName: "Example Charity CLG" });
    api.getClassificationPrerequisites.mockResolvedValue({
      ready: true,
      isFirstYear: false,
      firstBlockingPeriod: null,
      message: "Every earlier period has a current classification, so this period can be classified.",
    });
    api.runClassification.mockResolvedValue({
      calculatedClass: "Micro",
      qualificationNotes: "First financial year",
      canUseMicro: true,
      canFileAbridged: true,
      auditExempt: true,
      availableRegimes: ["Micro"],
      isIneligibleEntity: false,
      rawCurrentClass: "Micro",
      annualisedTurnover: 638,
      periodLengthInYears: 1,
    });
  });

  it("shows a saved zero for average employees instead of an empty box", async () => {
    api.getPeriod.mockResolvedValue({
      ...period,
      sizeClassification: { id: 1, turnover: 1640, balanceSheetTotal: 22358, avgEmployees: 0, thresholdElectionEffectiveFrom: "2024-01-01" },
    });

    await renderPage();

    expect(await screen.findByLabelText("Average Employees")).toHaveValue(0);
    expect(screen.getByLabelText("Turnover")).toHaveValue(1640);
    expect(screen.getByText("Not yet classified")).toBeInTheDocument();
    expect(screen.getByText(/saved but not yet classified/)).toBeInTheDocument();
  });

  it("saves the inputs and classifies in one request, sending zero employees as 0", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });

    await renderPage();

    await user.type(await screen.findByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");
    await user.type(screen.getByLabelText("Average Employees"), "0");
    expect(screen.getByLabelText("Average Employees")).toHaveValue(0);
    await user.click(screen.getByRole("button", { name: /Classify Company/ }));

    await waitFor(() => expect(api.runClassification).toHaveBeenCalledTimes(1));
    expect(api.runClassification).toHaveBeenCalledWith(1, 9, {
      turnover: 638,
      balanceSheetTotal: 17430,
      avgEmployees: 0,
      thresholdElectionEffectiveFrom: "2024-01-01",
    });
    expect(api.saveSizeClassification).not.toHaveBeenCalled();
  });

  it("does not send an empty employee count as a silent zero", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });

    await renderPage();

    await user.type(await screen.findByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");

    expect(screen.getByRole("button", { name: /Classify Company/ })).toBeDisabled();
    expect(api.runClassification).not.toHaveBeenCalled();
  });

  it("surfaces the s.280A predecessor requirement before submit and links to the period to start from", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });
    api.getClassificationPrerequisites.mockResolvedValue({
      ready: false,
      isFirstYear: false,
      firstBlockingPeriod: {
        periodId: 1,
        periodStart: "2017-07-20",
        periodEnd: "2018-12-30",
        reason: "not classified yet",
      },
      message:
        "Classify 1 Aug 2024 – 31 Jul 2025 first. Each year's size test uses the previous year's figures (Companies Act 2014, s.280A), so earlier periods must be classified in order, starting from 20 Jul 2017 – 30 Dec 2018.",
    });

    await renderPage();

    const notice = await screen.findByRole("status", { name: /Earlier periods must be classified first/ });
    expect(notice).toHaveTextContent("Companies Act 2014, s.280A");
    expect(notice).toHaveTextContent("starting from 20 Jul 2017 – 30 Dec 2018");
    expect(notice).toHaveTextContent("not classified yet");
    expect(within(notice).getByRole("link", { name: /Classify 20 Jul 2017 – 30 Dec 2018/ }))
      .toHaveAttribute("href", "/companies/1/periods/1/classify");
    expect(api.getClassificationPrerequisites).toHaveBeenCalledWith(1, 9);

    await user.type(screen.getByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");
    await user.type(screen.getByLabelText("Average Employees"), "0");
    const submit = screen.getByRole("button", { name: /Classify Company/ });
    expect(submit).toBeDisabled();
    expect(submit).toHaveAccessibleDescription(/earlier periods have been classified/);
    expect(api.runClassification).not.toHaveBeenCalled();
  });

  it("keeps the normal form when every earlier period is classified", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });

    await renderPage();

    await user.type(await screen.findByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");
    await user.type(screen.getByLabelText("Average Employees"), "0");
    expect(screen.queryByRole("status", { name: /Earlier periods must be classified first/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Classify Company/ })).toBeEnabled();
  });

  it("does not block the form when the prerequisites cannot be loaded (the server still enforces them)", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });
    api.getClassificationPrerequisites.mockRejectedValue(new Error("Network down"));

    await renderPage();

    await user.type(await screen.findByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");
    await user.type(screen.getByLabelText("Average Employees"), "0");
    expect(screen.getByRole("button", { name: /Classify Company/ })).toBeEnabled();
  });
});

describe("Classify page: historic figures from the filed accounts (s.280A)", () => {
  const predecessor = {
    id: 8,
    companyId: 1,
    periodStart: "2024-01-01",
    periodEnd: "2024-12-31",
    status: "Draft",
    isFirstYear: false,
    memberAuditNoticeReceived: false,
    goingConcernConfirmed: false,
  };
  const blockedPrerequisites = {
    ready: false,
    isFirstYear: false,
    firstBlockingPeriod: { periodId: 1, periodStart: "2018-01-01", periodEnd: "2018-12-31", reason: "not classified yet" },
    message: "Classify 1 Jan 2018 – 31 Dec 2018 first, or record historic figures from the filed accounts for 1 Jan 2024 – 31 Dec 2024.",
    historicFiguresPeriod: null,
    canRecordHistoricFigures: true,
    suggestedThresholdElection: "2023-01-01",
  };

  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    historic.recordHistoricSizeFigures.mockReset();
    historic.retireHistoricSizeFigures.mockReset();
    historic.getArchivedHistoricFigures.mockReset();
    historic.getArchivedHistoricFigures.mockResolvedValue([]);
    auth.canWriteWorkingPapers = true;
    auth.canApprove = true;
    auth.isOwner = false;
    auth.canReadInternalWorkingPapers = true;
    api.getCompany.mockResolvedValue({ id: 1, legalName: "Example Charity CLG" });
    api.getPeriod.mockResolvedValue({ ...predecessor });
    api.getClassificationPrerequisites.mockResolvedValue(blockedPrerequisites);
  });

  async function fillHistoricFigures(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole("button", { name: "Enter figures from the filed accounts instead" }));
    await user.type(screen.getByLabelText("Turnover from the filed accounts"), "1000000");
    await user.type(screen.getByLabelText("Balance sheet total from the filed accounts"), "500000");
    await user.type(screen.getByLabelText("Average employees from the filed accounts"), "0");
    await user.type(screen.getByLabelText("Which filed accounts"), "Fictitious members' accounts, year ended 31 December 2024");
    const evidence = new File(["%PDF-1.4 fictitious"], "accounts.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText("PDF of the filed accounts"), evidence);
    return evidence;
  }

  it("offers keyed-in figures on a predecessor's page with plain provenance wording", async () => {
    const user = userEvent.setup();
    historic.recordHistoricSizeFigures.mockResolvedValue({ id: 5 });
    await renderPage("8");

    const evidence = await fillHistoricFigures(user);
    expect(screen.getByText("These figures are keyed in from accounts filed elsewhere; the system did not compute them.")).toBeInTheDocument();
    // The consequence (undone only by an Owner retiring the figures) is stated before the button.
    const warning = screen.getByRole("note");
    expect(warning).toHaveTextContent(
      "Recording historic figures marks this period as history filed outside FilingBridge. It can't then be classified, finalised or filed here unless an Owner retires the historic figures: they are kept as evidence, and the period can then be classified normally.",
    );
    expect(warning.compareDocumentPosition(screen.getByRole("button", { name: "Record historic figures" })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // The election defaults to the dependent period's.
    expect(screen.getByLabelText("2024 threshold adjustment election", { selector: "#historic-threshold-election" })).toHaveValue("2023-01-01");
    await user.selectOptions(
      screen.getByLabelText("Size class the company qualified for in that year (attested by you; unlocks the carry-forward rule)"),
      "Small",
    );
    await user.click(screen.getByRole("button", { name: "Record historic figures" }));

    await waitFor(() => expect(historic.recordHistoricSizeFigures).toHaveBeenCalledTimes(1));
    expect(historic.recordHistoricSizeFigures).toHaveBeenCalledWith(1, 8, {
      turnover: 1000000,
      balanceSheetTotal: 500000,
      avgEmployees: 0,
      thresholdElectionEffectiveFrom: "2023-01-01",
      sourceReference: "Fictitious members' accounts, year ended 31 December 2024",
      evidence,
      attestedEffectiveClass: "Small",
    });
    expect(api.runClassification).not.toHaveBeenCalled();
  });

  it("asks for recent verification beside, not inside, the historic-figures form", async () => {
    const user = userEvent.setup();
    historic.recordHistoricSizeFigures.mockRejectedValue(
      new ApiError(428, "Precondition Required", JSON.stringify({ code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." })),
    );
    await renderPage("8");

    await fillHistoricFigures(user);
    await user.click(screen.getByRole("button", { name: "Record historic figures" }));

    const prompt = await screen.findByRole("form", { name: "Recent verification" });
    expect(prompt.parentElement?.closest("form")).toBeNull();
    expect(screen.getByRole("button", { name: "Record historic figures" })).toBeInTheDocument();
  });

  it("does not offer the keyed-in route to users who cannot approve", async () => {
    auth.canApprove = false;
    await renderPage("8");

    await screen.findByLabelText("Turnover");
    expect(screen.queryByRole("button", { name: "Enter figures from the filed accounts instead" })).not.toBeInTheDocument();
  });

  it("shows the historic-figures badge and provenance instead of a computed class", async () => {
    api.getPeriod.mockResolvedValue({
      ...predecessor,
      sizeClassification: {
        id: 5,
        turnover: 1000000,
        balanceSheetTotal: 500000,
        avgEmployees: 0,
        inputBasis: "HistoricAnchor",
        historicSourceReference: "Fictitious members' accounts, year ended 31 December 2024",
        historicAttestedBy: "Firm Reviewer",
        historicAttestedRole: "Reviewer",
        decisionInputFingerprintSha256: "a".repeat(64),
        calculatedClass: "Large",
        rawCurrentClass: "Small",
      },
    });
    api.getClassificationPrerequisites.mockResolvedValue({
      ready: false,
      isFirstYear: false,
      firstBlockingPeriod: null,
      message: "This period's size figures were keyed in from accounts filed elsewhere.",
      historicFiguresPeriod: null,
      canRecordHistoricFigures: true,
    });
    await renderPage("8");

    expect(await screen.findByText("Historic figures (from filed accounts)")).toBeInTheDocument();
    expect(screen.getByText("These figures are keyed in from accounts filed elsewhere; the system did not compute them.")).toBeInTheDocument();
    expect(screen.getByText(/No size class is attested/)).toBeInTheDocument();
    expect(screen.queryByText("Large Company")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Classify Company/ })).not.toBeInTheDocument();
  });

  it("links the dependent period's notice to the predecessor's historic-figures route", async () => {
    api.getPeriod.mockResolvedValue({
      id: 9,
      companyId: 1,
      periodStart: "2025-01-01",
      periodEnd: "2025-12-31",
      status: "Draft",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: false,
    });
    api.getClassificationPrerequisites.mockResolvedValue({
      ...blockedPrerequisites,
      historicFiguresPeriod: { periodId: 8, periodStart: "2024-01-01", periodEnd: "2024-12-31", reason: "record historic figures from the filed accounts" },
      canRecordHistoricFigures: false,
    });
    await renderPage();

    const notice = await screen.findByRole("status", { name: /Earlier periods must be classified first/ });
    expect(within(notice).getByRole("link", { name: /Classify 1 Jan 2018 – 31 Dec 2018/ }))
      .toHaveAttribute("href", "/companies/1/periods/1/classify");
    expect(within(notice).getByRole("link", { name: /record historic figures from the filed accounts for 1 Jan 2024 – 31 Dec 2024/i }))
      .toHaveAttribute("href", "/companies/1/periods/8/classify");
    expect(screen.queryByRole("button", { name: "Enter figures from the filed accounts instead" })).not.toBeInTheDocument();
  });
});

describe("Classify page: retiring historic figures (Owner)", () => {
  const anchorPeriod = {
    id: 8,
    companyId: 1,
    periodStart: "2024-01-01",
    periodEnd: "2024-12-31",
    status: "Draft",
    isFirstYear: false,
    memberAuditNoticeReceived: false,
    goingConcernConfirmed: false,
  };
  const anchor = {
    id: 5,
    turnover: 1000000,
    balanceSheetTotal: 500000,
    avgEmployees: 0,
    inputBasis: "HistoricAnchor",
    historicSourceReference: "Fictitious members' accounts, year ended 31 December 2024",
    historicAttestedBy: "Firm Reviewer",
    historicAttestedRole: "Reviewer",
    decisionInputFingerprintSha256: "a".repeat(64),
    calculatedClass: "Large",
    rawCurrentClass: "Small",
  };
  const anchorPrerequisites = {
    ready: false,
    isFirstYear: false,
    firstBlockingPeriod: null,
    message: "This period's size figures were keyed in from accounts filed elsewhere.",
    historicFiguresPeriod: null,
    canRecordHistoricFigures: true,
  };
  const reason = "The filed accounts were for the wrong financial year";

  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    historic.recordHistoricSizeFigures.mockReset();
    historic.retireHistoricSizeFigures.mockReset();
    historic.getArchivedHistoricFigures.mockReset();
    historic.getArchivedHistoricFigures.mockResolvedValue([]);
    auth.canWriteWorkingPapers = true;
    auth.canApprove = true;
    auth.isOwner = true;
    auth.canReadInternalWorkingPapers = true;
    api.getCompany.mockResolvedValue({ id: 1, legalName: "Example Charity CLG" });
    api.getPeriod.mockResolvedValue({ ...anchorPeriod, sizeClassification: { ...anchor } });
    api.getClassificationPrerequisites.mockResolvedValue(anchorPrerequisites);
  });

  it("lets an Owner retire the figures with a reason, stating the consequences first", async () => {
    const user = userEvent.setup();
    historic.retireHistoricSizeFigures.mockResolvedValue({ ...anchor, inputBasis: "RetiredHistoricAnchor" });
    await renderPage("8");

    const consequences = await screen.findByText(/no longer used as the previous year's figures in the next period's size test/);
    expect(consequences).toHaveTextContent("The following periods' classifications become out of date and must be re-run, so figures that a finalised, filed or locked later period relies on cannot be retired.");
    const retire = screen.getByRole("button", { name: "Retire historic figures" });
    expect(consequences.compareDocumentPosition(retire) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(retire).toBeDisabled();
    await user.type(screen.getByLabelText("Why are these figures being retired?"), reason);
    await user.click(retire);

    await waitFor(() => expect(historic.retireHistoricSizeFigures).toHaveBeenCalledWith(1, 8, reason));
    await waitFor(() => expect(api.getPeriod).toHaveBeenCalledTimes(2));
  });

  it("does not offer retirement to a Reviewer", async () => {
    auth.isOwner = false;
    await renderPage("8");

    expect(await screen.findByText("Historic figures (from filed accounts)")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retire historic figures" })).not.toBeInTheDocument();
  });

  it("asks for recent verification beside, not inside, the retirement controls", async () => {
    const user = userEvent.setup();
    historic.retireHistoricSizeFigures.mockRejectedValue(
      new ApiError(428, "Precondition Required", JSON.stringify({ code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." })),
    );
    await renderPage("8");

    await user.type(await screen.findByLabelText("Why are these figures being retired?"), reason);
    await user.click(screen.getByRole("button", { name: "Retire historic figures" }));

    const prompt = await screen.findByRole("form", { name: "Recent verification" });
    expect(prompt.parentElement?.closest("form")).toBeNull();
    expect(screen.getByRole("button", { name: "Retire historic figures" })).toBeInTheDocument();
  });

  it("shows retired figures as evidence only and classifies the period from fresh figures", async () => {
    api.getPeriod.mockResolvedValue({
      ...anchorPeriod,
      sizeClassification: {
        ...anchor,
        inputBasis: "RetiredHistoricAnchor",
        decisionInputFingerprintSha256: undefined,
        calculatedClass: undefined,
        rawCurrentClass: undefined,
        historicRetiredBy: "Example Owner",
        historicRetiredAt: "2026-10-12T09:00:00Z",
        historicRetiredReason: reason,
      },
    });
    api.getClassificationPrerequisites.mockResolvedValue({
      ready: true,
      isFirstYear: false,
      firstBlockingPeriod: null,
      message: "Every earlier period has a current classification, so this period can be classified.",
      historicFiguresPeriod: null,
      canRecordHistoricFigures: false,
    });
    historic.getArchivedHistoricFigures.mockResolvedValue([archivedRecord("Retired")]);
    await renderPage("8");

    expect(await screen.findByText("The historic figures recorded for this period were retired. Enter this period's own figures to classify it.")).toBeInTheDocument();
    expect(screen.getByText("Earlier historic figures (retired or replaced)")).toBeInTheDocument();
    expect(screen.getByText(/Retired by Example Owner/)).toBeInTheDocument();
    expect(screen.getByText(`Reason: ${reason}`)).toBeInTheDocument();
    expect(screen.queryByText("Historic figures (from filed accounts)")).not.toBeInTheDocument();
    expect(screen.queryByText(/saved but not yet classified/)).not.toBeInTheDocument();
    // The form is not pre-filled from the retired figures.
    expect(screen.getByLabelText("Turnover")).toHaveValue(null);
    expect(screen.getByRole("button", { name: /Classify Company/ })).toBeInTheDocument();
  });

  it("keeps the archived evidence visible after the period is classified again, with its PDF", async () => {
    const user = userEvent.setup();
    api.fetchDocumentBlob.mockResolvedValue(new Blob(["%PDF-1.4"], { type: "application/pdf" }));
    const createObjectURL = vi.fn(() => "blob:archived");
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() });
    api.getPeriod.mockResolvedValue({
      ...anchorPeriod,
      sizeClassification: {
        id: 5, turnover: 20000000, balanceSheetTotal: 10000000, avgEmployees: 60, inputBasis: "Computed",
        decisionInputFingerprintSha256: "b".repeat(64), calculatedClass: "Medium", rawCurrentClass: "Medium",
        periodLengthInYears: 1, annualisedTurnover: 20000000,
        historicRetiredBy: "Example Owner", historicRetiredAt: "2026-10-12T09:00:00Z", historicRetiredReason: reason,
      },
    });
    historic.getArchivedHistoricFigures.mockResolvedValue([archivedRecord("Replaced"), archivedRecord("Retired")]);
    await renderPage("8");

    expect(await screen.findByText("Earlier historic figures (retired or replaced)")).toBeInTheDocument();
    expect(historic.getArchivedHistoricFigures).toHaveBeenCalledWith(1, 8);
    expect(screen.getByText(/Replaced by new figures recorded by Example Reviewer/)).toBeInTheDocument();
    expect(screen.getByText(/Retired by Example Owner/)).toBeInTheDocument();
    const downloads = screen.getAllByRole("button", { name: /Download the PDF of the/ });
    expect(downloads).toHaveLength(2);
    await user.click(downloads[1]);
    await waitFor(() => expect(createObjectURL).toHaveBeenCalledTimes(1));
    expect(api.fetchDocumentBlob).toHaveBeenCalledWith("/api/companies/1/periods/8/classification/historic-anchor/archive/31/evidence");
  });

  it("does not load the archive for users without internal working-paper access", async () => {
    auth.canReadInternalWorkingPapers = false;
    auth.isOwner = false;
    await renderPage("8");

    expect(await screen.findByText("Historic figures (from filed accounts)")).toBeInTheDocument();
    expect(historic.getArchivedHistoricFigures).not.toHaveBeenCalled();
    expect(screen.queryByText("Earlier historic figures (retired or replaced)")).not.toBeInTheDocument();
  });

  function archivedRecord(event: "Retired" | "Replaced") {
    return {
      id: event === "Retired" ? 31 : 32,
      event,
      archivedAt: "2026-10-12T09:00:00Z",
      archivedBy: event === "Retired" ? "Example Owner" : "Example Reviewer",
      turnover: 1000000,
      balanceSheetTotal: 500000,
      avgEmployees: 0,
      sourceReference: "Fictitious members' accounts, year ended 31 December 2024",
      evidenceSha256: "c".repeat(64),
      attestedBy: "Firm Reviewer",
      attestedRole: "Reviewer",
      attestedAt: "2026-10-01T09:00:00Z",
      attestedEffectiveClass: null,
      retiredAt: event === "Retired" ? "2026-10-12T09:00:00Z" : null,
      retiredBy: event === "Retired" ? "Example Owner" : null,
      retiredReason: event === "Retired" ? reason : null,
    };
  }
});
