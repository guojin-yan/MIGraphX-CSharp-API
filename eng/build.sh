#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test -f "${repo_root}/MIGraphXSharp.sln"
cd "${repo_root}"

configuration="${CONFIGURATION:-Release}"
hipsharp_args=()
if [[ -n "${HIPSHARP_REPOSITORY_ROOT:-}" ]]; then
  hipsharp_args=(-HipSharpRepositoryRoot "${HIPSHARP_REPOSITORY_ROOT}")
fi
pwsh ./eng/build.ps1 -Configuration "${configuration}" "${hipsharp_args[@]}"
pwsh ./eng/test.ps1 -Configuration "${configuration}" -NoBuild "${hipsharp_args[@]}"
pwsh ./eng/docs.ps1 -Configuration "${configuration}" -NoBuild "${hipsharp_args[@]}"
