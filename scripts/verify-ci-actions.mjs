import { readFile } from "node:fs/promises";

const workflowPath = new URL("../.github/workflows/ci.yml", import.meta.url);

const approvedReferences = new Map([
  ["actions/checkout", "9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0"],
  ["actions/setup-node", "48b55a011bda9f5d6aeb4c2d9c7362e8dae4041e"],
  ["actions/setup-dotnet", "26b0ec14cb23fa6904739307f278c14f94c95bf1"],
  ["actions/upload-artifact", "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"],
  ["actions/download-artifact", "37930b1c2abaa49bbe596cd826c3c89aef350131"],
  ["docker/login-action", "c94ce9fb468520275223c153574b00df6fe4bcc9"],
  ["docker/setup-buildx-action", "8d2750c68a42422c14e847fe6c8ac0403b4cbd6f"],
  ["docker/build-push-action", "10e90e3645eae34f1e60eeb005ba3a3d33f178e8"],
  ["aquasecurity/trivy-action", "ed142fd0673e97e23eac54620cfb913e5ce36c25"],
  ["anchore/sbom-action", "43a17d6e7add2b5535efe4dcae9952337c479a93"],
  ["actions/attest-build-provenance", "43d14bc2b83dec42d39ecae14e916627a18bb661"],
]);

