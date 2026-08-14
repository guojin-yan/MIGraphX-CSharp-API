# MIGraphXSharp

[Chinese / 中文](README.zh-CN.md)

MIGraphXSharp `0.0.0` now contains the M1 lifecycle foundation and the M2 restricted ONNX parse/compile/run workflow for the official AMD MIGraphX C API. Forty-one cumulative declarations are generated from one frozen ROCm 7.2.1 manifest. This remains a local engineering candidate, not a published release.

## Status

- The core builds the exact 15-TFM matrix from `net46` through `net10.0`; .NET 7+ uses generated `LibraryImport`, and older targets use generated `DllImport`.
- The frozen input is ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`, header SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`.
- M1 provides explicit/system native loading diagnostics, exact status mapping, strict UTF-8, and owned target/program lifetimes.
- M2 adds explicit file and byte-buffer ONNX entry points for one static, standard float32 input/output, synchronous GPU-target compile with offload-copy, pinned input, and copied output.
- Local fake-native tests execute loader, frontend/export classification, parse, shape validation, compile, run, failure cleanup, and concurrency. They remain test-substitute evidence and are recorded separately from official runtime evidence.
- M1/M2 official runtime validation passed at `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The official loader, target/program lifecycle, file/buffer ONNX parse, GPU compile, synchronous run, and Identity reference comparison executed successfully.
- Dynamic shape, multiple inputs/outputs, non-float32 tensors, async/stream/device-buffer APIs, general Program/Shape/Argument objects, and runtime NuGet packages remain outside M2.
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
```

`ProbeSystem` audits application RID, application-base, and system-loader candidates. Linux candidates include `libmigraphx_c.so.3` and `migraphx_c`. Windows and macOS candidates are implemented for diagnostics but have no official MIGraphX runtime support claim.

## Build

Install the .NET 10 SDK selected by `global.json`, PowerShell 7, CMake, and a C compiler, then run:

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

Build, static official-ELF evidence, fake-native execution, and official MIGraphX runtime execution remain separate evidence levels. The M1/M2 runtime claim is limited to the exact pushed SHA, environment, model, shape, and synchronous offload-copy path recorded in the [official runtime summary](docs/validation/m1-m2-official-runtime.md).

## Documentation

See the [M2 ONNX design](docs/design/m2-onnx-workflow.md), [getting started guide](docs/guides/getting-started.md), [platform evidence](docs/compatibility/platforms.md), and [official runtime summary](docs/validation/m1-m2-official-runtime.md). The [M1 design](docs/design/m1-direct-pinvoke.md) remains the lifecycle foundation.

## License

Copyright 2026 Guojin Yan. This managed project is licensed under the [Apache License 2.0](LICENSE); see [NOTICE](NOTICE). AMD/ROCm/MIGraphX components are not included and remain subject to their own licenses.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and the machine-readable [M2 subset manifest](compatibility/m2-binding-subset.json). Public APIs require equivalent Chinese and English XML documentation. Never commit AMD binaries, fake-native build output, models, credentials, cloud connection data, or generated declarations that cannot be reproduced from the fixed input.
