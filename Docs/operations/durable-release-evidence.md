# Durable Release-Evidence Signing and Publication

This control prepares release evidence for independent authenticity checks without
pretending that a generated file is human approval. `P2-EVID-001` remains open until
real named reviewers complete the six templates, all required detached signatures
verify, and the resulting bundle is retrievable from an immutable GitHub Release.

The normal substantive verifier remains authoritative for the content of the six
human templates. The cryptographic verifier adds integrity, identity binding and
certificate trust; it does not decide whether an accountant's professional judgement
is correct.

## Trust model

Every signature is detached from the Markdown template. The signature envelope
contains an exact-byte JSON statement that binds:

- the 40-character candidate commit, exact GitHub Actions run URL, and the validated
  successful canonical Actions run's `updated_at` value;
- the template file name, byte size and SHA-256;
- one required signer slot, the signer's name and professional capacity;
- an independently checkable HTTPS credential reference;
- the signing certificate SHA-256 fingerprint, RFC 2253 subject and serial number;
- the UTC signing time.

OpenSSL signs those exact statement bytes with SHA-256. The verifier checks the
signature, template hash/size, candidate identity, an extension-specific leaf policy
(`Digital Signature`, `CA:FALSE`, and email-protection or code-signing EKU), and the
certificate chain both at signing time and at verification/publication time. Signing
cannot predate the successful candidate CI completion time. It also requires a separate
trust policy whose SHA-256 is provided out of band. The policy pins the expected
name, capacity, credential reference, certificate subject/fingerprint and trusted CA
root for every slot, plus who checked the credential and where that check is retained.
The certificate's RFC 2253 common name must exactly equal the policy-pinned signer
name. A certificate or self-authored sidecar is therefore insufficient by itself.

The required files are:

| Template | Required signer slot(s) |
| --- | --- |
| `visual-qa-signoff-template.md` | `visual-qa-reviewer` |
| `source-law-review-template.md` | `source-law-reviewer`, `source-law-qualified-accountant` |
| `external-ros-ixbrl-validation-template.md` | `external-ros-ixbrl-reviewer` |
| `qualified-accountant-acceptance-template.md` | `qualified-accountant` |
| `manual-handoff-acceptance-template.md` | `manual-handoff-reviewer` |
| `monitoring-provider-confirmation-template.md` | `monitoring-release-operator` |

Source-law review deliberately has two independent signatures. The verifier rejects
the same person or certificate in both slots. Every credential verifier must also be
a different named person from the signer.

Each sidecar name is deterministic:

```text
<template-file>.<signer-slot>.signature.json
```

Private keys must never be copied into the reviewer workspace, signature sidecar, trust
policy or published bundle. Use a hardware-backed or otherwise controlled signing key
where available. The helper accepts an OpenSSL-readable key only for the signing
operation, rejects keys/passphrase files inside the template directory, and refuses to
overwrite an existing sidecar. Credential URLs must be public HTTPS references without
userinfo, query strings, or fragments; never put bearer tokens in a signed reference.

## Trust-policy schema

Create `release-evidence-trust-policy.json` beside the six completed templates. CA
certificate paths are relative to the policy file and must remain inside that
directory. The out-of-band policy digest should be approved through a protected
release record or a second controlled channel before publication.

