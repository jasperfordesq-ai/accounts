# Ubuntu / Google Cloud Private Server readiness

This score is separate from both the Windows local score and the independent statutory/Public
Production audit. Implementation alone receives no live-acceptance marks.

## Acceptance matrix (1,000 points)

| Area | Weight | Evidence required |
|---|---:|---|
| Verified release and clean Ubuntu setup | 180 | Exact candidate, release/asset verification, manifest pass, clean setup |
| Google Cloud host and network isolation | 170 | VM specification, firewall inventory, loopback-only listeners, no Funnel/public application ingress |
| Authentication and owner workflow | 150 | Initial password change, MFA, logout/login, sample PDF and iXBRL |
| Durable lifecycle and reboot | 140 | stop/start, Docker boot enablement, real reboot prepare/verify |
| Backup, update, and same-host restore | 160 | encrypted set, independent verify, same-host restore, genuine forward update and failure recovery |
| Clean replacement-host recovery | 120 | source offline, clean second VM, separate three-part recovery trust, data/artifact inspection |
| Tailscale selected-device access | 50 | Serve HTTPS, tailnet policy, second-device login, no Funnel |
| Candidate-bound engineering evidence | 30 | Linux tests, release verification, green synchronized CI |
| **Total** | **1,000** | |

## Evidence files

Retain without secrets:

- release version, commit SHA, GitHub Actions run URL, ZIP SHA-256, release and asset verification;
- `verify-linux-private-host.sh` output;
- machine type, Ubuntu version, disk type/size, Shielded VM settings, service-account role inventory;
- effective VPC firewall rules and `ss -lntup` listener inventory;
- `local-check` and `diagnose` reports;
- redacted Tailscale status/Serve configuration and second-device HTTPS evidence;
- Owner workflow report with no password, TOTP seed, recovery code, cookie, or API key;
- encrypted backup envelope/report, independent verify report, restore report, and update/failure report;
- `reboot-check` report; and
- clean replacement-host recovery report plus manually inspected test artifacts.

## Rule for full marks

Award 1,000/1,000 only to one exact candidate after every row has live evidence. A Windows source
installation and Ubuntu replacement VM may prove cross-host portability only when the Windows source
is offline and the Ubuntu VM begins without any FilingBridge state, project, or volume.

A single VM cannot prove its own clean replacement-host recovery. Delete/recreate it only after the
backup, age identity, and recovery-authentication key are stored separately and the source state is
retained safely offline.

No Linux/cloud score changes the Windows 980/1,000 result or the independent 600/1,000 statutory and
Public Production baseline.
