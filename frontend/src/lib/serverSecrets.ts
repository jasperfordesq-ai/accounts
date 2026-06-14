import fs from "node:fs";

type ServerSecretEnv = Record<string, string | undefined>;

export function readServerSecret(env: ServerSecretEnv, name: string) {
  const directValue = env[name]?.trim();
  if (directValue) return directValue;

  const filePath = env[`${name}_FILE`]?.trim();
  if (!filePath) return "";

  try {
    return fs.readFileSync(filePath, "utf8").replace(/[\r\n]+$/, "");
  } catch {
    throw new Error(`${name}_FILE must point to a readable secret file.`);
  }
}
