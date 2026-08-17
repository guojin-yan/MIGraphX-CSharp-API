#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: run.sh --repo DIR --feed DIR --record DIR --fixtures DIR --native FILE --hip FILE --header FILE --source-sha SHA --version VERSION --core-sha SHA256 --adapter-sha SHA256 --hipsharp-sha SHA256" >&2
  exit 2
}

repo=''
feed=''
record=''
fixtures=''
native=''
hip=''
header=''
source_sha=''
version=''
core_sha=''
adapter_sha=''
hipsharp_sha=''
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --repo) repo="$2" ;;
    --feed) feed="$2" ;;
    --record) record="$2" ;;
    --fixtures) fixtures="$2" ;;
    --native) native="$2" ;;
    --hip) hip="$2" ;;
    --header) header="$2" ;;
    --source-sha) source_sha="$2" ;;
    --version) version="$2" ;;
    --core-sha) core_sha="$2" ;;
    --adapter-sha) adapter_sha="$2" ;;
    --hipsharp-sha) hipsharp_sha="$2" ;;
    *) usage ;;
  esac
  shift 2
done

for directory in "$repo" "$feed" "$record" "$fixtures"; do
  [[ "$directory" = /* && -d "$directory" ]] || usage
done
for file in "$native" "$hip" "$header"; do
  [[ "$file" = /* && -f "$file" ]] || usage
done
[[ "$source_sha" =~ ^[a-f0-9]{40}$ ]] || usage
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-rc\.[0-9]+$ ]] || usage
for hash in "$core_sha" "$adapter_sha" "$hipsharp_sha"; do
  [[ "$hash" =~ ^[a-f0-9]{64}$ ]] || usage
done

[[ "$(git -C "$repo" rev-parse HEAD)" == "$source_sha" ]] || { echo 'source SHA mismatch' >&2; exit 1; }
[[ -z "$(git -C "$repo" status --porcelain)" ]] || { echo 'source checkout is dirty' >&2; exit 1; }
[[ -z "$(git -C "$repo" branch --show-current)" ]] || { echo 'source checkout is not detached' >&2; exit 1; }

core="$feed/JYPPX.ROCm.MIGraphX.CSharp.API.$version.nupkg"
adapter="$feed/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.$version.nupkg"
hipsharp="$feed/JYPPX.ROCm.HIP.CSharp.API.0.9.1.nupkg"
for file in "$core" "$adapter" "$hipsharp"; do
  [[ -f "$file" ]] || { echo "package missing: $file" >&2; exit 1; }
done
[[ "$(sha256sum "$core" | awk '{print $1}')" == "$core_sha" ]] || { echo 'core package hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$adapter" | awk '{print $1}')" == "$adapter_sha" ]] || { echo 'adapter package hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$hipsharp" | awk '{print $1}')" == "$hipsharp_sha" ]] || { echo 'HipSharp package hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$header" | awk '{print $1}')" == 'a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2' ]] || { echo 'fixed header hash mismatch' >&2; exit 1; }

mkdir -p "$record/raw" "$record/build" "$record/packages"
started_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$record/build/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="m11-feed" value="$feed" /></packageSources>
</configuration>
EOF

cat > "$record/raw/source-metadata.json" <<EOF
{
  "sourceSha": "$source_sha",
  "cleanDetached": true,
  "version": "$version",
  "startedUtc": "$started_utc"
}
EOF
{
  uname -a
  sed -n 's/^\(NAME\|VERSION\|ID\|VERSION_ID\)=/\1=/p' /etc/os-release
  lscpu
  free -b
  dotnet --info
  if [[ -f /proc/self/cgroup ]]; then cat /proc/self/cgroup; fi
  echo 'gpuRuntimeQuery=not-invoked-by-runner'
} > "$record/raw/environment.txt"

resolved_native="$(realpath "$native")"
resolved_hip="$(realpath "$hip")"
command -v timeout >/dev/null 2>&1 || { echo 'GNU timeout is required for the bounded functional session' >&2; exit 1; }
{
  echo "native=$resolved_native"
  echo "nativeSha256=$(sha256sum "$resolved_native" | awk '{print $1}')"
  echo "hip=$resolved_hip"
  echo "hipSha256=$(sha256sum "$resolved_hip" | awk '{print $1}')"
  readelf -d "$resolved_native"
  ldd "$resolved_native"
} > "$record/raw/native-library.txt"
! ldd "$resolved_native" | grep -q 'not found' || { echo 'native dependency closure is incomplete' >&2; exit 1; }
nm -D --defined-only "$resolved_native" | awk '{print $3}' | sort -u > "$record/raw/native-exports.txt"
for export_name in migraphx_parse_onnx migraphx_parse_onnx_buffer migraphx_program_compile migraphx_program_run migraphx_program_run_async migraphx_save migraphx_load; do
  grep -Fxq "$export_name" "$record/raw/native-exports.txt" || { echo "required export missing: $export_name" >&2; exit 1; }
done

{
  echo "$core_sha  $core"
  echo "$adapter_sha  $adapter"
  echo "$hipsharp_sha  $hipsharp"
  echo "$(sha256sum "$header" | awk '{print $1}')  $header"
  echo "$(sha256sum "$resolved_native" | awk '{print $1}')  $resolved_native"
  echo "$(sha256sum "$resolved_hip" | awk '{print $1}')  $resolved_hip"
  find "$fixtures" -maxdepth 1 -type f -name '*.onnx' -print0 | sort -z | xargs -0 sha256sum
} > "$record/raw/identities.txt"

project="$repo/tools/m11-runtime-probe/M11RuntimeProbe.csproj"
dotnet restore "$project" --configfile "$record/build/NuGet.Config" --packages "$record/packages" -p:M11PackageVersion="$version" > "$record/raw/restore.log" 2>&1
dotnet build "$project" -c Release --no-restore -p:M11PackageVersion="$version" > "$record/raw/build.log" 2>&1

set +e
functional_session_timeout=1800
case_timeout=120
session_kill_after=10
session_started_epoch="$(date +%s)"
# Preserve timeout's process group so TERM and KILL reach the native probe worker.
timeout --kill-after="${session_kill_after}s" "${functional_session_timeout}s" dotnet run --project "$project" -c Release --no-build -p:M11PackageVersion="$version" -- \
  --native "$resolved_native" --hip "$resolved_hip" --fixtures "$fixtures" --record "$record" \
  --output "$record/raw/m11-functional.json" --phase functional --source-sha "$source_sha" --expected-version "$version" \
  > "$record/raw/functional-stdout.log" 2> "$record/raw/functional-stderr.log"
functional_exit=$?
restart_exit=125
if [[ $functional_exit -eq 0 ]]; then
  elapsed_seconds=$(( $(date +%s) - session_started_epoch ))
  remaining_seconds=$(( functional_session_timeout - elapsed_seconds ))
  restart_timeout=$(( remaining_seconds < case_timeout ? remaining_seconds : case_timeout ))
  if [[ $restart_timeout -gt 0 ]]; then
    timeout --kill-after="${session_kill_after}s" "${restart_timeout}s" dotnet run --project "$project" -c Release --no-build -p:M11PackageVersion="$version" -- \
      --native "$resolved_native" --hip "$resolved_hip" --fixtures "$fixtures" --record "$record" \
      --output "$record/raw/m11-cache-restart.json" --phase cache-restart --source-sha "$source_sha" --expected-version "$version" \
      > "$record/raw/cache-restart-stdout.log" 2> "$record/raw/cache-restart-stderr.log"
    restart_exit=$?
  else
    restart_exit=124
  fi
fi
set -e

completed_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$record/raw/run-metadata.json" <<EOF
{
  "schemaVersion": "1.0.0",
  "evidence": "runtime-candidate-executed-review-required",
  "sourceSha": "$source_sha",
  "version": "$version",
  "startedUtc": "$started_utc",
  "completedUtc": "$completed_utc",
  "functionalExitCode": $functional_exit,
  "cacheRestartExitCode": $restart_exit,
  "functionalSessionTimeoutSeconds": $functional_session_timeout,
  "caseTimeoutSeconds": $case_timeout,
  "sessionKillAfterSeconds": $session_kill_after,
  "gpuRuntimeQueryExecuted": false,
  "caseStageTraceFile": "raw/case-stages.jsonl",
  "longRunExecuted": false,
  "timingExecuted": false,
  "environmentChanged": false
}
EOF
find "$record" -type f ! -name artifact-hashes.txt -print0 | sort -z | xargs -0 sha256sum > "$record/raw/artifact-hashes.txt"

[[ $functional_exit -eq 0 && $restart_exit -eq 0 ]] || exit 1
exit 0
