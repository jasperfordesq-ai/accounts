import { DEFAULT_API_PROXY_MAX_BODY_BYTES } from "./apiProxyLimits.ts";

/**
 * Request-body bounding shared by the frontend proxies (`app/api/[...path]` and `app/mcp`). The
 * declared Content-Length is checked up front and the streamed body is counted as it flows, so a
 * chunked or lying request cannot exceed the limit either.
 */
export class PayloadTooLargeError extends Error {
  constructor() {
    super("Request body exceeds the frontend proxy limit.");
    this.name = "PayloadTooLargeError";
  }
}

export function positiveIntegerFromEnv(value: string | undefined, fallback: number) {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

export function maxProxyBodyBytes(env: Record<string, string | undefined> = process.env) {
  return positiveIntegerFromEnv(env.API_PROXY_MAX_BODY_BYTES, DEFAULT_API_PROXY_MAX_BODY_BYTES);
}

export function contentLengthExceedsLimit(headers: Headers, limitBytes: number) {
  const contentLength = headers.get("content-length");
  if (!contentLength) return false;

  const parsedLength = Number.parseInt(contentLength, 10);
  return Number.isFinite(parsedLength) && parsedLength > limitBytes;
}

export function boundedRequestBody(body: ReadableStream<Uint8Array> | null, limitBytes: number) {
  if (!body) return null;

  let bytesRead = 0;
  return body.pipeThrough(
    new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        bytesRead += chunk.byteLength;
        if (bytesRead > limitBytes) {
          throw new PayloadTooLargeError();
        }

        controller.enqueue(chunk);
      },
    }),
  );
}

export function isPayloadTooLargeError(error: unknown) {
  return error instanceof PayloadTooLargeError
    || (error instanceof Error && error.cause instanceof PayloadTooLargeError);
}
