# M10 local validation

M10 is locally closed at `fake-native-executed`/`statically-verified` and remains `runtime-deferred`. The unpublished managed candidate version is `0.9.0-rc.2`; the repository default remains `0.0.0`. No package, tag, release, Pages deployment, or Actions run is authorized.

## Local gates

The final source candidate must run:

```powershell
$commit = (git rev-parse HEAD).Trim()
$hipPackage = '<absolute path to the hash-reviewed HipSharp 0.9.1 nupkg>'
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m1-abi.ps1 -AcquireInputs
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
.\eng\verify-m4-coverage.ps1
.\eng\verify-m5-coverage.ps1
.\eng\verify-m6-coverage.ps1
.\eng\verify-m9-coverage.ps1
.\eng\verify-m10-coverage.ps1
.\eng\build.ps1 -Configuration Release -Version 0.9.0-rc.2 -RepositoryCommit $commit
.\eng\test.ps1 -Configuration Release -Version 0.9.0-rc.2 -RepositoryCommit $commit -NoBuild
.\eng\docs.ps1 -Configuration Release -Version 0.9.0-rc.2 -RepositoryCommit $commit -NoBuild
$core = .\eng\pack.ps1 -Configuration Release -Version 0.9.0-rc.2 -NoBuild
.\eng\verify-package.ps1 -PackagePath $core -Version 0.9.0-rc.2
$adapter = .\eng\pack-adapter.ps1 -Configuration Release -Version 0.9.0-rc.2 -HipSharpPackagePath $hipPackage -NoBuild
.\eng\verify-adapter-package.ps1 -PackagePath $adapter -Version 0.9.0-rc.2 -HipSharpVersion 0.9.1 -HipSharpPackagePath $hipPackage
.\eng\verify-release-candidate.ps1 -CorePackagePath $core -AdapterPackagePath $adapter -HipSharpPackagePath $hipPackage -Version 0.9.0-rc.2 -RepositoryCommit $commit
```

The M10 coverage gate recomputes the aggregate map from the reviewed M5 base plus M6, M9, and M10 overlays. It requires 84 supported, 107 planned, and one unsupported entry; all five decisions; four ownership records; exact generated declarations; the unchanged 192/159/158 inventory counts; public baseline entries; tests; documentation; and strict fake-native ABI allowlisting.

## Executed substitute cases

The registry substitute returns copied ASCII and non-ASCII names and an empty list. Fault modes cover count overflow, null and empty names, invalid UTF-8, exact size/name status, mid-copy failure, count drift, out-of-range index, a second library root, and a full M2/M4 library missing only M10 exports. Separate isolated processes verify exact missing-export diagnostics for both the registry and adopted equality sets. Partial results do not escape and native live counts return to zero.

Argument comparison covers self, independent equal content, one-element difference, shape difference, exact status, invalid or unwritten C bool, reverse concurrent calls, Dispose during a blocked comparison, and post-dispose failure. Program comparison covers empty, parsed, different input shapes, compile-state changes, independent equal structures, exact status, invalid or unwritten C bool, reverse concurrency, and Dispose races.

The representative interop runner executes M1/M2/M10 with `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0`. This traverses DllImport for legacy targets and LibraryImport for modern targets. It remains test-substitute evidence.

## Public and package boundary

The core keeps 27 public types and adds three non-type records: `MIGraphXOnnxWorkflow.GetRegisteredOperators`, `MIGraphXArgument.HasSameNativeContent`, and `MIGraphXProgram.HasSameNativeContent`. The adapter stays at 3 types and 11 members. Schema 2 requires identical signatures across all 15 TFMs.

The `0.9.0-rc.2` package evidence must be generated under its own source-SHA directory. It cannot overwrite or reuse M8 `0.9.0-rc.1` hashes. The final release-candidate command requires clean `main` with `HEAD == origin/main == $commit`. Core, adapter, and HipSharp identities, exact dependency ranges, ZIP hashes, normalized content hashes, managed manifest, CycloneDX SBOM, local unsigned provenance, vulnerability scan, and isolated consumers are regenerated after the final commit is pushed.

## Unexecuted work

No official MIGraphX registry or equality call ran in M10. M4-M6 official positive/negative execution, system-native isolation failures, restart/long-run work, performance methodology, and Windows runtime also remain open. The [M10 official runtime plan](m10-runtime-plan.md) is a future authorization request, not evidence.

中文摘要：M10 本地门禁执行 registry、argument/program comparison 的成功、失败、非法或未写 C bool、并发与 Dispose 边界，并在四个代表 TFM 穿过双 interop 路径。新增 3 条 core public 记录，累计映射为 84/107/1；没有新官方授权，因此阶段是 `runtime-deferred`。
