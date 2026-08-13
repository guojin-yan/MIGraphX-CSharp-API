# MIGraphXSharp

[Chinese / 中文](README.zh-CN.md)

MIGraphXSharp is a planned .NET binding for the official AMD MIGraphX C API. Version `0.0.0` is the M0 engineering and research baseline: it establishes reproducible multi-target builds, package and documentation gates, and a frozen upstream input. It does not yet implement a native MIGraphX call.

## Status

- Managed core builds for the exact 15-TFM matrix from `net46` through `net10.0`.
- The frozen research target is ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`.
- No MIGraphX P/Invoke, ONNX parse/compile/run, native loader, asynchronous execution, or GPU execution exists in M0.
- The local machine has no AMD GPU. Future GPU claims must link evidence from this project's Radeon Cloud records and a pushed 40-character Git SHA.
- The core package carries no MIGraphX or ROCm native files. Runtime packages are disabled and fail closed.
- The managed project is licensed under Apache-2.0. Version `0.0.0` remains a local engineering artifact and must not be published.

## Install

M0 produces a local-only candidate package:

The frozen NuGet package ID, core project, and assembly are `JYPPX.ROCm.MIGraphX.CSharp.API`. The C# namespace remains `JYPPX.ROCm.MIGraphXSharp`.

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.0.0
```

Do not publish or use this candidate as a functional MIGraphX binding. The future core package will remain managed-first and expect an independently validated native installation unless a verified RID runtime package is explicitly installed.

## Use

The only public API reports engineering status and never probes native software:

```csharp
using JYPPX.ROCm.MIGraphXSharp;

Console.WriteLine(MIGraphXBuildInfo.PackageVersion);        // 0.0.0
Console.WriteLine(MIGraphXBuildInfo.NativeBindingsAvailable); // false
```

## Build

Install a .NET 10 SDK compatible with `global.json`, then run:

```powershell
dotnet tool restore
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

Build success, package assets, clean-consumer compilation, native loading, and AMD GPU execution are separate evidence levels. M0 proves only the managed levels listed in the validation report.

## Documentation

The DocFX source is under [`docs`](docs/index.md). The native research baseline records the official package URLs, package/header/library SHA-256 values, API-family counts, SONAME, dependencies, exports, and evidence limits in [`docs/design/m0-native-baseline.md`](docs/design/m0-native-baseline.md).

## License

Copyright 2026 Guojin Yan. This managed project is licensed under the [Apache License 2.0](LICENSE); see [`NOTICE`](NOTICE) for attribution. AMD/ROCm/MIGraphX components are not included and remain subject to their own licenses.

## Contributing

Read [`CONTRIBUTING.md`](CONTRIBUTING.md), [`SECURITY.md`](SECURITY.md), and the machine-readable files under [`compatibility`](compatibility/upstream-c-api-manifest.json). New public APIs require equivalent Chinese and English XML documentation. Never commit AMD binaries, models, credentials, cloud connection data, or generated bindings that cannot be reproduced from a fixed input.
