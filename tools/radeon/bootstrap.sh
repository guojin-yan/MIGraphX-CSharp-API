#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
test "${repo_root}" = "/workspace/MIGraphX-CSharp-API"
cd "${repo_root}"
test -f global.json
require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    printf 'missing-tool=%s\n' "$1" >&2
    exit 1
  fi
}
for tool in git dotnet pwsh cmake gcc; do
  require_command "$tool"
done
printf 'tool-baseline=git,dotnet,pwsh,cmake,gcc\n'

if test -d /persistent; then
  cache_root=/persistent/projects/MIGraphX-CSharp-API/cache/nuget
else
  cache_root=/workspace/MIGraphX-CSharp-API/.cache/nuget
  printf 'persistent-volume=unavailable; cache is ephemeral\n'
fi
mkdir -p "${cache_root}"
export NUGET_PACKAGES="${cache_root}"

required_sdk="$(sed -n 's/.*"version": "\([0-9.]*\)".*/\1/p' global.json)"
printf 'required-dotnet-sdk=%s\n' "${required_sdk}"
dotnet --info
pwsh --version
dotnet tool restore
