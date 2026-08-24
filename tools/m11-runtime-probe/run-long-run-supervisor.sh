#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: run-long-run-supervisor.sh --repo DIR --feed DIR --record DIR --fixtures DIR --native FILE --alternate-native FILE --hip FILE --rocm-smi FILE --amd-smi FILE --source-sha SHA --version VERSION --core-sha SHA256 --adapter-sha SHA256 --hipsharp-sha SHA256 --long-run-phase preflight|managed|host-async|device-input|mixed [--restart-proof FILE]" >&2
  exit 2
}

repo=''; feed=''; record=''; fixtures=''; native=''; alternate_native=''; hip=''; rocm_smi=''; amd_smi=''; source_sha=''; version=''; core_sha=''; adapter_sha=''; hipsharp_sha=''; long_run_phase=''; restart_proof=''
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --repo) repo="$2" ;; --feed) feed="$2" ;; --record) record="$2" ;; --fixtures) fixtures="$2" ;;
    --native) native="$2" ;; --alternate-native) alternate_native="$2" ;; --hip) hip="$2" ;;
    --rocm-smi) rocm_smi="$2" ;; --amd-smi) amd_smi="$2" ;;
    --source-sha) source_sha="$2" ;; --version) version="$2" ;; --core-sha) core_sha="$2" ;;
    --adapter-sha) adapter_sha="$2" ;; --hipsharp-sha) hipsharp_sha="$2" ;; --long-run-phase) long_run_phase="$2" ;; --restart-proof) restart_proof="$2" ;;
    *) usage ;;
  esac
  shift 2
done

