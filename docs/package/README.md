# JYPPX.ROCm.MIGraphX.CSharp.API

Managed .NET bindings and resource wrappers for AMD MIGraphX. The package contains managed assemblies and Chinese/English XML API documentation for 15 target frameworks, from `net46` through `net10.0`. Version `0.0.1` is distributed through [GitHub Releases](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.1).

The C# namespace is `JYPPX.ROCm.MIGraphXSharp`. APIs include explicit native-library diagnostics, synchronous ONNX inference, shapes and host arguments, dynamic input overrides, program persistence/cache, parser introspection, graph construction and advanced experimental features.

Install ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04` through AMD's official Linux system repository. This package contains no native runtime. Managed Windows assets do not supply an official Windows MIGraphX runtime.

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3",
    "/absolute/path/to/model.onnx",
    new[] { 1f, 2f, 3f, 4f });
```

The convenience workflow requires one static, standard float32 input/output with matching element counts. Use explicit managed objects for other supported compositions.

Real-runtime qualification is partial. Advanced graph, TensorFlow, quantization, custom-operation, dynamic/cache and async/device-buffer semantics need workload-specific validation. General C varargs and independently owned modules without a native destroy contract are excluded. The optional HIP adapter is a separate package.

Read the [user documentation](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/README.md) for installation, ownership and support limitations. Licensed under Apache-2.0.
