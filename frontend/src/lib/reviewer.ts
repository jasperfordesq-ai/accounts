export function getReviewerName() {
  if (typeof window === "undefined") return "Accounts reviewer";

  const stored = window.localStorage.getItem("accounts.reviewerName")?.trim();
  return stored || "Accounts reviewer";
}
