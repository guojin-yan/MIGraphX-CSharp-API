# MIGraphXSharp

[Chinese / 中文](README.zh-CN.md)

MIGraphXSharp `0.0.0` now contains the M1 lifecycle foundation, M2 restricted ONNX workflow, M3 reproducible low-level binding pipeline, M4 resource-safe synchronous objects, M5 dynamic-shape/cache policy, and an optional M6 HipSharp async adapter for the official AMD MIGraphX C API. This remains a local engineering candidate, not a published release.

## Status

- The core builds the exact 15-TFM matrix from `net46` through `net10.0`; .NET 7+ uses generated `LibraryImport`, and older targets use generated `DllImport`.
- The frozen input is ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`, header SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`.
- M1 provides explicit/system native loading diagnostics, exact status mapping, strict UTF-8, and owned target/program lifetimes.
- M2 adds explicit file and byte-buffer ONNX entry points for one static, standard float32 input/output, synchronous GPU-target compile with offload-copy, pinned input, and copied output.
- M3 inventories 159 functions, 2 enums, 25 opaque handles, and 6 callbacks. Its 192 entities close as 144 generated, 47 handwritten-policy, 1 unsupported, and 0 configuration-unavailable; functions close as 117/41/1/0.
- M4 exposes explicit `MIGraphXProgram`, `MIGraphXShape`, `MIGraphXArgument`, `MIGraphXTarget`, ONNX/compile options, parameter map, and copied output collection objects. Its separate 192-item high-level map closes as 52 supported, 139 planned, and 1 unsupported.
- M5 adds immutable dynamic dimensions, strict static/dynamic ONNX overrides, fixed-version `msgpack` Save/Load, and an explicit-root integrity-checked model cache. Its map closes as 74 supported, 117 planned, and 1 unsupported.
- M6 adds the optional `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` adapter with three public types and eleven members. It submits native `migraphx_program_run_async` using the fixed `hipStream_t` name, retains program/map/input/output/device leases through HipStream completion, and closes the map as 75 supported, 116 planned, and 1 unsupported.
- Static shape metadata includes mapped scalar type, lengths, strides, rank, checked element/byte counts, standard, and packed flags. Typed arguments own copied host memory; parameter maps deep-copy arguments; run outputs are copied before native collections are released.
- One normalized model emits 158 matching `LibraryImport` and `DllImport` EntryPoints. The C-variadic `migraphx_operation_create` is explicitly unsupported instead of receiving a guessed ABI.
- All 159 header functions match the hash-verified official ELF; its additional private test export is separately classified. These M3 results are `statically-verified`, not official runtime execution.
- Local fake-native tests execute loader, frontend/export classification, object construction, parse, immutable shape snapshots, typed host copies, multi-item collections, compile, synchronous run, targeted failure cleanup, and concurrency/Dispose boundaries. They remain test-substitute evidence and are recorded separately from official runtime evidence.
- M1/M2 official runtime validation passed at `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The official loader, target/program lifecycle, file/buffer ONNX parse, GPU compile, synchronous run, and Identity reference comparison executed successfully.
- The M6 host path requires `offloadCopy=true`; its device-input path accepts only `HipDeviceMemory`, requires `offloadCopy=false`, and explicitly copies outputs D2H after stream completion. Custom ops, graph editing/capture interop, arbitrary device pointers, and runtime NuGet packages remain excluded.
- M4/M5/M6 behavior has only local `statically-verified` and `fake-native-executed` evidence. There is no M6 official GPU, zero-copy, overlap, or performance claim.
- The core package contains no AMD or fake-native binaries. Runtime packages remain disabled and fail closed.

## Install

Build the local-only managed candidate package:

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.0.0
```

The frozen NuGet/project/assembly name is `JYPPX.ROCm.MIGraphX.CSharp.API`; the C# namespace is `JYPPX.ROCm.MIGraphXSharp`. Do not publish this engineering candidate.

## Use

Native probing is explicit. A caller-supplied path must be absolute; the loader does not modify `PATH`, `LD_LIBRARY_PATH`, or TLS settings and does not download native files.

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var report = MIGraphXEnvironment.Probe(@"C:\absolute\path\to\migraphx_c.dll");
Console.WriteLine(report.State); // executed, loaded, or not-available

