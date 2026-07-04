import { cp, stat } from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";

const rootDir = process.cwd();
const standaloneDir = path.join(rootDir, ".next", "standalone");
const serverPath = path.join(standaloneDir, "server.js");

async function exists(target) {
  try {
    await stat(target);
    return true;
  } catch {
    return false;
  }
}

async function requirePath(target, message) {
  if (!await exists(target)) {
    throw new Error(message);
  }
}

async function copyDirectory(source, target, { optional = false } = {}) {
  if (!await exists(source)) {
    if (optional) return;
    throw new Error(`Missing required standalone source directory: ${source}`);
  }

  await cp(source, target, {
    recursive: true,
    force: true,
  });
}

await requirePath(serverPath, "Missing .next/standalone/server.js. Run `npm run build` before `npm start`.");
await copyDirectory(path.join(rootDir, ".next", "static"), path.join(standaloneDir, ".next", "static"));
await copyDirectory(path.join(rootDir, "public"), path.join(standaloneDir, "public"), { optional: true });

const server = spawn(process.execPath, [serverPath], {
  cwd: standaloneDir,
  env: process.env,
  stdio: "inherit",
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    server.kill(signal);
  });
}

server.on("exit", (code, signal) => {
  if (signal) {
    process.exit(1);
  }

  process.exit(code ?? 0);
});

server.on("error", (error) => {
  console.error(error);
  process.exit(1);
});
