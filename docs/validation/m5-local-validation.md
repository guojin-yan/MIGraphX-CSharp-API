# M5 local validation

M5 is local `statically-verified` and `fake-native-executed` evidence only. No new official ROCm session or authorization was supplied, so the four existing `runtime-executed` records remain tied to `f1a11cfd1701a041cee29188f7600c85b34ae260`.

The 18-test unit suite covers dynamic-dimension validation and managed/native equality, empty-rank and multi-axis dynamic shapes, static-to-dynamic ONNX override replacement, strict UTF-8 names, dynamic collection borrowed-null/count-drift failures, file-options/load cleanup, Save/Load ownership, cache cold rebuild/warm hit, payload corruption recovery, metadata-key invalidation, same-key concurrent writers, and temporary-file cleanup. Every focused failure path returns the M5 fake-native live count to zero. `eng/verify-m5-coverage.ps1` checks the 192-item map closure (`74 supported`, `117 planned`, `1 unsupported`), 13 ownership records, and the reviewed public baseline.

The complete local sequence remains:

```powershell
dotnet tool restore
./eng/generate-interop.ps1 -AcquireHeader -Verify
./eng/verify-m3-coverage.ps1
./eng/verify-m4-coverage.ps1
./eng/verify-m5-coverage.ps1
./eng/build.ps1 -Configuration Release
./eng/test.ps1 -Configuration Release -NoBuild
./eng/verify-m2-abi.ps1 -AcquireInputs
./eng/verify-m3-abi.ps1 -AcquireInputs
$package = ./eng/pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
./eng/verify-package.ps1 -PackagePath $package
./eng/docs.ps1 -Configuration Release -NoBuild
```

中文摘要：M5 目前只有本地静态和 fake-native 证据，没有新增官方 runtime。验证覆盖动态维度、ONNX override、Save/Load、缓存损坏恢复/并发写入以及 192 项映射和 ownership 闭合。
