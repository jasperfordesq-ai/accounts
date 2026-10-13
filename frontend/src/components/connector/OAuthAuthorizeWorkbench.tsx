"use client";

import { FormEvent, useEffect, useRef, useState, type ReactNode } from "react";
import { Button, Card, Input, Label, Spinner, TextField } from "@heroui/react";
import { AlertCircle, AlertTriangle, Bot, ShieldCheck } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  apiErrorDetail,
  describeOAuthAuthorization,
  submitOAuthAuthorization,
  type ConnectorAccessLevel,
  type ConnectorDataScope,
  type OAuthAuthorizeDescription,
} from "@/lib/connector";

const defaultTenantSlug = process.env.NEXT_PUBLIC_DEMO_TENANT_SLUG ?? "";

const accessLevels: { value: ConnectorAccessLevel; label: string; description: string }[] = [
  { value: "Read", label: "Read", description: "Look up records only. The agent cannot change anything." },
  {
    value: "Write",
    label: "Write",
    description: "Make the changes your role allows. Deletions and other irreversible steps still need your approval each time.",
  },
  {
    value: "Admin",
    label: "Admin",
    description: "Also administration-level actions your role allows, such as users, privacy and operations. Irreversible steps still need your approval each time.",
  },
];

const dataScopes: { value: ConnectorDataScope; label: string; description: string }[] = [
  {
    value: "Withheld",
    label: "Withheld",
    description: "Personal details (for example officer addresses and loan counterparties) and document downloads are withheld from the agent.",
  },
  {
    value: "Full",
    label: "Full",
    description: "Owner only. Nothing is withheld from the agent.",
  },
];

const refusalMessages: Record<string, string> = {
  INVALID_CREDENTIALS: "Invalid workspace, email, or password.",
  SECOND_FACTOR_REQUIRED: "Enter your authenticator code, or a recovery code, to connect this agent.",
  SECOND_FACTOR_REJECTED: "The authenticator or recovery code was not accepted.",
  MFA_ENROLMENT_REQUIRED: "Set up an authenticator by signing in to FilingBridge in this browser first, then connect the agent again.",
  PASSWORD_CHANGE_REQUIRED: "Your password must be changed first. Sign in to FilingBridge to change it, then connect the agent again.",
  DATA_SCOPE_FORBIDDEN: "Only an Owner can give an agent Full data scope. Choose Withheld instead.",
  CROSS_SITE_REJECTED: "This request did not come from the FilingBridge consent page. Start the connection again from your agent.",
};

type Phase =
  | { kind: "loading" }
  | { kind: "invalid"; message: string }
  | { kind: "ready"; description: OAuthAuthorizeDescription };

function isLoopbackHost(host: string) {
  const hostname = host.replace(/:\d+$/, "").replace(/^\[|\]$/g, "").toLowerCase();
  return hostname === "127.0.0.1" || hostname === "localhost" || hostname === "::1";
}

function refusalMessage(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 429) return "Too many attempts. Wait a minute, then try again.";
    if (error.status >= 500) return "FilingBridge is unavailable. Try again shortly.";
    const detail = apiErrorDetail(error);
    return detail.message
      ?? (detail.code ? refusalMessages[detail.code] : undefined)
      ?? "The agent could not be connected. Check the details and try again.";
  }
  return error instanceof Error ? error.message : "The agent could not be connected. Try again.";
}

/** Builds the RFC 6749 section 4.1.2.1 refusal redirect for a redirect URI that `describe` validated. */
export function accessDeniedRedirect(redirectUri: string, state: string | null, issuer: string) {
  const url = new URL(redirectUri);
  url.searchParams.set("error", "access_denied");
  if (state) url.searchParams.set("state", state);
  url.searchParams.set("iss", issuer);
  return url.toString();
}

export interface OAuthAuthorizeWorkbenchProps {
  /** Navigation side effect, injectable for tests. Defaults to `window.location.assign`. */
  navigate?: (url: string) => void;
}

/**
 * Public OAuth consent page for remote MCP clients. It never uses or creates the browser session:
 * the person's credentials authorize one agent grant, and the browser is sent back to the client's
 * redirect address only after `describe` has validated it. A request `describe` rejects shows an
 * error and never redirects.
 */
