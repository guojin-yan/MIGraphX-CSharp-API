#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: run.sh --repo DIR --feed DIR --record DIR --identity FILE --tensorflow-fixture FILE --calibration-map FILE --native FILE --header FILE --source-sha SHA --version VERSION --core-sha SHA256 [--case CASE] [--include-deferred]" >&2
  exit 2
}

repo=''
feed=''
record=''
identity=''
tensorflow_fixture=''
calibration_map=''
native=''
header=''
source_sha=''
version=''
core_sha=''
case_id=''
include_deferred=false
while [[ $# -gt 0 ]]; do
  if [[ "$1" = '--include-deferred' ]]; then
    [[ "$include_deferred" = false ]] || usage
    include_deferred=true
    shift
    continue
  fi
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --repo) repo="$2" ;;
    --feed) feed="$2" ;;
    --record) record="$2" ;;
    --identity) identity="$2" ;;
    --tensorflow-fixture) tensorflow_fixture="$2" ;;
    --calibration-map) calibration_map="$2" ;;
    --native) native="$2" ;;
    --header) header="$2" ;;
    --source-sha) source_sha="$2" ;;
    --version) version="$2" ;;
    --core-sha) core_sha="$2" ;;
    --case) case_id="$2" ;;
    *) usage ;;
  esac
  shift 2
done

