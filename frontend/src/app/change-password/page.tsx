"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, Input, Label, Spinner, TextField } from "@heroui/react";
import { AlertCircle, KeyRound } from "lucide-react";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/components/AuthProvider";
import { returnToFromLocation } from "@/lib/navigation";

export default function ChangePasswordPage() {
  const router = useRouter();
  const { changePassword } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (newPassword !== confirmPassword) {
      setError("New passwords do not match.");
      return;
    }

    setSubmitting(true);
    try {
      await changePassword(currentPassword, newPassword);
      router.replace(returnToFromLocation(
        typeof window === "undefined" ? undefined : new URLSearchParams(window.location.search)
      ));
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError(err instanceof Error ? err.message : "Password change failed. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="min-h-[calc(100vh-4rem)] flex items-center justify-center px-4 py-10">
      <Card className="w-full max-w-md bg-white dark:bg-neutral-900 border border-gray-200 dark:border-neutral-700 shadow-sm">
        <Card.Header>
          <div>
            <Card.Title className="text-xl text-gray-900 dark:text-gray-100">
              Set a new password
            </Card.Title>
            <Card.Description className="text-[var(--muted-foreground)]">
              Choose a new password using your current password.
            </Card.Description>
          </div>
        </Card.Header>

        <Card.Content>
          <form className="space-y-4" onSubmit={handleSubmit}>
            {error && (
              <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            <TextField fullWidth>
              <Label>Current password</Label>
              <Input
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                disabled={submitting}
                required
              />
            </TextField>

            <TextField fullWidth>
              <Label>New password</Label>
              <Input
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                disabled={submitting}
                minLength={20}
                required
              />
            </TextField>

            <TextField fullWidth>
              <Label>Confirm new password</Label>
              <Input
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
                disabled={submitting}
                minLength={20}
                required
              />
            </TextField>

            <Button
              type="submit"
              variant="primary"
              className="w-full"
              isDisabled={submitting || !currentPassword || !newPassword || !confirmPassword}
            >
              {submitting ? <Spinner size="sm" /> : <KeyRound className="h-4 w-4" />}
              Update password
            </Button>
          </form>
        </Card.Content>
      </Card>
    </div>
  );
}
