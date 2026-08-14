# Getting started

The core public surface supports explicit diagnostics, the unchanged M2 single-input/single-output static float32 workflow, M4 resource-safe synchronous objects, and M5 dynamic-shape metadata plus program persistence/cache APIs. The optional M6 adapter adds native asynchronous submission with HipSharp streams and `HipDeviceMemory` inputs. Graph editing/capture interop and arbitrary device pointers remain excluded.

## Prerequisites

- The .NET SDK selected by `global.json`.
- PowerShell 7 and a C compiler for the local fake-native gate.
- For official runtime probing: the frozen ROCm 7.2.1 MIGraphX installation and its transitive dependencies on a compatible Linux x86-64 host.

## Build and verify

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
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

`generate-interop.ps1` stops before generation if the header SHA-256 differs from the frozen value and verify mode never writes tracked outputs. `verify-m2-abi.ps1` preserves the 41-function workflow gate and deterministic Identity model. `verify-m3-abi.ps1` closes the complete 159-function header against 158 managed EntryPoints, one explicit variadic unsupported item, and 159 official public ELF exports.

For explicit target/program/options/argument composition, continue with the [managed object workflow](managed-objects.md). Its inputs and outputs are copied for safety; it does not expose zero-copy or async behavior.

## Run with a HipSharp stream

Reference the optional adapter and both core packages. Host execution requires a program compiled with `offloadCopy=true`:

```csharp
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;

using var hip = new HipRuntime();
using HipStream stream = hip.CreateStream();
using MIGraphXHipAsyncRun run = program.RunHostAsync(parameters, stream);
run.Synchronize();
float[] output = run.Outputs[0].ToArray<float>();
```

For device input, compile with `offloadCopy=false`, create a `MIGraphXHipDeviceInput` for every exact model parameter, and call `RunDeviceAsync`. The adapter validates shape, capacity, runtime client, and device ordinal. Completion explicitly copies device outputs to owned host arguments; this is not a zero-copy API. See the [M6 design](../design/m6-hip-async-interop.md).

## Run the restricted workflow

```csharp
var result = MIGraphXOnnxWorkflow.RunFile(
    nativeLibraryPath: "/absolute/path/to/libmigraphx_c.so.3",
    modelPath: "/absolute/path/to/model.onnx",
    input: new[] { 1f, 2f, 3f, 4f });
```

Both paths must be absolute. `RunBuffer` accepts a non-empty `byte[]` instead of a model path. The model must expose exactly one static, standard float32 input and output, and input length must match the shape. M2 enables offload-copy, pins input through the synchronous run only, and returns read-only managed snapshots.

## Probe an explicit library

```powershell
dotnet run --project .\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj -c Release -- \
  C:\absolute\path\to\migraphx_c.dll
```

The path must be an absolute file path. Missing files, bad image/architecture, dependency loading, missing export, and other platform failures are distinct diagnostic kinds, and original platform error text is retained. The loader does not change process search-path variables or download files.

Running the smoke command with no path has no native side effects and returns `nativeProbe: not-probed` and `nativeLibrary: not-available`; it never prints `ready`.

## Dynamic shapes and caching

Use `MIGraphXDynamicDimension` and `MIGraphXOnnxOptions` for explicit static or dynamic input overrides. A dynamic `MIGraphXShape` exposes `Rank` and `DynamicDimensions`; `Lengths`, `Strides`, `ElementCount`, and `ByteCount` intentionally throw because no single static value exists.

`MIGraphXProgram.Save` and `Load` accept only an explicit absolute path and `MIGraphXFileOptions("msgpack")`. `MIGraphXModelCache` requires an explicit absolute root and validates the model hash, frozen header/API identity, managed identity, native-library fingerprint, target/options/format, ordered overrides, and saved payload hash before a hit. See the [M5 design](../design/m5-dynamic-shape-cache.md) for the exact contract.

## Evidence boundary

The local test suite passes `--fake-native` only for the small C substitutes and emits `fake-native-executed`. M4/M5/M6 objects have no new official runtime session. The separate older session used a clean detached checkout of `f1a11cfd1701a041cee29188f7600c85b34ae260`, verified the frozen installation header and native library, and executed M1 plus both M2 parse paths on a gfx1100 GPU. See the [official runtime summary](../validation/m1-m2-official-runtime.md) for the exact boundary.

中文提示：显式路径只接受绝对文件路径。本地 fake-native 仅验证托管边界与 ABI 形状；官方 runtime 结论来自独立的精确 SHA 会话，不能扩展到未测试能力。
