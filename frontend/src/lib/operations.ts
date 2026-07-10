import { z } from "zod";
import { ApiError } from "./api.ts";

const csrfCookie = "accounts_csrf";
const csrfHeader = "X-CSRF-Token";

const deadlineRiskItemSchema = z.object({
  outboxId: z.string().uuid(),
  companyId: z.number().int().positive(),
  companyLegalName: z.string().min(1),
  periodId: z.number().int().positive(),
  deadlineType: z.union([z.literal(0), z.literal(1), z.literal(2)]),
  reminderKind: z.union([z.literal(0), z.literal(1), z.literal(2)]),
  state: z.union([z.literal(0), z.literal(1), z.literal(2), z.literal(3), z.literal(4), z.literal(5)]),
  dueDate: z.iso.date(),
  attemptCount: z.number().int().nonnegative(),
  nextAttemptAtUtc: z.iso.datetime({ offset: true }),
  lastFailureCode: z.string().min(1).nullable(),
}).strict();

const deadlineReminderRunSchema = z.object({
  jobRunId: z.string().uuid(),
  examinedCount: z.number().int().nonnegative(),
  enqueuedCount: z.number().int().nonnegative(),
  deliveredCount: z.number().int().nonnegative(),
  failedCount: z.number().int().nonnegative(),
  cancelledCount: z.number().int().nonnegative(),
  status: z.union([z.literal(0), z.literal(1), z.literal(2), z.literal(3)]),
  evidenceSha256: z.string().regex(/^[a-f0-9]{64}$/i),
}).strict();

export type DeadlineRiskQueueItem = z.infer<typeof deadlineRiskItemSchema>;
export type DeadlineReminderRun = z.infer<typeof deadlineReminderRunSchema>;

function readCsrfToken(): string | undefined {
  if (typeof document === "undefined") return undefined;
  const prefix = `${csrfCookie}=`;
  const item = document.cookie.split(";").map((part) => part.trim()).find((part) => part.startsWith(prefix));
  return item ? decodeURIComponent(item.slice(prefix.length)) : undefined;
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  const token = readCsrfToken();
  const response = await fetch(path, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { [csrfHeader]: token } : {}),
      ...init?.headers,
    },
  });
  if (!response.ok) {
    const body = await response.text().catch(() => "");
    throw new ApiError(response.status, response.statusText, body);
  }
  return response;
}

export async function getDeadlineRiskQueue(): Promise<DeadlineRiskQueueItem[]> {
  const response = await request("/api/operations/deadline-risk");
  return z.array(deadlineRiskItemSchema).parse(await response.json());
}

export async function retryDeadlineReminder(outboxId: string): Promise<void> {
  await request(`/api/operations/deadline-reminders/${encodeURIComponent(outboxId)}/retry`, { method: "POST" });
}

export async function runDeadlineReminders(): Promise<DeadlineReminderRun> {
  const response = await request("/api/operations/deadline-reminders/run", { method: "POST" });
  return deadlineReminderRunSchema.parse(await response.json());
}
