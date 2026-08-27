# MIGraphXSharp

[Chinese / 中文](README.zh-CN.md)

MIGraphXSharp now has an unpublished M11 `0.9.0-rc.8` local candidate plus an M12 interface-expansion batch. M12 adds managed wrappers for the remaining generated shape/argument, graph, TensorFlow, quantization, context, and experimental custom-op declarations. The batch has passed local managed compilation, fake-native fixture compilation, focused M12 tests, project-quality tests, and a package-only candidate run. The source-bound M12 record promotes two case-level decisions and retains thirteen deferred cases; the repository default remains `0.0.0` and no package is published.

## Status

- The core builds the exact 15-TFM matrix from `net46` through `net10.0`; .NET 7+ uses generated `LibraryImport`, and older targets use generated `DllImport`.
- The frozen input is ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`, header SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`.
- M1 provides explicit/system native loading diagnostics, exact status mapping, strict UTF-8, and owned target/program lifetimes.
- M2 adds explicit file and byte-buffer ONNX entry points for one static, standard float32 input/output, synchronous GPU-target compile with offload-copy, pinned input, and copied output.
- M3 inventories 159 functions, 2 enums, 25 opaque handles, and 6 callbacks. Its 192 entities close as 144 generated, 47 handwritten-policy, 1 unsupported, and 0 configuration-unavailable; functions close as 117/41/1/0.
- M4 exposes explicit `MIGraphXProgram`, `MIGraphXShape`, `MIGraphXArgument`, `MIGraphXTarget`, ONNX/compile options, parameter map, and copied output collection objects. Its separate 192-item high-level map closes as 52 supported, 139 planned, and 1 unsupported.
- M5 adds immutable dynamic dimensions, strict static/dynamic ONNX overrides, fixed-version `msgpack` Save/Load, and an explicit-root integrity-checked model cache. Its map closes as 74 supported, 117 planned, and 1 unsupported.
- M6 adds the optional `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` adapter with three public types and eleven members. It submits native `migraphx_program_run_async` using the fixed C ABI name `ihipStream_t`, retains program/map/input/output/device leases through HipStream completion, and closes the map as 75 supported, 116 planned, and 1 unsupported.
- M7 pins the signed ROCm 7.2.1 Ubuntu Noble amd64 source metadata and exact MIGraphX root package, then freezes `system-native` as the deployment mode. Users install the coherent native closure from AMD's official repository; this project ships managed assemblies only.
- M8 records a versioned compatibility baseline for core and adapter across all 15 TFMs. The baseline is intentionally reviewable during `0.x.x` interface expansion; candidate version, assembly/file/informational version, cache identity, exact package dependencies, source commit, product SBOM, and provenance remain one local evidence chain.
- M9 projects five inference-option entry points for ONNX Loop defaults/limits, external-data roots, fast-math, and exhaustive tuning. The aggregate map is 80 supported, 111 planned, and 1 unsupported. At pushed SHA `346cdd0b01a7f8039f5deb93058928403fccc7dd`, ROCm 7.2.1 accepted the five recorded values and completed a reviewed gfx1100 Identity compile/run with an exact reference match.
- M10 projects four entry points: a strict-UTF-8 copied ONNX parser-registry snapshot plus explicit argument/program native content comparison. Shape equality remains planned. Its post-build external record at pushed `e2386dc69e7640f8ff12d95284e56c3f02c87938` independently promoted the four adopted entry points to `runtime-executed` for one exact host/build.
- M11 keeps core `27 types / 160 members`, adapter `3 / 11`, and aggregate `84/107/1`. The rc.5 official diagnostic session passed registry/lifecycle checks, then timed out in the first file/buffer iteration and exposed a missing TERM-to-KILL escalation. rc.6 adds durable per-stage tracing and a fixed 10-second kill escalation, but has no official runtime promotion. Isolation negatives, long-run, and timing remain `runtime-deferred`. Fixed MIGraphX 2.15.0 Windows runtime is `not-applicable` because AMD documents MIGraphX for Linux and marks Windows AI libraries unavailable.
- M12 is tracked separately from the historical M3-M11 evidence map. It adds `MIGraphXShape` scalar/stride/index snapshots, argument empty/generate/load/save/clone, assign-to clones for owned options/programs, module/instruction graph handles, TensorFlow options/parsing, FP16/BF16/INT8/FP8 option paths, experimental context, raw custom-op callback registration, and restricted operation factories/clones with no attributes or typed, fully materialized common attributes. `MIGraphXOperationAttributes` now includes named reshape, transpose, slice, multibroadcast, and topk factories; arbitrary C format placeholders and general varargs remain unsupported. Two case-level runtime decisions are reviewed; the remaining thirteen cases stay deferred. See [the M12 local interface expansion](docs/design/m12-local-interface-expansion.md).
- Static shape metadata includes mapped scalar type, lengths, strides, rank, checked element/byte counts, standard, and packed flags. Typed arguments own copied host memory; parameter maps deep-copy arguments; run outputs are copied before native collections are released.
- One normalized model emits 158 matching `LibraryImport` and `DllImport` EntryPoints. The general C-varargs `migraphx_operation_create` contract remains explicitly unsupported; constrained factories expose the no-attribute call shape plus typed, fully materialized common attributes without passing variadic values.
- All 159 header functions match the hash-verified official ELF; its additional private test export is separately classified. These M3 results are `statically-verified`, not official runtime execution.
- Local fake-native tests execute loader, frontend/export classification, object construction, parse, immutable shape snapshots, typed host copies, multi-item collections, compile, synchronous run, targeted failure cleanup, and concurrency/Dispose boundaries. They remain test-substitute evidence and are recorded separately from official runtime evidence.
- M1/M2 official runtime validation was revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The official loader, target/program lifecycle, file/buffer ONNX parse, GPU compile, synchronous run, and Identity reference comparison executed successfully.
- The M6 host path requires `offloadCopy=true`; its device-input path accepts only `HipDeviceMemory`, requires `offloadCopy=false`, and explicitly copies outputs D2H after stream completion. M12 locally adds graph/module, context, and custom-op wrappers; fake-native evidence is local test-substitute evidence only. Graph capture interop, arbitrary device pointers, and Runtime NuGet packaging remain excluded.
- M4/M5/M6 behavior has only local `statically-verified` and `fake-native-executed` evidence. There is no M6 official GPU, zero-copy, overlap, or performance claim.
- The core and adapter packages contain no AMD or fake-native binaries. No `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.*` package is produced or planned; `eng/pack.ps1 -Runtime` reports `MIGRAPHX1001` and directs users to the AMD official system repository.

