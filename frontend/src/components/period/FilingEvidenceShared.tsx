"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button, Spinner } from "@heroui/react";
import { ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { useAuth } from "@/components/AuthProvider";

export const inputClass = "w-full rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 py-2 text-sm text-[var(--foreground)] outline-none transition focus:border-[var(--ring)] focus:ring-2 focus:ring-teal-100 dark:focus:ring-teal-900/40";

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <div className="text-sm font-medium text-[var(--foreground)]">
      <label className="block">
        <span className="mb-1.5 block">{label}</span>
        {children}
      </label>
      {hint && <p className="mt-1 text-xs font-normal leading-5 text-[var(--muted-foreground)]">{hint}</p>}
    </div>
  );
}

export function shortHash(hash: string) {
  return hash.slice(0, 12);
}

export function formatDateTime(value: string) {
  return new Date(value).toLocaleString("en-IE", { dateStyle: "medium", timeStyle: "short" });
}

export function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

/** Noon UTC on the chosen calendar day, so the Irish date of the signing never drifts across midnight. */
export function signedAtUtcForDate(date: string) {
  return `${date}T12:00:00Z`;
}

export function todayIsoDate() {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Shown after the server answers 428 recent_mfa_required: collects the password plus a fresh
 * authenticator code through the existing session re-authentication, then asks the user to repeat the action.
 */
export function RecentVerificationPrompt({ onVerified }: { onVerified: () => void }) {
  const { reauthenticate } = useAuth();
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    try {
      await reauthenticate(password, code);
      setPassword("");
      setCode("");
      toast.success("Verified. Repeat the action to record it.");
      onVerified();
    } catch (error) {
      setCode("");
      toast.error(errorMessage(error, "Verification failed"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      onSubmit={(event) => void submit(event)}
      aria-label="Recent verification"
      className="space-y-3 rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-950 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-100"
    >
      <p className="flex items-start gap-2 font-medium">
        <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" />
        Verify with your password and authenticator code to record this. It only takes a moment.
      </p>
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="Current password">
          <input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required className={inputClass} />
        </Field>
        <Field label="Authenticator code">
          <input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(event) => setCode(event.target.value)} required className={inputClass} />
        </Field>
      </div>
      <Button type="submit" size="sm" variant="secondary" aria-label="Verify" isDisabled={busy || !password || !code}>
        {busy ? <Spinner size="sm" /> : "Verify"}
      </Button>
    </form>
  );
}
