#!/usr/bin/env bash
set -euo pipefail

failures=0
check() {
  local name="$1"
  shift
  if "$@" >/dev/null 2>&1; then
    printf 'PASS %s\n' "$name"
  else
    printf 'FAIL %s\n' "$name" >&2
    failures=$((failures + 1))
  fi
}

check ubuntu-24.04 bash -c '. /etc/os-release && [[ "$ID" == ubuntu && "$VERSION_ID" == 24.04* ]]'
check x86-64 bash -c '[[ "$(uname -m)" == x86_64 ]]'
check two-vcpus bash -c '(( $(nproc) >= 2 ))'
check eight-gib-memory bash -c 'awk "/^MemTotal:/ { exit !(\$2 >= 7340032) }" /proc/meminfo'
check forty-gib-free bash -c '(( $(df --output=avail -k / | tail -1) >= 41943040 ))'
check powershell-seven bash -c 'command -v pwsh >/dev/null && pwsh -NoProfile -Command "exit \$([int](\$PSVersionTable.PSVersion.Major -lt 7))"'
check docker-engine bash -c 'docker version --format "{{.Server.Os}}" | grep -qx linux'
check docker-compose bash -c 'docker compose version --short | grep -Eq "^[2-9][0-9]*\\.|^2\\.(2[0-9]|[3-9][0-9])\\."'
check docker-boot-enabled bash -c 'systemctl is-enabled docker.service | grep -qx enabled'
check loopback-port-3500 bash -c '! ss -H -ltn "sport = :3500" | grep -q .'

if command -v tailscale >/dev/null 2>&1; then
  check tailscale-connected bash -c 'tailscale status --json | grep -q "\\\"BackendState\\\"[[:space:]]*:[[:space:]]*\\\"Running\\\""'
else
  printf 'INFO tailscale not installed; it is required before private remote access, not for loopback setup\n'
fi

if (( failures > 0 )); then
  printf '%d Linux Private Server prerequisite check(s) failed.\n' "$failures" >&2
  exit 1
fi
printf 'Linux Private Server host prerequisites passed.\n'
