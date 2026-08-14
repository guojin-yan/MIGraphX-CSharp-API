# Getting started

The public surface supports explicit diagnostics, the unchanged M2 single-input/single-output static float32 workflow, and M4 resource-safe synchronous objects. M4 models static tensors with ten mapped scalar types and deterministic parameter/output snapshots. Dynamic shapes, async, streams, device buffers, save/load, and graph editing remain excluded.

## Prerequisites

- The .NET SDK selected by `global.json`.
- PowerShell 7 and a C compiler for the local fake-native gate.
- For official runtime probing: the frozen ROCm 7.2.1 MIGraphX installation and its transitive dependencies on a compatible Linux x86-64 host.

## Build and verify

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m4-coverage.ps1
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

`generate-interop.ps1` stops before generation if the header SHA-256 differs from the frozen value and verify mode never writes tracked outputs. `verify-m2-abi.ps1` preserves the 41-function workflow gate and deterministic Identity model. `verify-m3-abi.ps1` closes the complete 159-function header against 158 managed EntryPoints, one explicit variadic unsupported item, and 159 official public ELF exports.

For explicit target/program/options/argument composition, continue with the [managed object workflow](managed-objects.md). Its inputs and outputs are copied for safety; it does not expose zero-copy or async behavior.

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

## Evidence boundary

The local test suite passes `--fake-native` only for the small C test substitute and emits `fake-native-executed`. M4 objects have no new official runtime session. The separate older session used a clean detached checkout of `f1a11cfd1701a041cee29188f7600c85b34ae260`, verified the frozen installation header and native library, and executed M1 plus both M2 parse paths on a gfx1100 GPU. See the [official runtime summary](../validation/m1-m2-official-runtime.md) for the exact boundary.

中文提示：显式路径只接受绝对文件路径。本地 fake-native 仅验证托管边界与 ABI 形状；官方 runtime 结论来自独立的精确 SHA 会话，不能扩展到未测试能力。
