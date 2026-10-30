# MIGraphXSharp

[Chinese / 中文](README.zh-CN.md)

MIGraphXSharp provides .NET bindings and managed resource wrappers for AMD MIGraphX. Use it to load and compile ONNX models, run inference, inspect shapes and parser capabilities, and integrate with HipSharp streams.

The project is unpublished; the default package version is `0.0.0`. The native baseline is ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04` on Ubuntu 24.04 x86-64. See [platform support and limitations](docs/compatibility/platforms.md) before selecting a workload.

## Packages and prerequisites

| Package | Purpose |
| --- | --- |
| `JYPPX.ROCm.MIGraphX.CSharp.API` | Core bindings, synchronous inference, managed objects and diagnostics |
| `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` | Optional HipSharp stream and device-input integration |

The core namespace is `JYPPX.ROCm.MIGraphXSharp`. Both packages provide [15 target frameworks](docs/compatibility/frameworks.md), from `net46` through `net10.0`. Managed framework availability does not imply native platform support.

Install MIGraphX and ROCm through [AMD's official system repository](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html). This project distributes managed assemblies only (`system-native`); it does not supply native Runtime NuGet packages.

## Quick start

Install the .NET SDK selected in `global.json` and PowerShell 7, configure the [HipSharp source dependency](CONTRIBUTING.md), then build a local package:

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.0.0
```

Packages are written to `../MIGraphXSharp.Workspace/artifacts/packages/`. For full solution builds, configure the [HipSharp source dependency](CONTRIBUTING.md).

For a model with exactly one static, standard float32 input and output:

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3",
    "/absolute/path/to/model.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));
```

Paths must be absolute and input length must match the model. The workflow compiles for GPU with offload-copy, executes synchronously, and returns copied host output. See [Getting started](docs/guides/getting-started.md) for library diagnostics and the [managed object workflow](docs/guides/managed-objects.md) for multiple parameters and outputs.

## Features and limits

The API includes explicit resource ownership, immutable shape snapshots, typed host arguments, ONNX options, dynamic dimensions, program persistence and caching, graph construction, typed operation attributes, TensorFlow parsing, quantization and experimental custom operations.

Runtime qualification is partial. Advanced APIs and HIP async/device-buffer paths still have pending real-runtime semantics; local substitute tests do not establish GPU support or performance. General C variadic arguments and an independently owned module without a native destroy contract are excluded. Read [platform support](docs/compatibility/platforms.md) for the applicable boundaries.

## Documentation

- [Getting started](docs/guides/getting-started.md)
- [Managed objects and ownership](docs/guides/managed-objects.md)
- [Dynamic shapes, persistence and cache](docs/guides/dynamic-shapes-cache.md)
- [Typed operation attributes](docs/guides/operation-attributes.md)
- [HIP async integration](docs/guides/hip-interop.md)
- [Graph, TensorFlow, quantization and experimental APIs](docs/guides/advanced-apis.md)
- [Deployment and troubleshooting](docs/guides/runtime-deployment.md)
- [Framework compatibility](docs/compatibility/frameworks.md) and [API versioning](docs/guides/api-versioning.md)
- [API reference](docs/api/index.md)

## Repository layout

`src/` contains the libraries; `samples/` contains examples; `docs/` contains user documentation; `tests/` and `native/` contain test sources; `eng/` and `tools/` contain reproducible build and diagnostic scripts; `compatibility/` contains generator inputs and API/support contracts.

Build output, generated API pages, caches and diagnostic results default to the sibling `MIGraphXSharp.Workspace/` directory. Set `MIGRAPHX_WORKSPACE_ROOT` to an absolute directory outside the checkout to override it. Development plans, session records and article drafts belong outside the code repository. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Copyright 2026 Guojin Yan. Licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE). AMD/ROCm/MIGraphX components remain subject to their own licenses.
