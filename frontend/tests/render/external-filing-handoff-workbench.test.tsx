import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { ExternalFilingHandoffWorkbench } from "@/components/period/ExternalFilingHandoffWorkbench";
import { externalFilingHandoffWorkspaceFixture } from "../fixtures/external-filing-handoff";

describe("ExternalFilingHandoffWorkbench", () => {
  it("renders authority, exact hashes, field rows, correction history and no submission action", async () => {
    const user = userEvent.setup();
    const prepare = vi.fn();
    const amend = vi.fn();
    const recordOutcome = vi.fn();
    render(
      <ExternalFilingHandoffWorkbench
        workspace={externalFilingHandoffWorkspaceFixture}
        canPrepare
        canRecordExternalOutcome
        onPrepareSnapshot={prepare}
        onAmendSnapshot={amend}
        onRecordExternalOutcome={recordOutcome}
      />,
    );

    expect(screen.getByText(/External handoff only — no CRO or ROS submission/)).toBeInTheDocument();
    expect(screen.getByText("CRO B1 manual handoff")).toBeInTheDocument();
    expect(screen.getByText("Revenue CT1 support handoff")).toBeInTheDocument();
    expect(screen.getAllByText("Authority active")).toHaveLength(2);
    expect(screen.getAllByText("TAIN-****42").length).toBeGreaterThan(0);
    expect(screen.getByText("Protected entry not retained")).toBeInTheDocument();
    expect(screen.getByText("Corrected shareholder holding after CRO send-back.")).toBeInTheDocument();
    expect(screen.getByText("Correction required")).toBeInTheDocument();
    expect(screen.getByText(/Correction deadline: 2026-07-24 08:00:00 UTC/)).toBeInTheDocument();
    expect(screen.getAllByText(/not a CT1 return/).length).toBeGreaterThan(0);
    expect(screen.queryByText("1234567A")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /submit|send to cro|send to revenue|file with/i })).not.toBeInTheDocument();

    const cards = screen.getAllByText("Generate immutable snapshot");
    await user.click(cards[0]);
    expect(prepare).toHaveBeenCalledWith("CroB1");

    const amendmentButtons = screen.getAllByRole("button", { name: "Create linked amendment" });
    await user.click(amendmentButtons[0]);
    expect(amend).toHaveBeenCalledWith(expect.objectContaining({ document: expect.objectContaining({ version: 2 }) }));

    const croCard = screen.getByText("CRO B1 manual handoff").closest("section");
    expect(croCard).not.toBeNull();
    const outcomeButton = within(croCard as HTMLElement).getByRole("button", { name: "Record external outcome" });
    await user.click(outcomeButton);
    expect(recordOutcome).toHaveBeenCalledOnce();
  });

  it("keeps preparation controls unavailable for evidence-only viewers", () => {
    render(
      <ExternalFilingHandoffWorkbench
        workspace={externalFilingHandoffWorkspaceFixture}
        canPrepare={false}
        canRecordExternalOutcome={false}
      />,
    );

    expect(screen.getAllByText("Preparation permission required")).toHaveLength(2);
    expect(screen.queryByRole("button", { name: "Generate immutable snapshot" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create linked amendment" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Record external outcome" })).not.toBeInTheDocument();
  });
});