```json
{
  "schemaVersion": "accounts.release-evidence.trust-policy/v1",
  "releaseCandidate": {
    "commitSha": "<40 lowercase hexadecimal characters>",
    "githubActionsRunUrl": "https://github.com/<owner>/<repo>/actions/runs/<id>",
    "githubActionsCompletedAtUtc": "2026-07-10T12:00:00.0000000+00:00"
  },
  "publicationManifest": {
    "fileName": "release-evidence-publication-manifest.json",
    "sha256": "<64 lowercase hexadecimal characters>"
  },
  "trustedRoots": [
    {
      "rootId": "reviewer-identity-ca-2026",
      "certificateFile": "trust/reviewer-identity-ca-2026.pem",
      "sha256Fingerprint": "<64 lowercase hexadecimal characters>"
    }
  ],
  "signers": [
    {
      "templateFile": "qualified-accountant-acceptance-template.md",
      "signerSlot": "qualified-accountant",
      "expectedName": "<real name>",
      "expectedProfessionalCapacity": "<real qualification and review capacity>",
      "expectedCredentialReference": "https://<professional-register-or-verification-reference>",
      "expectedCertificateSubjectRfc2253": "CN=<certificate subject>",
      "allowedCertificateFingerprintsSha256": [
        "<64 lowercase hexadecimal characters>"
      ],
      "trustedRootId": "reviewer-identity-ca-2026",
      "credentialVerification": {
        "status": "verified",
        "verifiedAtUtc": "2026-07-10T12:00:00Z",
        "verifiedBy": "<independent verifier name and capacity>",
        "evidenceReference": "https://<retained-verification-evidence>"
      }
    }
  ]
}
```

`githubActionsCompletedAtUtc` is not a reviewer-entered timestamp and is not a check,
job, artifact, or local verification time. The private publication workflow validates
that the supplied run ID is the successful canonical `main` push run for the exact
candidate, reads that Actions run's `updated_at`, and supplies it as
`CandidateRunCompletedAtUtc`. The signing helper and both durable verifiers normalize
that value to the round-trip UTC form shown above. The trust policy, publication
manifest, every signed statement, and generated verification reports must agree.

The pinned `release-evidence-publication-manifest.json` uses schema
`accounts.release-evidence.publication-manifest/v1`. It contains only
`schemaVersion`, `releaseCandidate`, and `files`. Each `files` row contains exactly
`relativePath`, positive `byteSize`, lowercase `sha256`, exact `mediaType`, and a
lowercase `classification`. It inventories every regular file in the evidence
directory except the manifest itself and `release-evidence-trust-policy.json`; an
unlisted extra or missing row fails verification. Publication-time recheck reports
are generated outside that immutable input directory and added later under
`verified-publication/` in the release archive. The complete input namespace
`verified-publication/**` is therefore reserved and forbidden. Paths are compared
case-insensitively on every runner, and Windows device basenames such as `CON`, `NUL`,
`AUX`, `COM1` and `LPT1` are rejected so an archive cannot become ambiguous or unsafe
when inspected on another operating system.

Exactly five rows must have classification `external-ixbrl`. They are the raw,
uncompressed HTML/XHTML inputs actually submitted to external validation—not a PDF,
screenshot, archive, digest-only placeholder, or regenerated excerpt. Each has a
distinct SHA-256, uses one of these canonical base names, and its exact hash must be
recorded in the matching row of the signed
`external-ros-ixbrl-validation-template.md`:

| Canonical scenario | Conventional retained path | Required content |
| --- | --- | --- |
| `micro-ltd` | `external-ixbrl/micro-ltd.xhtml` | Raw micro-company iXBRL HTML/XHTML |
| `small-abridged-ltd` | `external-ixbrl/small-abridged-ltd.xhtml` | Raw small-abridged iXBRL HTML/XHTML |
| `dac-small` | `external-ixbrl/dac-small.xhtml` | Raw small-DAC iXBRL HTML/XHTML |
| `clg-charity` | `external-ixbrl/clg-charity.xhtml` | Raw CLG/charity iXBRL HTML/XHTML |
| `medium-audit-required` | `external-ixbrl/medium-audit-required.xhtml` | Raw audit-required handoff iXBRL HTML/XHTML |

The verifier accepts `.html` or `.xhtml`, but the file base name must be the exact
scenario code above and there must be exactly one distinct inventoried file per
scenario. The signed external-validation template and its
`external-ros-ixbrl-reviewer` sidecar are themselves mandatory manifest entries. Raw
iXBRL remains untrusted active-document evidence: the verifier requires well-formed,
DTD-free XHTML/XML and rejects script/event handlers, active embedded elements, external
HTML URL attributes, remote/active CSS and similar browser loading paths, but reviewers
must still use an offline sandboxed viewer.

