"use client";

import { Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { ActionTokenPasswordForm } from "@/components/identity/ActionTokenPasswordForm";

function PasswordResetForm() {
  const search = useSearchParams();
  return <ActionTokenPasswordForm mode="password-reset" token={search.get("token") ?? ""} />;
}

export default function ResetPasswordPage() {
  return <Suspense><PasswordResetForm /></Suspense>;
}