## Install

Build the local-only managed candidate package:

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.9.0-rc.8
```

The frozen NuGet/project/assembly name is `JYPPX.ROCm.MIGraphX.CSharp.API`; the C# namespace is `JYPPX.ROCm.MIGraphXSharp`. Do not publish this engineering candidate.

Install ROCm 7.2.1 and MIGraphX `2.15.0.70201-81~24.04` using AMD's [official ROCm 7.2.1 Linux quick-start guide](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html). For the audited Ubuntu 24.04 repository, the exact MIGraphX package is `migraphx-rpath7.2.1`; its declared dependencies must be resolved by the same AMD repository rather than copied into the application.

## Use

Native probing is explicit. A caller-supplied path must be absolute; the loader does not modify `PATH`, `LD_LIBRARY_PATH`, or TLS settings and does not download native files.

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var report = MIGraphXEnvironment.Probe("/opt/rocm-7.2.1/lib/libmigraphx_c.so.3");
Console.WriteLine(report.State); // executed, loaded, or not-available

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3",
    "/absolute/path/to/identity.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));

var modelBytes = System.IO.File.ReadAllBytes("/absolute/path/to/identity.onnx");
using var parseOptions = new MIGraphXOnnxOptions("/opt/rocm-7.2.1/lib/libmigraphx_c.so.3");
using var program = MIGraphXProgram.ParseOnnxBuffer(modelBytes, parseOptions);
var inputShape = program.GetParameterShapes()["input"];
```

## M5 dynamic shapes and cache

`MIGraphXDynamicDimension` and `MIGraphXShape.CreateDynamic` model ranges without exposing native handles. `MIGraphXOnnxOptions` accepts strict UTF-8 static or dynamic input overrides; concrete static shapes are still required before creating typed host arguments. `MIGraphXFileOptions` restricts Save/Load to the tested `msgpack` format.

