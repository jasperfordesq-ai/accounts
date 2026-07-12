"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, Input, Label, Spinner, TextField } from "@heroui/react";
import { AlertCircle, LogIn } from "lucide-react";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/components/AuthProvider";
import { isMfaChallenge, type MfaChallenge } from "@/lib/auth";
import { changePasswordRouteForReturnTo, returnToFromLocation } from "@/lib/navigation";

const defaultEmail = process.env.NEXT_PUBLIC_DEMO_LOGIN_EMAIL ?? "";
const defaultTenantSlug = process.env.NEXT_PUBLIC_DEMO_TENANT_SLUG ?? "";

export default function LoginPage() {
  const router = useRouter();
  const { login, completeMfaChallenge } = useAuth();
  const [tenantSlug, setTenantSlug] = useState(defaultTenantSlug);
  const [email, setEmail] = useState(defaultEmail);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [challenge, setChallenge] = useState<MfaChallenge | null>(null);
  const [mfaCode, setMfaCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [recoveryContinuation, setRecoveryContinuation] = useState<{ mustChangePassword: boolean } | null>(null);

  function finishLogin(user: { mustChangePassword: boolean }) {
    const returnTo = returnToFromLocation(
      typeof window === "undefined" ? undefined : new URLSearchParams(window.location.search)
    );
    router.replace(user.mustChangePassword ? changePasswordRouteForReturnTo(returnTo) : returnTo);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      const outcome = await login(tenantSlug.trim().toLowerCase(), email.trim(), password);
      if (isMfaChallenge(outcome)) {
        setChallenge(outcome);
        setPassword("");
        return;
      }
      finishLogin(outcome);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.status === 401 ? "Invalid workspace, email, or password." : err.message);
      } else {
        setError(err instanceof Error ? err.message : "Sign in failed. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  async function handleMfaSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!challenge) return;
    setError(null);
    setSubmitting(true);
    try {
      const completion = await completeMfaChallenge(
        challenge.challengeToken,
        useRecoveryCode ? undefined : mfaCode.trim(),
        useRecoveryCode ? mfaCode.trim() : undefined,
      );
      if (completion.recoveryCodes.length > 0) {
        setRecoveryContinuation({ mustChangePassword: completion.user.mustChangePassword });
        setRecoveryCodes(completion.recoveryCodes);
        return;
      }
      finishLogin(completion.user);
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? "The authenticator or recovery code was not accepted."
        : err instanceof Error ? err.message : "Authenticator verification failed.");
    } finally {
      setSubmitting(false);
    }
  }

  if (recoveryCodes.length > 0) {
    return (
      <div className="min-h-[calc(100vh-4rem)] flex items-center justify-center px-4 py-10">
        <Card className="w-full max-w-lg border border-emerald-200 bg-white shadow-sm dark:border-emerald-900 dark:bg-neutral-900">
          <Card.Header><div><Card.Title>Save your recovery codes</Card.Title><Card.Description>Each code works once. Store them in your approved password manager before continuing; they will not be shown again.</Card.Description></div></Card.Header>
          <Card.Content className="space-y-4">
            <ul aria-label="One-time MFA recovery codes" className="grid grid-cols-1 gap-2 rounded-lg bg-gray-50 p-4 font-mono text-sm text-gray-900 dark:bg-neutral-950 dark:text-gray-100 sm:grid-cols-2">
              {recoveryCodes.map((code) => <li key={code}>{code}</li>)}
            </ul>
            <Button className="w-full" variant="primary" onPress={() => finishLogin(recoveryContinuation ?? { mustChangePassword: true })}>I have stored these codes</Button>
          </Card.Content>
        </Card>
      </div>
    );
  }

  return (
    <div className="min-h-[calc(100vh-4rem)] flex items-center justify-center px-4 py-10">
      <Card className="w-full max-w-md bg-white dark:bg-neutral-900 border border-gray-200 dark:border-neutral-700 shadow-sm">
        <Card.Header>
          <div>
            <Card.Title className="text-xl text-gray-900 dark:text-gray-100">
              Sign in
            </Card.Title>
            <Card.Description className="text-[var(--muted-foreground)]">
              Access your firm workspace and client accounts.
            </Card.Description>
          </div>
        </Card.Header>

        <Card.Content>
          <form className="space-y-4" onSubmit={challenge ? handleMfaSubmit : handleSubmit}>
            {error && (
              <div role="alert" aria-live="assertive" className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            {!challenge ? (
              <>
                <TextField fullWidth>
                  <Label>Workspace slug</Label>
                  <Input
                    type="text"
                    autoCapitalize="none"
                    autoCorrect="off"
                    spellCheck={false}
                    minLength={3}
                    maxLength={120}
                    pattern="[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?"
                    value={tenantSlug}
                    onChange={(event) => setTenantSlug(event.target.value)}
                    disabled={submitting}
                    required
                    aria-describedby="workspace-slug-help"
                  />
                  <p id="workspace-slug-help" className="text-sm text-[var(--muted-foreground)]">
                    Enter the workspace slug supplied by your administrator. Private Server setup prints this value.
                  </p>
                </TextField>
                <TextField fullWidth>
                  <Label>Email</Label>
                  <Input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} disabled={submitting} required />
                </TextField>
                <TextField fullWidth>
                  <Label>Password</Label>
                  <Input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} disabled={submitting} required />
                </TextField>
              </>
            ) : (
              <>
                {challenge.requiresEnrollment && challenge.enrollmentSecret && (
                  <div className="space-y-2 rounded-lg border border-blue-200 bg-blue-50 p-3 text-sm text-blue-950 dark:border-blue-900 dark:bg-blue-950/30 dark:text-blue-100">
                    <p className="font-semibold">Set up your authenticator</p>
                    <p>Add this one-time setup key to a TOTP authenticator. It is encrypted at rest and will not be shown after enrollment.</p>
                    <code aria-label="Authenticator setup key" className="block break-all rounded bg-white px-2 py-1 font-mono dark:bg-neutral-950">{challenge.enrollmentSecret}</code>
                  </div>
                )}
                <TextField fullWidth>
                  <Label>{useRecoveryCode ? "Recovery code" : "6-digit authenticator code"}</Label>
                  <Input
                    inputMode={useRecoveryCode ? "text" : "numeric"}
                    autoComplete="one-time-code"
                    value={mfaCode}
                    onChange={(event) => setMfaCode(event.target.value)}
                    disabled={submitting}
                    required
                  />
                </TextField>
                {!challenge.requiresEnrollment && (
                  <button type="button" className="text-sm font-medium text-emerald-700 underline underline-offset-2 dark:text-emerald-300" onClick={() => { setUseRecoveryCode((value) => !value); setMfaCode(""); }}>
                    {useRecoveryCode ? "Use authenticator code" : "Use a recovery code"}
                  </button>
                )}
              </>
            )}

            <Button
              type="submit"
              variant="primary"
              className="w-full"
              isDisabled={submitting || (!challenge ? !tenantSlug.trim() || !email.trim() || !password : !mfaCode.trim())}
            >
              {submitting ? <Spinner size="sm" /> : <LogIn className="h-4 w-4" />}
              {challenge ? challenge.requiresEnrollment ? "Verify and enable MFA" : "Verify and sign in" : "Sign in"}
            </Button>
          </form>
        </Card.Content>
      </Card>
    </div>
  );
}
