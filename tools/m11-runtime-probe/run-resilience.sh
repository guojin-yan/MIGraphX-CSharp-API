#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: run-resilience.sh --repo DIR --feed DIR --record DIR --fixtures DIR --native FILE --alternate-native FILE --hip FILE --source-sha SHA --version VERSION --core-sha SHA256 --adapter-sha SHA256 --hipsharp-sha SHA256 --phase isolation|timing|long-run" >&2
  exit 2
}

repo=''; feed=''; record=''; fixtures=''; native=''; alternate_native=''; hip=''; source_sha=''; version=''; core_sha=''; adapter_sha=''; hipsharp_sha=''; phase=''; long_run_phase=''
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --repo) repo="$2" ;; --feed) feed="$2" ;; --record) record="$2" ;; --fixtures) fixtures="$2" ;;
    --native) native="$2" ;; --alternate-native) alternate_native="$2" ;; --hip) hip="$2" ;;
    --source-sha) source_sha="$2" ;; --version) version="$2" ;; --core-sha) core_sha="$2" ;;
    --adapter-sha) adapter_sha="$2" ;; --hipsharp-sha) hipsharp_sha="$2" ;; --phase) phase="$2" ;; --long-run-phase) long_run_phase="$2" ;;
    *) usage ;;
  esac
  shift 2
done

for directory in "$repo" "$feed" "$record" "$fixtures"; do
  [[ "$directory" = /* && -d "$directory" ]] || usage
done
for file in "$native" "$alternate_native" "$hip"; do
  [[ "$file" = /* && -f "$file" ]] || usage
done
[[ "$source_sha" =~ ^[a-f0-9]{40}$ ]] || usage
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-rc\.[0-9]+$ ]] || usage
[[ "$phase" = isolation || "$phase" = timing || "$phase" = long-run ]] || usage
if [[ "$phase" = long-run && "$long_run_phase" != preflight && "$long_run_phase" != managed && "$long_run_phase" != host-async && "$long_run_phase" != device-input && "$long_run_phase" != mixed ]]; then
  usage
fi
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

mkdir -p "$record/raw" "$record/build" "$record/packages"
cat > "$record/build/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="m11-feed" value="$feed" /></packageSources></configuration>
EOF
project="$repo/tools/m11-runtime-probe/M11RuntimeProbe.csproj"
dotnet restore "$project" --configfile "$record/build/NuGet.Config" --packages "$record/packages" -p:M11PackageVersion="$version" > "$record/raw/restore.log" 2>&1
dotnet build "$project" -c Release --no-restore -p:M11PackageVersion="$version" > "$record/raw/build.log" 2>&1

run_probe() {
  local label="$1"; shift
  local timeout_seconds="$1"; shift
  set +e
  timeout --kill-after=10s "${timeout_seconds}s" dotnet run --project "$project" -c Release --no-build -p:M11PackageVersion="$version" -- \
    --native "$native" --hip "$hip" --fixtures "$fixtures" --record "$record" --output "$record/raw/${label}.json" \
    --phase "$phase" --source-sha "$source_sha" --expected-version "$version" "$@" \
    > "$record/raw/${label}-stdout.log" 2> "$record/raw/${label}-stderr.log"
  local exit_code=$?
  set -e
  echo "$exit_code" > "$record/raw/${label}.exit-code"
  return "$exit_code"
}

echo "nativeSha256=$(sha256sum "$native" | awk '{print $1}')" > "$record/raw/provider-identities.txt"
echo "alternateNativeSha256=$(sha256sum "$alternate_native" | awk '{print $1}')" >> "$record/raw/provider-identities.txt"
echo "phase=$phase" >> "$record/raw/provider-identities.txt"

case "$phase" in
  isolation)
    run_probe isolation-second-root 600 --alternate-native "$alternate_native" --isolation-mode second-root || exit 1
    run_probe isolation-mixed-patch 600 --alternate-native "$alternate_native" --isolation-mode mixed-patch || exit 1
    ;;
  timing)
    for process in 1 2 3 4 5; do
      run_probe "timing-process-$process" 3600 --warmups 20 --measured-iterations 200 --seed "$((110 + process))" || exit 1
    done
    ;;
  long-run)
    case "$long_run_phase" in
      preflight) run_probe long-run-preflight 720 --duration-seconds 600 --phase-label preflight ;;
      managed) run_probe long-run-managed 3900 --duration-seconds 3600 --phase-label managed ;;
      host-async) run_probe long-run-host-async 3900 --duration-seconds 3600 --phase-label host-async ;;
      device-input) run_probe long-run-device-input 3900 --duration-seconds 3600 --phase-label device-input ;;
      mixed) run_probe long-run-mixed 2100 --duration-seconds 1800 --phase-label mixed ;;
    esac
    ;;
esac

date -u +%Y-%m-%dT%H:%M:%SZ > "$record/raw/completed-utc.txt"
find "$record" -type f ! -name artifact-hashes.txt -print0 | sort -z | xargs -0 sha256sum > "$record/raw/artifact-hashes.txt"
