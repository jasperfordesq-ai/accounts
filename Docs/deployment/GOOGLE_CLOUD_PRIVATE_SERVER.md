# Google Cloud Ubuntu Private Server preparation

This is the exact VM plan for a private owner-operated FilingBridge test installation. Complete the
Linux release work and obtain a verified bundle before creating the permanent VM.

## Recommended VM

| Setting | Recommended value |
|---|---|
| Product | Compute Engine VM |
| Region | EU region convenient to Ireland, for example `europe-west1` |
| Availability | One zonal VM; this is not high availability |
| Machine family | E2 general purpose |
| Machine type | `e2-standard-2` (2 vCPU, 8 GB RAM) |
| Architecture | x86-64 |
| OS | Ubuntu 24.04 LTS |
| Boot disk | 100 GB Balanced Persistent Disk (`pd-balanced`) |
| Service account | Dedicated, no broad Editor role; no application cloud API permission required |
| Application ingress | None |
| Private user access | Tailscale Serve only |
| Deletion protection | Enable after acceptance |
| Automatic restart | Enable |
| On-host display | None |

Use `e2-standard-4` (4 vCPU, 16 GB RAM) only if you intend to build images on the VM or want more
headroom. Normal verified-release operation pulls compiled images and is sized for
`e2-standard-2`. Google documents E2 as a general-purpose machine family and Balanced Persistent
Disk as the general cost/performance disk choice:

- [Compute Engine machine families](https://cloud.google.com/compute/docs/machine-resource)
- [Persistent Disk types](https://cloud.google.com/compute/docs/disks)

The 100 GB recommendation covers Ubuntu, Docker images/layers, PostgreSQL growth, temporary
verified-restore space, support evidence, and update overlap. It does not replace off-VM backup.
Google Persistent Disk can be enlarged later, but it cannot be shrunk.

## Network design

The VM does not need public application ingress: no application ports are exposed publicly.
Recommended administration choices are:

1. **Preferred:** no external IPv4, IAP SSH, and Cloud NAT for outbound package/image/Tailscale
   traffic; or
2. an ephemeral external IPv4 with ingress denied except IAP SSH from `35.235.240.0/20` to a VM
   target tag such as `iap-ssh`.

Do not rely on a default-network `allow-ssh` rule open to `0.0.0.0/0`. Do not create ingress rules
for FilingBridge or Tailscale. Tailscale establishes its connection outbound. Review Google's
[IAP TCP forwarding](https://cloud.google.com/iap/docs/using-tcp-forwarding) and Tailscale's
[firewall guidance](https://tailscale.com/kb/1082/firewall-ports).

For the simplest owner test, an ephemeral external IP with only IAP-restricted SSH is acceptable.
Remove the external IP after Tailscale administration is proven if Cloud NAT or another outbound
path remains available.

## Console creation checklist

1. Select the intended Google Cloud project and confirm billing alerts/budgets.
2. Enable Compute Engine and IAP APIs.
3. Create a dedicated service account with no project-wide Editor role.
4. Create the IAP SSH firewall rule and remove/avoid broad default SSH ingress.
5. Create the VM with the table settings above.
6. Set Shielded VM Secure Boot, vTPM, and integrity monitoring on.
7. Enable automatic restart and host maintenance migration.
8. Disable IP forwarding.
9. Do not add a startup script containing credentials or Tailscale auth keys.
10. Connect through IAP SSH and apply all Ubuntu security updates.

Use an interactive Tailscale login or a short-lived, tagged, pre-authorized auth key constrained by
tailnet policy. Never paste a reusable Tailscale key into instance metadata, a shell-history command,
the repository, or a support bundle.

## Installation order

1. Patch Ubuntu and install official Docker Engine/Compose, PowerShell 7, `age`, GitHub CLI,
   Tailscale, `jq`, and `unzip`.
2. Enable Docker and Tailscale systemd services.
3. Add only the dedicated operator to the Docker group, sign out, and reconnect.
4. Authenticate Tailscale and set that user as the local Tailscale operator.
5. Download and verify the FilingBridge release and attestation.
6. Run `./scripts/verify-linux-private-host.sh`.
7. Run loopback-only setup and complete Owner password/MFA checks.
8. Enable Tailscale Serve and test from a second tailnet device.
9. Run encrypted backup, independent verification, and same-install restore.
10. Run reboot prepare/verify.
11. Retain the evidence listed in [LINUX_CLOUD_READINESS.md](LINUX_CLOUD_READINESS.md).

## Cost and lifecycle controls

- Set a project budget alert before creation.
- Stop the VM when it is not needed; persistent disk and snapshots still incur storage charges.
- Never use Spot/Preemptible capacity for the only durable installation.
- Take infrastructure snapshots only after an application backup, and label their candidate/version.
- Keep at least one authenticated encrypted application backup outside the VM and outside its boot
  disk's failure/deletion boundary.
- Enable deletion protection only after clean-install testing; disable it deliberately for the later
  clean replacement-host drill.

## Pre-deployment stop point

Do not enter real company data during the operational preview. Use a test tenant and synthetic data.
Do not describe a passing VM as Public Production or filing acceptance; the independent platform
audit remains authoritative.
