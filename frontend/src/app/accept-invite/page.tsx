"use client";

import { Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { ActionTokenPasswordForm } from "@/components/identity/ActionTokenPasswordForm";

function InvitationForm() {
  const search = useSearchParams();
  return <ActionTokenPasswordForm mode="invitation" token={search.get("token") ?? ""} />;
}

export default function AcceptInvitationPage() {
  return <Suspense><InvitationForm /></Suspense>;
}
