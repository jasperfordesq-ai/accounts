import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { accessDeniedRedirect, OAuthAuthorizeWorkbench } from "@/components/connector/OAuthAuthorizeWorkbench";
import { clearCookies, installFetchMock, type RecordedRequest, type RouteResponse } from "./harness";

const authorizeQuery = new URLSearchParams({
  client_id: "fbcl1.client",
  redirect_uri: "https://claude.ai/api/mcp/auth_callback",
  response_type: "code",
  code_challenge: "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
  code_challenge_method: "S256",
  scope: "fb:read",
  resource: "http://localhost:3000/mcp",
  state: "state-123",
});

const description = {
  clientName: "Claude",
  redirectUri: "https://claude.ai/api/mcp/auth_callback",
  redirectHost: "claude.ai",
  requestedAccessLevel: "Read",
  requestedDataScope: "Withheld",
  resource: "http://localhost:3000/mcp",
};

function routes(overrides: { describe?: RouteResponse; authorize?: RouteResponse } = {}) {
  return (request: RecordedRequest): RouteResponse | undefined => {
    if (request.url.startsWith("/api/oauth/authorize/describe")) return overrides.describe ?? { body: description };
    if (request.url === "/api/oauth/authorize") return overrides.authorize ?? { body: { redirectTo: "https://claude.ai/api/mcp/auth_callback?code=fbac1.1.abc&state=state-123&iss=http%3A%2F%2Flocalhost%3A3000" } };
    return { status: 404, body: { error: "unexpected" } };
  };
}

