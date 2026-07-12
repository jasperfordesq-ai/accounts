import { readFile } from "node:fs/promises";

const workflowPath = new URL("../.github/workflows/private-server-release.yml", import.meta.url);
const workflow = (await readFile(workflowPath, "utf8")).replace(/\r\n/g, "\n");
const failures = [];

const approvedActions = new Map([
  ["actions/checkout", "9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0"],
  ["actions/upload-artifact", "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"],
  ["actions/download-artifact", "37930b1c2abaa49bbe596cd826c3c89aef350131"],
  ["actions/attest-build-provenance", "43d14bc2b83dec42d39ecae14e916627a18bb661"],
]);

for (const match of workflow.matchAll(/^\s*uses:\s*([^@\s]+)@([^\s#]+)/gm)) {
  const [, action, reference] = match;
  const approved = approvedActions.get(action);
  if (!/^[0-9a-f]{40}$/.test(reference)) {
    failures.push(`${action}@${reference} is not pinned to a full commit SHA.`);
  } else if (approved == null) {
    failures.push(`${action}@${reference} is not in the Private Server release allowlist.`);
  } else if (reference !== approved) {
    failures.push(`${action}@${reference} must use reviewed commit ${approved}.`);
  }
}

for (const [action, reference] of approvedActions) {
  const needle = `uses: ${action}@${reference}`;
  if (!workflow.includes(needle)) failures.push(`Workflow must retain ${needle}.`);
}

const requireText = (needle, message) => {
  if (!workflow.includes(needle)) failures.push(message);
};

const requireJobText = (job, needle, message) => {
  if (!job.includes(needle)) failures.push(message);
};

requireText("workflow_dispatch:", "Private releases must be explicitly dispatched.");
requireText("environment: private-server-release", "Private releases must use the protected release environment.");
requireText("permissions:\n  contents: read\n  actions: read", "The workflow default token must be read-only.");
requireText("cancel-in-progress: false", "A started release must not be silently cancelled by a second dispatch.");
requireText("persist-credentials: false", "The candidate checkout must not persist Git credentials.");
requireText("== \"CI\"", "The candidate must be produced by the canonical CI workflow.");
requireText("/actions/workflows/ci.yml", "Candidate resolution must look up the canonical CI workflow identity by file name.");
requireText("== \".github/workflows/ci.yml\"", "Candidate resolution must bind the run to the canonical CI workflow path.");
requireText(".workflow_id", "Candidate resolution must bind the run to the canonical CI workflow ID.");
requireText("== \"push\"", "The candidate must come from a trusted push event.");
requireText("== \"main\"", "The candidate must come from main.");
requireText("== \"success\"", "The candidate CI run must have succeeded.");
requireText("ref: refs/heads/main", "Candidate checkouts must use the constant protected main ref.");
requireText("Verify checked-out main matches candidate", "Preparation must compare checked-out main with the resolved candidate SHA.");
requireText('"$(git rev-parse HEAD)" == "$CANDIDATE_SHA"', "Preparation must fail unless checked-out main exactly matches the resolved candidate SHA.");
requireText("--name container-supply-chain", "The exact candidate supply-chain artifact must be downloaded.");
requireText("./scripts/verify-container-supply-chain-report.ps1", "Promoted supply-chain evidence must be verified.");
requireText("./scripts/verify-private-compose.ps1", "The Private Server topology must be verified before release.");
requireText("docker logout ghcr.io", "The workflow must prove anonymous public GHCR pulls.");
requireText("docker pull --platform linux/amd64 \"$backend\"", "The exact backend digest must be pulled for x64.");
requireText("docker pull --platform linux/amd64 \"$frontend\"", "The exact frontend digest must be pulled for x64.");
requireText("docker pull --platform linux/amd64 \"$POSTGRES_IMAGE\"", "The exact PostgreSQL digest must be pulled for x64.");
requireText("test -f /etc/alpine-release", "The selected PostgreSQL runtime must be independently identified as Alpine.");
requireText("postgres (PostgreSQL) 16.", "The selected PostgreSQL runtime must remain on the reviewed PostgreSQL 16 line.");
requireText("./scripts/build-private-server-release.ps1", "The reviewed release builder must create the asset.");
requireText("./scripts/verify-private-server-release.ps1", "The completed asset must be independently verified.");
requireText("needs:\n      - prepare", "The protected publishing job must consume the completed preparation job.");
requireText("Independently recheck asset identity and inventory", "The protected publishing job must independently verify the downloaded asset.");
requireText("unsafe or duplicate archive entry", "The protected job must reject archive traversal, links, duplicates, and expansion bombs before extraction.");
requireText("Download trusted candidate container evidence independently", "The protected job must fetch trusted CI supply-chain evidence itself.");
requireText("container-supply-chain-protected", "Protected publication must use a distinct trusted-evidence directory.");
requireText("Trusted candidate supply-chain evidence is invalid or mismatched", "Protected publication must re-verify trusted candidate identity and report binding.");
requireText("Private Server release version must be valid semantic version syntax", "The protected job must validate release semantic-version syntax before using it as an asset name.");
requireText("manifest.generatedAtUtc", "The protected job must require a UTC release-manifest generation timestamp.");
requireText("@($manifest.supportedHosts).Count -ne 1", "The protected job must require exactly one supported host.");
requireText("[string]$manifest.supportedHosts[0] -cne 'windows-x64'", "The protected job must require exactly windows-x64.");
requireText("Prepared release manifest files inventory must be nonempty", "The protected job must reject an empty manifest file inventory.");
requireText("Assert-PackagedTrustedEvidence", "The protected job must compare packaged evidence with independently downloaded trusted evidence.");
requireText("container-supply-chain-verification-report.json", "The protected job must retain the trusted supply-chain verification report.");
requireText("Packaged retained evidence metadata differs from trusted verification", "The protected job must compare every retained evidence file hash and size with trusted verification metadata.");
requireText("manifest.images.backend.exactDigestReference -cne [string]$backendEvidence[0].exactDigestReference", "The protected job must bind the bundle backend image to trusted CI evidence.");
requireText("manifest.images.frontend.exactDigestReference -cne [string]$frontendEvidence[0].exactDigestReference", "The protected job must bind the bundle frontend image to trusted CI evidence.");
requireText("manifest.images.postgres.exactDigestReference -cne $env:POSTGRES_IMAGE", "The protected job must bind PostgreSQL to the reviewed workflow input.");
requireText("subject-path: ${{ steps.bundle.outputs.archive }}", "Provenance must cover the exact ZIP.");
requireText("if gh release view \"$tag\"", "Release creation must refuse an existing version/tag.");
requireText("/git/ref/tags/$tag", "Release creation must refuse a pre-existing orphan git tag.");
requireText("--draft", "Private Server releases must be created as drafts for human review.");
requireText("Statutory use remains blocked", "Release notes must preserve the statutory acceptance boundary.");
requireText("Direct CRO/ROS submission remains unsupported", "Release notes must preserve the no-direct-filing boundary.");

const prepareStart = workflow.indexOf("  prepare:");
const publishStart = workflow.indexOf("  publish-draft:");
if (prepareStart < 0 || publishStart < 0 || publishStart <= prepareStart) {
  failures.push("The release workflow must retain separate prepare and publish-draft jobs.");
} else {
  const prepareJob = workflow.slice(prepareStart, publishStart);
  const publishJob = workflow.slice(publishStart);
  for (const [needle, message] of [
    ['"/repos/$GITHUB_REPOSITORY/actions/workflows/ci.yml"', "The preparation job must resolve the canonical CI workflow object."],
    ['"$(jq -r \'.workflow_id\' <<<"$run_json")" == "$workflow_id"', "The preparation job must bind the run workflow ID to the canonical CI workflow object."],
    ['"$(jq -r \'.path\' <<<"$run_json")" == ".github/workflows/ci.yml"', "The preparation job must bind the run to the canonical CI workflow path."],
    ['"$(jq -r \'.repository.full_name\' <<<"$run_json")" == "$GITHUB_REPOSITORY"', "The preparation job must bind the candidate repository identity."],
    ['workflow-id=%s', "The preparation job must retain the resolved canonical workflow ID."],
  ]) {
    requireJobText(prepareJob, needle, message);
  }
  for (const forbiddenPermission of ["contents: write", "id-token: write", "attestations: write"]) {
    if (prepareJob.includes(forbiddenPermission)) {
      failures.push(`The candidate-controlled prepare job must not receive ${forbiddenPermission}.`);
    }
  }
  requireText("actions/download-artifact@37930b1c2abaa49bbe596cd826c3c89aef350131", "The protected job must download the prepared asset using the reviewed action.");
  if (!publishJob.includes("contents: write") || !publishJob.includes("id-token: write") || !publishJob.includes("attestations: write")) {
    failures.push("Only the protected publishing job may receive release and attestation write permissions.");
  }
  for (const [needle, message] of [
    ["Re-resolve trusted candidate independently", "The protected job must independently re-query candidate identity."],
    ['"/repos/$GITHUB_REPOSITORY/actions/runs/$RUN_ID"', "The protected job must independently re-query the exact candidate run."],
    ['"/repos/$GITHUB_REPOSITORY/actions/workflows/ci.yml"', "The protected job must independently resolve the canonical CI workflow object."],
    ['"$(jq -r \'.workflow_id\' <<<"$run_json")" == "$workflow_id"', "The protected job must bind the run workflow ID to the canonical CI workflow object."],
    ['"$(jq -r \'.path\' <<<"$run_json")" == ".github/workflows/ci.yml"', "The protected job must bind the run to the canonical CI workflow path."],
    ['"$(jq -r \'.repository.full_name\' <<<"$run_json")" == "$GITHUB_REPOSITORY"', "The protected job must independently bind repository identity."],
    ['Protected candidate resolution differs from preparation.', "The protected candidate resolution must match the prepared candidate."],
    ['CANDIDATE_SHA: ${{ steps.protected-candidate.outputs.sha }}', "Protected validation and publication must use the independently resolved candidate SHA."],
    ['CANDIDATE_RUN_URL: ${{ steps.protected-candidate.outputs.run-url }}', "Protected validation and publication must use the independently resolved run URL."],
    ["Check out exact candidate for byte comparison only", "The protected job must check out the exact independently resolved candidate as inert comparison data."],
    ["ref: refs/heads/main", "The protected comparison checkout must use the constant protected main ref."],
    ["path: protected-candidate-source", "The protected comparison checkout must use a distinct source directory."],
    ["persist-credentials: false", "The protected comparison checkout must not persist write-capable credentials."],
    ["CANDIDATE_SOURCE_ROOT: ${{ github.workspace }}/protected-candidate-source", "Protected validation must receive the exact candidate comparison checkout path."],
    ["Source checkout does not match the independently resolved candidate commit", "Protected validation must verify comparison-checkout HEAD."],
    ["Source checkout is not a clean representation of the candidate commit", "Protected validation must reject modified or untracked comparison sources."],
    ["git -C $candidateSourceRoot ls-files --stage", "Protected validation must require every source payload path to be a regular tracked candidate file (not a symlink or submodule)."],
    ["Manifest source payload differs from the exact candidate commit", "Protected validation must byte-compare every source payload with the exact candidate checkout."],
    ["Prepared release evidence inventory is not exactly the independently trusted evidence set", "Protected validation must reject untrusted extra evidence payloads."],
    ['"archive-sha256=$archiveHash"', "Protected validation must expose the exact attested subject digest."],
    ['"candidate-sha=$env:CANDIDATE_SHA"', "Protected validation must expose the candidate bound into the attested subject manifest."],
    ['CANDIDATE_SHA: ${{ steps.bundle.outputs.candidate-sha }}', "Release creation must use the candidate identity emitted only after protected bundle validation."],
    ["Attested subject:", "Release notes must identify the exact attestation subject."],
    ["manifest candidate was independently byte-matched", "Release notes must state the candidate-to-attested-subject association."],
  ]) {
    requireJobText(publishJob, needle, message);
  }
  for (const requiredPackageFile of [
    "FilingBridge.cmd",
    "compose.private.yml",
    ".env.private.example",
    "scripts/private-server.ps1",
    "scripts/PrivateServer/PrivateServer.psm1",
    "scripts/smoke-production.ps1",
    "Docs/deployment/README.md",
    "Docs/deployment/private-server.md",
    "Docs/deployment/LOCAL_WINDOWS_READINESS.md",
    "deploy/private/release-manifest.schema.json",
    "README.md",
    "LICENSE",
    "NOTICE",
    "THIRD_PARTY_NOTICES.md",
    "CONTRIBUTORS.md",
  ]) {
    requireJobText(publishJob, `'${requiredPackageFile}'`, `Protected validation must require package file ${requiredPackageFile}.`);
  }
  const protectedCheckouts = [...publishJob.matchAll(/^\s*uses:\s*actions\/checkout@/gm)].length;
  if (protectedCheckouts !== 1) {
    failures.push("The write-capable publishing job must use exactly one inert candidate checkout for byte comparison.");
  }
  if (publishJob.includes("./scripts/") || publishJob.includes("working-directory: protected-candidate-source")) {
    failures.push("The write-capable publishing job must not execute candidate repository code.");
  }
}

for (const forbidden of ["pull_request_target:", "docker build ", "docker buildx build", ":latest", "tailscale funnel"]) {
  if (workflow.toLowerCase().includes(forbidden.toLowerCase())) {
    failures.push(`Workflow contains forbidden release text: ${forbidden}`);
  }
}

if (failures.length > 0) {
  console.error("Private Server release workflow policy failed:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log("Private Server release workflow policy OK");
