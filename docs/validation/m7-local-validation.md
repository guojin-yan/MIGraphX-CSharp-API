# M7 local Runtime validation

M7 local validation is intentionally split between reproducible metadata checks and work that cannot be claimed without a complete candidate. The final local state is `runtime-deferred`, not `runtime-candidate-staged` or `runtime-executed`.

## Reproducible local gates

```powershell
.\eng\generate-runtime-metadata.ps1 -Check
.\eng\validate-runtime-manifest.ps1
.\eng\test-runtime-supply-chain.ps1
.\eng\test-runtime-source.ps1
```

The first three commands need only tracked metadata. They verify deterministic SBOM/provenance, closure and size calculations, topology, state separation, path/alias rules, and both controlled/direct pack rejection. Fourteen mutation cases cover source, license, closure, architecture, traversal, alias, SBOM, promotion receipt, package marker, state, release authorization, size, exact dependency version, and duplicate package paths.

`test-runtime-source.ps1` uses the Git-ignored cache populated by `prepare-runtime.ps1`. It verifies the AMD key fingerprint, signed `InRelease`, signed `Packages.gz` binding, exact root package metadata and bytes, then rejects package hash/version, architecture, host, and `InRelease` mutations. Cached `.deb`, extracted ELF, keyring, and temporary reports are not tracked.

Unit tests use small synthetic files to prove the Runtime closure marker accepts a complete same-directory family and rejects a missing marker, changed dependency, and traversal. The legacy explicit/system-native path remains marker-free. Package and ProjectQuality tests bind manifest counts/hashes/state, the exact layered dependency, CycloneDX version, unconditional direct-pack error, navigation, and evidence language.

## Verified facts

- RID/source: Ubuntu 24.04 Noble amd64, ROCm 7.2.1.
- MIGraphX root package: `migraphx-rpath7.2.1` `2.15.0.70201-81~24.04`, 68,651,368 bytes.
- Audited MIGraphX inventory: six canonical ELF files, six aliases, 849,471,088 canonical bytes and 1,698,942,176 alias-materialized bytes.
- Root C ELF: SHA-256 `581582270fe1a8bb323eba04fb23f2969bbcdcff4a2a92d501eba7adf6a349ac`, SONAME `libmigraphx_c.so.3`, ELF64 x86-64.
- Metadata: CycloneDX 1.5 SBOM, provenance, dependency report, and declared content digest are deterministic and mutation-tested.
- Pack state: no Runtime nupkg; every pack entry remains fail closed with `MIGRAPHX1001`.

## Not executed

Provider archives were not promoted into staging, and no Runtime package was built. Consequently there is no nupkg content/ELF audit, local-feed Runtime restore, clean package-only consumer, mixed package/system process test, package-only `ldd`/loader trace/process map, GPU run, M4/M5 smoke, or M6 host/device async run. No new Radeon Cloud or official-host authorization was provided, and old M1/M2 access or results were not reused.

中文摘要：本地门禁证明签名源锁、根 ELF、确定性元数据、mutation 拒绝与 loader marker 失败关闭；它没有证明完整可再分发闭包、Runtime 包或 package-only GPU 执行。
