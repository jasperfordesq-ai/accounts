# Local Windows readiness score

This score is deliberately narrower than the independent Public Production/statutory audit. It
answers one question: **how ready is FilingBridge for its owner to run, test, and continue coding
privately on a Windows machine?** It does not approve real CRO/Revenue filing reliance.

## Current accepted score on 12 July 2026

Exact candidate: `0226c3a750ae4a9b174c51d098c8d5995a6f0c7e`, Private Server
`0.1.0-preview.4`. Canonical CI run `29199796404` and protected release workflow run
`29200905370` passed. The verified draft bundle SHA-256 is
`1aebcad90084f85641598a98a37521dcd49a28a91221fbd8794833f7b83209e2`.

| Area | Weight | Accepted | Remaining evidence for full marks |
|---|---:|---:|---|
| Core accounting functionality | 250 | 250 | Complete |
| Windows installation and lifecycle | 200 | 195 | Real reboot prepare/verify evidence |
| Security and local isolation | 200 | 200 | Complete for loopback-only owner use |
| Backup, restore and failure recovery | 150 | 135 | Clean replacement-host recovery drill |
| Interface and operator usability | 120 | 120 | Complete for the tested Owner workflow |
| Testing and engineering quality | 80 | 80 | Complete for this exact candidate |
| **Total** | **1,000** | **980** | **20 points remain live-evidence gated** |

The historic 900/1,000 statement was the pre-acceptance baseline. On the exact preview.4 candidate,
the owner has now completed clean compiled-bundle setup, `local-check`, password recovery and
rotation, privileged MFA enforcement, authenticated PDF/iXBRL downloads, encrypted complete backup,
independent verification, same-installation restore, genuine release update, forced update failure
and explicit recovery. The installation is healthy on loopback at the recorded candidate and no
API, PostgreSQL, LAN or public listener was enabled.

The implemented evidence commands are:

- `local-check` produces machine-readable health, loopback-port and data-fingerprint evidence;
- `reboot-check prepare|verify` binds pre/post-boot identities, services, readiness and data;
- `export-recovery-key` creates a separate replacement-host authentication trust anchor;
- `recover-host` builds an isolated replacement installation from an authenticated complete set;
- forced update failure is regression-tested through writer shutdown, retained verified backup,
  explicit `updateFailed`, authenticated restore, and return to `ready`; and
- the compiled bundle retains `scripts/smoke-production.ps1` for an authenticated Owner/MFA,
  first-login password rotation, readiness, CSRF/logout and optional PDF/iXBRL journey, with a
  machine-readable `owner-workflow-report.json` that contains no password or TOTP secret.

## Rule for awarding 1,000/1,000

Coding completeness is necessary but not sufficient for a product-readiness score. Award full
marks only when one exact candidate has all of the following retained evidence:

1. clean Windows x64 release-bundle setup and `local-check` pass;
2. first-login password rotation, Owner MFA, authenticated smoke, and safe test output downloads;
3. `reboot-check prepare` before a real reboot and `reboot-check verify` afterward;
4. complete encrypted backup, independent verification and same-installation restore;
5. genuine older-to-newer release update plus forced-failure recovery;
6. `recover-host` on a clean replacement host using separately stored backup, age identity and
   authentication key, followed by artifact/business-data inspection; and
7. exact candidate backend/frontend/operator/release/Compose gates and green CI.

Second-device Tailscale Serve is required only if the owner wants another device to access the
machine. It is not required for loopback-only local use. Public Production, independent GitHub
review, qualified-accountant acceptance and external CRO/Revenue evidence remain governed by
`Docs/PLATFORM_AUDIT_2026-07-10.md` and are not points in this local score.

## Remaining finish line

Only two local-score gates remain:

1. run `reboot-check prepare`, perform a real Windows reboot, and retain a passing
   `reboot-check verify` report for this installation; and
2. run `recover-host` on a genuinely clean replacement Windows host using the separately stored
   encrypted backup, age identity, and recovery-authentication key, then inspect recovered data and
   sample artifacts.

The current machine correctly refuses to masquerade as a replacement host while the source Docker
project and volume still exist. Automated seams and same-host recovery do not satisfy that gate.
