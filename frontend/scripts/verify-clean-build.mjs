import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDir, "..");
const cacheKey = await dependencyCacheKey();
const tempRoot = path.join(os.tmpdir(), `accounts-frontend-clean-build-${cacheKey}`);
const buildRoot = path.join(tempRoot, "frontend");
const sourceNodeModules = path.join(frontendRoot, "node_modules");
const targetNodeModules = path.join(buildRoot, "node_modules");
const nextBin = path.join(targetNodeModules, "next", "dist", "bin", "next");
const ignoredTopLevel = new Set([
  "node_modules",
  ".next",
  ".next-probe",
  ".next-codex-probe",
  ".codex-acl-probe",
  "coverage",
  "out",
]);
const standaloneServer = ".next/standalone/server.js";

async function pathExists(target) {
  try {
    await fs.access(target);
    return true;
  } catch {
    return false;
  }
}

async function dependencyCacheKey() {
  const hash = createHash("sha256");

  for (const filename of ["package.json", "package-lock.json"]) {
    hash.update(filename);
    hash.update(await fs.readFile(path.join(frontendRoot, filename)));
  }

  return hash.digest("hex").slice(0, 16);
}

function isIgnoredTopLevel(name) {
  return ignoredTopLevel.has(name) || name.startsWith(".tmp-");
}

function run(command, args, options) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      ...options,
      stdio: "inherit",
      shell: false,
    });

    child.on("error", reject);
    child.on("exit", (code, signal) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`${command} ${args.join(" ")} failed with ${signal ?? `exit code ${code}`}`));
    });
  });
}

async function cleanBuildRoot() {
  await fs.mkdir(buildRoot, { recursive: true });

  for (const entry of await fs.readdir(buildRoot, { withFileTypes: true })) {
    if (entry.name === "node_modules") continue;

    await fs.rm(path.join(buildRoot, entry.name), {
      recursive: true,
      force: true,
      maxRetries: 3,
      retryDelay: 500,
    });
  }
}

async function copyProjectSource() {
  for (const entry of await fs.readdir(frontendRoot, { withFileTypes: true })) {
    if (isIgnoredTopLevel(entry.name)) continue;

    await fs.cp(path.join(frontendRoot, entry.name), path.join(buildRoot, entry.name), {
      recursive: true,
      errorOnExist: false,
      force: true,
    });
  }
}

async function copyDependencies() {
  try {
    await fs.access(sourceNodeModules);
  } catch {
    throw new Error("frontend/node_modules is missing. Run npm ci in frontend before npm run build:clean.");
  }

  if (await pathExists(path.join(targetNodeModules, "next", "package.json"))) {
    return;
  }

  await fs.cp(sourceNodeModules, targetNodeModules, {
    recursive: true,
    dereference: true,
    errorOnExist: false,
  });
}

async function assertStandaloneOutput() {
  const standalonePath = path.join(buildRoot, ...standaloneServer.split("/"));
  await fs.access(standalonePath);
}

try {
  await cleanBuildRoot();
  await copyProjectSource();
  await copyDependencies();

  await run(process.execPath, [nextBin, "build"], {
    cwd: buildRoot,
    env: {
      ...process.env,
      NEXT_TELEMETRY_DISABLED: "1",
      NEXT_TURBOPACK_USE_WORKER: process.env.NEXT_TURBOPACK_USE_WORKER ?? "0",
      NEXT_BUILD_WORKER_THREADS: process.env.NEXT_BUILD_WORKER_THREADS ?? "1",
    },
  });

  await assertStandaloneOutput();
  console.log(`Clean frontend build verified at ${buildRoot}`);
  console.log(`Reusable dependency cache retained at ${tempRoot}`);
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
