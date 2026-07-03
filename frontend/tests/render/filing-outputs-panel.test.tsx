import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FilingOutputsPanel } from "@/components/period/FilingOutputsPanel";

describe("FilingOutputsPanel", () => {
  it("surfaces filing outputs, checklist progress and regime-gated CRO downloads", () => {
    render(
      <FilingOutputsPanel
        filingRegimeReady={false}
        downloadingDocument={null}
        checklist={{
          transactionsCategorised: true,
          adjustmentsReviewed: true,
          balanceSheetBalances: false,
          filingReadinessComplete: false,
          accountsPdfGenerated: true,
          croPackAndSignatureGenerated: false,
        }}
        onDownloadAgmPack={vi.fn()}
        onDownloadCroFilingPack={vi.fn()}
        onDownloadSignaturePage={vi.fn()}
        onDownloadIxbrl={vi.fn()}
      />,
    );

    expect(screen.getByText("Filing outputs")).toBeInTheDocument();
    expect(screen.getByText("3 of 6 checklist items complete")).toBeInTheDocument();
    expect(screen.getByText("AGM Pack")).toBeInTheDocument();
    expect(screen.getByText("CRO Filing Pack")).toBeInTheDocument();
    expect(screen.getByText("Signature Page")).toBeInTheDocument();
    expect(screen.getByText("iXBRL Filing")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download CRO PDF" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Download Signature PDF" })).toBeDisabled();
    expect(screen.getAllByText("Filing regime required")).toHaveLength(2);

    const balanceSheetRow = screen.getByText("Balance sheet balances").closest("li");
    expect(balanceSheetRow).not.toBeNull();
    expect(within(balanceSheetRow as HTMLElement).getByText("Open")).toBeInTheDocument();

    const accountsPdfRow = screen.getByText("CRO accounts PDF generated").closest("li");
    expect(accountsPdfRow).not.toBeNull();
    expect(within(accountsPdfRow as HTMLElement).getByText("Complete")).toBeInTheDocument();
  });

  it("calls the selected download action when an output is available", async () => {
    const user = userEvent.setup();
    const onDownloadCroFilingPack = vi.fn();
    const onDownloadIxbrl = vi.fn();

    render(
      <FilingOutputsPanel
        filingRegimeReady
        downloadingDocument={null}
        checklist={completeChecklist()}
        onDownloadAgmPack={vi.fn()}
        onDownloadCroFilingPack={onDownloadCroFilingPack}
        onDownloadSignaturePage={vi.fn()}
        onDownloadIxbrl={onDownloadIxbrl}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Download CRO PDF" }));
    await user.click(screen.getByRole("button", { name: "Download iXBRL" }));

    expect(onDownloadCroFilingPack).toHaveBeenCalledTimes(1);
    expect(onDownloadIxbrl).toHaveBeenCalledTimes(1);
  });
});

function completeChecklist() {
  return {
    transactionsCategorised: true,
    adjustmentsReviewed: true,
    balanceSheetBalances: true,
    filingReadinessComplete: true,
    accountsPdfGenerated: true,
    croPackAndSignatureGenerated: true,
  };
}
