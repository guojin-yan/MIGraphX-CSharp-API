# Getting started

The core package is `JYPPX.ROCm.MIGraphX.CSharp.API`; its C# namespace is `JYPPX.ROCm.MIGraphXSharp`. Version `1.0.0` is distributed through NuGet.org and [GitHub Releases](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v1.0.0).

For the native engine choose the matching [runtime package and ROCm system prerequisites](runtime-deployment.md).

## Install and build

Install from NuGet.org:

```bash
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API --version 1.0.0
# Optional HIP stream/device-input adapter:
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop --version 1.0.0
```

The published 0.0.2 adapter resolves exact core `0.0.2` and HipSharp `0.10.0` dependencies. Release 1.0.0 requires core `1.0.0` and stable HipSharp `1.0.0`; upgrade any explicit HIP package reference together. See the [release notes](../release-notes/1.0.0.md). The HipSharp dependency does not install ROCm or HIP native libraries. Prebuilt packages do not require a HipSharp source checkout.

For offline installation, download the `.nupkg` assets from GitHub Releases into an absolute directory outside the checkout. Include all three packages when using the adapter, add that directory with `dotnet nuget add source /absolute/path/to/packages --name migraphx-release`, and use it for both `JYPPX.ROCm.MIGraphX.*` and `JYPPX.ROCm.HIP.CSharp.API` if your application uses package-source mapping. Verify downloaded files against the release's `SHA256SUMS.txt`.

To build from source instead:

Install the .NET SDK selected by `global.json` and PowerShell 7, and configure the HipSharp source dependency described in [CONTRIBUTING.md](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/CONTRIBUTING.md):

```powershell
.\eng\pack.ps1 -Configuration Release -Version 1.0.0
```

Add `../MIGraphXSharp.Workspace/artifacts/packages/` as a local NuGet source in your consuming application, then reference the exact package version. Full solution builds also need the HipSharp source dependency described in [CONTRIBUTING.md](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/CONTRIBUTING.md).

For inference, install the platform runtime package and matching ROCm 10.1 system prerequisites using the [deployment guide](runtime-deployment.md). An existing compatible system MIGraphX installation can also be selected by absolute path. Building managed code does not install a native runtime.

## Probe the library

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var nativePath = "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3";
var report = MIGraphXEnvironment.Probe(nativePath);
Console.WriteLine(report.State);
foreach (var diagnostic in report.Diagnostics)
    Console.WriteLine(diagnostic);
```

The library path must be absolute. Probing reports loading and export failures; it does not certify inference on every model or device. `ProbeSystem` checks application RID, application-base and system-loader candidates. The loader downloads no files and changes no search-path environment variables.

## Run a model

```csharp
var result = MIGraphXOnnxWorkflow.RunFile(
    nativePath,
    "/absolute/path/to/model.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));
```

This convenience API requires exactly one static, standard-layout float32 input and output. Supply the exact number of input elements. It compiles for GPU with offload-copy, runs synchronously and copies the output into managed memory. `RunBuffer` takes a non-empty model byte array instead of a file path.

For multiple inputs or outputs, use [managed objects](managed-objects.md). Dynamic inputs, persistence and cache are covered in [their guide](dynamic-shapes-cache.md).

## Inspect parser capabilities

```csharp
var operators = MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath);
Console.WriteLine(string.Join(",", operators));
```

The read-only list is a copy of the loaded version's parser registry. A name in this list does not guarantee support for a particular opset, model, target or device.

See [platform limitations](../compatibility/platforms.md) before using advanced paths, and [HIP integration](hip-interop.md) for asynchronous execution.

中文提示：路径必须是绝对路径。便捷推理入口仅处理单输入、单输出的静态标准 float32 模型；其他模型请使用显式对象 API。
