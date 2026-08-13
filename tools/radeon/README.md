# Radeon Cloud scripts

These scripts are an idempotent, credential-free M0 baseline for `/workspace/MIGraphX-CSharp-API`. They require a clean detached checkout of a pushed 40-character SHA. They do not connect to Radeon Cloud, install floating tool versions, disable TLS, package native files, or claim GPU execution.

Run `env-report.sh`, `bootstrap.sh`, then `COMMIT_SHA=<sha> cloud-test.sh` only after the owner explicitly authorizes a cloud session. Preserve and redact `test-results` using the outer Radeon Cloud record policy.
