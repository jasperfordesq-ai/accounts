import Link from "next/link";
import {
  COPYRIGHT_NOTICE,
  FOOTER_ATTRIBUTION,
  LICENSE_NAME,
  REPOSITORY_URL,
} from "@/lib/attribution";

export function AppFooter() {
  return (
    <footer
      role="contentinfo"
      className="border-t border-[var(--border)] bg-[var(--surface)] px-6 py-4 text-xs text-[var(--muted-foreground)]"
    >
      <div className="mx-auto flex max-w-7xl flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0 space-y-1">
          <p className="font-medium text-[var(--foreground)]">{COPYRIGHT_NOTICE}</p>
          <p className="max-w-3xl leading-5">
            <a
              href={REPOSITORY_URL}
              target="_blank"
              rel="noreferrer"
              className="font-semibold text-[var(--foreground)] underline-offset-4 hover:underline"
            >
              {FOOTER_ATTRIBUTION}
            </a>
            {" "}Licensed under {LICENSE_NAME}. Source code must remain available under the AGPL.
          </p>
        </div>
        <nav aria-label="Attribution links" className="flex shrink-0 flex-wrap items-center gap-3">
          <Link
            href="/about"
            className="font-semibold text-[var(--foreground)] underline-offset-4 hover:underline"
          >
            About
          </Link>
          <a
            href={REPOSITORY_URL}
            target="_blank"
            rel="noreferrer"
            className="font-semibold text-[var(--foreground)] underline-offset-4 hover:underline"
          >
            GitHub repository
          </a>
        </nav>
      </div>
    </footer>
  );
}
