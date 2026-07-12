# Local Windows readiness score

This score is deliberately narrower than the independent Public Production/statutory audit. It
answers one question: **how ready is FilingBridge for its owner to run, test, and continue coding
privately on a Windows machine?** It does not approve real CRO/Revenue filing reliance.

## Baseline recorded on 12 July 2026

| Area | Weight | Baseline | Evidence needed for full marks |
|---|---:|---:|---|
| Core accounting functionality | 250 | 235 | Authenticated retained-host Owner journey and safe test-period output downloads |
| Windows installation and lifecycle | 200 | 185 | Clean release-bundle install plus machine reboot evidence |
| Security and local isolation | 200 | 190 | Live local binding report and retained credential/token inspection |
| Backup, restore and failure recovery | 150 | 105 | Encrypted same-host restore, genuine update failure recovery, and replacement-host drill |
| Interface and operator usability | 120 | 110 | Complete routine workflow exercised by the owner |
| Testing and engineering quality | 80 | 75 | Exact candidate-bound gates and release evidence retained |
| **Total** | **1,000** | **900** | |

The historic 900/1,000 statement was valid for disposable local evaluation on the already-tested
host. The following coding work closes the previously missing implementation paths:

- `local-check` produces machine-readable health, loopback-port and data-fingerprint evidence;
- `reboot-check prepare|verify` binds pre/post-boot identities, services, readiness and data;
- `export-recovery-key` creates a separate replacement-host authentication trust anchor;
- `recover-host` builds an isolated replacement installation from an authenticated complete set;
- forced update failure is regression-tested through writer shutdown, retained verified backup,
  explicit `updateFailed`, authenticated restore, and return to `ready`; and
- the compiled bundle retains `scripts/smoke-production.ps1` for an authenticated Owner/MFA,
  readiness, CSRF/logout and optional PDF/iXBRL journey.

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
