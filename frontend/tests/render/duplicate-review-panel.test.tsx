import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { DuplicateReviewPanel } from "@/components/period/DuplicateReviewPanel";
import { installFetchMock, setCsrfCookie } from "./harness";

describe("DuplicateReviewPanel", () => {
  it("shows retained source comparisons and records an explicit keep-both decision", async () => {
    setCsrfCookie("duplicate-review-csrf");
    let resolved = false;
    const fetchMock = installFetchMock((request) => {
      if (request.method === "POST") {
        resolved = true;
        return { body: candidate("Retained") };
      }
      if (request.url.includes("duplicate-review")) {
        const item = candidate(resolved ? "Retained" : "Pending");
        return {
          body: {
            pendingCount: resolved ? 0 : 1,
            retainedCount: resolved ? 1 : 0,
            discardedCount: 0,
            total: 1,
            page: 1,
            pageSize: 50,
            totalPages: 1,
            ...emptyBatchPage,
            items: [item],
          },
        };
      }
      return undefined;
    });
    const user = userEvent.setup();

    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite />);

    await screen.findByText("Incoming retained row");
    expect(screen.getByText(/provisionally included and block finalisation/i)).toBeInTheDocument();
    expect(screen.getByText("Possible match #40")).toBeInTheDocument();
    expect(screen.getByText(/Same date, amount \+ description/)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Reviewer decision reason for transaction 41/i), "Bank statement confirms two genuine separate card payments.");
    fireEvent.click(screen.getByRole("button", { name: /Retain incoming transaction 41/i }));

    await waitFor(() => expect(fetchMock.byMethod("POST")).toHaveLength(1));
    const request = fetchMock.one("POST", "/transactions/41/duplicate-review");
    expect(request.body).toEqual({
      decision: "Retained",
      reason: "Bank statement confirms two genuine separate card payments.",
      expectedStatus: "Pending",
      expectedDecisionVersion: 0,
    });
    expect(request.csrf).toBe("duplicate-review-csrf");
    await screen.findByText("Explicitly retained");
  });

  it("keeps destructive decisions unavailable to a read-only reviewer", async () => {
    installFetchMock((request) => request.url.includes("duplicate-review") ? {
      body: { pendingCount: 1, retainedCount: 0, discardedCount: 0, total: 1, page: 1, pageSize: 50, totalPages: 1, ...emptyBatchPage, items: [candidate("Pending")] },
    } : undefined);

    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite={false} />);

    await screen.findByText("Incoming retained row");
    expect(screen.queryByRole("button", { name: /Discard transaction/i })).toBeNull();
    expect(screen.queryByLabelText(/Reviewer decision reason for transaction/i)).toBeNull();
  });

  it("retains the versioned audit evidence when a resolved decision is reopened", async () => {
    installFetchMock((request) => request.url.includes("duplicate-review") ? {
      body: { pendingCount: 1, retainedCount: 0, discardedCount: 0, total: 1, page: 1, pageSize: 50, totalPages: 1, ...emptyBatchPage, items: [candidate("Pending", 2)] },
    } : undefined);

    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite />);

    expect(await screen.findByText("Decision v2 by Qualified Reviewer")).toBeInTheDocument();
    expect(screen.getByText(/Reopened after comparing the retained statement evidence/i)).toBeInTheDocument();
  });

  it("requires a clear confirmation before discarding a retained row from accounting", async () => {
    setCsrfCookie("duplicate-review-csrf");
    let discarded = false;
    const fetchMock = installFetchMock((request) => {
      if (request.method === "POST") {
        discarded = true;
        return { body: candidate("Discarded") };
      }
      return request.url.includes("duplicate-review") ? {
        body: {
          pendingCount: discarded ? 0 : 1,
          retainedCount: 0,
          discardedCount: discarded ? 1 : 0,
          total: 1,
          page: 1,
          pageSize: 50,
          totalPages: 1,
          ...emptyBatchPage,
          items: [candidate(discarded ? "Discarded" : "Pending")],
        },
      } : undefined;
    });
    const user = userEvent.setup();
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite />);

    await user.type(await screen.findByLabelText(/Reviewer decision reason for transaction 41/i), "Exact source hash proves this row came from a re-imported statement.");
    await user.click(screen.getByRole("button", { name: /Discard transaction 41/i }));
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
    const dialog = screen.getByRole("alertdialog", { name: "Discard this row from the ledger?" });
    expect(within(dialog).getByText(/source row and its audit evidence will remain retained/i)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Discard from ledger" }));

    await waitFor(() => expect(fetchMock.byMethod("POST")).toHaveLength(1));
    expect(fetchMock.one("POST", "/transactions/41/duplicate-review").body).toEqual({
      decision: "Discarded",
      reason: "Exact source hash proves this row came from a re-imported statement.",
      expectedStatus: "Pending",
      expectedDecisionVersion: 0,
    });
  });

  it("paginates a large duplicate queue without rendering every retained candidate", async () => {
    const fetchMock = installFetchMock((request) => request.url.includes("duplicate-review") ? {
      body: {
        pendingCount: 51,
        retainedCount: 0,
        discardedCount: 0,
        total: 51,
        page: request.url.includes("page=2") ? 2 : 1,
        pageSize: 50,
        totalPages: 2,
        ...emptyBatchPage,
        items: [candidate("Pending")],
      },
    } : undefined);
    const user = userEvent.setup();
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite={false} />);

    expect(await screen.findByText("Page 1 of 2 · 51 candidates")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Next" }));
    expect(await screen.findByText("Page 2 of 2 · 51 candidates")).toBeInTheDocument();
    expect(fetchMock.requests.some((request) => request.url.includes("duplicate-review?page=2&pageSize=50"))).toBe(true);
  });

  it("labels a legacy locked exclusion without implying that signed figures changed", async () => {
    const legacy = {
      ...candidate("Pending"),
      importBatchId: null,
      sourceRowNumber: null,
      sourceRowSha256: null,
      status: "LegacyLockedUnverified",
      includedInLedger: false,
      candidateKind: "LegacyUnverified",
      confidence: 0,
      reasons: ["Legacy duplicate flag had no retained reviewer evidence."],
    };
    installFetchMock((request) => request.url.includes("duplicate-review") ? {
      body: { pendingCount: 1, retainedCount: 0, discardedCount: 0, total: 1, page: 1, pageSize: 50, totalPages: 1, ...emptyBatchPage, items: [legacy] },
    } : undefined);
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite={false} />);

    expect(await screen.findByText("Legacy locked review")).toBeInTheDocument();
    expect(screen.getByText("Excluded from ledger")).toBeInTheDocument();
    expect(screen.getByText(/preserved only to avoid changing locked accounts/i)).toBeInTheDocument();
  });

  it("reloads stale reviewer state after a concurrency conflict", async () => {
    setCsrfCookie("duplicate-review-csrf");
    let changed = false;
    const fetchMock = installFetchMock((request) => {
      if (request.method === "POST") {
        changed = true;
        return { status: 409, body: { error: "Decision changed", reloadRequired: true } };
      }
      return request.url.includes("duplicate-review") ? {
        body: {
          pendingCount: changed ? 0 : 1,
          retainedCount: changed ? 1 : 0,
          discardedCount: 0,
          total: 1,
          page: 1,
          pageSize: 50,
          totalPages: 1,
          ...emptyBatchPage,
          items: [candidate(changed ? "Retained" : "Pending")],
        },
      } : undefined;
    });
    const user = userEvent.setup();
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite />);

    await user.type(await screen.findByLabelText(/Reviewer decision reason for transaction 41/i), "The browser loaded an older duplicate decision version.");
    await user.click(screen.getByRole("button", { name: /Retain incoming transaction 41/i }));

    await screen.findByText("Decision v1 by Qualified Reviewer");
    expect(fetchMock.byMethod("GET").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText(/queue was reloaded/i)).toBeInTheDocument();
  });

  it("keeps a locked period read-only and explains how to continue", async () => {
    installFetchMock((request) => request.url.includes("duplicate-review") ? {
      body: { pendingCount: 1, retainedCount: 0, discardedCount: 0, total: 1, page: 1, pageSize: 50, totalPages: 1, ...emptyBatchPage, items: [candidate("Pending")] },
    } : undefined);
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite periodLocked />);

    expect(await screen.findByText(/This period is locked/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Reviewer decision reason for transaction/i)).toBeNull();
    expect(screen.queryByRole("button", { name: /Discard transaction/i })).toBeNull();
  });

  it("formats statement evidence in the bank-account currency", async () => {
    const sterling = { ...candidate("Pending"), currency: "GBP", matchedCurrency: "GBP" };
    installFetchMock((request) => request.url.includes("duplicate-review") ? {
      body: { pendingCount: 1, retainedCount: 0, discardedCount: 0, total: 1, page: 1, pageSize: 50, totalPages: 1, ...emptyBatchPage, items: [sterling] },
    } : undefined);
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite={false} />);

    await screen.findByText(/Current account \(GBP\)/);
    expect(screen.getAllByText((content) => content.includes("£12.34")).length).toBeGreaterThan(0);
  });

  it("records a confirmed atomic exact re-import batch decision", async () => {
    setCsrfCookie("duplicate-review-csrf");
    let resolved = false;
    const batch = exactBatch("Pending");
    const fetchMock = installFetchMock((request) => {
      if (request.method === "POST") {
        resolved = true;
        return { body: { importBatchId: 22, decision: "Discarded", updatedCount: 2, rowEvidenceSha256: "d".repeat(64) } };
      }
      return request.url.includes("duplicate-review") ? {
        body: {
          pendingCount: 0,
          retainedCount: 0,
          discardedCount: 0,
          total: 0,
          page: 1,
          pageSize: 50,
          totalPages: 1,
          exactReimportBatchTotal: 1,
          exactReimportBatchPage: 1,
          exactReimportBatchPageSize: 10,
          exactReimportBatchTotalPages: 1,
          exactReimportBatches: [resolved ? exactBatch("Discarded") : batch],
          items: [],
        },
      } : undefined;
    });
    const user = userEvent.setup();
    render(<DuplicateReviewPanel companyId={7} periodId={3} canWrite />);

    await user.type(await screen.findByLabelText(/Batch decision reason for batch 22/i), "The byte-identical statement was imported twice by mistake.");
    await user.click(screen.getByRole("button", { name: /Discard all 2 rows/i }));
    const dialog = screen.getByRole("alertdialog", { name: "Discard this exact re-import batch?" });
    await user.click(within(dialog).getByRole("button", { name: "Discard batch rows" }));

    await waitFor(() => expect(fetchMock.byMethod("POST")).toHaveLength(1));
    expect(fetchMock.one("POST", "/duplicate-review/batches/22").body).toEqual({
      decision: "Discarded",
      reason: "The byte-identical statement was imported twice by mistake.",
      expectedStatus: "Pending",
      expectedCandidateCount: 2,
      expectedDecisionToken: "c".repeat(64),
    });
    await screen.findByText("Discarded");
  });
});