for directory in "$repo" "$feed"; do
  [[ "$directory" = /* && -d "$directory" ]] || usage
done
[[ "$record" = /* && -d "$(dirname "$record")" ]] || usage
for file in "$identity" "$tensorflow_fixture" "$calibration_map" "$native" "$header"; do
  [[ "$file" = /* && -f "$file" ]] || usage
done
[[ "$source_sha" =~ ^[a-f0-9]{40}$ ]] || usage
[[ "$version" == '0.0.0' ]] || usage
[[ "$core_sha" =~ ^[a-f0-9]{64}$ ]] || usage
if [[ -n "$case_id" ]]; then
  case "$case_id" in
    m12-shape-argument-factories|m12-argument-persistence-clone|m12-assign-to-clone|m12-graph-parent-lease|m12-graph-editing|m12-operation-materialized-attributes|m12-context-lifetime|m12-tensorflow-parse|m12-quantization-options|m12-custom-op-registration|m12-concurrent-dispose) ;;
    *) usage ;;
  esac
fi
[[ "$include_deferred" = false || -z "$case_id" ]] || usage

repo="$(realpath "$repo")"
feed="$(realpath "$feed")"
record="$(realpath -m "$record")"
is_same_or_child() { [[ "$1" == "$2" || "$1" == "$2"/* ]]; }
if is_same_or_child "$record" "$repo" || is_same_or_child "$repo" "$record" ||
   is_same_or_child "$record" "$feed" || is_same_or_child "$feed" "$record"; then
  echo 'evidence record must be isolated from repository and package feed' >&2
  exit 1
fi
if [[ -e "$record" ]]; then
  [[ -d "$record" && -z "$(find "$record" -mindepth 1 -print -quit)" ]] || {
    echo 'evidence record directory must be new or empty before a new run' >&2
    exit 1
  }
else
  mkdir -p "$record"
fi

[[ "$(git -C "$repo" rev-parse HEAD)" == "$source_sha" ]] || { echo 'source SHA mismatch' >&2; exit 1; }
[[ -z "$(git -C "$repo" status --porcelain)" ]] || { echo 'source checkout is dirty' >&2; exit 1; }
[[ -z "$(git -C "$repo" branch --show-current)" ]] || { echo 'source checkout is not detached' >&2; exit 1; }

core="$feed/JYPPX.ROCm.MIGraphX.CSharp.API.$version.nupkg"
[[ -f "$core" ]] || { echo "core package missing: $core" >&2; exit 1; }
[[ "$(sha256sum "$core" | awk '{print $1}')" == "$core_sha" ]] || { echo 'core package hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$header" | awk '{print $1}')" == 'a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2' ]] || { echo 'fixed header hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$identity" | awk '{print $1}')" == '0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9' ]] || { echo 'identity fixture hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$tensorflow_fixture" | awk '{print $1}')" == 'de8be9fda62bbbffb72ce46ac91426b336be60f882e227b6e71e1407c584740e' ]] || { echo 'TensorFlow fixture hash mismatch' >&2; exit 1; }
[[ "$(sha256sum "$calibration_map" | awk '{print $1}')" == '15f8698707b49e1c92021d833bc0b79c1455f777241e80a7e500619309eda1af' ]] || { echo 'calibration map hash mismatch' >&2; exit 1; }

mkdir -p "$record/raw" "$record/build" "$record/packages"
started_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$record/build/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="m12-feed" value="$feed" /></packageSources>
</configuration>
EOF

resolved_native="$(realpath "$native")"
{
  echo "sourceSha=$source_sha"
  echo "version=$version"
  echo "coreSha256=$core_sha"
  echo "headerSha256=$(sha256sum "$header" | awk '{print $1}')"
  echo "identitySha256=$(sha256sum "$identity" | awk '{print $1}')"
  echo "tensorflowFixtureSha256=$(sha256sum "$tensorflow_fixture" | awk '{print $1}')"
  echo "calibrationFixtureSha256=$(sha256sum "$calibration_map" | awk '{print $1}')"
  echo "nativeSha256=$(sha256sum "$resolved_native" | awk '{print $1}')"
  echo "cleanDetached=true"
} > "$record/raw/identities.txt"
{
  readelf -d "$resolved_native"
  ldd "$resolved_native"
} > "$record/raw/native-library.txt"
! ldd "$resolved_native" | grep -q 'not found' || { echo 'native dependency closure is incomplete' >&2; exit 1; }
nm -D --defined-only "$resolved_native" | awk '{print $3}' | sort -u > "$record/raw/native-exports.txt"
for export_name in migraphx_parse_onnx migraphx_argument_save migraphx_argument_load migraphx_program_assign_to migraphx_program_get_main_module migraphx_module_add_parameter migraphx_operation_create migraphx_operation_assign_to migraphx_operation_name migraphx_operation_destroy; do
  grep -Fxq "$export_name" "$record/raw/native-exports.txt" || { echo "required export missing: $export_name" >&2; exit 1; }
done

project="$repo/tools/m12-runtime-probe/M12RuntimeProbe.csproj"
dotnet restore "$project" --configfile "$record/build/NuGet.Config" --packages "$record/packages" --no-cache --force-evaluate -p:M12PackageVersion="$version" > "$record/raw/restore.log" 2>&1
dotnet build "$project" -c Release --no-restore -p:M12PackageVersion="$version" > "$record/raw/build.log" 2>&1

command -v timeout >/dev/null 2>&1 || { echo 'GNU timeout is required for the bounded M12 session' >&2; exit 1; }
probe_case_args=()
if [[ -n "$case_id" ]]; then probe_case_args=(--case "$case_id"); fi
if [[ "$include_deferred" = true ]]; then probe_case_args+=(--include-deferred); fi
set +e
timeout --kill-after=10s 300s dotnet run --project "$project" -c Release --no-build -p:M12PackageVersion="$version" -- \
  --native "$resolved_native" --identity "$identity" --record "$record" --output "$record/raw/m12-functional.json" \
  --tensorflow-fixture "$tensorflow_fixture" --calibration-map "$calibration_map" \
  --source-sha "$source_sha" --expected-version "$version" \
  "${probe_case_args[@]}" \
  > "$record/raw/functional-stdout.log" 2> "$record/raw/functional-stderr.log"
functional_exit=$?
set -e

completed_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
case_filter="${case_id:-all}"
if [[ "$include_deferred" = true ]]; then case_filter='all-candidate'; fi
cat > "$record/raw/run-metadata.json" <<EOF
{
  "schemaVersion": "1.0.0",
  "evidence": "runtime-candidate-executed-review-required",
  "sourceSha": "$source_sha",
  "version": "$version",
  "startedUtc": "$started_utc",
  "completedUtc": "$completed_utc",
  "functionalExitCode": $functional_exit,
  "functionalSessionTimeoutSeconds": 300,
  "sessionKillAfterSeconds": 10,
  "caseFilter": "$case_filter",
  "includeDeferred": $include_deferred,
  "environmentChanged": false,
  "promotionRequested": false
}
EOF
find "$record" -type f ! -name artifact-hashes.txt -print0 | sort -z | xargs -0 sha256sum > "$record/raw/artifact-hashes.txt"

[[ $functional_exit -eq 0 ]]
