const LOGOUT_FAILURE_MESSAGE = "Sign out did not complete. Your session is still active, so please try again.";

function hasHttpStatus(error: unknown, status: number): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "status" in error &&
    (error as { status?: unknown }).status === status
  );
}

export function shouldClearLocalSessionAfterLogout(error?: unknown): boolean {
  return error === undefined || hasHttpStatus(error, 401);
}

export function logoutFailureMessage(error: unknown): string {
  void error;
  return LOGOUT_FAILURE_MESSAGE;
}
