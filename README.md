# MIGraphXSharp

[Chinese / 中文](README.zh-CN.md)

MIGraphXSharp `0.0.0` now contains the M1 Direct P/Invoke vertical subset for the official AMD MIGraphX C API. It binds six target/program create, assign, and destroy functions from the frozen ROCm 7.2.1 header through one manifest. This remains a local engineering candidate, not a published release.

## Status

- The core builds the exact 15-TFM matrix from `net46` through `net10.0`; .NET 7+ uses generated `LibraryImport`, and older targets use generated `DllImport`.
- The frozen input is ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`, header SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`.
- M1 provides explicit/system native loading diagnostics, exact status mapping, strict UTF-8, and owned target/program SafeHandles behind a deliberately small public diagnostic surface.
- Local fake-native tests execute loader, export, status, UTF-8, assign, construction cleanup, disposal, and concurrency paths. They are test-substitute evidence, not official MIGraphX, AMD GPU, or Radeon Cloud evidence.
- Official Linux ELF exports are statically verified. Official loader and target/program runtime execution remain planned because no Radeon Cloud session is authorized.
- ONNX parse, compile, run, argument buffers, shape APIs, async execution, and GPU inference are outside M1.
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
```

`ProbeSystem` audits application RID, application-base, and system-loader candidates. Linux candidates include `libmigraphx_c.so.3` and `migraphx_c`. Windows and macOS candidates are implemented for diagnostics but have no official MIGraphX runtime support claim.

## Build

Install the .NET 10 SDK selected by `global.json`, PowerShell 7, CMake, and a C compiler, then run:

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

Build, static official-ELF evidence, fake-native execution, and official MIGraphX runtime execution are separate evidence levels. M1 is blocked until the last category is recorded on an authorized Radeon Cloud session against a pushed 40-character SHA.

## Documentation

See the [M1 Direct P/Invoke design](docs/design/m1-direct-pinvoke.md), [getting started guide](docs/guides/getting-started.md), [platform evidence](docs/compatibility/platforms.md), and [validation summary](docs/validation/README.md). The [M0 baseline](docs/design/m0-native-baseline.md) remains the source-input record.

## License

Copyright 2026 Guojin Yan. This managed project is licensed under the [Apache License 2.0](LICENSE); see [NOTICE](NOTICE). AMD/ROCm/MIGraphX components are not included and remain subject to their own licenses.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and the machine-readable [M1 subset manifest](compatibility/m1-binding-subset.json). Public APIs require equivalent Chinese and English XML documentation. Never commit AMD binaries, fake-native build output, models, credentials, cloud connection data, or generated declarations that cannot be reproduced from the fixed input.
