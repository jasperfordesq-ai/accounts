import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import NewCompanyPage from "@/app/companies/new/page";
import { onboardCompany, type CompanyOnboardingInput } from "@/lib/api";
import { clearCookies, setCsrfCookie } from "./harness";

const mocks = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
  success: vi.fn(),
  error: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mocks.push, replace: mocks.replace }),
}));

vi.mock("@/components/AuthProvider", () => ({
  useAuth: () => ({ isOwner: true, loading: false }),
}));

vi.mock("sonner", () => ({
  toast: { success: mocks.success, error: mocks.error },
}));

describe("atomic company onboarding", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useRealTimers();
    clearCookies();
    setCsrfCookie("onboarding-csrf");
    global.fetch = vi.fn() as unknown as typeof fetch;
  });

  it("blocks a missing incorporation date with an accessible inline error", async () => {
    const user = userEvent.setup();
    render(<NewCompanyPage />);

    await user.type(screen.getByLabelText("Legal Name *"), "Missing Date Limited");
    await user.click(screen.getByRole("button", { name: "Next" }));

    const error = await screen.findByRole("alert");
    expect(error).toHaveTextContent("Incorporation date is required");
    expect(screen.getAllByText("Legal Details").length).toBeGreaterThan(0);
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("advances the onboarding journey with keyboard-only activation", async () => {
    const user = userEvent.setup();
    render(<NewCompanyPage />);

    await user.type(screen.getByLabelText("Legal Name *"), "Keyboard Journey Limited");
    fireEvent.change(screen.getByLabelText("Incorporation Date *"), {
      target: { value: "2025-01-01" },
    });

    const next = screen.getByRole("button", { name: "Next" });
    next.focus();
    await user.keyboard("{Enter}");

    expect(screen.getByRole("heading", { name: "Trading Status" })).toBeInTheDocument();
    const trading = screen.getByRole("checkbox", { name: "Currently Trading" });
    expect(trading).toBeChecked();
    trading.focus();
    await user.keyboard(" ");
    expect(trading).not.toBeChecked();
  });

  it("issues one idempotent aggregate request and reports success only after its atomic response", async () => {
    const user = userEvent.setup();
    let completeRequest: ((response: Response) => void) | undefined;
    const pendingResponse = new Promise<Response>((resolve) => {
      completeRequest = resolve;
    });
    const fetchMock = vi.fn(() => pendingResponse);
    global.fetch = fetchMock as unknown as typeof fetch;

    render(<NewCompanyPage />);
    await user.type(screen.getByLabelText("Legal Name *"), "Atomic UI Limited");
    fireEvent.change(screen.getByLabelText("Incorporation Date *"), {
      target: { value: "2025-01-01" },
    });
    await user.click(screen.getByRole("button", { name: "Next" }));
    await user.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByLabelText("First period start")).toHaveValue("2025-01-01");
    expect(screen.getByLabelText("Period End *")).toHaveValue("2025-12-31");
    fireEvent.change(screen.getByLabelText("Exact Annual Return Date from CRO CORE"), {
      target: { value: "2025-07-01" },
    });
    await user.type(screen.getByLabelText("Annual Return Date evidence reference"), "CRO-CORE-ARD-2025");
    await user.click(screen.getByRole("button", { name: "Next" }));
    await user.type(screen.getByLabelText("Name"), "Aisling Director");
    await user.click(screen.getByRole("button", { name: "Create Company" }));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(mocks.push).not.toHaveBeenCalled();
    expect(mocks.success).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Creating..." })).toBeDisabled();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/companies/onboard");
    expect(init.method).toBe("POST");
    const headers = new Headers(init.headers);
    expect(headers.get("Idempotency-Key")).toMatch(/^[A-Za-z0-9._:-]{8,128}$/);
    expect(headers.get("X-CSRF-Token")).toBe("onboarding-csrf");
    const payload = JSON.parse(String(init.body));
    expect(payload.company.incorporationDate).toBe("2025-01-01");
    expect(payload.company).toMatchObject({
      annualReturnDate: "2025-07-01",
      annualReturnDateEffectiveFrom: "2025-07-01",
      annualReturnDateSource: "CroRecord",
      annualReturnDateEvidenceReference: "CRO-CORE-ARD-2025",
    });
    expect(payload.firstPeriod).toMatchObject({
      periodStart: "2025-01-01",
      periodEnd: "2025-12-31",
      isFirstYear: true,
    });
    expect(payload.openingBankAccount).toMatchObject({
      name: "Main Current Account",
      currency: "EUR",
      openingBalance: 0,
    });
    expect(payload.officers).toEqual([{ name: "Aisling Director", role: "Director" }]);

    completeRequest?.({
      ok: true,
      status: 201,
      statusText: "Created",
      headers: new Headers(),
      json: async () => ({
        companyId: 41,
        companyLegalName: "Atomic UI Limited",
        firstPeriodId: 42,
        firstPeriodStart: "2025-01-01",
        firstPeriodEnd: "2025-12-31",
        openingBankAccountId: 43,
        openingBankAccountName: "Main Current Account",
        categoryCount: 52,
        officers: [{ id: 44, name: "Aisling Director", role: "Director" }],
      }),
      text: async () => "",
    } as Response);

    await waitFor(() => expect(mocks.push).toHaveBeenCalledWith("/companies/41"));
    expect(mocks.success).toHaveBeenCalledWith(
      "Atomic UI Limited and its opening records were created successfully",
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("retries a network failure with the identical idempotency key", async () => {
    vi.useFakeTimers();
    const outcome = {
      companyId: 51,
      companyLegalName: "Retry Limited",
      firstPeriodId: 52,
      firstPeriodStart: "2025-01-01",
      firstPeriodEnd: "2025-12-31",
      openingBankAccountId: 53,
      openingBankAccountName: "Main Current Account",
      categoryCount: 52,
      officers: [{ id: 54, name: "Retry Director", role: "Director" }],
    };
    const fetchMock = vi.fn()
      .mockRejectedValueOnce(new TypeError("fetch failed"))
      .mockResolvedValue({
        ok: true,
        status: 200,
        statusText: "OK",
        headers: new Headers(),
        json: async () => outcome,
        text: async () => "",
      } as Response);
    global.fetch = fetchMock as unknown as typeof fetch;
    const input: CompanyOnboardingInput = {
      company: { legalName: "Retry Limited", incorporationDate: "2025-01-01" },
      officers: [{ name: "Retry Director", role: "Director" }],
      firstPeriod: {
        periodStart: "2025-01-01",
        periodEnd: "2025-12-31",
        isFirstYear: true,
        memberAuditNoticeReceived: false,
        goingConcernConfirmed: true,
      },
      openingBankAccount: {
        name: "Main Current Account",
        currency: "EUR",
        openingBalance: 0,
      },
    };

    const request = onboardCompany(input, "retry-identical-key-0001");
    await vi.runAllTimersAsync();
    await expect(request).resolves.toEqual(outcome);

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const keys = fetchMock.mock.calls.map((call) => new Headers(call[1]?.headers).get("Idempotency-Key"));
    expect(keys).toEqual(["retry-identical-key-0001", "retry-identical-key-0001"]);
    vi.useRealTimers();
  });
});
