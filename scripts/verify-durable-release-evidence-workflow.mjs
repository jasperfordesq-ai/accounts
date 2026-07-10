import { readFile } from "node:fs/promises";

const workflowUrl = new URL(
  "../.github/workflows/publish-durable-release-evidence.yml",
  import.meta.url,
);
const workflow = await readFile(workflowUrl, "utf8");
const failures = [];

const reviewedActions = new Map([
  ["actions/checkout", "9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0"],
  ["actions/attest-build-provenance", "43d14bc2b83dec42d39ecae14e916627a18bb661"],
  ["actions/create-github-app-token", "fee1f7d63c2ff003460e3d139729b119787bc349"],
]);

for (const match of workflow.matchAll(/^\s*uses:\s*([^@\s]+)@([^\s#]+)/gm)) {
  const [, action, reference] = match;
  if (!/^[0-9a-f]{40}$/.test(reference)) {
    failures.push(`${action}@${reference} must be pinned to a full commit SHA.`);
  }
  if (!reviewedActions.has(action)) {
    failures.push(`${action} is not in the durable-publication action allowlist.`);
  } else if (reviewedActions.get(action) !== reference) {
    failures.push(`${action} must use reviewed commit ${reviewedActions.get(action)}.`);
  }
}

const requireText = (needle, message) => {
  if (!workflow.includes(needle)) failures.push(message);
};

requireText("workflow_call:", "Durable publication must be called from the private evidence repository.");
requireText("publication_authorized:", "Reusable publication must require an explicit authorization input.");
requireText("environment: durable-release-evidence", "Publication must use the protected durable-release-evidence environment.");
requireText('[[ "$GITHUB_EVENT_NAME" == "workflow_dispatch" ]]', "Private publication must originate from an explicit manual caller dispatch.");
requireText("GITHUB_WORKFLOW_REF", "Publication must bind the canonical private caller workflow ref.");
requireText("job_workflow_ref", "Publication must bind the exact reusable-workflow ref through GitHub OIDC.");
requireText("job_workflow_sha", "Publication must bind the exact reusable-workflow commit through GitHub OIDC.");
requireText("workflow_sha", "Publication must bind the exact private caller commit through GitHub OIDC.");
requireText('repository_visibility == "private"', "Publication OIDC claims must prove a private allowlisted caller.");
requireText('.environment == "durable-release-evidence"', "Publication OIDC claims must bind the protected evidence environment.");
requireText("cancel-in-progress: false", "Concurrent publication must never cancel an in-flight immutable publication.");
requireText("persist-credentials: false", "Checkout must not persist Git credentials.");
requireText("./scripts/test-durable-release-evidence.ps1", "Workflow must execute behavioral signature tests.");
requireText("./scripts/test-durable-release-publication-inventory.ps1", "Workflow must execute publication-inventory adversarial tests.");
requireText(".head_sha", "Workflow must bind the source CI run to the candidate commit.");
requireText(".head_branch", "Workflow must require a trusted main-branch candidate run.");
requireText(".conclusion", "Workflow must require a successful completed candidate run.");
requireText(".github/workflows/ci.yml", "Workflow must bind candidate and evidence runs to canonical CI.");
requireText("path: trusted-source", "Verifier scripts must be checked out from the trusted candidate commit.");
requireText("path: evidence-source", "Evidence data must use a separate non-executable checkout.");
requireText("Join-Path $env:TRUSTED_SOURCE", "Publication must execute verifier code from the trusted candidate checkout.");
requireText("jasperfordesq-ai/accounts-release-evidence", "Publication must be hard-bound to the private evidence repository.");
requireText("Evidence repository must remain private", "Publication must API-verify private evidence storage.");
requireText("Evidence default branch must be protected", "Publication must require protected evidence history.");
requireText("Evidence commit must have a verified signature", "Publication must require a verified evidence commit signature.");
requireText(".github/workflows/evidence-ci.yml", "Publication must require canonical private evidence CI.");
requireText('-f branch="$evidence_default_branch"', "Evidence CI lookup must filter the protected default branch.");
requireText('.head_branch == $branch', "Evidence CI result must prove the protected default branch.");
requireText("secret_scanning_push_protection", "Publication must require evidence-repository push protection.");
requireText("/environments/durable-release-evidence", "Publication must inspect the private caller repository environment through the API.");
requireText(".can_admins_bypass // true", "Publication environment must fail closed when administrator bypass is enabled or unknown.");
requireText('.type == "required_reviewers" and .prevent_self_review == true', "Publication environment must require an independent reviewer and prevent self-review.");
requireText(".deployment_branch_policy.protected_branches // false", "Publication environment must restrict deployment to protected branches.");
requireText("Candidate must be the current protected application main head", "Publication must bind the exact current application candidate.");
requireText("Application candidate commit must have a verified signature", "Publication must require verified application commit identity.");
requireText('candidate_completed_at="$(jq -r .updated_at <<<"$run_json")"', "Publication must bind the validated Actions run update/completion time.");
requireText("actions/create-github-app-token@", "Publication must use a short-lived evidence-repository GitHub App token.");
requireText("permission-administration: read", "Evidence token must have immutable-release preflight access.");
requireText("permission-contents: read", "Governance App tokens must be read-only for repository contents.");
requireText("release_governance_app_id:", "Publication must receive the protected read-only governance App identity.");
requireText("id: application-token", "Publication must mint a separately repository-scoped application governance token.");
requireText("/branches/$branch/protection", "Publication must inspect exact branch protection rather than a coarse protected flag.");
requireText("required_approving_review_count >= 1", "Publication must require independent pull-request review.");
requireText("require_code_owner_reviews == true", "Publication must require code-owner review.");
requireText(".enforce_admins.enabled == true", "Publication must require administrator branch-protection enforcement.");
requireText(".allow_force_pushes.enabled == false", "Publication must block force pushes.");
requireText(".allow_deletions.enabled == false", "Publication must block protected-branch deletion.");
requireText("/protection/required_signatures", "Publication must require signed commits in both trust repositories.");
requireText('"Evidence CI"', "Private evidence branch protection must require canonical Evidence CI.");
requireText("immutable-releases", "Workflow must fail closed unless GitHub immutable releases are enabled.");
requireText(".enabled // false", "Workflow must assert that immutable releases are actually enabled.");
requireText("Refusing to overwrite existing release", "Workflow must refuse an existing durable release.");
requireText("Refusing to reuse existing tag", "Workflow must refuse an existing durable evidence tag.");
requireText("BEGIN ([A-Z0-9 ]+ )?PRIVATE KEY", "Workflow must reject private-key material from evidence assets.");
requireText("repository-relative path using safe filename characters", "Workflow must constrain evidence-directory input before writing step outputs.");
requireText("path segments must not begin with a hyphen", "Workflow must block path-to-tar option injection.");
requireText("verify-durable-release-publication-inventory.ps1", "Workflow must enforce an exact manifest-derived publication inventory.");
requireText('-C "$staging" -- .', "Archive creation must terminate options and root itself inside trusted staging.");
requireText("verify-release-evidence.ps1", "Workflow must retain substantive human-evidence verification.");
requireText("verify-durable-release-evidence.ps1", "Workflow must retain detached-signature verification.");
requireText("verify-release-artifact-pack.ps1", "Workflow must retain full release-artifact verification.");
requireText("trust_policy_sha256", "Workflow must require an out-of-band trust-policy digest.");
requireText("actions/attest-build-provenance@", "Workflow must attest the exact published archive.");
requireText("gh release create", "Workflow must publish the verified bundle as a GitHub Release.");
requireText("gh release verify ", "Workflow must verify GitHub's immutable release attestation.");
requireText("gh release verify-asset", "Workflow must verify the exact published release asset.");
requireText("gh attestation verify", "Workflow must verify explicit workflow provenance.");
requireText('--target "$EVIDENCE_REF"', "Private release tag must target the verified evidence-repository commit.");
requireText("GH_TOKEN: ${{ github.token }}", "Release creation and attestation verification must use the caller repository token with attestation access.");

if (workflow.includes("workflow_dispatch:")) {
  failures.push("The public application repository must not directly dispatch evidence publication.");
}

const verificationStepStart = workflow.indexOf("- name: Verify substantive evidence, signatures, artifact pack, and exact inventory");
const verificationStepEnd = workflow.indexOf("- name: Build exact candidate-bound private evidence archive");
const verificationStep = workflow.slice(verificationStepStart, verificationStepEnd);
if (verificationStep.includes("GH_TOKEN") || verificationStep.includes("evidence-token.outputs.token")) {
  failures.push("Candidate verifier code must never receive the evidence-repository token.");
}

const callerValidationStart = workflow.indexOf("- name: Validate allowlisted caller and reusable-workflow provenance");
const tokenCreationStart = workflow.indexOf("- name: Create short-lived evidence-repository administration token");
const privateCheckoutStart = workflow.indexOf("- name: Check out private evidence commit as data only");
if (
  callerValidationStart < 0 ||
  tokenCreationStart < 0 ||
  privateCheckoutStart < 0 ||
  callerValidationStart > tokenCreationStart ||
  tokenCreationStart > privateCheckoutStart
) {
  failures.push("Allowlisted caller/OIDC validation must run before any secret-backed token minting or private evidence checkout.");
}

const releaseStepStart = workflow.indexOf("- name: Create publication report and private immutable GitHub Release");
const provenanceStepStart = workflow.indexOf("- name: Verify private provenance with caller repository token");
const releaseStep = workflow.slice(releaseStepStart, provenanceStepStart);
if (!releaseStep.includes("GH_TOKEN: ${{ github.token }}") || releaseStep.includes("evidence-token.outputs.token")) {
  failures.push("Release creation and immutable-release verification must use the caller token, never the administration-only App token.");
}

const finalRevalidationStart = workflow.indexOf("- name: Revalidate live controls immediately before publication");
if (finalRevalidationStart < 0 || finalRevalidationStart > releaseStepStart) {
  failures.push("Live repository, CI, environment, immutability, and tag controls must be revalidated immediately before publication.");
} else {
  const finalRevalidation = workflow.slice(finalRevalidationStart, releaseStepStart);
  for (const requiredControl of [
    "publication governance changed during verification",
    "Application main moved during evidence verification",
    "Evidence main moved during evidence verification",
    "Application candidate CI identity changed",
    "Evidence candidate CI is no longer",
    "administrator bypass changed",
    "independent-review policy changed",
    "branch policy changed",
    "immutable releases were disabled",
    "release appeared during verification",
    "tag appeared during verification",
  ]) {
    if (!finalRevalidation.includes(requiredControl)) {
      failures.push(`Final pre-publication revalidation is missing control: ${requiredControl}.`);
    }
  }
}

if (workflow.includes("check_suite_json") || workflow.includes(".completed_at <<<\"$check_suite_json\"")) {
  failures.push("Candidate completion time must not use the nonexistent check-suite completed_at field.");
}

if (workflow.includes("--clobber")) {
  failures.push("Durable publication must never use an overwrite/clobber option.");
}
if (workflow.includes("permission-contents: write")) {
  failures.push("Read-only governance App tokens must not request Contents write.");
}
if (/\bgh\s+release\s+(?:delete|edit|upload)\b/.test(workflow)) {
  failures.push("Durable publication must not delete, edit, or append to a release.");
}

if (failures.length > 0) {
  console.error("Durable release-evidence workflow policy failed:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log("Durable release-evidence workflow policy OK");
