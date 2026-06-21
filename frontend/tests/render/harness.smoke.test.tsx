// Smoke test for the render harness itself (frontend-render-harness).
//
// Proves the three things every money-entry test below relies on:
//   1. @testing-library/react renders a component into jsdom and queries resolve.
//   2. A HeroUI v3 Button (React Aria `usePress`) fires `onPress` under user-event in jsdom.
//   3. The real `@/lib/api` client issues a fetch with the exact path / method / JSON body and attaches
//      the `X-CSRF-Token` double-submit header read from `document.cookie`.
import { Button } from "@heroui/react";
import userEvent from "@testing-library/user-event";
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createShareCapital } from "@/lib/api";
import { clearCookies, installFetchMock, setCsrfCookie } from "./harness";

afterEach(() => {
  clearCookies();
});

describe("render harness", () => {
  it("renders a component into jsdom", () => {
    render(<p>year-end harness online</p>);
    expect(screen.getByText("year-end harness online")).toBeInTheDocument();
  });

  it("fires a HeroUI Button onPress under user-event", async () => {
    const onPress = vi.fn();
    render(
      <Button onPress={onPress}>
        Add
      </Button>,
    );

    await userEvent.click(screen.getByRole("button", { name: "Add" }));

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it("drives the real api client: POST path, payload and CSRF header", async () => {
    const fetchMock = installFetchMock(() => ({ status: 201, body: { id: 1 } }));
    setCsrfCookie("csrf-smoke-token");

    await createShareCapital(7, {
      shareClass: "Ordinary",
      nominalValue: 1,
      numberIssued: 100,
      totalValue: 100,
      isFullyPaid: true,
      issueDate: "2025-01-01",
    });

    const request = fetchMock.one("POST", "/api/companies/7/share-capital");
    expect(request.csrf).toBe("csrf-smoke-token");
    expect(request.headers.get("Content-Type")).toBe("application/json");
    expect(request.body).toMatchObject({
      shareClass: "Ordinary",
      numberIssued: 100,
      nominalValue: 1,
    });
  });
});