The real policy must contain exactly seven signer entries matching the table above.
Do not use the example values as evidence. A credential URL is a signed and
policy-pinned reference, not proof that the professional register was checked; the
named `credentialVerification` record is still required. Certificate revocation or
professional-register status must be checked under the issuing authority's process
at review time and retained at the policy's evidence reference. The verifier rejects
a credential check performed after signing or more than 30 days before signing.
Each trusted-root file must contain exactly one PEM certificate. The separately pinned
publication manifest lists every regular evidence file other than itself and the trust
policy; unlisted extras, missing files, archives, opaque binaries, unsafe HTML, and hash
or size drift fail publication. It must include five distinct uncompressed HTML/XHTML
`external-ixbrl` entries named for the canonical golden scenarios, with hashes matching
the signed external ROS/iXBRL template.

## Signing

Locate the OpenSSL supplied by the host or Git for Windows automatically, or pass
`-OpenSslPath`. Run the helper once per required slot after the corresponding
Markdown content is final:

```powershell
.\scripts\new-release-evidence-signature.ps1 `
  -TemplatePath D:\release-evidence\qualified-accountant-acceptance-template.md `
  -SignerSlot qualified-accountant `
  -SignerName "<real accountant name>" `
  -ProfessionalCapacity "<qualification and release-review capacity>" `
  -CredentialReference "https://<independent credential reference>" `
  -CommitSha <candidate-commit-sha> `
  -GitHubActionsRunUrl <exact-candidate-ci-run-url> `
  -CandidateRunCompletedAtUtc <validated-actions-run-updated-at-utc> `
  -CertificatePath D:\reviewer-certs\accountant.pem `
  -PrivateKeyPath D:\controlled-keys\accountant.key.pem `
  -PrivateKeyPassphraseFile D:\controlled-secrets\accountant-key-passphrase.txt
```

The `CandidateRunCompletedAtUtc` argument above must be the canonical run
`updated_at` value described in the trust-policy section, not a manually chosen time.
The reusable publication workflow independently reads it again from GitHub and fails
if the policy, manifest, statements, or verifier inputs drift.

If a template changes after signing, discard the whole candidate signature set under
the controlled restart process, re-run the substantive review, and create new
sidecars. Never delete a sidecar merely to bypass the helper's overwrite refusal.

## Verification

Run the existing content verifier first, followed by detached-signature verification:

```powershell
.\scripts\verify-release-evidence.ps1 `
  -EvidenceDirectory D:\release-evidence `
  -ReportPath D:\release-evidence\release-evidence-report.json

.\scripts\verify-durable-release-evidence.ps1 `
  -EvidenceDirectory D:\release-evidence `
  -TrustPolicyPath D:\release-evidence\release-evidence-trust-policy.json `
  -TrustPolicySha256 <out-of-band-policy-sha256> `
  -CommitSha <candidate-commit-sha> `
  -GitHubActionsRunUrl <exact-candidate-ci-run-url> `
  -CandidateRunCompletedAtUtc <validated-actions-run-updated-at-utc> `
  -ReportPath D:\verification-output\durable-release-evidence-report.json

.\scripts\verify-durable-release-publication-inventory.ps1 `
  -EvidenceDirectory D:\release-evidence `
  -TrustPolicyPath D:\release-evidence\release-evidence-trust-policy.json `
  -TrustPolicySha256 <out-of-band-policy-sha256> `
  -CommitSha <candidate-commit-sha> `
  -GitHubActionsRunUrl <exact-candidate-ci-run-url> `
  -CandidateRunCompletedAtUtc <validated-actions-run-updated-at-utc> `
  -ReportPath D:\verification-output\durable-publication-inventory-report.json