`MIGraphXModelCache` requires an explicit absolute root. Its key is a SHA-256 of normalized model, fixed-header/API, managed build, native fingerprint, target, compile options, format, and ordered overrides. A JSON sidecar (schema 1) authenticates the payload hash; same-directory temporary files are atomically replaced. Cache hits, misses, corruption, and rebuild provenance are observable through `MIGraphXCacheResult`. The cache is not portable across MIGraphX versions, targets, compile options, or native fingerprints.

`ProbeSystem` audits application RID, application-base, and system-loader candidates. Linux candidates include `libmigraphx_c.so.3` and `migraphx_c`. For fixed MIGraphX 2.15.0, the Windows native provider is `not-applicable` by AMD's documented component boundary; the loader candidate remains diagnostic only. macOS also has no official runtime support claim.

## M6 async HIP interop

The optional adapter depends on both managed core packages while keeping the cores independent. `RunHostAsync` accepts a host parameter map compiled with `offloadCopy=true`. `RunDeviceAsync` validates exact names, concrete packed shapes, capacity, runtime client, and device ordinal before borrowing `HipDeviceMemory` for a program compiled with `offloadCopy=false`.

`MIGraphXHipAsyncRun.TryComplete` is non-blocking, `Synchronize` blocks, and `Outputs` becomes available only after stream completion has produced owned host copies. Explicit result disposal waits when pending. The adapter exposes no raw pointer or free-form backend name, rejects graph capture, and performs an explicit D2H output copy for device inputs. See the [M6 design](docs/design/m6-hip-async-interop.md) for the state and ownership contract.

## M7 system-native deployment

The reviewed native closure is too large for a maintainable Runtime nupkg and overlaps ROCm assets already governed by AMD's package repository. M7 therefore rejects native NuGet distribution permanently: the managed core and adapter stay native-free, and users install MIGraphX plus its ROCm dependencies through one coherent AMD system repository/version family.

The loader keeps the established explicit path, application RID directory, application base, and system-loader diagnostics. It does not download libraries or modify `PATH`/`LD_LIBRARY_PATH`. Use an absolute path when deterministic selection matters, and do not assemble a private closure from `.cache`, extracted Debian packages, or mixed ROCm versions. See the [M7 design](docs/design/m7-runtime-packaging.md), [deployment guide](docs/guides/runtime-deployment.md), and [M7 validation status](docs/validation/m7-local-validation.md).

## M8 API baseline and prerelease readiness

Schema 2 snapshots baseline signatures, defaults, generic constraints, nullable metadata, identity, and identical 15-TFM availability. Intentional `0.x.x` API additions update the snapshots through review. The managed SemVer is independent of ROCm/MIGraphX; changing the managed package does not update APT. Historical `0.9.0-rc.1`, `0.9.0-rc.2`, `0.9.0-rc.3`, `0.9.0-rc.4`, and `0.9.0-rc.5` identities remain immutable; the rc.6 adapter restores exact `[0.9.0-rc.6]` core and `[0.9.1]` HipSharp packages from a mapped local feed.

The candidate gate produces per-file managed SBOM data, local unsigned provenance, NuGet ZIP hashes, and separate normalized content hashes. The authorized `346cdd0...` session revalidated M1/M2 and executed the M9 option smoke; M4-M6, system-native negatives, restart/long-run work, and performance remain unexecuted beyond their stated historical scope. `release-candidate-local` is not `release-ready` or published.

## M9 inference options and cloud record

`MIGraphXOnnxOptions` now exposes non-negative Loop defaults/limits and an absolute strict-UTF-8 external-data path. `MIGraphXCompileOptions` keeps its existing constructor and adds an explicit fast-math/exhaustive-tune overload. Local tests verify forwarding, path validation, exact native failure attribution, and cleanup. The credential-free cloud script ran on the clean pushed `346cdd0...` checkout; transferred hashes and independent JSON review promoted official setter acceptance plus the Identity compile/run to `runtime-executed`. Loop behavior, real external payloads, exhaustive tuning enabled, and representative fast-math accuracy remain planned.

## M10 registry and native content comparison

