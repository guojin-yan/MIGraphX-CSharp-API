# Radeon Cloud scripts

These credential-free scripts require `/workspace/MIGraphX-CSharp-API` to be a clean detached checkout of a pushed 40-character SHA. They do not connect to Radeon Cloud, install floating tool versions, disable TLS, package native files, or persist connection details.

The M1 cloud test verifies the frozen installed header, official C library export subset, strict non-ASCII UTF-8 target name, and explicit target/program create, assign, and destroy smoke. It does not parse ONNX, compile a graph, run inference, exercise async APIs, or make a GPU performance claim.

Run `env-report.sh`, `bootstrap.sh`, then `COMMIT_SHA=<sha> cloud-test.sh` only after the Owner explicitly authorizes the current cloud session. Preserve and redact `test-results` under the outer Radeon Cloud record policy.
