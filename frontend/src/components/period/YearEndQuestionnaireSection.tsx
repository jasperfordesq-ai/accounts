"use client";

import { useId, useState, type ComponentType, type ReactNode } from "react";
import { Button, Card, Chip, Spinner } from "@heroui/react";
import { CheckCircle2, ChevronDown, ChevronRight } from "lucide-react";

import type { YearEndReviewConfirmation } from "@/lib/api";

interface YearEndQuestionnaireSectionProps {
  title: string;
  subtitle: string;
  icon: ComponentType<{ className?: string }>;
  completed: boolean;
  review?: YearEndReviewConfirmation;
  onConfirmReview?: () => void;
  reviewSaving?: boolean;
  reviewDisabled?: boolean;
  reviewDisabledReason?: string;
  children: ReactNode;
  defaultOpen?: boolean;
}

export function YearEndQuestionnaireSection({
  title,
  subtitle,
  icon: Icon,
  completed,
  review,
  onConfirmReview,
  reviewSaving,
  reviewDisabled = false,
  reviewDisabledReason,
  children,
  defaultOpen = false,
}: YearEndQuestionnaireSectionProps) {
  const [open, setOpen] = useState(defaultOpen);
  const [hasOpened, setHasOpened] = useState(defaultOpen);
  const reactId = useId();
  const panelId = `year-end-section-${reactId.replace(/[^a-zA-Z0-9_-]/g, "")}`;
  const isReviewed = review?.confirmed === true;
  const isComplete = !reviewDisabled && (completed || isReviewed);

  const toggleOpen = () => {
    if (!open) setHasOpened(true);
    setOpen((current) => !current);
  };

  return (
    <Card className="bg-white dark:bg-neutral-900 shadow-sm border border-gray-200 dark:border-neutral-700">
      <button
        type="button"
        className="w-full text-left px-6 py-4 flex items-center gap-4 hover:bg-gray-50/50 dark:hover:bg-neutral-800/50 transition-colors"
        onClick={toggleOpen}
        aria-expanded={open}
        aria-controls={panelId}
        aria-label={`${open ? "Collapse" : "Expand"} ${title} section`}
      >
        <div className="w-10 h-10 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 flex items-center justify-center shrink-0">
          <Icon className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">{title}</h3>
            {isComplete && (
              <CheckCircle2 className="w-4 h-4 text-emerald-500" />
            )}
            {onConfirmReview && (
              <Chip size="sm" color={reviewDisabled ? "danger" : isReviewed ? "success" : "warning"} variant="soft">
                {reviewDisabled ? "Evidence unavailable" : isReviewed ? "Reviewed" : "Needs review"}
              </Chip>
            )}
          </div>
          <p className="text-xs text-[var(--muted-foreground)] mt-0.5">{subtitle}</p>
        </div>
        {open ? (
          <ChevronDown className="w-5 h-5 text-[var(--muted-foreground)] shrink-0" />
        ) : (
          <ChevronRight className="w-5 h-5 text-[var(--muted-foreground)] shrink-0" />
        )}
      </button>
      {hasOpened && (
        <div
          id={panelId}
          role="region"
          aria-label={`${title} review evidence`}
          hidden={!open}
          className="animate-slide-down px-6 pb-6 border-t border-gray-100 dark:border-neutral-700 pt-4"
        >
          {children}
          {onConfirmReview && (
            <div className="mt-5 flex flex-col gap-3 rounded-md border border-slate-200 bg-slate-50 px-4 py-3 text-sm dark:border-neutral-700 dark:bg-neutral-800/60 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className="font-medium text-slate-800 dark:text-slate-100">
                  {reviewDisabled
                    ? "Confirmation blocked until evidence is available"
                    : isReviewed
                      ? "Section review confirmed"
                      : "Review this section before final accounts"}
                </p>
                {reviewDisabled && reviewDisabledReason && (
                  <p className="mt-1 text-xs text-amber-700 dark:text-amber-300">{reviewDisabledReason}</p>
                )}
                {isReviewed && review?.confirmedBy && (
                  <p className="text-xs text-[var(--muted-foreground)]">
                    Confirmed by {review.confirmedBy}
                    {review.confirmedAt && ` on ${new Date(review.confirmedAt).toLocaleDateString("en-IE")}`}
                  </p>
                )}
              </div>
              <Button
                size="sm"
                variant={isReviewed ? "outline" : "primary"}
                onPress={onConfirmReview}
                isDisabled={reviewSaving || reviewDisabled}
              >
                {reviewSaving ? <Spinner size="sm" /> : <CheckCircle2 className="w-4 h-4 mr-1" />}
                {isReviewed ? "Refresh confirmation" : "Confirm reviewed"}
              </Button>
            </div>
          )}
        </div>
      )}
    </Card>
  );
}
