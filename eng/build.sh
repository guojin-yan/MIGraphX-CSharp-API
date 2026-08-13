#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test -f "${repo_root}/MIGraphXSharp.sln"
cd "${repo_root}"

configuration="${CONFIGURATION:-Release}"
dotnet restore ./MIGraphXSharp.sln
dotnet build ./MIGraphXSharp.sln -c "${configuration}" --no-restore
dotnet test ./MIGraphXSharp.sln -c "${configuration}" --no-build
dotnet tool restore
dotnet tool run docfx ./docfx.json --warningsAsErrors