for directory in "$repo" "$feed" "$fixtures"; do
  [[ "$directory" = /* && -d "$directory" ]] || usage
done
[[ "$record" = /* ]] || usage
for file in "$native" "$alternate_native" "$hip" "$rocm_smi" "$amd_smi"; do
  [[ "$file" = /* && -f "$file" && -x "$file" ]] || usage
done
[[ "$source_sha" =~ ^[a-f0-9]{40}$ ]] || usage
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-rc\.[0-9]+$ ]] || usage
for hash in "$core_sha" "$adapter_sha" "$hipsharp_sha"; do
  [[ "$hash" =~ ^[a-f0-9]{64}$ ]] || usage
done

case "$long_run_phase" in
  preflight) duration_seconds=600 ;;
  managed|host-async|device-input) duration_seconds=3600 ;;
  mixed) duration_seconds=1800 ;;
  *) usage ;;
esac
if [[ "$long_run_phase" = host-async ]]; then
  [[ "$restart_proof" = /* && -f "$restart_proof" ]] || { echo "host-async requires --restart-proof" >&2; exit 1; }
fi

if [[ -e "$record" ]]; then
  [[ -d "$record" && -z "$(find "$record" -mindepth 1 -print -quit)" ]] || { echo "record directory must be new or empty: $record" >&2; exit 1; }
else
  mkdir -p "$record"
fi
mkdir -p "$record/raw"

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
runner="$script_directory/run-resilience.sh"
[[ -x "$runner" ]] || { echo "missing resilience runner: $runner" >&2; exit 1; }
command -v setsid >/dev/null || { echo 'setsid is required for supervised telemetry' >&2; exit 1; }
if [[ "$long_run_phase" = host-async ]]; then
  python3 "$script_directory/verify-restart-proof.py" "$restart_proof" > "$record/raw/restart-proof-validation.txt"
  cp -- "$restart_proof" "$record/raw/host-restart-proof.json"
fi

telemetry_pid=''
stop_telemetry() {
  if [[ -n "$telemetry_pid" ]]; then
    kill -- "-$telemetry_pid" 2>/dev/null || true
    wait "$telemetry_pid" 2>/dev/null || true
    telemetry_pid=''
  fi
}
trap stop_telemetry EXIT

capture_snapshot() {
  local label="$1"
  local started_utc
  local completed_utc
  local exit_code
  started_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  if timeout --kill-after=10s 30s "$rocm_smi" --showmeminfo vram --showuse \
    > "$record/raw/gpu-snapshot-${label}.txt" \
    2> "$record/raw/gpu-snapshot-${label}.err"; then
    exit_code=0
  else
    exit_code=$?
  fi
  completed_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  cat > "$record/raw/gpu-snapshot-${label}.json" <<EOF
{
  "schemaVersion": "1.0.0",
  "command": "rocm-smi --showmeminfo vram --showuse",
  "startedUtc": "$started_utc",
  "completedUtc": "$completed_utc",
  "exitCode": $exit_code
}
EOF
  return "$exit_code"
}

write_supervisor_metadata() {
  local pre_snapshot_exit="$1"
  local probe_exit="$2"
  local telemetry_exit="$3"
  local post_snapshot_exit="$4"
  local state="$5"
  cat > "$record/raw/long-run-supervisor.json" <<EOF
{
  "schemaVersion": "1.0.0",
  "evidence": "runtime-candidate-executed-review-required",
  "state": "$state",
  "longRunPhase": "$long_run_phase",
  "durationSeconds": $duration_seconds,
  "processRestartIntervalSeconds": 600,
  "preSnapshotExitCode": $pre_snapshot_exit,
  "probeExitCode": $probe_exit,
  "telemetryExitCode": $telemetry_exit,
  "postSnapshotExitCode": $post_snapshot_exit,
  "hostRestartHandledBySupervisor": false,
  "hostRestartProofValidated": $([[ "$long_run_phase" = host-async ]] && echo true || echo false),
  "completedUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
EOF
}

write_final_manifest() {
  (
    cd -- "$record"
    find . -type f ! -path './raw/artifact-hashes.txt' -printf '%P\0' | sort -z | xargs -0 sha256sum
  ) > "$record/raw/artifact-hashes.txt"
}

pre_snapshot_exit=0
if capture_snapshot pre; then
  pre_snapshot_exit=0
else
  pre_snapshot_exit=$?
fi
if [[ $pre_snapshot_exit -ne 0 ]]; then
  write_supervisor_metadata "$pre_snapshot_exit" 125 125 125 failed
  write_final_manifest
  exit 1
fi

telemetry_seconds=$((duration_seconds + 10))
telemetry_timeout=$((duration_seconds + 30))
setsid timeout --kill-after=10s "${telemetry_timeout}s" "$amd_smi" monitor --json -p -u -m -v -w 10 -W "$telemetry_seconds" \
  --file "$record/raw/gpu-telemetry.json" \
  > "$record/raw/gpu-telemetry-stdout.log" 2> "$record/raw/gpu-telemetry.err" &
telemetry_pid=$!

set +e
"$runner" \
  --repo "$repo" \
  --feed "$feed" \
  --record "$record" \
  --fixtures "$fixtures" \
  --native "$native" \
  --alternate-native "$alternate_native" \
  --hip "$hip" \
  --source-sha "$source_sha" \
  --version "$version" \
  --core-sha "$core_sha" \
  --adapter-sha "$adapter_sha" \
  --hipsharp-sha "$hipsharp_sha" \
  --phase long-run \
  --long-run-phase "$long_run_phase" \
  --defer-artifact-manifest
probe_exit=$?
if [[ $probe_exit -eq 0 ]]; then
  wait "$telemetry_pid"
  telemetry_exit=$?
else
  kill -- "-$telemetry_pid" 2>/dev/null || true
  wait "$telemetry_pid"
  telemetry_exit=$?
fi
telemetry_pid=''
printf '%s\n' "$telemetry_exit" > "$record/raw/gpu-telemetry.exit-code"
post_snapshot_exit=0
capture_snapshot post || post_snapshot_exit=$?
set -e

state=executed
if [[ $probe_exit -ne 0 || $telemetry_exit -ne 0 || $post_snapshot_exit -ne 0 ]]; then
  state=failed
fi
write_supervisor_metadata 0 "$probe_exit" "$telemetry_exit" "$post_snapshot_exit" "$state"
write_final_manifest

[[ "$state" = executed ]]