`MIGraphXOnnxWorkflow.GetRegisteredOperators` returns a read-only managed copy of the loaded version's ONNX parser names. It is a capability hint, not a model/opset/device support guarantee. `MIGraphXArgument.HasSameNativeContent` compares exact host-backed shape/data content; `MIGraphXProgram.HasSameNativeContent` compares the fixed version's printed program structure. Neither method changes general .NET equality/hash/operator semantics. Reverse concurrent comparisons use a stable two-owner lock order and serialize with Dispose.

Local fake-native tests cover strict UTF-8, overflow, missing exports, mid-copy failure, invalid bool, comparison differences, reverse concurrency, and Dispose races through legacy and modern interop paths. The later M10 post-build external record reviewed registry stability plus argument/program true/false/Dispose cases on the exact rc.2 candidate. See the [M10 design](docs/design/m10-onnx-registry-native-comparison.md) and [runtime plan](docs/validation/m10-runtime-plan.md).

## M11 official-runtime hardening

M11 generates project-owned Identity, ordered Identity+Neg multi-output, and dynamic Identity ONNX fixtures with frozen hashes. `compatibility/m11-runtime-cases.json` records each M4-M6 success/rejection boundary, synchronization/copy boundary, ownership, iteration, timeout, prerequisite, evidence level, and uncovered claim. `tools/m11-runtime-probe` restores only exact core/adapter/HipSharp packages and can write only `runtime-candidate-executed-review-required`; a separate reviewer recomputes identities and case results.

The rc.5 failed bounded functional record is diagnostic only; rc.6 has no official host/time-window execution. Fresh-process cache cases, official isolation negatives, the five-hour long-run layer, and timing samples remain unexecuted for the new candidate. Enqueue is not inference timing, device pointers are not a zero-copy claim, and no performance comparison is permitted. See the [M11 hardening plan](docs/validation/m11-runtime-hardening-plan.md).

## Build

Install the .NET 10 SDK selected by `global.json`, PowerShell 7, CMake, and a C compiler, then run:

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m3-coverage.ps1
.\eng\verify-m4-coverage.ps1
.\eng\verify-m5-coverage.ps1
.\eng\verify-m6-coverage.ps1
.\eng\verify-m9-coverage.ps1
.\eng\verify-m10-coverage.ps1
.\eng\verify-m11-coverage.ps1
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.9.0-rc.8 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package -Version 0.9.0-rc.8
$adapter = .\eng\pack-adapter.ps1 -Configuration Release -Version 0.9.0-rc.8 -HipSharpPackagePath $hipPackage -NoBuild
.\eng\verify-adapter-package.ps1 -PackagePath $adapter -Version 0.9.0-rc.8 -HipSharpPackagePath $hipPackage
.\eng\docs.ps1 -Configuration Release -Version 0.9.0-rc.8 -NoBuild
```

Build, static official-ELF evidence, fake-native execution, and official MIGraphX runtime execution remain separate evidence levels. The M1/M2 runtime claim is limited to the exact pushed SHA, environment, model, shape, and synchronous offload-copy path recorded in the [official runtime summary](docs/validation/m1-m2-official-runtime.md).

## Documentation

See the [M8 design](docs/design/m8-api-release-readiness.md), [M9 option design](docs/design/m9-inference-options.md), [M10 design](docs/design/m10-onnx-registry-native-comparison.md), [M10 local validation](docs/validation/m10-local-validation.md), [M11 hardening plan](docs/validation/m11-runtime-hardening-plan.md), [API/versioning guide](docs/guides/api-versioning.md), [Runtime deployment guide](docs/guides/runtime-deployment.md), and [official M1/M2 runtime summary](docs/validation/m1-m2-official-runtime.md).

## License

Copyright 2026 Guojin Yan. This managed project is licensed under the [Apache License 2.0](LICENSE); see [NOTICE](NOTICE). AMD/ROCm/MIGraphX components are not included and remain subject to their own licenses.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), the [M3 normalized model](compatibility/m3-normalized-api.json), and the [M10 high-level map](compatibility/m10-high-level-api-map.json). Public APIs require equivalent Chinese and English XML documentation. Never commit AMD binaries, fake-native build output, models, credentials, cloud connection data, or generated declarations that cannot be reproduced from the fixed input.
