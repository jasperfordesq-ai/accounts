"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, Input, Label, Spinner, TextField } from "@heroui/react";
import { AlertCircle, LogIn } from "lucide-react";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/components/AuthProvider";

const defaultEmail = process.env.NEXT_PUBLIC_DEMO_LOGIN_EMAIL ?? "";

export default function LoginPage() {
  const router = useRouter();
  const { login } = useAuth();
  const [email, setEmail] = useState(defaultEmail);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      await login(email.trim(), password);
      router.replace("/");
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError(err instanceof Error ? err.message : "Sign in failed. Please try again.");
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
              Sign in
            </Card.Title>
            <Card.Description className="text-gray-500 dark:text-gray-400">
              Access your firm workspace and client accounts.
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
              <Label>Email</Label>
              <Input
                type="email"
                autoComplete="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                disabled={submitting}
                required
              />
            </TextField>

            <TextField fullWidth>
              <Label>Password</Label>
              <Input
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                disabled={submitting}
                required
              />
            </TextField>

            <Button
              type="submit"
              variant="primary"
              className="w-full"
              isDisabled={submitting || !email.trim() || !password}
            >
              {submitting ? <Spinner size="sm" /> : <LogIn className="h-4 w-4" />}
              Sign in
            </Button>
          </form>
        </Card.Content>
      </Card>
    </div>
  );
}
