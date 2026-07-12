import { PageShell, ReviewPanel, StatusBadge } from "@/components/workbench";
import {
  ABOUT_ATTRIBUTION_LINES,
  CREATOR_NAME,
  LICENSE_NAME,
  ORGANISATIONAL_CONTRIBUTORS,
  PRODUCT_NAME,
  REPOSITORY_URL,
} from "@/lib/attribution";

export default function AboutPage() {
  return (
    <PageShell
      title={`About ${PRODUCT_NAME}`}
      subtitle="Source, attribution and production-use boundaries for the Irish statutory accounts platform."
      meta={<StatusBadge tone="warn">Qualified-accountant review required</StatusBadge>}
    >
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.45fr)]">
        <ReviewPanel
          title="Source, licence and attribution"
          description="Public deployments must preserve visible attribution and source-code access."
        >
          <div className="space-y-4 text-sm leading-6 text-[var(--muted-foreground)]">
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 font-semibold leading-6 text-[var(--foreground)]">
              {ABOUT_ATTRIBUTION_LINES.map((line) => (
                <p key={line}>{line}</p>
              ))}
            </div>
            <p>
              {CREATOR_NAME} is the creator, main contributor, and copyright holder for {PRODUCT_NAME}.
              This software is licensed under {LICENSE_NAME}, with additional attribution terms
              recorded in the NOTICE file.
            </p>
            <p>
              Recognised organisational contributor: {ORGANISATIONAL_CONTRIBUTORS.join(", ")}.
              Contributor acknowledgement records participation in the project and does not alter
              the creator or copyright-holder designation.
            </p>
            <p>
              Interactive interfaces based on {PRODUCT_NAME} must keep a visible attribution
              path and source-code link that is no less accessible than the original footer
              and About page.
            </p>
            <a
              href={REPOSITORY_URL}
              target="_blank"
              rel="noreferrer"
              className="inline-flex min-h-9 items-center rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 text-sm font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
            >
              accounts repository
            </a>
          </div>
        </ReviewPanel>

        <ReviewPanel
          title="Production-use boundary"
          description="The platform prepares evidence and packs; real-world filing remains professionally gated."
        >
          <ul className="space-y-3 text-sm leading-6 text-[var(--muted-foreground)]">
            <li>Final CRO or Revenue use requires named qualified-accountant review.</li>
            <li>Direct CRO or ROS submission remains unsupported by this application.</li>
            <li>Workflow states record preparation, review and handoff evidence only.</li>
          </ul>
        </ReviewPanel>
      </div>
    </PageShell>
  );
}
