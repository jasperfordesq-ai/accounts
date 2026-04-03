"use client";

/** Reusable skeleton building blocks for loading states */

export function SkeletonLine({ className = "" }: { className?: string }) {
  return (
    <div
      className={`skeleton-shimmer h-4 rounded ${className}`}
      aria-hidden="true"
    />
  );
}

export function SkeletonBlock({ className = "" }: { className?: string }) {
  return (
    <div
      className={`skeleton-shimmer rounded-lg ${className}`}
      aria-hidden="true"
    />
  );
}

/** Card-shaped skeleton — matches the Card component dimensions */
export function SkeletonCard() {
  return (
    <div
      className="rounded-xl border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 p-5 space-y-3"
      aria-hidden="true"
    >
      <SkeletonLine className="h-3 w-24" />
      <SkeletonLine className="h-6 w-48" />
      <SkeletonLine className="h-3 w-36" />
    </div>
  );
}

/** Dashboard-style: stats bar + company cards */
export function DashboardSkeleton() {
  return (
    <div className="animate-fade-in" aria-label="Loading dashboard">
      {/* Header */}
      <div className="flex items-center justify-between mb-8">
        <div className="space-y-2">
          <SkeletonLine className="h-7 w-36" />
          <SkeletonLine className="h-4 w-56" />
        </div>
        <SkeletonBlock className="h-9 w-32" />
      </div>
      {/* Stats bar */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        {[...Array(4)].map((_, i) => (
          <SkeletonCard key={i} />
        ))}
      </div>
      {/* Company cards */}
      <div className="space-y-2 mb-4">
        <SkeletonLine className="h-5 w-28" />
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {[...Array(4)].map((_, i) => (
          <div
            key={i}
            className="rounded-xl border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 p-5 space-y-4"
          >
            <div className="flex items-center gap-3">
              <SkeletonBlock className="h-10 w-10 rounded-lg" />
              <div className="space-y-2 flex-1">
                <SkeletonLine className="h-5 w-48" />
                <SkeletonLine className="h-3 w-32" />
              </div>
            </div>
            <div className="flex gap-2">
              <SkeletonBlock className="h-6 w-16 rounded-full" />
              <SkeletonBlock className="h-6 w-20 rounded-full" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Company detail skeleton */
export function CompanyDetailSkeleton() {
  return (
    <div className="animate-fade-in" aria-label="Loading company details">
      <SkeletonLine className="h-4 w-36 mb-4" />
      <div className="flex items-center gap-3 mb-8">
        <SkeletonBlock className="h-14 w-14 rounded-xl" />
        <div className="space-y-2">
          <SkeletonLine className="h-7 w-64" />
          <SkeletonLine className="h-4 w-32" />
        </div>
      </div>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        {[...Array(3)].map((_, i) => (
          <SkeletonCard key={i} />
        ))}
      </div>
      <SkeletonCard />
    </div>
  );
}

/** Period workspace skeleton */
export function PeriodWorkspaceSkeleton() {
  return (
    <div className="animate-fade-in" aria-label="Loading period workspace">
      <div className="mb-6 space-y-2">
        <SkeletonLine className="h-7 w-64" />
        <SkeletonLine className="h-5 w-48" />
      </div>
      <div className="flex gap-1 border-b border-gray-200 dark:border-neutral-700 mb-6 pb-2">
        {[...Array(6)].map((_, i) => (
          <SkeletonBlock key={i} className="h-9 w-24 rounded-lg" />
        ))}
      </div>
      <div className="space-y-4">
        <SkeletonCard />
        <SkeletonCard />
      </div>
    </div>
  );
}