export function OAuthAuthorizeWorkbench({ navigate }: OAuthAuthorizeWorkbenchProps) {
  const [query, setQuery] = useState<URLSearchParams | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [tenantSlug, setTenantSlug] = useState(defaultTenantSlug);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [accessLevel, setAccessLevel] = useState<ConnectorAccessLevel>("Write");
  const [dataScope, setDataScope] = useState<ConnectorDataScope>("Withheld");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [leaving, setLeaving] = useState<"approved" | "denied" | null>(null);
  const submissionInFlight = useRef(false);

  const go = (url: string) => (navigate ?? ((target: string) => window.location.assign(target)))(url);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    let cancelled = false;
    const settle = (next: Phase) => {
      if (cancelled) return;
      setQuery(params);
      setPhase(next);
    };

    // The server decides whether a request is valid; this page never skips that check. A link
    // missing its application details is explained in plain words when the server refuses it.
    const missingDetails = !params.has("client_id") || !params.has("redirect_uri");
    describeOAuthAuthorization(params)
      .then((description) => {
        if (cancelled) return;
        setAccessLevel(description.requestedAccessLevel);
        setDataScope(description.requestedDataScope);
        settle({ kind: "ready", description });
      })
      .catch((err: unknown) => {
        settle({
          kind: "invalid",
          message: missingDetails
            ? "This link is missing the application details FilingBridge needs. Start the connection again from your agent."
            : apiErrorDetail(err).message
              ?? "FilingBridge could not verify this connection request. Start the connection again from your agent.",
        });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  async function handleApprove(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (phase.kind !== "ready" || !query || submissionInFlight.current) return;
    submissionInFlight.current = true;
    setSubmitting(true);
    setError(null);
    try {
      const trimmedCode = code.trim();
      const redirectTo = await submitOAuthAuthorization({
        clientId: query.get("client_id") ?? "",
        redirectUri: phase.description.redirectUri,
        state: query.get("state"),
        codeChallenge: query.get("code_challenge") ?? "",
        codeChallengeMethod: query.get("code_challenge_method") ?? "",
        resource: query.get("resource"),
        tenantSlug: tenantSlug.trim().toLowerCase(),
        email: email.trim(),
        password,
        totpCode: !useRecoveryCode && trimmedCode ? trimmedCode : null,
        recoveryCode: useRecoveryCode && trimmedCode ? trimmedCode : null,
        accessLevel,
        dataScope,
      });
      setPassword("");
      setCode("");
      setLeaving("approved");
      go(redirectTo);
    } catch (err) {
      setError(refusalMessage(err));
    } finally {
      submissionInFlight.current = false;
      setSubmitting(false);
    }
  }

  function handleDeny() {
    if (phase.kind !== "ready" || !query) return;
    setLeaving("denied");
    go(accessDeniedRedirect(phase.description.redirectUri, query.get("state"), window.location.origin));
  }

  if (phase.kind === "loading") {
    return (
      <ConsentFrame>
        <Card.Content>
          <div role="status" className="flex items-center gap-3 text-sm text-[var(--muted-foreground)]">
            <Spinner size="sm" />
            Checking the connection request...
          </div>
        </Card.Content>
      </ConsentFrame>
    );
  }

  if (phase.kind === "invalid") {
    return (
      <ConsentFrame>
        <Card.Header>
          <div>
            <Card.Title className="flex items-center gap-2 text-xl text-gray-900 dark:text-gray-100">
              <AlertTriangle className="h-5 w-5 text-red-600 dark:text-red-400" aria-hidden="true" />
              Connection request not accepted
            </Card.Title>
            <Card.Description className="text-[var(--muted-foreground)]">
              No access has been granted and you have not been sent anywhere.
            </Card.Description>
          </div>
        </Card.Header>
        <Card.Content>
          <div role="alert" className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <span>{phase.message}</span>
          </div>
        </Card.Content>
      </ConsentFrame>
    );
  }

  const { description } = phase;
  const loopback = isLoopbackHost(description.redirectHost);
  const busy = submitting || leaving !== null;

  return (
    <ConsentFrame>
      <Card.Header>
        <div>
          <Card.Title className="flex items-center gap-2 text-xl text-gray-900 dark:text-gray-100">
            <Bot className="h-5 w-5 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
            Connect {description.clientName} to FilingBridge
          </Card.Title>
          <Card.Description className="text-[var(--muted-foreground)]">
            The agent will act as you, within your role and company assignments, at the access level you choose below.
          </Card.Description>
        </div>
      </Card.Header>

      <Card.Content>
        <form className="space-y-5" onSubmit={handleApprove} aria-busy={busy}>
          <section
            aria-labelledby="oauth-redirect-heading"
            className="space-y-1 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100"
          >
            <h2 id="oauth-redirect-heading" className="font-semibold">Access will be sent to</h2>
            <p className="break-all font-mono text-base font-semibold" data-testid="oauth-redirect-host">{description.redirectHost}</p>
            <p>
              {loopback ? "This is an application running on this computer. " : ""}
              FilingBridge will send an access grant for your account to this address. Continue only if you started
              this connection from {description.clientName} and recognise the address.
            </p>
          </section>

          {error && (
            <div role="alert" aria-live="assertive" className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span>{error}</span>
            </div>
          )}

          {leaving && (
            <p role="status" className="text-sm text-[var(--muted-foreground)]">
              {leaving === "approved"
                ? `Returning you to ${description.clientName}...`
                : `Telling ${description.clientName} that access was denied...`}
            </p>
          )}

          <div className="space-y-4">
            <TextField fullWidth>
              <Label>Workspace slug</Label>
              <Input
                type="text"
                autoCapitalize="none"
                autoCorrect="off"
                spellCheck={false}
                minLength={3}
                maxLength={120}
                value={tenantSlug}
                onChange={(event) => setTenantSlug(event.target.value)}
                disabled={busy}
                required
              />
            </TextField>
            <TextField fullWidth>
              <Label>Email</Label>
              <Input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} disabled={busy} required />
            </TextField>
            <TextField fullWidth>
              <Label>Password</Label>
              <Input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} disabled={busy} required />
            </TextField>
            <TextField fullWidth>
              <Label>{useRecoveryCode ? "Recovery code" : "Authenticator code"}</Label>
              <Input
                inputMode={useRecoveryCode ? "text" : "numeric"}
                autoComplete="one-time-code"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                disabled={busy}
                aria-describedby="oauth-second-factor-help"
              />
              <p id="oauth-second-factor-help" className="text-sm text-[var(--muted-foreground)]">
                Required for Owner, Accountant and Reviewer accounts.
              </p>
            </TextField>
            <button
              type="button"
              className="text-sm font-medium text-emerald-700 underline underline-offset-2 dark:text-emerald-300"
              onClick={() => { setUseRecoveryCode((value) => !value); setCode(""); }}
              disabled={busy}
            >
              {useRecoveryCode ? "Use an authenticator code instead" : "Use a recovery code instead"}
            </button>
          </div>

          <ChoiceGroup
            legend="Access level"
            name="oauth-access-level"
            options={accessLevels}
            value={accessLevel}
            onChange={setAccessLevel}
            disabled={busy}
          />
          <ChoiceGroup
            legend="Personal data"
            name="oauth-data-scope"
            options={dataScopes}
            value={dataScope}
            onChange={setDataScope}
            disabled={busy}
          />

          <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <Button type="button" variant="outline" onPress={handleDeny} isDisabled={busy}>
              Deny
            </Button>
            <Button
              type="submit"
              variant="primary"
              isDisabled={busy || !tenantSlug.trim() || !email.trim() || !password}
            >
              {submitting ? <Spinner size="sm" /> : <ShieldCheck className="h-4 w-4" />}
              Approve
            </Button>
          </div>
          <p className="text-xs text-[var(--muted-foreground)]">
            Approving does not sign you in to FilingBridge in this browser. You can disconnect the agent at any time
            from the Agents page.
          </p>
        </form>
      </Card.Content>
    </ConsentFrame>
  );
}