function candidate(status: "Pending" | "Retained" | "Discarded", version = status === "Pending" ? 0 : 1) {
  const hasDecisionEvidence = version > 0;
  return {
    transactionId: 41,
    bankAccountId: 8,
    bankAccountName: "Current account",
    currency: "EUR",
    importBatchId: 9,
    sourceFilename: "january-repeat.csv",
    sourceFileSha256: "f".repeat(64),
    sourceImportedAtUtc: "2026-07-10T09:00:00Z",
    sourceRowNumber: 2,
    sourceRowSha256: "a".repeat(64),
    date: "2026-01-05",
    description: "Card Shop",
    amount: -12.34,
    balance: 987.66,
    reference: "REF-2",
    status,
    includedInLedger: status !== "Discarded",
    candidateKind: "SameDateAmountDescription",
    confidence: 0.55,
    reasons: ["Date, amount and normalised description match a retained transaction."],
    matchedTransactionId: 40,
    matchedBankAccountId: 8,
    matchedBankAccountName: "Current account",
    matchedCurrency: "EUR",
    matchedImportBatchId: 8,
    matchedSourceFilename: "january.csv",
    matchedSourceFileSha256: "e".repeat(64),
    matchedSourceImportedAtUtc: "2026-07-09T09:00:00Z",
    matchedSourceRowNumber: 2,
    matchedSourceRowSha256: "b".repeat(64),
    matchedDate: "2026-01-05",
    matchedDescription: "Card Shop",
    matchedAmount: -12.34,
    matchedBalance: 1000,
    matchedReference: "REF-1",
    decidedByDisplayName: hasDecisionEvidence ? "Qualified Reviewer" : null,
    decidedAtUtc: hasDecisionEvidence ? "2026-07-10T10:00:00Z" : null,
    decisionReason: hasDecisionEvidence
      ? status === "Pending"
        ? "Reopened after comparing the retained statement evidence."
        : "Bank statement confirms two genuine separate card payments."
      : null,
    decisionVersion: version,
    batchDecisionAvailable: false,
  };
}

const emptyBatchPage = {
  exactReimportBatchTotal: 0,
  exactReimportBatchPage: 1,
  exactReimportBatchPageSize: 10,
  exactReimportBatchTotalPages: 1,
  exactReimportBatches: [],
};

function exactBatch(currentStatus: "Pending" | "Retained" | "Discarded") {
  return {
    importBatchId: 22,
    bankAccountId: 8,
    bankAccountName: "Current account",
    currency: "EUR",
    sourceFilename: "january-copy.csv",
    sourceFileSha256: "f".repeat(64),
    importedAtUtc: "2026-07-10T09:00:00Z",
    currentStatus,
    candidateCount: 2,
    decisionToken: "c".repeat(64),
  };
}
