#!/usr/bin/env python3

import json
import pathlib
import re
import sys


def fail(message):
    raise SystemExit(message)


if len(sys.argv) != 2:
    fail("usage: verify-restart-proof.py PROOF_JSON")

path = pathlib.Path(sys.argv[1])
try:
    with path.open(encoding="utf-8") as stream:
        proof = json.load(stream)
except (OSError, json.JSONDecodeError) as exc:
    fail(f"restart proof is not readable JSON: {exc}")

if not isinstance(proof, dict):
    fail("restart proof must be a JSON object")
if proof.get("schemaVersion") != "1.0.0":
    fail("restart proof schemaVersion must be 1.0.0")
if proof.get("previousPhase") != "managed" or proof.get("nextPhase") != "host-async":
    fail("restart proof must bridge managed to host-async")
if proof.get("restartObserved") is not True:
    fail("restart proof must explicitly record restartObserved=true")

sha_pattern = re.compile(r"^[a-f0-9]{64}$")
for name in ("preBootFingerprintSha256", "postBootFingerprintSha256"):
    value = proof.get(name)
    if not isinstance(value, str) or sha_pattern.fullmatch(value) is None:
        fail(f"restart proof has an invalid {name}")
if proof["preBootFingerprintSha256"] == proof["postBootFingerprintSha256"]:
    fail("restart proof boot fingerprints must differ")

for name in ("preCapturedUtc", "postCapturedUtc"):
    value = proof.get(name)
    if not isinstance(value, str) or not value:
        fail(f"restart proof has no {name}")

print("restart-proof-valid")
