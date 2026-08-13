#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
test "${repo_root}" = "/workspace/MIGraphX-CSharp-API"
cd "${repo_root}"
test -z "$(git status --porcelain)"
test -z "$(git symbolic-ref -q HEAD || true)"
test "$(git rev-parse HEAD)" = "${COMMIT_SHA:?Set COMMIT_SHA to the pushed 40-character commit under test}"
test "${#COMMIT_SHA}" -eq 40

cpu_max="$(cat /sys/fs/cgroup/cpu.max 2>/dev/null || printf '1200000 100000')"
quota="${cpu_max%% *}"
period="${cpu_max##* }"
if test "${quota}" = max; then quota=12; else quota=$((quota / period)); fi
jobs="${MIGRAPHXSHARP_JOBS:-${quota}}"
test "${jobs}" -le 16
test "${jobs}" -ge 1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

results=/workspace/MIGraphX-CSharp-API/test-results
mkdir -p "${results}"
./tools/radeon/env-report.sh | tee "${results}/environment.txt"
./eng/build.sh 2>&1 | tee "${results}/managed-gates.txt"
printf '{"schemaVersion":"1.0.0","commit":"%s","managedGates":"completed","nativeRuntime":"not-executed-by-m0-script","gpu":"not-executed-by-m0-script"}\n' "${COMMIT_SHA}" > "${results}/summary.json"
