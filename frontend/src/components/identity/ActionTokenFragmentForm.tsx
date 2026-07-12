"use client";

import { useEffect, useRef, useState } from "react";
import { ActionTokenPasswordForm } from "@/components/identity/ActionTokenPasswordForm";

type ActionTokenFragmentFormProps = {
  mode: "invitation" | "password-reset";
};

export function readActionTokenFragment(hash: string) {
  if (!hash.startsWith("#")) return "";
  return new URLSearchParams(hash.slice(1)).get("token") ?? "";
}

export function ActionTokenFragmentForm({ mode }: ActionTokenFragmentFormProps) {
  const captured = useRef({ complete: false, token: "" });
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    if (!captured.current.complete) {
      captured.current = {
        complete: true,
        token: readActionTokenFragment(window.location.hash),
      };
    }

    // URL fragments are not sent to the server. Remove the one-time secret from
    // the visible address and browser history immediately after capturing it.
    const sanitizedSearch = new URLSearchParams(window.location.search);
    sanitizedSearch.delete("token");
    const retainedSearch = sanitizedSearch.size > 0 ? `?${sanitizedSearch.toString()}` : "";
    window.history.replaceState(
      window.history.state,
      document.title,
      `${window.location.pathname}${retainedSearch}`,
    );
    setToken(captured.current.token);
  }, []);

  if (token === null) {
    return <p role="status" className="p-6 text-sm text-gray-600">Reading the secure one-time link…</p>;
  }

  return <ActionTokenPasswordForm mode={mode} token={token} />;
}
