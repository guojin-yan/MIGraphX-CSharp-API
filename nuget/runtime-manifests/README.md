# Archived Runtime-package feasibility evidence

These files record the M7 analysis that rejected a native Runtime nupkg for ROCm 7.2.1 on Ubuntu 24.04 amd64. They preserve the independently audited MIGraphX root package/ELF bytes, proposed closure, size study, historical SBOM, and provenance for review.

They are no longer an active package allowlist, build input, promotion state machine, or product SBOM. The current policy is managed-only distribution with AMD official system installation. `prepare-runtime.ps1` reads only the signed source-lock fields for optional verification and never stages native files.