function ChoiceGroup<T extends string>({
  legend,
  name,
  options,
  value,
  onChange,
  disabled,
}: {
  legend: string;
  name: string;
  options: { value: T; label: string; description: string }[];
  value: T;
  onChange: (value: T) => void;
  disabled: boolean;
}) {
  return (
    <fieldset className="space-y-2">
      <legend className="mb-1 text-sm font-semibold text-gray-900 dark:text-gray-100">{legend}</legend>
      {options.map((option) => (
        <label
          key={option.value}
          className="flex cursor-pointer items-start gap-3 rounded-lg border border-[var(--border)] p-3 text-sm has-[:checked]:border-emerald-500 has-[:checked]:bg-emerald-50 dark:has-[:checked]:bg-emerald-950/30"
        >
          <input
            type="radio"
            name={name}
            value={option.value}
            checked={value === option.value}
            onChange={() => onChange(option.value)}
            disabled={disabled}
            className="mt-1"
          />
          <span>
            <span className="block font-semibold">{option.label}</span>
            <span className="block text-[var(--muted-foreground)]">{option.description}</span>
          </span>
        </label>
      ))}
    </fieldset>
  );
}

function ConsentFrame({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-[calc(100vh-4rem)] items-center justify-center px-4 py-10">
      <Card className="w-full max-w-lg border border-gray-200 bg-white shadow-sm dark:border-neutral-700 dark:bg-neutral-900">
        {children}
      </Card>
    </div>
  );
}
