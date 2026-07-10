"use client";

import type { InteractionAnnouncement as InteractionAnnouncementState } from "@/lib/interactionState";

export function InteractionAnnouncement({ announcement }: { announcement: InteractionAnnouncementState }) {
  const isError = announcement.tone === "error";
  return (
    <p
      key={announcement.id}
      className="sr-only"
      role={isError ? "alert" : "status"}
      aria-live={isError ? "assertive" : "polite"}
      aria-atomic="true"
    >
      {announcement.message}
    </p>
  );
}