const workflow = await readFile(workflowPath, "utf8");
const failures = [];
const usesPattern = /^\s*uses:\s*([^@\s]+)@([^\s#]+)/gm;

for (const match of workflow.matchAll(usesPattern)) {
  const [, action, reference] = match;
  if (!/^[0-9a-f]{40}$/.test(reference)) {
    failures.push(`${action}@${reference} must be pinned to a full immutable 40-character commit SHA.`);
  }

  const approved = approvedReferences.get(action);
  if (approved == null) {
    failures.push(`${action}@${reference} is not in the reviewed CI action allowlist.`);
  } else if (reference !== approved) {
    failures.push(`${action}@${reference} must use the reviewed commit ${approved}.`);
  }
}

for (const [action, reference] of approvedReferences) {
  if (!workflow.includes(`uses: ${action}@${reference}`)) {
    failures.push(`Workflow must retain reviewed action ${action}@${reference}.`);
  }
}

const count = (needle) => workflow.split(needle).length - 1;
const requireText = (needle, message) => {
  if (!workflow.includes(needle)) failures.push(message);
};

requireText(
  "push:\n    branches:\n      - main",
  "CI push events must be restricted to main so pull-request branches do not run duplicate push and pull-request suites.",
);
requireText(
  "group: ci-${{ github.event_name }}-${{ github.event.pull_request.number || github.ref || github.run_id }}",
  "CI must serialize superseded runs independently by event and branch or pull request.",
);
requireText("cancel-in-progress: true", "CI must cancel superseded runs in the same concurrency group.");
const caddyImage = "caddy:2@sha256:af5fdcd76f2db5e4e974ee92f96ee8c0fc3edb55bd4ba5032547cbf3f65e486d";
requireText(caddyImage, "The CI HTTPS ingress image must be pinned to the reviewed Caddy digest.");
if (count(caddyImage) !== 2) {
  failures.push("Both Caddy validation and HTTPS smoke must use the reviewed immutable image digest.");
}
requireText("--network bridge", "The CI HTTPS ingress must expose loopback ports through a non-internal bridge.");
requireText(
  'docker network connect "$frontend_network" accounts-production-smoke-ingress',
  "The CI HTTPS ingress must also join the private frontend network before it starts.",
);
requireText("--noproxy '*' --resolve accounts-smoke.local:443:127.0.0.1", "The HTTPS ingress probe must deterministically target runner loopback without a proxy.");
requireText(
  "NODE_EXTRA_CA_CERTS: ${{ github.workspace }}/.tmp/production-smoke-caddy/caddy-local-root.crt",
  "The Node capacity profile must trust the exact generated Caddy root certificate.",
);
if (workflow.includes("--network host")) {
  failures.push("The CI HTTPS ingress must not use host networking.");
}
if (/^\s+no_proxy:/m.test(workflow)) {
  failures.push("Use the canonical uppercase NO_PROXY key; GitHub treats case variants as duplicate environment keys.");
}

if (count(`uses: docker/build-push-action@${approvedReferences.get("docker/build-push-action")}`) !== 2) {
  failures.push("CI must invoke the container builder exactly once for backend and once for frontend.");
}
if (count(`uses: aquasecurity/trivy-action@${approvedReferences.get("aquasecurity/trivy-action")}`) !== 2) {
  failures.push("CI must retain one exact-image Trivy scan per application image.");
}
if (count(`uses: anchore/sbom-action@${approvedReferences.get("anchore/sbom-action")}`) !== 2) {
  failures.push("CI must retain one SPDX SBOM generation step per application image.");
}
if (count(`uses: actions/attest-build-provenance@${approvedReferences.get("actions/attest-build-provenance")}`) !== 2) {
  failures.push("CI must retain one GitHub provenance attestation per promoted image.");
}
if (/\bdocker\s+(?:build|buildx\s+build)\b/.test(workflow)) {
  failures.push("CI must not perform an additional raw Docker application-image build.");
}
if (count("persist-credentials: false") !== count(`uses: actions/checkout@${approvedReferences.get("actions/checkout")}`)) {
  failures.push("Every checkout must disable persisted Git credentials, including pull request and fork runs.");
}
if (count("push: ${{ steps.promotion-mode.outputs.enabled == 'true' }}") !== 2 ||
    count("load: ${{ steps.promotion-mode.outputs.enabled != 'true' }}") !== 2) {
  failures.push("Each application image build must push only for trusted promotion and load only for verification-only runs.");
}

requireText(
  `if [[ "$GITHUB_EVENT_NAME" == "push" && "$GITHUB_REF" == "refs/heads/main" ]]; then`,
  "Container promotion must be restricted to trusted pushes on main.",
);
requireText(
  "if: steps.promotion-mode.outputs.enabled == 'true'\n        uses: docker/login-action@",
  "GHCR credentials must be used only by the trusted promotion path.",
);
requireText("backend_ref='${{ steps.promotion-mode.outputs.backend-name }}'@\"$backend_digest\"", "Backend promotion must resolve an immutable digest reference.");
requireText("frontend_ref='${{ steps.promotion-mode.outputs.frontend-name }}'@\"$frontend_digest\"", "Frontend promotion must resolve an immutable digest reference.");
requireText("severity: HIGH,CRITICAL", "Trivy scans must fail the release on HIGH and CRITICAL findings.");
requireText("format: spdx-json", "Container SBOMs must use SPDX JSON.");
requireText("push-to-registry: true", "GitHub provenance attestations must be associated with the promoted registry images.");
requireText('docker pull "$ACCOUNTS_API_IMAGE"', "Production smoke must pull the exact backend digest.");
requireText('docker pull "$ACCOUNTS_FRONTEND_IMAGE"', "Production smoke must pull the exact frontend digest.");
requireText("./scripts/write-container-supply-chain-report.ps1", "CI must emit the machine-checkable container supply-chain report.");
requireText("./scripts/verify-container-supply-chain-report.ps1", "CI must verify container supply-chain evidence before release packing.");
requireText("name: container-supply-chain", "CI must retain the container supply-chain artifact.");
requireText('pattern: "!*.dockerbuild"', "CI evidence download must exclude Buildx record artifacts.");
requireText(
  "if: github.event_name == 'pull_request' || (github.event_name == 'push' && github.ref == 'refs/heads/main')",
  "The CI machine evidence check must run for protected-branch pull requests and trusted main pushes.",
);

if (failures.length > 0) {
  console.error("CI action policy failed:");
  for (const failure of failures) {
    console.error(`- ${failure}`);
  }
  process.exit(1);
}

console.log("CI action policy OK");
