import { DEFAULT_API_PROXY_MAX_BODY_BYTES } from "./apiProxyLimits.ts";

export interface EvidenceFilePayload {
  base64: string;
  sha256: string;
}

/** Reads an uploaded evidence file into the base64 body and lowercase hex SHA-256 the filing endpoints verify. */
export async function readEvidenceFile(file: Blob): Promise<EvidenceFilePayload> {
  const buffer = await file.arrayBuffer();
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", buffer));
  const sha256 = Array.from(digest, (byte) => byte.toString(16).padStart(2, "0")).join("");
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return { base64: btoa(binary), sha256 };
}

/** Room left in the proxy body limit for the JSON field names, the other fields and quoting. */
const EVIDENCE_UPLOAD_JSON_MARGIN_BYTES = 16 * 1024;

/** Raised in the browser, before anything is sent, when evidence files would exceed the proxy body limit. */
export class EvidenceUploadTooLargeError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "EvidenceUploadTooLargeError";
  }
}

/**
 * Evidence files travel base64-encoded in one JSON body (about 4/3 of their size), which must fit
 * the frontend API proxy's body limit. Checks the encoded size before reading or sending anything.
 */
export function assertEvidenceUploadFits(files: ReadonlyArray<Blob | null | undefined>) {
  const present = files.filter((file): file is Blob => file != null);
  const encodedBytes = present.reduce((total, file) => total + Math.ceil(file.size / 3) * 4, 0);
  const allowedEncodedBytes = DEFAULT_API_PROXY_MAX_BODY_BYTES - EVIDENCE_UPLOAD_JSON_MARGIN_BYTES;
  if (encodedBytes <= allowedEncodedBytes) return;
  const maxMegabytes = Math.floor(((allowedEncodedBytes * 3) / 4 / (1024 * 1024)) * 10) / 10;
  throw new EvidenceUploadTooLargeError(
    present.length > 1
      ? `These files are too large to upload together (max about ${maxMegabytes} MB). Please compress or rescan at a lower resolution.`
      : `This file is too large to upload (max about ${maxMegabytes} MB). Please compress or rescan at a lower resolution.`,
  );
}
