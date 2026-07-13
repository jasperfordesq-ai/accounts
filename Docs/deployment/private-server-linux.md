# FilingBridge Private Server on Ubuntu

> **Operational preview:** Ubuntu support is implemented and automated, but it is not live-accepted
> until one exact release bundle completes the Google Cloud matrix in
> [LINUX_CLOUD_READINESS.md](LINUX_CLOUD_READINESS.md). It does not change statutory or Public
> Production acceptance.

This profile runs the same compiled `PrivateServer` Compose topology and security contract as the
Windows profile on Ubuntu 24.04 LTS x86-64. It publishes only the frontend on
`127.0.0.1:<port>`. The API and PostgreSQL have no host ports. Selected-device access is through
host-level Tailscale **Serve**, never Funnel.

## Supported host

- Ubuntu 24.04 LTS x86-64;
- at least 2 vCPUs and 8 GiB RAM;
- at least 40 GiB free before setup; 100 GB total balanced storage is recommended;
- Docker Engine with Compose 2.20 or newer, configured to start at boot;
- PowerShell 7 (`pwsh`), used by the cross-platform hardened operator;
- `age` for complete encrypted backups; and
- Tailscale only when remote private access is wanted.

The supported release manifest must declare `ubuntu-x64`. ARM, Debian, other Ubuntu releases,
Kubernetes, rootless Docker, Podman, and public ingress are outside this preview.

## Install prerequisites

Use the official [Docker Engine Ubuntu instructions](https://docs.docker.com/engine/install/ubuntu/),
[PowerShell Ubuntu instructions](https://learn.microsoft.com/powershell/scripting/install/install-ubuntu),
and [Tailscale Linux instructions](https://tailscale.com/docs/install/linux). Do not use an
unreviewed convenience Docker package or expose the Docker socket over TCP.

After installing Docker Engine and the Compose plugin:

```bash
sudo systemctl enable --now docker.service
sudo usermod -aG docker "$USER"
# Sign out and back in before continuing.
docker version
docker compose version
```

Install PowerShell 7, `age`, `jq`, `unzip`, and the GitHub CLI. If Tailscale is required:

```bash
sudo systemctl enable --now tailscaled.service
sudo tailscale up
sudo tailscale set --operator="$USER"
tailscale status
```

The Tailscale operator setting lets the dedicated Linux user manage only this host's Tailscale
client without running the FilingBridge lifecycle as root. Keep the Linux account private and do
not add unrelated users to the `docker` group; Docker membership is host-root-equivalent.

## Obtain and verify the release

Use the versioned ZIP and checksum from GitHub Releases. Verify the immutable release and exact
asset attestation before extraction:

```bash
tag='private-server-v<version>'
zip='FilingBridge-PrivateServer-<version>.zip'
gh release verify "$tag" --repo jasperfordesq-ai/accounts
gh release verify-asset "$tag" "$zip" --repo jasperfordesq-ai/accounts
sha256sum --check "$zip.sha256"
mkdir "filingbridge-<version>"
unzip "$zip" -d "filingbridge-<version>"
cd "filingbridge-<version>"
chmod 0755 filingbridge scripts/verify-linux-private-host.sh
```

Do not run the generated GitHub source archive as an installer. The release manifest binds every
payload file and exact container digest after the GitHub release/attestation trust check.

## Preflight and setup

```bash
./scripts/verify-linux-private-host.sh
./filingbridge setup \
  -TenantName 'Your organisation' \
  -TenantSlug 'your-workspace' \
  -OwnerEmail 'you@example.com' \
  -OwnerName 'Your name'
```

State defaults to `${XDG_STATE_HOME:-$HOME/.local/state}/filingbridge/server`. Directories are mode
`0700`; files are mode `0600`. Setup refuses symlink traversal, an existing state directory, an
existing labelled FilingBridge project/volume, a non-loopback collision, unsupported host
resources, mutable release files, or a Docker service that is not boot-enabled.

Record the one-time Owner password, sign in locally through an SSH tunnel if needed, change the
password, enrol MFA, and retain recovery codes outside the VM. Routine commands are:

```bash
./filingbridge start
./filingbridge status
./filingbridge logs
./filingbridge diagnose
./filingbridge local-check
./filingbridge stop
```

## Tailscale Serve

After loopback login works:

```bash
./filingbridge tailscale enable
./filingbridge tailscale status
```

The operator discovers the exact MagicDNS name, updates the allowed origin, recreates only API and
frontend, installs an owned HTTPS Serve route to `http://127.0.0.1:<port>`, and verifies the remote
HTTPS readiness endpoint. It refuses to overwrite an unrelated Serve configuration. Disable it
with `./filingbridge tailscale disable`.

Tailscale network access never replaces FilingBridge login, MFA, roles, company access, RLS, CSRF,
or audit controls. Use tailnet grants/ACLs to restrict which people or devices can reach the VM.

## Backup, update, recovery, and reboot

The Windows and Ubuntu launchers use the same authenticated, age-encrypted recovery format:

```bash
./filingbridge backup -BackupRecipient '<age-recipient>' -OutputDirectory "$HOME/filingbridge-backups"
./filingbridge verify-backup -BackupPath '<set.fbbackup.age>' -AgeIdentityFile '<age-key>'
./filingbridge export-recovery-key -OutputDirectory '<separate-offline-directory>'
./filingbridge update -ReleaseManifest '<new-release>/release.json' \
  -BackupRecipient '<age-recipient>' -AgeIdentityFile '<age-key>' \
  -OutputDirectory "$HOME/filingbridge-backups"
```

Move the encrypted backup off the VM. Store its age identity and exported recovery-authentication
key separately from each other and from the backup. A VM disk snapshot is useful infrastructure
evidence but does not replace an application-authenticated recovery set.

For reboot acceptance:

```bash
./filingbridge reboot-check prepare
sudo reboot
# Reconnect after boot.
cd '<same extracted release directory>'
./filingbridge reboot-check verify
```

The verifier binds the Linux kernel boot ID, exact release and Compose identity, the same owned
container IDs restarted after boot, readiness, and important-table fingerprints.

For a genuinely clean replacement VM, keep the source installation offline and run:

```bash
./filingbridge recover-host \
  -BackupPath '<set.fbbackup.age>' \
  -AgeIdentityFile '<age-key>' \
  -RecoveryAuthenticationKeyFile '<exported-recovery-key>' \
  -ReleaseManifest './release.json'
```

Do not delete the source installation until the recovered business data and sample PDF/iXBRL
artifacts have been inspected.

## Security boundary

- Do not create GCP firewall rules for ports 3500, 3000, 8080, 5432, 80, or 443.
- Do not enable Tailscale Funnel.
- Do not use `compose.yml` on the VM.
- Do not run FilingBridge commands with `sudo`; configure the dedicated operator account instead.
- Do not expose the Docker daemon or copy secrets into the release directory.
- Keep Ubuntu, Docker, PowerShell, Tailscale, `age`, and FilingBridge updated.
- Treat the VM's Docker-capable account and Google Cloud project administrators as trusted host
  administrators.

## Acceptance boundary

Automated Linux compatibility is coding evidence only. Until the live matrix passes, call this an
Ubuntu x64 operational preview. It never establishes qualified-accountant, CRO, Revenue, source-law,
monitoring-provider, external iXBRL, Public Production, or independent-review acceptance.
