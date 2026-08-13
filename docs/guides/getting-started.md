# Getting started

M2 supports explicit diagnostics plus a restricted single-input, single-output, static float32 ONNX parse/compile/run workflow. It does not expose dynamic shapes, multiple inputs/outputs, non-float32 tensors, async, streams, device buffers, or a general Program/Shape/Argument object model.

## Prerequisites

- The .NET SDK selected by `global.json`.
- PowerShell 7 and a C compiler for the local fake-native gate.
- For official runtime probing: the frozen ROCm 7.2.1 MIGraphX installation and its transitive dependencies on a compatible Linux x86-64 host.

## Build and verify

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

`generate-interop.ps1` stops before generation if the header SHA-256 differs from the frozen value. `verify-m2-abi.ps1` compares all 41 cumulative manifest names with the header-validated generated declarations, fake exports, and hash-verified official ELF exports, then verifies the generated Identity model hash.

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

The local test suite passes `--fake-native` only for the small C test substitute and emits `fake-native-executed`. Official runtime claims additionally require the unified M1/M2 Radeon Cloud record, exact pushed SHA, frozen installation header, and redacted environment evidence. The Owner deferred that session, so M1/M2 remain `runtime-deferred`.

中文提示：显式路径只接受绝对文件路径。本地 fake-native 仅验证托管边界与 ABI 形状，不能作为官方 MIGraphX 或 AMD GPU 已运行的证据。
