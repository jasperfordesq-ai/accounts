import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const digestPattern = "sha256:[0-9a-f]{64}";

function fromImages(dockerfile) {
  return [...dockerfile.matchAll(/^\s*FROM\s+(?:--platform=\S+\s+)?(\S+)/gim)].map(
    (match) => match[1],
  );
}

function packageEntries(lock) {
  const frameworks = Object.values(lock.dependencies ?? {});
  return frameworks.flatMap((framework) => Object.values(framework ?? {}));
}

export function evaluateBuildInputs(inputs) {
  const failures = [];
  const {
    policy,
    nvmrc,
    globalJson,
    packageJson,
    packageLock,
    directoryBuildProps,
    dockerfiles,
    nugetLocks,
    ciWorkflow,
    scheduledWorkflow,
    dependabot,
  } = inputs;

  if (nvmrc.trim() !== policy.nodeVersion) {
    failures.push(`.nvmrc must pin Node ${policy.nodeVersion} exactly.`);
  }
  if (globalJson.sdk?.version !== policy.dotnetSdkVersion) {
    failures.push(`global.json must pin .NET SDK ${policy.dotnetSdkVersion} exactly.`);
  }
  if (globalJson.sdk?.rollForward !== "disable") {
    failures.push("global.json must disable SDK roll-forward.");
  }

  const expectedNodeEngine = `>=${policy.nodeMajor} <${policy.nodeMajor + 1}`;
  const expectedNpmEngine = `>=${policy.npmMajor} <${policy.npmMajor + 1}`;
  if (packageJson.engines?.node !== expectedNodeEngine) {
    failures.push(`package.json Node engine must be ${expectedNodeEngine}.`);
  }
  if (packageJson.engines?.npm !== expectedNpmEngine) {
    failures.push(`package.json npm engine must be ${expectedNpmEngine}.`);
  }
  if (packageLock.lockfileVersion !== 3) {
    failures.push("package-lock.json must use lockfileVersion 3.");
  }
  if (packageLock.packages?.[""]?.engines?.node !== packageJson.engines?.node) {
    failures.push("package-lock.json Node engine does not match package.json.");
  }
  if (packageLock.packages?.[""]?.engines?.npm !== packageJson.engines?.npm) {
    failures.push("package-lock.json npm engine does not match package.json.");
  }

  if (!/<RestorePackagesWithLockFile>true<\/RestorePackagesWithLockFile>/.test(directoryBuildProps)) {
    failures.push("Directory.Build.props must enable NuGet lock files.");
  }
  for (const [name, lock] of Object.entries(nugetLocks)) {
    if (lock.version !== 1 || !lock.dependencies?.["net10.0"]) {
      failures.push(`${name} is not a complete net10.0 NuGet lock file.`);
      continue;
    }
    const incomplete = packageEntries(lock).find(
      (entry) => entry?.type !== "Project" && (!entry?.resolved || !entry?.contentHash),
    );
    if (incomplete) {
      failures.push(`${name} contains an unlocked package entry.`);
    }
  }

  const allImages = Object.values(dockerfiles).flatMap(fromImages);
  for (const image of allImages) {
    if (!new RegExp(`@${digestPattern}$`).test(image)) {
      failures.push(`Container base image is not digest-pinned: ${image}`);
    }
  }
  for (const [tag, digest] of Object.entries(policy.baseImages)) {
    const expected = `${tag}@${digest}`;
    const occurrences = allImages.filter((image) => image === expected).length;
    const expectedOccurrences = tag === "node:24-alpine" ? 3 : 1;
    if (occurrences !== expectedOccurrences) {
      failures.push(
        `Expected ${expectedOccurrences} exact FROM reference(s) for ${expected}; found ${occurrences}.`,
      );
    }
  }
  if (!dockerfiles.backend.includes("dotnet restore backend/Accounts.Api/Accounts.Api.csproj --locked-mode")) {
    failures.push("Backend container restore must use NuGet locked mode.");
  }

  if (!ciWorkflow.includes("dotnet restore backend/Accounts.slnx --locked-mode")) {
    failures.push("CI backend restore must use NuGet locked mode.");
  }
  if (!ciWorkflow.includes("global-json-file: global.json")) {
    failures.push("CI must install the exact SDK declared by global.json.");
  }
  if (!ciWorkflow.includes("node scripts/verify-build-inputs.mjs")) {
    failures.push("CI must enforce the build-input policy verifier.");
  }

  for (const required of [
    "schedule:",
    "workflow_dispatch:",
    "npm audit",
    "dotnet restore backend/Accounts.slnx --locked-mode",
    "aquasecurity/trivy-action@",
    "anchore/sbom-action@",
    "actions/upload-artifact@",
    "retention-days:",
    "node scripts/verify-security-audit-report.mjs",
  ]) {
    if (!scheduledWorkflow.includes(required)) {
      failures.push(`Scheduled security audit is missing: ${required}`);
    }
  }

  for (const ecosystem of ["npm", "nuget", "docker", "github-actions"]) {
    if (!new RegExp(`package-ecosystem:\\s*${ecosystem}`).test(dependabot)) {
      failures.push(`Dependabot does not cover ${ecosystem}.`);
    }
  }

  return failures;
}

export function collectRepoInputs(repoRoot) {
  const read = (relativePath) => fs.readFileSync(path.join(repoRoot, relativePath), "utf8");
  const readJson = (relativePath) => JSON.parse(read(relativePath));
  return {
    policy: readJson("config/build-input-policy.json"),
    nvmrc: read(".nvmrc"),
    globalJson: readJson("global.json"),
    packageJson: readJson("frontend/package.json"),
    packageLock: readJson("frontend/package-lock.json"),
    directoryBuildProps: read("backend/Directory.Build.props"),
    dockerfiles: {
      backend: read("Dockerfile.backend"),
      frontend: read("Dockerfile.frontend"),
    },
    nugetLocks: {
      "Accounts.Api/packages.lock.json": readJson("backend/Accounts.Api/packages.lock.json"),
      "Accounts.Tests/packages.lock.json": readJson("backend/Accounts.Tests/packages.lock.json"),
    },
    ciWorkflow: read(".github/workflows/ci.yml"),
    scheduledWorkflow: read(".github/workflows/scheduled-security-audit.yml"),
    dependabot: read(".github/dependabot.yml"),
  };
}

function main() {
  const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const failures = evaluateBuildInputs(collectRepoInputs(repoRoot));
  if (failures.length > 0) {
    console.error("Build-input policy failed:");
    for (const failure of failures) console.error(`- ${failure}`);
    process.exitCode = 1;
    return;
  }
  console.log("Build-input policy passed.");
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main();
}
