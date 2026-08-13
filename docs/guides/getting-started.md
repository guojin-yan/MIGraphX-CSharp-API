# Getting started

M1 supports explicit diagnostics and a minimal target/program lifetime probe. It does not expose ONNX, compile, run, argument, shape, async, or GPU-inference workflows.

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
.\eng\verify-m1-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

`generate-interop.ps1` stops before generation if the header SHA-256 differs from the frozen value. `verify-m1-abi.ps1` compares the six manifest names with the header-validated generated declarations, fake exports, and hash-verified official ELF exports.

## Probe an explicit library

```powershell
dotnet run --project .\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj -c Release -- \
  C:\absolute\path\to\migraphx_c.dll
```

The path must be an absolute file path. Missing files, bad image/architecture, dependency loading, missing export, and other platform failures are distinct diagnostic kinds, and original platform error text is retained. The loader does not change process search-path variables or download files.

Running the smoke command with no path has no native side effects and returns `nativeProbe: not-probed` and `nativeLibrary: not-available`; it never prints `ready`.

## Evidence boundary

The local test suite passes `--fake-native` only for the small C test substitute and emits `fake-native-executed`. A normal explicit path emits `runtime-executed` only for calls actually made against that supplied binary; public official-runtime evidence additionally requires the Radeon Cloud record policy, exact pushed SHA, frozen installation header, and redacted environment record.

中文提示：显式路径只接受绝对文件路径。本地 fake-native 仅验证托管边界与 ABI 形状，不能作为官方 MIGraphX 或 AMD GPU 已运行的证据。