describe("OAuth consent page", () => {
  beforeEach(() => {
    clearCookies();
    window.history.replaceState({}, "", `/oauth/authorize?${authorizeQuery.toString()}`);
  });

  afterEach(() => {
    window.history.replaceState({}, "", "/");
  });

  it("shows an error card and never navigates when describe rejects the request", async () => {
    const fetchMock = installFetchMock(routes({
      describe: { status: 400, body: { error: "invalid_request", error_description: "The redirect address does not match the application's registration.", code: "INVALID_CLIENT" } },
    }));
    const navigate = vi.fn();
    render(<OAuthAuthorizeWorkbench navigate={navigate} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("The redirect address does not match the application's registration.");
    expect(screen.getByText("Connection request not accepted")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Deny" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Approve/ })).not.toBeInTheDocument();
    expect(navigate).not.toHaveBeenCalled();
    const describeRequest = fetchMock.one("GET", "/api/oauth/authorize/describe");
    expect(new URLSearchParams(describeRequest.url.split("?")[1]).get("client_id")).toBe("fbcl1.client");
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });

  it("lets the server refuse a link without client details and explains it plainly", async () => {
    window.history.replaceState({}, "", "/oauth/authorize");
    const fetchMock = installFetchMock(routes({ describe: { status: 400, body: { error: "invalid_request", error_description: "client_id is required." } } }));
    const navigate = vi.fn();
    render(<OAuthAuthorizeWorkbench navigate={navigate} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/missing the application details/);
    expect(fetchMock.requests.some((request) => request.url.includes("/api/oauth/authorize/describe"))).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it("shows the client and redirect host, pre-fills the requested access and navigates to redirectTo on approval", async () => {
    const user = userEvent.setup();
    const fetchMock = installFetchMock(routes());
    const navigate = vi.fn();
    render(<OAuthAuthorizeWorkbench navigate={navigate} />);

    expect(await screen.findByText("Connect Claude to FilingBridge")).toBeInTheDocument();
    expect(screen.getByTestId("oauth-redirect-host")).toHaveTextContent("claude.ai");
    expect(screen.getByText(/will send an access grant for your account to this address/)).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /^Read/ })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^Withheld/ })).toBeChecked();

    await user.clear(screen.getByLabelText("Workspace slug"));
    await user.type(screen.getByLabelText("Workspace slug"), "Example-Firm");
    await user.type(screen.getByLabelText("Email"), "owner@example.ie");
    await user.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await user.type(screen.getByLabelText("Authenticator code"), "123456");
    await user.click(screen.getByRole("radio", { name: /^Write/ }));
    await user.click(screen.getByRole("button", { name: "Approve" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith(
      "https://claude.ai/api/mcp/auth_callback?code=fbac1.1.abc&state=state-123&iss=http%3A%2F%2Flocalhost%3A3000",
    ));
    const post = fetchMock.one("POST", "/api/oauth/authorize");
    expect(post.body).toEqual({
      clientId: "fbcl1.client",
      redirectUri: "https://claude.ai/api/mcp/auth_callback",
      state: "state-123",
      codeChallenge: "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
      codeChallengeMethod: "S256",
      resource: "http://localhost:3000/mcp",
      tenantSlug: "example-firm",
      email: "owner@example.ie",
      password: "correct horse battery staple",
      totpCode: "123456",
      recoveryCode: null,
      accessLevel: "Write",
      dataScope: "Withheld",
    });
    expect(screen.getByRole("status")).toHaveTextContent("Returning you to Claude");
  });

  it("sends a recovery code instead of a TOTP code when toggled", async () => {
    const user = userEvent.setup();
    const fetchMock = installFetchMock(routes());
    render(<OAuthAuthorizeWorkbench navigate={vi.fn()} />);

    await screen.findByText("Connect Claude to FilingBridge");
    await user.type(screen.getByLabelText("Workspace slug"), "firm");
    await user.type(screen.getByLabelText("Email"), "owner@example.ie");
    await user.type(screen.getByLabelText("Password"), "pw");
    await user.click(screen.getByRole("button", { name: "Use a recovery code instead" }));
    expect(screen.queryByLabelText("Authenticator code")).not.toBeInTheDocument();
    await user.type(screen.getByLabelText("Recovery code"), "ABCD-EFGH-IJKL");
    expect(screen.getByRole("button", { name: "Use an authenticator code instead" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Approve" }));

    await waitFor(() => expect(fetchMock.byMethod("POST")).toHaveLength(1));
    const post = fetchMock.one("POST", "/api/oauth/authorize");
    expect(post.body).toMatchObject({ totpCode: null, recoveryCode: "ABCD-EFGH-IJKL" });
  });

  it("renders the server refusal message and stays on the page", async () => {
    const user = userEvent.setup();
    installFetchMock(routes({
      authorize: { status: 403, body: { error: "Full data scope requires the Owner role.", code: "DATA_SCOPE_FORBIDDEN" } },
    }));
    const navigate = vi.fn();
    render(<OAuthAuthorizeWorkbench navigate={navigate} />);

    await screen.findByText("Connect Claude to FilingBridge");
    await user.type(screen.getByLabelText("Workspace slug"), "firm");
    await user.type(screen.getByLabelText("Email"), "client@example.ie");
    await user.type(screen.getByLabelText("Password"), "pw");
    await user.click(screen.getByRole("radio", { name: /^Full/ }));
    await user.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Full data scope requires the Owner role.");
    expect(navigate).not.toHaveBeenCalled();
  });

  it("denies by redirecting to the validated redirect URI with access_denied, state and iss", async () => {
    const user = userEvent.setup();
    const fetchMock = installFetchMock(routes());
    const navigate = vi.fn();
    render(<OAuthAuthorizeWorkbench navigate={navigate} />);

    await screen.findByText("Connect Claude to FilingBridge");
    await user.click(screen.getByRole("button", { name: "Deny" }));

    expect(navigate).toHaveBeenCalledTimes(1);
    const target = new URL(navigate.mock.calls[0][0]);
    expect(`${target.origin}${target.pathname}`).toBe("https://claude.ai/api/mcp/auth_callback");
    expect(target.searchParams.get("error")).toBe("access_denied");
    expect(target.searchParams.get("state")).toBe("state-123");
    expect(target.searchParams.get("iss")).toBe(window.location.origin);
    expect(fetchMock.byMethod("POST")).toHaveLength(0);
  });

  it("builds the refusal redirect without a state when none was sent", () => {
    expect(accessDeniedRedirect("http://127.0.0.1:5555/callback?x=1", null, "https://fb.example"))
      .toBe("http://127.0.0.1:5555/callback?x=1&error=access_denied&iss=https%3A%2F%2Ffb.example");
  });
});
