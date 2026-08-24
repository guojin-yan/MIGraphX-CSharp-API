#!/usr/bin/env python3

import hashlib
import json
import pathlib
import sys


def fail(message):
    raise SystemExit(message)


if len(sys.argv) != 3:
    fail("usage: verify-supervisor-record.py RECORD PHASE")

record = pathlib.Path(sys.argv[1])
phase = sys.argv[2]
if not record.is_absolute() or not record.is_dir():
    fail("record must be an existing absolute directory")
if phase not in {"preflight", "managed", "host-async", "device-input", "mixed"}:
    fail("unknown long-run phase")

raw = record / "raw"
metadata_path = raw / "long-run-supervisor.json"
if not metadata_path.is_file():
    fail("long-run-supervisor.json is missing")
with metadata_path.open(encoding="utf-8") as stream:
    metadata = json.load(stream)
if metadata.get("schemaVersion") != "1.0.0":
    fail("unsupported supervisor metadata schema")
if metadata.get("evidence") != "runtime-candidate-executed-review-required":
    fail("supervisor evidence classification drifted")
if metadata.get("state") != "executed":
    fail("supervisor state is not executed")
if metadata.get("longRunPhase") != phase:
    fail("supervisor phase mismatch")
expected_duration = {
    "preflight": 600,
    "managed": 3600,
    "host-async": 3600,
    "device-input": 3600,
    "mixed": 1800,
}[phase]
if metadata.get("durationSeconds") != expected_duration:
    fail("supervisor phase duration drifted")
if metadata.get("processRestartIntervalSeconds") != 600:
    fail("process restart interval must remain 600 seconds")
if any(metadata.get(name) != 0 for name in ("preSnapshotExitCode", "probeExitCode", "telemetryExitCode", "postSnapshotExitCode")):
    fail("supervisor contains a nonzero exit code")
if metadata.get("hostRestartHandledBySupervisor") is not False:
    fail("supervisor cannot claim to handle a host restart")
if phase == "host-async" and metadata.get("hostRestartProofValidated") is not True:
    fail("host-async record lacks validated restart proof")
if phase != "host-async" and metadata.get("hostRestartProofValidated") is not False:
    fail("non-async record unexpectedly claims restart proof")

for label in ("pre", "post"):
    snapshot = raw / f"gpu-snapshot-{label}.json"
    output = raw / f"gpu-snapshot-{label}.txt"
    error = raw / f"gpu-snapshot-{label}.err"
    if not snapshot.is_file() or not output.is_file() or not error.is_file():
        fail(f"GPU snapshot files are incomplete: {label}")
    with snapshot.open(encoding="utf-8") as stream:
        snapshot_data = json.load(stream)
    if snapshot_data.get("schemaVersion") != "1.0.0" or snapshot_data.get("exitCode") != 0:
        fail(f"GPU snapshot did not pass: {label}")
    if error.stat().st_size != 0:
        fail(f"GPU snapshot stderr is non-empty: {label}")

telemetry_exit = raw / "gpu-telemetry.exit-code"
telemetry_json = raw / "gpu-telemetry.json"
telemetry_err = raw / "gpu-telemetry.err"
telemetry_stdout = raw / "gpu-telemetry-stdout.log"
if not all(path.is_file() for path in (telemetry_exit, telemetry_json, telemetry_err, telemetry_stdout)):
    fail("GPU telemetry files are incomplete")
if telemetry_exit.read_text(encoding="utf-8").strip() != "0":
    fail("GPU telemetry did not exit successfully")
if telemetry_err.stat().st_size != 0 or telemetry_json.stat().st_size == 0:
    fail("GPU telemetry is empty or has stderr")

manifest = raw / "artifact-hashes.txt"
if not manifest.is_file():
    fail("final artifact manifest is missing")
entries = {}
for line in manifest.read_text(encoding="utf-8").splitlines():
    try:
        digest, relative = line.split("  ", 1)
    except ValueError:
        fail("malformed final artifact manifest entry")
    if len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
        fail("final artifact manifest contains an invalid digest")
    relative = relative.replace("\\", "/")
    if relative.startswith("./"):
        relative = relative[2:]
    path = pathlib.PurePosixPath(relative)
    if path.is_absolute() or not relative or ".." in path.parts:
        fail("final artifact manifest contains an unsafe path")
    if relative in entries:
        fail(f"final artifact manifest contains a duplicate path: {relative}")
    entries[relative] = digest
    target = record.joinpath(*path.parts)
    if not target.is_file():
        fail(f"manifest references missing file: {relative}")
    actual = hashlib.sha256(target.read_bytes()).hexdigest()
    if actual != digest:
        fail(f"manifest hash mismatch: {relative}")
for required in (
    "raw/long-run-supervisor.json",
    "raw/gpu-snapshot-pre.json",
    "raw/gpu-snapshot-post.json",
    "raw/gpu-telemetry.json",
    "raw/gpu-telemetry-stdout.log",
):
    if required not in entries:
        fail(f"manifest is missing required file: {required}")

print("supervisor-record-valid")