var result = MIGraphXOnnxWorkflow.RunFile(
    @"C:\absolute\path\to\migraphx_c.dll",
    @"C:\absolute\path\to\identity.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));

var modelBytes = System.IO.File.ReadAllBytes(@"C:\absolute\path\to\identity.onnx");
using var parseOptions = new MIGraphXOnnxOptions(@"C:\absolute\path\to\migraphx_c.dll");
using var program = MIGraphXProgram.ParseOnnxBuffer(modelBytes, parseOptions);
var inputShape = program.GetParameterShapes()["input"];
```

## M5 dynamic shapes and cache

`MIGraphXDynamicDimension` and `MIGraphXShape.CreateDynamic` model ranges without exposing native handles. `MIGraphXOnnxOptions` accepts strict UTF-8 static or dynamic input overrides; concrete static shapes are still required before creating typed host arguments. `MIGraphXFileOptions` restricts Save/Load to the tested `msgpack` format.

`MIGraphXModelCache` requires an explicit absolute root. Its key is a SHA-256 of normalized model, fixed-header/API, managed build, native fingerprint, target, compile options, format, and ordered overrides. A JSON sidecar (schema 1) authenticates the payload hash; same-directory temporary files are atomically replaced. Cache hits, misses, corruption, and rebuild provenance are observable through `MIGraphXCacheResult`. The cache is not portable across MIGraphX versions, targets, compile options, or native fingerprints.

`ProbeSystem` audits application RID, application-base, and system-loader candidates. Linux candidates include `libmigraphx_c.so.3` and `migraphx_c`. Windows and macOS candidates are implemented for diagnostics but have no official MIGraphX runtime support claim.

## M6 async HIP interop

The optional adapter depends on both managed core packages while keeping the cores independent. `RunHostAsync` accepts a host parameter map compiled with `offloadCopy=true`. `RunDeviceAsync` validates exact names, concrete packed shapes, capacity, runtime client, and device ordinal before borrowing `HipDeviceMemory` for a program compiled with `offloadCopy=false`.

`MIGraphXHipAsyncRun.TryComplete` is non-blocking, `Synchronize` blocks, and `Outputs` becomes available only after stream completion has produced owned host copies. Explicit result disposal waits when pending. The adapter exposes no raw pointer or free-form backend name, rejects graph capture, and performs an explicit D2H output copy for device inputs. See the [M6 design](docs/design/m6-hip-async-interop.md) for the state and ownership contract.

## Build

Install the .NET 10 SDK selected by `global.json`, PowerShell 7, CMake, and a C compiler, then run:

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m3-coverage.ps1
.\eng\verify-m4-coverage.ps1
.\eng\verify-m5-coverage.ps1
.\eng\verify-m6-coverage.ps1
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
$adapter = .\eng\pack-adapter.ps1 -Configuration Release -Version 0.0.0 -HipSharpVersion 0.9.1 -NoBuild
.\eng\verify-adapter-package.ps1 -PackagePath $adapter -Version 0.0.0 -HipSharpVersion 0.9.1
.\eng\docs.ps1 -Configuration Release -NoBuild
```

Build, static official-ELF evidence, fake-native execution, and official MIGraphX runtime execution remain separate evidence levels. The M1/M2 runtime claim is limited to the exact pushed SHA, environment, model, shape, and synchronous offload-copy path recorded in the [official runtime summary](docs/validation/m1-m2-official-runtime.md).

## Documentation

See the [M4 managed-object design](docs/design/m4-managed-object-model.md), [M5 dynamic-shape and cache design](docs/design/m5-dynamic-shape-cache.md), [M6 async/HIP design](docs/design/m6-hip-async-interop.md), [managed object guide](docs/guides/managed-objects.md), [M6 local validation](docs/validation/m6-local-validation.md), [platform evidence](docs/compatibility/platforms.md), and [official M1/M2 runtime summary](docs/validation/m1-m2-official-runtime.md).

## License

Copyright 2026 Guojin Yan. This managed project is licensed under the [Apache License 2.0](LICENSE); see [NOTICE](NOTICE). AMD/ROCm/MIGraphX components are not included and remain subject to their own licenses.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), the [M3 normalized model](compatibility/m3-normalized-api.json), and the [M6 high-level map](compatibility/m6-high-level-api-map.json). Public APIs require equivalent Chinese and English XML documentation. Never commit AMD binaries, fake-native build output, models, credentials, cloud connection data, or generated declarations that cannot be reproduced from the fixed input.
