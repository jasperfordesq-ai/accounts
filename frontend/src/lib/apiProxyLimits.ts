/**
 * Default request-body limit of the frontend API proxy (`app/api/[...path]/route.ts`). A deployment
 * may override it server-side with `API_PROXY_MAX_BODY_BYTES`; the browser checks evidence uploads
 * against this default so an oversized upload gets a clear message instead of a generic 413.
 */
export const DEFAULT_API_PROXY_MAX_BODY_BYTES = 5 * 1024 * 1024;
