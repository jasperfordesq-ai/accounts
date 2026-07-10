"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { Button, Card, Input, Label, Spinner, TextField } from "@heroui/react";
import { AlertCircle, CheckCircle2, KeyRound } from "lucide-react";
import { acceptInvitation, completePasswordReset } from "@/lib/identity";
import { ActionLink } from "@/components/workbench";

type ActionMode = "invitation" | "password-reset";

const copy = {
  invitation: {
    title: "Accept your invitation",
    description: "Set a strong password for your firm account. Privileged roles will enrol an authenticator at first sign-in.",
    button: "Activate account",
    success: "Your account is active. Sign in to complete any required MFA enrolment.",
  },
  "password-reset": {
    title: "Reset your password",
    description: "Choose a new strong password. Existing sessions have been revoked and this one-time link cannot be reused.",
    button: "Set new password",
    success: "Your password has been reset. Sign in again with the new password.",
  },
} as const;

export function ActionTokenPasswordForm({ mode, token }: { mode: ActionMode; token: string }) {
  const labels = copy[mode];
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [completed, setCompleted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const tokenMissing = token.trim().length === 0;
  const mismatch = confirmation.length > 0 && password !== confirmation;
  const passwordTooShort = password.length > 0 && password.length < 20;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (tokenMissing || password.length < 20 || password !== confirmation) return;
    setSubmitting(true);
    setError(null);
    try {
      if (mode === "invitation") await acceptInvitation(token, password);
      else await completePasswordReset(token, password);
      setPassword("");
      setConfirmation("");
      setCompleted(true);
    } catch {
      setError("This one-time link is invalid, expired, or already used, or the password does not meet the security policy. Request a new link from your firm Owner.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="flex min-h-[calc(100vh-4rem)] items-center justify-center px-4 py-10">
      <Card className="w-full max-w-md border border-gray-200 bg-white shadow-sm dark:border-neutral-700 dark:bg-neutral-900">
        <Card.Header>
          <div>
            <Card.Title className="flex items-center gap-2 text-xl"><KeyRound className="h-5 w-5" />{labels.title}</Card.Title>
            <Card.Description>{labels.description}</Card.Description>
          </div>
        </Card.Header>
        <Card.Content>
          {completed ? (
            <div className="space-y-4" role="status" aria-live="polite">
              <div className="flex items-start gap-2 rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-900 dark:border-emerald-900 dark:bg-emerald-950/30 dark:text-emerald-100">
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{labels.success}</span>
              </div>
              <ActionLink href="/login" variant="primary" className="w-full justify-center">Continue to sign in</ActionLink>
            </div>
          ) : (
            <form className="space-y-4" onSubmit={submit}>
              {tokenMissing && (
                <div role="alert" className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/30 dark:text-red-200">
                  <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                  <span>This link does not contain a one-time token. Ask your firm Owner to issue a new link.</span>
                </div>
              )}
              {error && <div role="alert" aria-live="assertive" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/30 dark:text-red-200">{error}</div>}
              <TextField fullWidth isInvalid={passwordTooShort}>
                <Label>New password</Label>
                <Input type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} minLength={20} required disabled={submitting || tokenMissing} />
                <p className="mt-1 text-xs text-gray-600 dark:text-gray-400">Use at least 20 characters. Known-compromised passwords are rejected.</p>
              </TextField>
              <TextField fullWidth isInvalid={mismatch}>
                <Label>Confirm new password</Label>
                <Input type="password" autoComplete="new-password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} minLength={20} required disabled={submitting || tokenMissing} />
                {mismatch && <p role="alert" className="mt-1 text-xs text-red-700 dark:text-red-300">The passwords do not match.</p>}
              </TextField>
              <Button type="submit" variant="primary" className="w-full" isDisabled={submitting || tokenMissing || password.length < 20 || password !== confirmation}>
                {submitting ? <Spinner size="sm" /> : <KeyRound className="h-4 w-4" />}
                {labels.button}
              </Button>
              <p className="text-center text-sm text-gray-600 dark:text-gray-400"><Link href="/login" className="font-medium text-emerald-700 underline underline-offset-2 dark:text-emerald-300">Return to sign in</Link></p>
            </form>
          )}
        </Card.Content>
      </Card>
    </main>
  );
}
