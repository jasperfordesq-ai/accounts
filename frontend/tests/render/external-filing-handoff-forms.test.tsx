import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import {
  CroSnapshotForm,
  OutcomeEvidenceForm,
  RevenueSnapshotForm,
} from "@/components/period/ExternalFilingHandoffForms";
import { ExternalFilingHandoffRuntime } from "@/components/period/ExternalFilingHandoffRuntime";
import { externalFilingHandoffWorkspaceFixture } from "../fixtures/external-filing-handoff";

const api = vi.hoisted(() => ({
  getExternalFilingHandoffWorkspace: vi.fn(),
  recordExternalFilingAuthority: vi.fn(),
  revokeExternalFilingAuthority: vi.fn(),
  generateCroHandoffSnapshot: vi.fn(),
  generateRevenueHandoffSnapshot: vi.fn(),
  recordExternalFilingOutcome: vi.fn(),
  getExternalFilingHandoffArtifactUrl: vi.fn(() => "/artifact"),
  fetchDocumentBlob: vi.fn(),
}));

vi.mock("@/lib/api", () => api);

describe("external filing handoff command forms", () => {
  it("validates and emits the complete CRO officer/member/no-allotment payload without protected identifiers", async () => {
    const user = userEvent.setup();
    const save = vi.fn().mockResolvedValue(undefined);
    const { container } = render(
      <CroSnapshotForm
        workspace={externalFilingHandoffWorkspaceFixture}
        busy={false}
        onSave={save}
        onCancel={vi.fn()}
      />,
    );

    await user.type(container.querySelector("#politicalDonationsEvidenceReference")!, "POLITICAL-EVIDENCE-01");
    await user.type(container.querySelector("#officer-0-identity-ref")!, "CORE-IDENTITY-EVIDENCE-01");
    await user.type(container.querySelector("#officer-0-identity-sha")!, "a".repeat(64));
    await user.type(container.querySelector("#officer-0-directorships")!, "OTHER-DIRECTORSHIPS-01");
    await user.click(screen.getByRole("checkbox", { name: /protected PPSN\/IPN\/RBO step was completed/i }));
    await user.type(container.querySelector("#shareholder-0-ref")!, "MEMBER-REGISTER-01");
    await user.type(container.querySelector("#shareholder-0-name")!, "Fixture Member");
    await user.type(container.querySelector("#shareholder-0-address-line1")!, "1 Member Street");
    await user.type(container.querySelector("#shareholder-0-display")!, "100 Ordinary shares");
    await user.type(container.querySelector("#shareholder-0-evidence")!, "REGISTER-MEMBERS-01");
    await user.click(screen.getByRole("checkbox", { name: "I confirm there were no allotments" }));
    await user.click(screen.getByRole("button", { name: "Retain CRO snapshot" }));

    expect(save).toHaveBeenCalledOnce();
    expect(save).toHaveBeenCalledWith(expect.objectContaining({
      officers: [expect.objectContaining({
        officerId: 4,
        identityEvidenceReference: "CORE-IDENTITY-EVIDENCE-01",
        protectedIdentifierEntryConfirmed: true,
      })],
      shareholders: [expect.objectContaining({ memberReference: "MEMBER-REGISTER-01" })],
      allotments: [],
      noAllotmentsInReturnPeriodConfirmed: true,
      supersedesSnapshotId: null,
    }));
    expect(JSON.stringify(save.mock.calls[0][0])).not.toMatch(/1234567[A-Z]/);
  });

  it("adds explicit allotment rows and keeps an amendment bound to its exact predecessor", async () => {
    const user = userEvent.setup();
    const save = vi.fn();
    const predecessor = externalFilingHandoffWorkspaceFixture.snapshots[1];
    const { container } = render(
      <CroSnapshotForm workspace={externalFilingHandoffWorkspaceFixture} predecessor={predecessor} busy={false} onSave={save} onCancel={vi.fn()} />,
    );
    const addButtons = screen.getAllByRole("button", { name: "Add row" });
    await user.click(addButtons[2]);
    expect(container.querySelector("#allotment-0-allotmentReference")).toBeInTheDocument();
    expect(container.querySelector("#amendmentReason")).toBeRequired();
    expect(screen.getByText(/Linked CRO amendment after v2/)).toBeInTheDocument();
  });

  it("emits bounded Revenue support input and an internal readiness event without external evidence", async () => {
    const user = userEvent.setup();
    const revenueSave = vi.fn().mockResolvedValue(undefined);
    const { unmount } = render(
      <RevenueSnapshotForm workspace={externalFilingHandoffWorkspaceFixture} busy={false} onSave={revenueSave} onCancel={vi.fn()} />,
    );
    await user.click(screen.getByRole("checkbox", { name: /qualified reviewer checked every unsupported CT1 section/i }));
    await user.click(screen.getByRole("button", { name: "Retain Revenue support snapshot" }));
    expect(revenueSave).toHaveBeenCalledWith(expect.objectContaining({
      unsupportedSectionsReviewed: true,
      manualCt1CompletionItems: [],
      supersedesSnapshotId: null,
    }));
    unmount();

    const outcomeSave = vi.fn().mockResolvedValue(undefined);
    render(
      <OutcomeEvidenceForm
        snapshot={externalFilingHandoffWorkspaceFixture.snapshots[1]}
        workspace={externalFilingHandoffWorkspaceFixture}
        busy={false}
        onSave={outcomeSave}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByText(/will not claim an external reference/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Append chronology event" }));
    expect(outcomeSave).toHaveBeenCalledWith({
      outcome: "ReadyForManualHandoff",
      externalReference: null,
      externalOccurredAtUtc: null,
      reason: null,
      correctionDeadlineUtc: null,
      evidenceReference: null,
      evidenceArtifact: null,
      evidenceSha256: null,
      supersedingSnapshotId: null,
    });
  });

  it("loads the live workspace, exposes governed commands, and never renders a submission control", async () => {
    const user = userEvent.setup();
    api.getExternalFilingHandoffWorkspace.mockResolvedValue(externalFilingHandoffWorkspaceFixture);
    render(
      <ExternalFilingHandoffRuntime
        companyId={42}
        periodId={77}
        canRead
        canPrepare
        canReview
      />,
    );
    expect(await screen.findByRole("heading", { name: "CRO / Revenue handoff workspace" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Record / revoke authority" }));
    expect(screen.getByText("Presenter / ROS authority evidence")).toBeInTheDocument();
    expect(screen.getByText("Revoke current authority")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /submit to|send to cro|send to revenue|file now/i })).not.toBeInTheDocument();

    const authorityPanel = screen.getByText("Presenter / ROS authority evidence").closest("section");
    expect(authorityPanel).not.toBeNull();
    const maskedIdentifier = within(authorityPanel as HTMLElement).getByRole("textbox", { name: /Masked presenter \/ TAIN/i });
    expect(maskedIdentifier).toHaveAccessibleDescription(/Never enter an unmasked identifier/i);
  });
});