```

`durable-release-evidence-report.json` records the candidate, pinned and actual
policy digests, root count, all seven signature results, certificate identifiers,
public-key algorithm/bit strength, name/capacity/credential bindings and failures. It
uses only the pinned root (`-no-CApath -no-CAstore`), OpenSSL authentication level 2 and
strict X.509 checks at both signing and verification time. It fails closed for a missing
or recursively discovered additional sidecar, changed template, changed statement,
invalid signature, wrong
candidate, untrusted chain, fingerprint/subject/name/capacity mismatch, malformed
credential evidence, current certificate validity, certificate extension policy,
source-reviewer independence, decoded private-key markers, or a changed trust policy.
The inventory report must be written outside the immutable evidence directory.

The cross-platform behavioral test uses short-lived synthetic EC certificates and
proves the valid path and failures for backdating/current validity, extension policy,
weak keys, ambient CA-store injection, self-verification, duplicate source-law identity,
path containment, recursive unexpected sidecars, multi-cert roots, decoded private-key
material, changed policy/template/statement and untrusted CAs. The publication-inventory
suite proves exact files plus extra, drift, traversal, cross-platform reserved device
and case collisions, reserved staging collisions, active/network content, encoded private
keys, symlink-when-supported, archive, missing and noncanonical-scenario failures:

```powershell
.\scripts\test-durable-release-evidence.ps1
.\scripts\test-durable-release-publication-inventory.ps1
node .\scripts\verify-durable-release-evidence-workflow.mjs
```

Synthetic names and certificates are test machinery only. They cannot satisfy any
human or external acceptance gate.

## Durable publication

Completed reviewer evidence must never be committed to this public application
repository. Publication is designed to run from the dedicated private repository
`jasperfordesq-ai/accounts-release-evidence`. That external operating environment has
not been provisioned or verified by this engineering work.

### Current provisioning status

| Required real-world control | Current status |
| --- | --- |
| Private `accounts-release-evidence` repository and protected default branch | Unprovisioned/unverified |
| Private-repository `durable-release-evidence` environment, independent required reviewers, self-approval prohibition, administrator-bypass prohibition, and protected-branch-only policy on a GitHub plan that supports those controls for private repositories | Unprovisioned/unverified |
| Read-only release-governance GitHub App installed only on `accounts` and `accounts-release-evidence`, with protected `release_governance_app_id` / `release_governance_app_private_key` caller secrets | Unprovisioned/unverified |
| Real reviewer certificates, CA trust anchors, trust policy, credential checks, and independently retained policy digest | Unprovisioned |
| Completed six human templates, seven sidecars, full machine pack, exact manifest, and five raw external iXBRL files | Not collected or accepted |
| Private immutable GitHub Release, release attestation, explicit provenance, later retrieval, and independent download verification | Not published or proven |

All named human/external gates remain open, including source-law review, visual QA,
external ROS/iXBRL acceptance, qualified-accountant acceptance, manual handoff review,
and monitoring-provider confirmation. The scripts, tests, and workflow definition are
engineering preparation only and award none of those approvals.

The checked-in workflow is reusable (`workflow_call`) rather than directly dispatchable
from this public repository. It intentionally has no public `workflow_dispatch`; its
`pull_request` event can run only the synthetic policy tests and cannot authorize
publication. A protected caller workflow in the private evidence repository must pin
the reusable workflow to the exact application candidate SHA. Because reusable jobs run
in the caller's repository context, the publish job uses the **private evidence
repository's** `durable-release-evidence` environment. That environment and its
independent required-reviewer/no-self-approval/no-administrator-bypass and protected-
branch-only rules are not currently provisioned. GitHub does not offer all private-
repository environment protection rules on every plan; publication must remain blocked
unless the environment API proves every required rule rather than silently accepting an
unprotected environment.

Prepare the private evidence repository with all of these controls:

1. Keep it private and enable secret scanning plus push protection, protected default
   branch review/CI, verified signed commits, and repository-level immutable releases.
2. Install one dedicated read-only release-governance GitHub App only on `accounts` and
   `accounts-release-evidence`. Grant Actions read, Administration read, Metadata read,
   and Contents read; grant no repository write permission. The reusable workflow mints
   separate short-lived repository-scoped tokens for the two repositories. Store its ID
   and private key in the private caller as `release_governance_app_id` and
   `release_governance_app_private_key`. A long-lived cross-repository PAT is not an
   acceptable substitute.
3. Add canonical `.github/workflows/evidence-ci.yml` checks for the content/signature/
   inventory tests and the manual caller at exactly
   `.github/workflows/publish-durable-release-evidence.yml`. Its only publication trigger
   must be `workflow_dispatch`, and it must invoke:
   `jasperfordesq-ai/accounts/.github/workflows/publish-durable-release-evidence.yml@<candidate-sha>`.
4. Put only the completed evidence in `completed-release-evidence/`. Never use Git as a
   key vault: sign with keys outside that directory, pre-scan before commit, and treat any
   secret-scanning alert as an incident even though the repository is private.
5. Complete and verify all templates, seven sidecars, single-certificate public trust
   roots, the full release pack, the exact publication manifest, and five retained
   uncompressed iXBRL HTML/XHTML files. Obtain the trust-policy SHA-256 through an
   independent out-of-band channel. Treat the raw iXBRL documents as untrusted active-
   document evidence: the inventory verifier requires well-formed DTD-free XHTML/XML and
   rejects common active/external browser content, but reviewers must still extract and
   inspect them only in an offline sandboxed viewer. The verifier is not a complete HTML
   safety guarantee.

The private caller supplies the candidate SHA, canonical application CI run ID, exact
private evidence commit, evidence directory and policy digest. It does not supply a
completion timestamp. The called workflow validates the Actions run first, derives
`CandidateRunCompletedAtUtc` from that run's exact `updated_at`, and then:

- requires the candidate to be the current protected, verified application `main` head
  with a successful canonical CI run, and binds the validated Actions run `updated_at`;
- requires the caller to be the hard-coded private evidence repository, at its current
  protected, verified default-branch head, with successful same-branch canonical evidence
  CI; validates the manual event, canonical caller ref and exact caller/called workflow
  commits through GitHub OIDC before any secret-backed token is minted or private evidence
  is checked out;
- checks both repositories' exact strict required checks, PR/code-owner review, stale-
  review dismissal, administrator enforcement, signed commits and force-push/deletion
  blocking; also checks private-repository secret scanning, push protection, protected
  environment rules and immutable releases;
- runs verifier code only from the candidate checkout and exposes the short-lived
  evidence App token only to private checkout and administration preflight, never to
  verifier or release/attestation steps; publication and verification use the caller
  repository's short-lived `GITHUB_TOKEN` with explicit contents/attestations permissions;
- rejects submodules, symlinks, an unpinned trust policy, unsafe paths and any file set
  that differs from the pinned exact publication manifest;
- creates deterministic private staging, attaches both application and evidence
  identities, and generates provenance in the private evidence repository;
- refuses any existing release/tag and never edits, deletes, uploads to, or clobbers a
  prior release;
- immediately before creating the release, rechecks both protected heads, both canonical
  CI identities, exact branch/environment controls, immutable-release state and tag/
  release absence so a state change during verification fails closed;
- targets the release tag at the verified **evidence** commit (the unrelated application
  SHA remains separately bound in signatures, manifest, tag name and report);
- verifies the immutable release, exact asset and evidence-repository attestation.

GitHub immutable releases lock the tag and assets after publication and generate a
release attestation. See GitHub's documentation on
[immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases),
[release integrity verification](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/verify-release-integrity),
and [artifact attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations).

An independent consumer should download the archive without overwrite, compare its
SHA-256 with the publication report and then run:

```powershell
gh release verify accounts-release-evidence-<candidate-sha> --repo jasperfordesq-ai/accounts-release-evidence
gh release verify-asset accounts-release-evidence-<candidate-sha> <downloaded-archive> --repo jasperfordesq-ai/accounts-release-evidence
gh attestation verify <downloaded-archive> --repo jasperfordesq-ai/accounts-release-evidence
.\scripts\verify-durable-release-evidence.ps1 <same candidate, run, completion time, policy and digest arguments>
```

The control is complete only when those checks succeed against the real immutable,
retrievable release and real reviewer credentials. A passing synthetic test, blank
prepared workspace, mutable Actions artifact, workflow definition, draft release or
locally generated report is not durable human evidence. Nothing in this document
closes a human gate before those named people and external systems actually complete
and sign their reviews.
