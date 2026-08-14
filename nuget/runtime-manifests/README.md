# Runtime manifests

`linux-x64.json` is the M7 source and proposed-payload allowlist for ROCm 7.2.1 on Ubuntu 24.04 amd64. It records the independently audited MIGraphX root package and ELF bytes, the layered HipSharp runtime dependency, the provider closure, licenses, system/driver boundary, size gate, SBOM, and verification state.

The manifest is deliberately `runtime-deferred`. It is not a package receipt and does not authorize packing or publication. `candidateStaged`, `verified`, `publishAuthorized`, and `releaseAuthorized` remain separate false fields. The generated SBOM covers the deferred inventory; it does not claim that missing provider licenses or package-only loader traces have been completed.
