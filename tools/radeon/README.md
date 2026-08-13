# Radeon Cloud scripts

These credential-free scripts require `/workspace/MIGraphX-CSharp-API` to be a clean detached checkout of a pushed 40-character SHA. They do not connect to Radeon Cloud, install floating tool versions, disable TLS, package native files, or persist connection details.

The unified M1/M2 cloud test verifies the frozen installed header, official C library export subset, strict non-ASCII UTF-8 target name, target/program lifecycle, deterministic Identity model, file/buffer ONNX parse, static shapes, GPU-target compile, synchronous run, and reference output. It does not exercise async, streams, device buffers, dynamic shapes, multiple inputs/outputs, or performance.

Run `env-report.sh`, `bootstrap.sh`, then `COMMIT_SHA=<sha> cloud-test.sh` only after the Owner explicitly authorizes the current cloud session. The script labels raw M2 output as a runtime candidate; only reviewed, redacted evidence may promote it to `runtime-executed`. Preserve and redact `test-results` under the outer Radeon Cloud record policy.
