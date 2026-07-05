import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";

export async function withScreenshotEvidence(artifact) {
  const bytes = await readFile(artifact.artifactPath);
  if (bytes.length === 0) {
    throw new Error(`visual smoke screenshot is empty: ${artifact.fileName}`);
  }

  return {
    ...artifact,
    byteSize: bytes.length,
    sha256: `sha256:${createHash("sha256").update(bytes).digest("hex")}`,
  };
}
