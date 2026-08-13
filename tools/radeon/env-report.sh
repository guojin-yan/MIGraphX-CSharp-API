#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
test "${repo_root}" = "/workspace/MIGraphX-CSharp-API"
cd "${repo_root}"

printf 'schema-version=1.0.0\n'
printf 'captured-at=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf 'git-sha=%s\n' "$(git rev-parse HEAD)"
printf 'git-clean=%s\n' "$(test -z "$(git status --porcelain)" && printf true || printf false)"
printf 'os=%s\n' "$(. /etc/os-release && printf '%s %s' "${NAME}" "${VERSION_ID}")"
printf 'cpu-quota=%s\n' "$(cat /sys/fs/cgroup/cpu.max 2>/dev/null || printf unavailable)"
printf 'memory-limit=%s\n' "$(cat /sys/fs/cgroup/memory.max 2>/dev/null || printf unavailable)"
printf 'dotnet=%s\n' "$(dotnet --version 2>/dev/null || printf unavailable)"
printf 'cmake=%s\n' "$(cmake --version 2>/dev/null | head -n 1 || printf unavailable)"
printf 'rocm=%s\n' "$(cat /opt/rocm/.info/version 2>/dev/null || printf unavailable)"
printf 'migraphx-header=%s\n' "$(test -f /opt/rocm/include/migraphx/migraphx.h && sha256sum /opt/rocm/include/migraphx/migraphx.h | cut -d' ' -f1 || printf unavailable)"
printf 'persistent=%s\n' "$(test -d /persistent && printf available || printf unavailable)"
df -hT /workspace
test ! -d /persistent || df -hT /persistent
command -v rocminfo >/dev/null 2>&1 && rocminfo | sed -n '1,80p' || true
