#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test -f "${repo_root}/MIGraphXSharp.sln"
cd "${repo_root}"

configuration="${CONFIGURATION:-Release}"
pwsh ./eng/build.ps1 -Configuration "${configuration}"
pwsh ./eng/test.ps1 -Configuration "${configuration}" -NoBuild
pwsh ./eng/docs.ps1 -Configuration "${configuration}" -NoBuild
