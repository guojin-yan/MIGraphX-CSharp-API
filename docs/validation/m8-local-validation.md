# M8 local release-candidate validation

M8 can reach `release-candidate-local` without a GPU host. That status requires a clean pushed `main`, exact source-bound RC packages, and all managed, package, API, documentation, and no-GPU gates. It does not establish runtime readiness.

## Local candidate gates

Run the following against `0.9.0-rc.1` and the current 40-character commit:

```powershell
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m1-abi.ps1 -AcquireInputs
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
.\eng\test.ps1 -Configuration Release -Version 0.9.0-rc.1 -RepositoryCommit $commit
$core = .\eng\pack.ps1 -Configuration Release -Version 0.9.0-rc.1 -NoBuild
$adapter = .\eng\pack-adapter.ps1 -Configuration Release -Version 0.9.0-rc.1 -HipSharpPackagePath $hip -NoBuild
.\eng\docs.ps1 -Configuration Release -Version 0.9.0-rc.1 -RepositoryCommit $commit -NoBuild
.\eng\verify-release-candidate.ps1 -CorePackagePath $core -AdapterPackagePath $adapter -HipSharpPackagePath $hip -Version 0.9.0-rc.1 -RepositoryCommit $commit
```

The final command requires clean `main` with `HEAD == origin/main == $commit`. Its evidence index under `artifacts/release-evidence/0.9.0-rc.1/<commit>/` binds source, package ZIP hashes, normalized package contents, exact HipSharp `0.9.1`, licenses, vulnerability scan, CycloneDX SBOM, and unsigned local provenance. All commands and exit codes are repeated in the outer M8 phase report.

## Evidence retained and not upgraded

- Core public API: 27 types and 151 records on all 15 TFMs.
- Adapter public API: 3 types and 11 members on all 15 TFMs.
- Managed inventory: 192 items; 75 supported, 116 planned, 1 unsupported.
- Native baseline: Ubuntu 24.04 amd64, ROCm 7.2.1, MIGraphX `2.15.0.70201-81~24.04`.
- M1/M2 runtime evidence was revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd`, one gfx1100 device, and static float32 Identity `[1,4]`; the same session's M9 option smoke is tracked separately.
- M4/M5/M6 remain `statically-verified` and `fake-native-executed`; local RC work does not promote them.

The later `346cdd0...` official session revalidated M1/M2 and M9 option integration only. System-native M4-M6 positive/negative/restart/long-run execution and performance methodology remain unexecuted for M8. The [runtime/long-run/performance methodology](m8-runtime-methodology.md) stays frozen for a future scoped session, and no speed, overlap, zero-copy, or leak claim is made.

中文摘要：M8 本地候选证明 15 TFM、API 冻结、managed 包、clean consumer、SBOM/provenance 与文档门禁；没有新官方主机授权，因此 M4-M6、长跑和性能都不升级为真实运行证据。
